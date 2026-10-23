namespace Ion;

/// <summary>
/// Optional storage overrides, bound from the <c>Ion:Storage</c> configuration section.
/// Relative paths are resolved against the content root: the folder of the game's executable
/// (<see cref="AppContext.BaseDirectory"/>), unless <c>--contentRoot</c> (or <c>DOTNET_CONTENTROOT</c>) sets another.
/// </summary>
public class StorageConfig
{
	/// <summary>
	/// Root of the read-only game content. Defaults to the content root (the folder of the game's executable, where
	/// <c>appsettings.json</c> is read from).
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
