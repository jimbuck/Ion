using System.Diagnostics.CodeAnalysis;

using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Ion.Tests;

/// <summary>
/// Storage whose providers are all rooted in one temporary folder.
/// </summary>
internal sealed class TempStorage(string root) : IPersistentStorage
{
	private readonly TempProvider _provider = new(root);

	private sealed class TempProvider(string root) : IPersistentStorageProvider
	{
		public string GetPath(params string[] path) => Path.Combine(root, Path.Combine(path));
		public Stream Read(params string[] path) => File.OpenRead(GetPath(path));
		public void CreateDirectory(params string[] path) => Directory.CreateDirectory(GetPath(path));
		public void Write(string text, params string[] path) => File.WriteAllText(GetPath(path), text);
		public void Write(byte[] bytes, params string[] path) => File.WriteAllBytes(GetPath(path), bytes);
		public BinaryWriter OpenWrite(params string[] path) => new(File.Create(GetPath(path)));
		public void Append(string text, params string[] path) => File.AppendAllText(GetPath(path), text);
		public IEnumerable<string> List(params string[] path) => Directory.EnumerateFileSystemEntries(GetPath(path));
		public void DeleteFile(params string[] path) => File.Delete(GetPath(path));
		public void DeleteDirectory(params string[] path) => Directory.Delete(GetPath(path), recursive: true);
	}

	public IPersistentStorageProvider Game => _provider;
	public IPersistentStorageProvider Assets => _provider;
	public IPersistentStorageProvider User => _provider;
	public IPersistentStorageProvider Saves => _provider;
}

internal sealed class RecordingEmitter : IEvents
{
	public List<object> Emitted { get; } = [];

	public void Emit<T>(in T e) where T : unmanaged => Emitted.Add(e);

	public EventReader<T> Reader<T>() where T : unmanaged => default;
}

/// <summary>
/// A loader that cannot reload in place: the asset manager swaps a new instance in.
/// </summary>
public sealed class TextAsset(string name, string text) : IAsset
{
	private static int _nextId;

	public nint Id { get; } = 10_000 + Interlocked.Increment(ref _nextId);
	public string Name { get; } = name;
	public string Text { get; } = text;
	public bool IsDisposed { get; private set; }

	public void Dispose() => IsDisposed = true;
}

internal sealed class TextLoader(IPersistentStorage storage) : IAssetLoader<TextAsset>
{
	public Type AssetType => typeof(TextAsset);

	public TextAsset Load(string path)
	{
		using var reader = new StreamReader(storage.Assets.Read(path));
		return new TextAsset(path, reader.ReadToEnd());
	}
}

public sealed class HotReloadTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "ion-assets-" + Guid.NewGuid().ToString("N"));
	private readonly TempStorage _storage;
	private readonly GlobalAssetManager _global;
	private readonly AssetWatcher _watcher;
	private readonly RecordingEmitter _events = new();
	private readonly AssetReloadSystem _system;

	public HotReloadTests()
	{
		Directory.CreateDirectory(_root);
		_storage = new TempStorage(_root);
		_global = new GlobalAssetManager(NullLogger<GlobalAssetManager>.Instance,
			[new NullTexture2DLoader(_storage), new NullFontLoader(_storage), new TextLoader(_storage)]);
		_watcher = new AssetWatcher(_root, enabled: false);
		_system = new AssetReloadSystem(_watcher, _global, _events);
	}

	public void Dispose()
	{
		_watcher.Dispose();
		try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
	}

	private void WritePng(string path, int width, int height)
	{
		var full = Path.Combine(_root, path);
		Directory.CreateDirectory(Path.GetDirectoryName(full)!);
		using var image = new Image<Rgba32>(width, height);
		image.SaveAsPng(full);
	}

	private void WriteText(string path, string text) => File.WriteAllText(Path.Combine(_root, path), text);

	private AssetReloadedEvent SingleEvent() => Assert.IsType<AssetReloadedEvent>(Assert.Single(_events.Emitted));

	[Fact, Trait(CATEGORY, UNIT)]
	public void DisabledWatcherDoesNotWatchButAcceptsManualReloads()
	{
		Assert.False(_watcher.IsEnabled);
		Assert.Equal(Path.GetFullPath(_root), _watcher.Root);
		Assert.Equal(0, _system.ReloadPending());
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TextureReloadsInPlaceAndEmitsAnEvent()
	{
		WritePng("tiles.png", 4, 4);
		var texture = (NullTexture2D)_global.Load<ITexture2D>("tiles.png");
		Assert.Equal(4u, texture.Width);

		WritePng("tiles.png", 16, 2);
		_watcher.Enqueue("tiles.png");
		Assert.Equal(1, _system.ReloadPending());

		Assert.Same(texture, _global.Load<ITexture2D>("tiles.png"));
		Assert.Equal(16u, texture.Width);
		Assert.Equal(2u, texture.Height);
		Assert.Equal(5u, texture.MipLevels);
		Assert.Equal(1, texture.ReloadCount);
		Assert.False(texture.IsDisposed);

		var e = SingleEvent();
		Assert.True(e.InPlace);
		Assert.Equal(texture.Id, e.AssetId);
		Assert.Equal("tiles.png", _global.Get<IAsset>(e.AssetId)?.Name);

		// The queue is drained: nothing more happens.
		Assert.Equal(0, _system.ReloadPending());
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void FontSetReloadsInPlace()
	{
		WriteText("font.ttf", "not really a font");
		var fontSet = (NullFontSet)_global.Load<IFontSet>("font.ttf");

		_watcher.Enqueue("font.ttf");
		Assert.Equal(1, _system.ReloadPending());

		Assert.Equal(1, fontSet.ReloadCount);
		Assert.True(SingleEvent().InPlace);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void NonReloadableAssetIsSwappedForANewInstance()
	{
		WriteText("dialog.txt", "hello");
		var first = _global.Load<TextAsset>("dialog.txt");

		WriteText("dialog.txt", "goodbye");
		_watcher.Enqueue("dialog.txt");
		Assert.Equal(1, _system.ReloadPending());

		var second = _global.Load<TextAsset>("dialog.txt");
		Assert.NotSame(first, second);
		Assert.Equal("goodbye", second.Text);
		Assert.False(first.IsDisposed); // holders may still use it

		var e = SingleEvent();
		Assert.False(e.InPlace);
		Assert.Equal(second.Id, e.AssetId);
		Assert.Equal(first.Id, e.PreviousAssetId);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void SceneScopedCachesAreReloadedToo()
	{
		WritePng("sub/hero.png", 2, 2);
		using var scope = new ScopedAssetManager(NullLogger<ScopedAssetManager>.Instance, _global);
		var texture = (NullTexture2D)scope.Load<ITexture2D>("sub/hero.png");

		WritePng("sub/hero.png", 8, 8);
		_watcher.Enqueue(@".\sub\hero.png");
		Assert.Equal(1, _system.ReloadPending());
		Assert.Equal(8u, texture.Width);

		scope.Dispose();
		_events.Emitted.Clear();
		_watcher.Enqueue("sub/hero.png");
		Assert.Equal(0, _system.ReloadPending());
		Assert.Empty(_events.Emitted);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AFailedReloadKeepsTheLoadedAsset()
	{
		WritePng("gone.png", 3, 3);
		var texture = (NullTexture2D)_global.Load<ITexture2D>("gone.png");

		File.Delete(Path.Combine(_root, "gone.png"));
		_watcher.Enqueue("gone.png");
		Assert.Equal(0, _system.ReloadPending());

		Assert.Same(texture, _global.Load<ITexture2D>("gone.png"));
		Assert.Equal(3u, texture.Width);
		Assert.Empty(_events.Emitted);

		File.WriteAllText(Path.Combine(_root, "gone.png"), "not an image");
		_watcher.Enqueue("gone.png");
		Assert.Equal(0, _system.ReloadPending());
		Assert.Equal(3u, texture.Width);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void UnrelatedChangesReloadNothing()
	{
		WritePng("a.png", 2, 2);
		_global.Load<ITexture2D>("a.png");

		_watcher.Enqueue("b.png");
		Assert.Equal(0, _system.ReloadPending());
		Assert.Empty(_events.Emitted);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void EnabledWatcherQueuesChangedFiles()
	{
		WritePng("watched.png", 2, 2);
		using var watcher = new AssetWatcher(_root, enabled: true) { SettleTime = TimeSpan.Zero };
		Assert.True(watcher.IsEnabled);

		WritePng("watched.png", 4, 4);

		// File system events arrive asynchronously: poll the queue, with a generous bound.
		var changed = new List<string>();
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (!changed.Contains("watched.png") && DateTime.UtcNow < deadline)
		{
			watcher.Drain(changed);
			Thread.Sleep(20);
		}

		Assert.Contains("watched.png", changed);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void HotReloadConfiguration()
	{
		static IConfiguration Config(string? value) =>
			new ConfigurationBuilder().AddInMemoryCollection([new(AssetWatcher.HotReloadKey, value)]).Build();

		Assert.True(AssetWatcher.IsHotReloadEnabled(Config("true")));
		Assert.False(AssetWatcher.IsHotReloadEnabled(Config("false")));
		Assert.Equal(AssetWatcher.HotReloadDefault, AssetWatcher.IsHotReloadEnabled(Config(null)));
		Assert.Equal(AssetWatcher.HotReloadDefault, AssetWatcher.IsHotReloadEnabled(null));
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	[SuppressMessage("Reliability", "CA2000", Justification = "Disposed by the application.")]
	public void ReloadSystemRunsInFirstAndEmitsOnTheEventBus()
	{
		WritePng("level.png", 2, 2);

		var builder = IonApplication.CreateBuilder();
		builder.Configuration[AssetWatcher.HotReloadKey] = "false";
		builder.Services.AddAssets();
		builder.Services.AddNullGraphics(builder.Configuration);
		builder.Services.AddSingleton<IPersistentStorage>(_storage);
		using var app = builder.Build();
		app.UseEvents();
		app.UseAssets();

		var seen = new List<AssetReloadedEvent>();
		var reloads = app.Services.GetRequiredService<IEvents>().Reader<AssetReloadedEvent>();
		app.Update(dt =>
		{
			while (reloads.TryRead(out var e)) seen.Add(e);
		});

		var assets = app.Services.GetRequiredService<GlobalAssetManager>();
		var texture = (NullTexture2D)assets.Load<ITexture2D>("level.png");
		var watcher = app.Services.GetRequiredService<IAssetWatcher>();
		Assert.False(watcher.IsEnabled);

		var loop = app.Build();
		var dt = new GameTime { Delta = 0.016f };
		loop.Step(dt);
		Assert.Empty(seen);

		WritePng("level.png", 6, 6);
		watcher.Enqueue("level.png");
		dt.Frame++;
		loop.Step(dt);

		Assert.Equal(6u, texture.Width);
		var reloaded = Assert.Single(seen);
		Assert.Equal(texture.Id, reloaded.AssetId);
	}
}
