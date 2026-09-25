using Microsoft.Extensions.Options;

namespace Ion;

internal class PersistentStorage : IPersistentStorage
{
	private readonly PersistentStorageProvider _game;
	private readonly PersistentStorageProvider _assets;
	private readonly PersistentStorageProvider _user;
	private readonly PersistentStorageProvider _saves;

	public IPersistentStorageProvider Game => _game;
	public IPersistentStorageProvider Assets => _assets;

	public IPersistentStorageProvider User => _user;
	public IPersistentStorageProvider Saves => _saves;

	public PersistentStorage(IOptions<GameConfig> config, IOptions<StorageConfig> storageConfig)
	{
		var storage = storageConfig.Value;

		var gameRoot = ResolveGameRoot(storage);
		_game = new PersistentStorageProvider(gameRoot);
		_assets = new PersistentStorageProvider(ResolveAssetsRoot(storage, gameRoot));
		_user = new PersistentStorageProvider(ResolveUserRoot(storage, config.Value.Title));
		_saves = _user.Subpath("Saves");
	}

	internal static string ResolveGameRoot(StorageConfig storage)
	{
		return string.IsNullOrWhiteSpace(storage.GamePath)
			? AppContext.BaseDirectory
			: Path.GetFullPath(storage.GamePath, AppContext.BaseDirectory);
	}

	internal static string ResolveAssetsRoot(StorageConfig storage, string gameRoot)
	{
		return string.IsNullOrWhiteSpace(storage.AssetsPath)
			? Path.Combine(gameRoot, "Assets")
			: Path.GetFullPath(storage.AssetsPath, gameRoot);
	}

	internal static string ResolveUserRoot(StorageConfig storage, string? title)
	{
		if (!string.IsNullOrWhiteSpace(storage.UserPath)) return Path.GetFullPath(storage.UserPath, AppContext.BaseDirectory);

		var gameFolder = SanitizeFolderName(title);
		var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);

		// Some environments (containers, service accounts without a home) have no local app data folder.
		if (string.IsNullOrEmpty(localAppData)) return Path.Combine(AppContext.BaseDirectory, "UserData", gameFolder);

		return Path.Combine(localAppData, gameFolder);
	}

	internal static string SanitizeFolderName(string? title)
	{
		if (string.IsNullOrWhiteSpace(title)) return "Ion";

		// Use the union of characters that are invalid on any supported OS so the folder name is the same everywhere.
		const string invalid = "<>:\"/\\|?*";
		var chars = title.Trim().ToCharArray();
		for (var i = 0; i < chars.Length; i++)
		{
			if (char.IsControl(chars[i]) || invalid.Contains(chars[i])) chars[i] = '_';
		}

		return new string(chars);
	}

	public void Initialize()
	{
		_game.Initialize();
		_assets.Initialize();
		_user.Initialize();
		_saves.Initialize();
	}
}
