namespace Kyber.Storage;

public interface IPersistentStorage
{
	IPersistentStorageProvider Game { get; }
	IPersistentStorageProvider Assets { get; }

	IPersistentStorageProvider User { get; }
	IPersistentStorageProvider Saves { get; }
}

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

	public PersistentStorage(IGameConfig config)
	{
		_game = new PersistentStorageProvider(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), config.Title);
		_assets = _game.Subpath("Assets");

		_user = new PersistentStorageProvider(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), config.Title);
		_saves = _user.Subpath("Saves");
	}

	public void Initialize()
	{
		_game.Initialize();
		_assets.Initialize();
		_user.Initialize();
		_saves.Initialize();
	}
}
