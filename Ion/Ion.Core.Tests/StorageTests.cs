using Microsoft.Extensions.Options;

namespace Ion.Tests;

public class StorageTests : IDisposable
{
	private readonly string _root;

	public StorageTests()
	{
		_root = Path.Combine(Path.GetTempPath(), "ion-storage-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_root);
	}

	public void Dispose()
	{
		try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
	}

	private static PersistentStorage Create(StorageConfig storage, string title = "My Game")
	{
		return new PersistentStorage(Options.Create(new GameConfig { Title = title }), Options.Create(storage));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void DefaultsRootGameAndAssetsAtBaseDirectory()
	{
		var storage = Create(new StorageConfig());

		Assert.Equal(Path.Combine(AppContext.BaseDirectory, "x.txt"), storage.Game.GetPath("x.txt"));
		Assert.Equal(Path.Combine(AppContext.BaseDirectory, "Assets", "a.png"), storage.Assets.GetPath("a.png"));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void DefaultUserFolderIsPerGame()
	{
		var storage = Create(new StorageConfig(), title: "Block:Breaker");

		var userPath = storage.User.GetPath("settings.json");
		Assert.Equal("Block_Breaker", Path.GetFileName(Path.GetDirectoryName(userPath)));
		Assert.Equal(Path.Combine(Path.GetDirectoryName(userPath)!, "Saves", "slot1.sav"), storage.Saves.GetPath("slot1.sav"));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void OverridesFromStorageConfig()
	{
		var storage = Create(new StorageConfig
		{
			GamePath = _root,
			AssetsPath = "Content",
			UserPath = Path.Combine(_root, "user"),
		});

		Assert.Equal(Path.Combine(_root, "Content", "a.png"), storage.Assets.GetPath("a.png"));
		Assert.Equal(Path.Combine(_root, "user", "Saves", "s"), storage.Saves.GetPath("s"));
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void StorageConfigIsBoundFromIonStorageSection()
	{
		var builder = IonApplication.CreateBuilder();
		builder.Configuration["Ion:Storage:AssetsPath"] = Path.Combine(_root, "BoundAssets");
		using var app = builder.Build();

		var storage = app.Services.GetRequiredService<IPersistentStorage>();

		Assert.Equal(Path.Combine(_root, "BoundAssets", "a.png"), storage.Assets.GetPath("a.png"));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void OpenWriteTruncatesExistingFile()
	{
		var storage = Create(new StorageConfig { UserPath = _root });

		storage.User.Write("a much longer original content", "file.bin");

		using (var writer = storage.User.OpenWrite("file.bin"))
		{
			writer.Write((byte)1);
			writer.Write((byte)2);
		}

		Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(Path.Combine(_root, "file.bin")));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void WritesCreateMissingDirectories()
	{
		var storage = Create(new StorageConfig { UserPath = Path.Combine(_root, "new-user") });

		storage.Saves.Write("hello", "slot1.txt");
		storage.Saves.Append(" world", "slot1.txt");

		using var reader = new StreamReader(storage.Saves.Read("slot1.txt"));
		Assert.Equal("hello world", reader.ReadToEnd());
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ReadMissingFileNamesPathAndCasing()
	{
		var storage = Create(new StorageConfig { GamePath = _root });
		Directory.CreateDirectory(Path.Combine(_root, "Assets"));
		File.WriteAllText(Path.Combine(_root, "Assets", "bonk.wav"), "");

		var ex = Assert.Throws<FileNotFoundException>(() => storage.Assets.Read("Bonk.wav"));

		Assert.Contains("'Bonk.wav'", ex.Message);
		Assert.Contains(Path.Combine(_root, "Assets", "Bonk.wav"), ex.Message);
		Assert.Contains("case-sensitive", ex.Message);
		Assert.Equal(Path.Combine(_root, "Assets", "Bonk.wav"), ex.FileName);

		if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
		{
			Assert.Contains("'bonk.wav'", ex.Message);
		}
	}
}
