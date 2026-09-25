namespace Ion;

/// <summary>
/// Optional storage overrides, bound from the <c>Ion:Storage</c> configuration section.
/// Relative paths are resolved against <see cref="AppContext.BaseDirectory"/>.
/// </summary>
public class StorageConfig
{
	/// <summary>
	/// Root of the read-only game content. Defaults to <see cref="AppContext.BaseDirectory"/> (the folder of the game's executable).
	/// </summary>
	public string? GamePath { get; set; }

	/// <summary>
	/// Folder that assets are loaded from. Defaults to <c>Assets</c> under <see cref="GamePath"/>.
	/// Relative values are resolved against <see cref="GamePath"/>.
	/// </summary>
	public string? AssetsPath { get; set; }

	/// <summary>
	/// Folder for per-user game data (settings, saves). Defaults to a per-game folder named after
	/// <see cref="GameConfig.Title"/> under the user's local application data folder.
	/// </summary>
	public string? UserPath { get; set; }
}
