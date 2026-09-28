---
title: Assets
description: Load textures, fonts, sounds, models and cube maps through IAssetManager, understand caching and scopes, and hot reload changed files.
sidebar:
  order: 7
---

The asset module (`Ion.Extensions.Assets`) loads files from your game's assets folder into typed, cached objects:
textures, fonts, sounds, glTF models, cube maps. You ask for an asset by its **interface** and its path, and get back the
same instance every time:

```csharp
using Ion;
using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;

public sealed class LoadSystem(IAssetManager assets)
{
	private ITexture2D _tiles = null!;
	private IFont _font = null!;

	[Init]
	public void Load(GameTime dt)
	{
		_tiles = assets.Load<ITexture2D>("tiles.png");
		_font = assets.Load<IFontSet>("Bungee-Regular.ttf").CreateStyle(24);
	}
}
```

`AddIon()` registers the asset managers, the loaders for the registered renderers and audio, and hot reload. Nothing
else is needed.

## The assets folder

Paths are relative to the **assets root**, which is the `Assets` folder next to your game's executable by default.
Subfolders are part of the path, with forward slashes: `assets.Load<IModel>("Avocado/Avocado.gltf")`.

Copy the folder to the build output in your project file (the templates already do this):

```xml title="MyGame.csproj"
<ItemGroup>
  <Content Include="Assets\**\*" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

To load from somewhere else, set `Ion:Storage:AssetsPath` (relative paths resolve against the game root, which is the
executable's folder unless `Ion:Storage:GamePath` says otherwise):

```json title="appsettings.json"
{ "Ion": { "Storage": { "AssetsPath": "../../../Assets" } } }
```

Loaders read files through `IPersistentStorage.Assets`, so assets and other game files share one storage abstraction.
See [Storage](/Ion/concepts/storage/).

## Asset types

Load assets through the interfaces below, never the backend's concrete classes: the same call then works windowed, with
headless rendering, and under `--headless` (where the null loaders read only what tests need).

| Interface | Files | Loader (registered by) | Headless (`--headless`) |
|---|---|---|---|
| `ITexture2D` | PNG, JPEG and other formats ImageSharp decodes | 2D renderer (`AddIon`) | Size read from the image header, no pixels |
| `IFontSet` | `.ttf`, `.otf` | 2D renderer (`AddIon`) | Monospaced stand-in font |
| `ISoundEffect` | WAV, OGG Vorbis, MP3 | Audio (`AddIon`) | Decoded, played by the null output |
| `IModel` | glTF 2.0 `.gltf` and `.glb` | 3D renderer (`AddRendering3D`) | Geometry and materials loaded, textures are placeholders |
| `ICubemap` | A folder with six face images | 3D renderer (`AddRendering3D`) | Placeholder texture |

See [Text](/Ion/rendering/text/) for fonts, [Models and glTF](/Ion/rendering/3d/models-gltf/) for models and cube maps,
and [Audio](/Ion/interaction/audio/) for sounds.

### What the texture loader does

The 2D renderer's texture loader decodes the image with ImageSharp to RGBA8, **premultiplies alpha**, builds the full
mip chain on the CPU (a 2x2 box filter of the premultiplied texels), and uploads every level with `IQueue.WriteTexture`.
Nothing waits for the GPU. The resulting `ITexture2D` has `Width`, `Height` and `MipLevels`.

For textures made from pixels in memory, use `TextureFactory` (see [Sprites](/Ion/rendering/sprites/#textures-from-memory)).

## Caching and ownership

`IBaseAssetManager` (implemented by every asset manager):

| Member | Meaning |
|---|---|
| `Load<T>(path)` | The asset of type `T` loaded from `path`: cached on first use, the same instance afterwards |
| `GetOrLoad<T>(path, load)` | The cached asset for `path`, or the result of your `load` function, cached. Use it in loader extension methods |
| `Get<T>(id)` | A loaded asset by its `IAsset.Id` |
| `Set<T>(asset)` | Adds an asset you created to the manager (it then owns and disposes it) |
| `Unload<T>(asset)` | Removes the asset from the caches and disposes it |
| `GetLoader(type)` | The loader registered for an asset type |

Every asset implements `IAsset`: an `Id` (unique per process), a `Name` (usually its path) and `Dispose`.

Two managers exist:

- The **global** manager caches assets for the whole application. Global assets live as long as the process.
- A **scoped** `IAssetManager` exists per DI scope, which means per [scene](/Ion/ecs/scenes/). A scene's `Load` returns a
  globally cached asset if there is one; otherwise it loads the asset into the scene's own cache, and the asset is
  disposed when the scene unloads. `IAssetManager.Global` reaches the global manager from a scene.

```csharp
public sealed class LevelSystem(IAssetManager assets)
{
	[Init]
	public void Load(GameTime dt)
	{
		var level = assets.Load<ITexture2D>("levels/forest.png");        // owned by this scene: freed on unload
		var ui = assets.Global.Load<IFontSet>("Bungee-Regular.ttf");     // shared: kept across scenes
	}
}
```

:::caution
GPU textures loaded by the 2D renderer are released at the renderer's teardown, before the device is destroyed, even if
the asset manager still holds them. Do not use loaded textures in `[Destroy]` steps that run after the renderer's
teardown.
:::

When no loader is registered for `T` itself, the loader for an interface that `T` implements is used and the result is
cast. That keeps legacy calls such as `Load<Texture2D>` working, but it ties the game to one backend. `Load` throws
`InvalidOperationException` when no loader fits and `FileNotFoundException` when the file is missing.

## Hot reload

With hot reload on, editing an asset file while the game runs updates it in the next frame: change a sprite in your
image editor, save, and see it in game.

| Setting | Default | Meaning |
|---|---|---|
| `Ion:Assets:HotReload` | `true` in Debug builds of the assets package, `false` in Release | Watch the assets folder |

How it works:

1. `AssetWatcher` (an `IAssetWatcher`) runs a `FileSystemWatcher` over the assets root, subfolders included, and queues
   the relative paths of created, changed and renamed files.
2. A file must stop changing for a settle time (`AssetWatcher.SettleTime`, 100 ms) before it is reloaded, so an editor
   that writes in several steps triggers one reload of the finished file.
3. `AssetReloadSystem` runs at the start of every frame (First stage, `StageOrder.AssetReload`, -920: after input, before
   any of your steps). It reloads every cached asset loaded from a changed path, in the global cache and in every live
   scene scope.
4. Each reload emits an `AssetReloadedEvent(AssetId, PreviousAssetId)`.

There are two kinds of reload:

| Kind | When | What you see |
|---|---|---|
| **In place** (`e.InPlace` is true) | The loader implements `IReloadableAssetLoader` and can update the object: the 2D texture loader when the image keeps its size, the headless texture and font loaders | Every holder of the asset sees the new content. Nothing to do |
| **New instance** | Any other case: a texture whose size changed, fonts of the 2D renderer, sounds, models | The cache now returns a new object. Code holding the old one should load it again |

Handle new-instance reloads by reading the event:

```csharp
using Ion;
using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;

public sealed class TileSystem(IAssetManager assets, IEvents events)
{
	// Create readers once and keep them in a mutable field.
	private EventReader<AssetReloadedEvent> _reloads = events.Reader<AssetReloadedEvent>();
	private ITexture2D _tiles = null!;

	[Init]
	public void Load(GameTime dt) => _tiles = assets.Load<ITexture2D>("tiles.png");

	[Update]
	public void Update(GameTime dt)
	{
		while (_reloads.TryRead(out var e))
		{
			if (!e.InPlace && e.PreviousAssetId == _tiles.Id) _tiles = assets.Load<ITexture2D>("tiles.png");
		}
	}
}
```

A reload that fails (a half-written or deleted file, an image that no longer decodes) logs a warning and keeps the
loaded asset.

You can also queue a reload by hand, with or without the file system watcher:

```csharp
watcher.Enqueue("tiles.png");   // IAssetWatcher, injected; reloaded at the start of the next frame
```

:::note
Only the file an asset was loaded **from** is watched. Editing a texture that a glTF model references, or a `.bin`
buffer, does not reload the model; touch the `.gltf` file to reload it. `IonTestHost` turns hot reload off so tests stay
deterministic.
:::

## Writing a loader

Add support for a new file type by implementing `IAssetLoader<T>` for an asset interface and registering it as
`IAssetLoader`. Loaders are looked up by `AssetType`, which should be the interface games use.

```csharp
using System.Text.Json;

using Microsoft.Extensions.DependencyInjection;

using Ion;
using Ion.Extensions.Assets;

public interface ILevelData : IAsset
{
	string[] Rows { get; }
}

internal sealed class LevelData(string name, string[] rows) : ILevelData
{
	private static long _nextId;
	public nint Id { get; } = (nint)Interlocked.Increment(ref _nextId);
	public string Name { get; } = name;
	public string[] Rows { get; } = rows;
	public void Dispose() { }
}

internal sealed class LevelLoader(IPersistentStorage storage) : IAssetLoader<ILevelData>
{
	public Type AssetType { get; } = typeof(ILevelData);

	public ILevelData Load(string path)
	{
		using var reader = new StreamReader(storage.Assets.Read(path));
		var rows = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
		return new LevelData(path, rows);
	}
}
```

Register it in `Program.cs`, then load it like any other asset:

```csharp title="Program.cs"
builder.Services.AddSingleton<IAssetLoader, LevelLoader>();
```

```csharp
var level = assets.Load<ILevelData>("levels/1.txt");
```

To support in-place hot reload, also implement `IReloadableAssetLoader.TryReload(asset, path)` and return `true` when
you updated the existing object; return `false` to have the manager load a new instance instead.

## See also

- [Storage](/Ion/concepts/storage/): the storage roots behind asset paths
- [Sprites](/Ion/rendering/sprites/) and [Text](/Ion/rendering/text/)
- [Models and glTF](/Ion/rendering/3d/models-gltf/)
- [Scenes](/Ion/ecs/scenes/): scene-scoped asset lifetimes
- [Events](/Ion/concepts/events/)
