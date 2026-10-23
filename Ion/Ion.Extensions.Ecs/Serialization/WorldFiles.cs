using Arch.Core;

using Ion.Extensions.Assets;

namespace Ion.Extensions.Ecs;

/// <summary>
/// A saved entity tree (a world, or part of one, as <see cref="JsonWorldSerializer"/> or <see cref="BinaryWorldSerializer"/>
/// writes it), loaded through the assets: <c>assets.Load&lt;WorldFile&gt;("levels/one.ion")</c>. It keeps the file's bytes,
/// so it can be instantiated any number of times (a prefab), in any world.
/// </summary>
public sealed class WorldFile : IAsset
{
	private static long _nextId;
	private readonly byte[] _data;
	private readonly IWorldSerializer _serializer;

	internal WorldFile(string name, byte[] data, JsonWorldSerializer json, BinaryWorldSerializer binary)
	{
		Name = name;
		_data = data;
		IsBinary = BinaryWorldSerializer.HasHeader(data);
		_serializer = IsBinary ? binary : json;
	}

	/// <inheritdoc/>
	public nint Id { get; } = (nint)Interlocked.Increment(ref _nextId);

	/// <summary>The asset path it was loaded from.</summary>
	public string Name { get; }

	/// <summary>Whether the file is in the binary form (<c>IONW</c> header) rather than JSON.</summary>
	public bool IsBinary { get; }

	/// <summary>The size of the file in bytes.</summary>
	public int Length => _data.Length;

	/// <summary>
	/// Creates the saved entities in <paramref name="world"/>, with their components and hierarchy; returns them in saved
	/// order. Not inside a query (it makes structural changes).
	/// </summary>
	public IReadOnlyList<Entity> Instantiate(World world)
	{
		ArgumentNullException.ThrowIfNull(world);
		using var stream = new MemoryStream(_data, writable: false);
		return _serializer.Deserialize(stream, world);
	}

	/// <summary>
	/// Creates the saved entities in <paramref name="world"/> (see <see cref="Instantiate(World)"/>) and parents those that
	/// were roots in the file to <paramref name="parent"/>, appended to its children in saved order: a prefab placed by its
	/// parent's transform.
	/// </summary>
	public IReadOnlyList<Entity> Instantiate(World world, Entity parent)
	{
		var entities = Instantiate(world);
		foreach (var entity in entities)
		{
			if (!world.Has<Parent>(entity)) world.SetParent(entity, parent);
		}

		return entities;
	}

	/// <summary>Nothing to release: the bytes are managed.</summary>
	public void Dispose() { }
}

/// <summary>Loads <see cref="WorldFile"/> assets from the assets storage (registered by <c>AddEcsSerialization</c>).</summary>
internal sealed class WorldFileLoader(IPersistentStorage storage, JsonWorldSerializer json, BinaryWorldSerializer binary) : IAssetLoader<WorldFile>
{
	public Type AssetType { get; } = typeof(WorldFile);

	public WorldFile Load(string path)
	{
		ArgumentException.ThrowIfNullOrEmpty(path);
		using var stream = storage.Assets.Read(path);
		using var bytes = new MemoryStream();
		stream.CopyTo(bytes);
		return new WorldFile(path, bytes.ToArray(), json, binary);
	}
}

/// <summary>The world files a scene declared with <c>LoadWorld</c> (one per scene scope).</summary>
public sealed class SceneWorldFiles
{
	/// <summary>The asset paths, in declaration order.</summary>
	public List<string> Paths { get; } = [];
}

/// <summary>
/// Instantiates the scene's <see cref="SceneWorldFiles"/> into its world when the scene loads (Init, at
/// <see cref="StageOrder.WorldFiles"/>: before the game's Init steps). Added by <c>scene.LoadWorld(path)</c>.
/// </summary>
public sealed class WorldFileLoadSystem(SceneWorldFiles files, World world, IAssetManager assets)
{
	/// <summary>The entities the scene's files created, in file order then saved order.</summary>
	public List<Entity> Loaded { get; } = [];

	/// <summary>Loads the files.</summary>
	[Init(Order = StageOrder.WorldFiles)]
	public void Load(GameTime dt)
	{
		foreach (var path in files.Paths) Loaded.AddRange(assets.Load<WorldFile>(path).Instantiate(world));
	}
}
