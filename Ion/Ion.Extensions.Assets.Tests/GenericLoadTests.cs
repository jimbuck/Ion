using Ion.Extensions.Assets;

using Microsoft.Extensions.Logging.Abstractions;

namespace Ion.Tests;

public interface IFakeTexture : IAsset
{
	int Size { get; }
}

public class FakeTexture(string name, int size) : IFakeTexture
{
	private static int _nextId = 1000;

	public nint Id { get; } = Interlocked.Increment(ref _nextId);
	public string Name { get; } = name;
	public int Size { get; } = size;
	public int DisposeCount { get; private set; }

	public void Dispose() => DisposeCount++;
}

public class OtherFakeTexture(string name) : FakeTexture(name, 0);

/// <summary>
/// An asset type no loader handles.
/// </summary>
public class UnloadableAsset(string name) : FakeTexture(name, 0);

/// <summary>
/// Matches both the <see cref="FakeAsset"/> and the <see cref="IFakeTexture"/> loaders.
/// </summary>
public class AmbiguousAsset(string name) : FakeAsset(name), IFakeTexture
{
	public int Size => 0;
}

/// <summary>
/// A loader registered under the interface type, as backends register theirs.
/// </summary>
public class FakeTextureLoader : IAssetLoader<IFakeTexture>
{
	public Type AssetType => typeof(IFakeTexture);

	public int LoadCount { get; private set; }

	public IFakeTexture Load(string path)
	{
		LoadCount++;
		if (path.StartsWith("missing", StringComparison.Ordinal)) throw new FileNotFoundException("not found", "/fake/root/" + path);
		return new FakeTexture(path, path.Length);
	}
}

public class GenericLoadTests
{
	private readonly FakeTextureLoader _textures = new();
	private readonly FakeLoader _legacy = new();
	private readonly GlobalAssetManager _global;

	public GenericLoadTests()
	{
		_global = new GlobalAssetManager(NullLogger<GlobalAssetManager>.Instance, [_textures, _legacy]);
	}

	private ScopedAssetManager NewScope() => new(NullLogger<ScopedAssetManager>.Instance, _global);

	[Fact, Trait(CATEGORY, UNIT)]
	public void LoadByInterface_UsesTheRegisteredLoaderAndCaches()
	{
		var first = _global.Load<IFakeTexture>("a.png");
		var second = _global.Load<IFakeTexture>("a.png");

		Assert.IsType<FakeTexture>(first);
		Assert.Equal(5, first.Size);
		Assert.Same(first, second);
		Assert.Equal(1, _textures.LoadCount);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void LoadByConcreteType_FallsBackToTheInterfaceLoaderAndSharesTheCache()
	{
		var byInterface = _global.Load<IFakeTexture>("a.png");
		var byConcrete = _global.Load<FakeTexture>("a.png");

		Assert.Same(byInterface, byConcrete);
		Assert.Equal(1, _textures.LoadCount);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void LoadByAnotherImplementation_ThrowsAClearError()
	{
		var ex = Assert.Throws<InvalidOperationException>(() => _global.Load<OtherFakeTexture>("a.png"));

		Assert.Contains("FakeTexture", ex.Message);
		Assert.Contains("OtherFakeTexture", ex.Message);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void LoadWithoutALoader_Throws()
	{
		var ex = Assert.Throws<InvalidOperationException>(() => new GlobalAssetManager(NullLogger<GlobalAssetManager>.Instance, [_legacy]).Load<UnloadableAsset>("a.png"));

		Assert.Contains("No loader registered", ex.Message);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void LoadMatchingSeveralLoaders_ThrowsAmbiguous()
	{
		var ex = Assert.Throws<InvalidOperationException>(() => _global.Load<AmbiguousAsset>("a.png"));

		Assert.Contains("ambiguous", ex.Message);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void LoadWithANonGenericLoader_Throws()
	{
		var ex = Assert.Throws<InvalidOperationException>(() => _global.Load<FakeAsset>("a.png"));

		Assert.Contains("IAssetLoader<FakeAsset>", ex.Message);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void MissingFile_ThrowsFileNotFoundWithTheInterfaceName()
	{
		var ex = Assert.Throws<FileNotFoundException>(() => _global.Load<IFakeTexture>("missing.png"));

		Assert.Contains("IFakeTexture", ex.Message);
		Assert.Contains("'missing.png'", ex.Message);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void Scope_ReusesGlobalAssetsAndOwnsItsOwnLoads()
	{
		var global = _global.Load<IFakeTexture>("a.png");

		FakeTexture scoped;
		using (var scope = NewScope())
		{
			Assert.Same(global, scope.Load<IFakeTexture>("a.png"));

			scoped = (FakeTexture)scope.Load<IFakeTexture>("b.png");
			Assert.Same(scoped, scope.Load<IFakeTexture>("b.png"));
		}

		Assert.Equal(1, scoped.DisposeCount);
		Assert.Equal(0, ((FakeTexture)global).DisposeCount);
		Assert.Equal(2, _textures.LoadCount);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void Scope_LoadAfterDisposeThrows()
	{
		var scope = NewScope();
		scope.Dispose();

		Assert.Throws<ObjectDisposedException>(() => scope.Load<IFakeTexture>("a.png"));
	}
}
