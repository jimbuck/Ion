using Ion.Extensions.Assets;

using Microsoft.Extensions.Logging.Abstractions;

namespace Ion.Tests;

public class FakeAsset(string name) : IAsset
{
	private static int _nextId;

	public nint Id { get; } = Interlocked.Increment(ref _nextId);
	public string Name { get; } = name;
	public int DisposeCount { get; private set; }

	public void Dispose() => DisposeCount++;
}

public class OtherFakeAsset(string name) : FakeAsset(name) { }

public class FakeLoader : IAssetLoader
{
	public Type AssetType => typeof(FakeAsset);

	public HashSet<string> ExistingFiles { get; } = [];
	public int LoadCount { get; private set; }

	public FakeAsset Load(string path)
	{
		LoadCount++;
		if (!ExistingFiles.Contains(path)) throw new FileNotFoundException("not found", "/fake/root/" + path);
		return new FakeAsset(path);
	}

	public OtherFakeAsset LoadOther(string path)
	{
		LoadCount++;
		return new OtherFakeAsset(path);
	}
}

public static class FakeAssetManagerExtensions
{
	public static FakeAsset LoadFake(this IBaseAssetManager assets, string path)
	{
		var loader = (FakeLoader)assets.GetLoader(typeof(FakeAsset));
		return assets.GetOrLoad(path, loader.Load);
	}
}

public class AssetManagerTests
{
	private readonly FakeLoader _loader = new();
	private readonly GlobalAssetManager _global;

	public AssetManagerTests()
	{
		_loader.ExistingFiles.Add("a.png");
		_loader.ExistingFiles.Add("b.png");
		_global = new GlobalAssetManager(NullLogger<GlobalAssetManager>.Instance, [_loader]);
	}

	private ScopedAssetManager NewScope() => new(NullLogger<ScopedAssetManager>.Instance, _global);

	[Fact, Trait(CATEGORY, UNIT)]
	public void LoadingSamePathTwiceReturnsCachedAsset()
	{
		var first = _global.LoadFake("a.png");
		var second = _global.LoadFake("a.png");

		Assert.Same(first, second);
		Assert.Equal(1, _loader.LoadCount);

		var other = _global.LoadFake("b.png");
		Assert.NotSame(first, other);
		Assert.Equal(2, _loader.LoadCount);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void CacheIsKeyedByAssetTypeAndPath()
	{
		var fake = _global.GetOrLoad("a.png", _loader.Load);
		var other = _global.GetOrLoad("a.png", _loader.LoadOther);

		Assert.NotSame(fake, other);
		Assert.Same(other, _global.GetOrLoad("a.png", _loader.LoadOther));
		Assert.Equal(2, _loader.LoadCount);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void SetWithDuplicateNameReplaces()
	{
		var first = new FakeAsset("font");
		var second = new FakeAsset("font");

		_global.Set(first);
		var ex = Record.Exception(() => _global.Set(second));
		Assert.Null(ex);

		Assert.Same(second, _global.Get<FakeAsset>(second.Id));
		Assert.Same(first, _global.Get<FakeAsset>(first.Id));

		// Setting the same instance again is fine too.
		Assert.Null(Record.Exception(() => _global.Set(second)));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void MissingFileThrowsClearFileNotFound()
	{
		var ex = Assert.Throws<FileNotFoundException>(() => _global.LoadFake("Bonk.wav"));

		Assert.Contains("FakeAsset", ex.Message);
		Assert.Contains("'Bonk.wav'", ex.Message);
		Assert.Contains("/fake/root/Bonk.wav", ex.Message);
		Assert.Contains("case-sensitive", ex.Message);
		Assert.Equal("/fake/root/Bonk.wav", ex.FileName);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void UnloadDisposesAndEvictsFromCache()
	{
		var first = _global.LoadFake("a.png");
		_global.Unload(first);

		Assert.Equal(1, first.DisposeCount);
		Assert.Null(_global.Get<FakeAsset>(first.Id));

		var second = _global.LoadFake("a.png");
		Assert.NotSame(first, second);
		Assert.Equal(2, _loader.LoadCount);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ScopedManagerDisposesItsOwnAssetsOnly()
	{
		var globalAsset = _global.LoadFake("a.png");

		FakeAsset scopedAsset;
		FakeAsset setAsset = new("set");
		using (var scope = NewScope())
		{
			// Globally cached assets are reused, not reloaded.
			Assert.Same(globalAsset, scope.LoadFake("a.png"));

			scopedAsset = scope.LoadFake("b.png");
			Assert.Same(scopedAsset, scope.LoadFake("b.png"));
			scope.Set(setAsset);
		}

		Assert.Equal(1, scopedAsset.DisposeCount);
		Assert.Equal(1, setAsset.DisposeCount);
		Assert.Equal(0, globalAsset.DisposeCount);
		Assert.Equal(2, _loader.LoadCount);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ScopedManagerDisposeIsIdempotent()
	{
		var scope = NewScope();
		var asset = scope.LoadFake("a.png");

		scope.Dispose();
		scope.Dispose();

		Assert.Equal(1, asset.DisposeCount);
		Assert.Throws<ObjectDisposedException>(() => scope.LoadFake("a.png"));
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void DiScopeDisposesSceneAssets()
	{
		var builder = IonApplication.CreateBuilder();
		builder.Services.AddAssets();
		var loader = new FakeLoader();
		loader.ExistingFiles.Add("a.png");
		builder.Services.AddSingleton<IAssetLoader>(loader);
		using var app = builder.Build();

		FakeAsset asset;
		using (var scope = app.Services.CreateScope())
		{
			var assets = scope.ServiceProvider.GetRequiredService<IAssetManager>();
			asset = assets.LoadFake("a.png");
			Assert.Equal(0, asset.DisposeCount);
		}

		Assert.Equal(1, asset.DisposeCount);
	}
}
