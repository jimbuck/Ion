﻿using Microsoft.Extensions.Options;

namespace Kyber;

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

	public PersistentStorage(IOptions<GameConfig> config)
	{
		_game = new PersistentStorageProvider(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), config.Value.Title);
		_assets = _game.Subpath("Assets");

		_user = new PersistentStorageProvider(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), config.Value.Title);
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
