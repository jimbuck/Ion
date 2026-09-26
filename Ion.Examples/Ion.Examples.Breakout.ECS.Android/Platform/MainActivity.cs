using Android.App;
using Android.Content.PM;
using Android.Content.Res;

using Silk.NET.Windowing.Sdl.Android;

namespace Ion.Examples.Breakout.ECS.Android;

/// <summary>
/// The app's activity: Silk.NET's SDL activity (a full-screen SDL view, SDL's input and lifecycle), which calls
/// <see cref="OnRun"/> on SDL's main thread. Ion's loop runs there until the game exits.
/// </summary>
[Activity(Label = "Ion Breakout", MainLauncher = true, Theme = "@android:style/Theme.NoTitleBar.Fullscreen",
	ScreenOrientation = ScreenOrientation.SensorLandscape, LaunchMode = LaunchMode.SingleInstance,
	ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden | ConfigChanges.Keyboard | ConfigChanges.UiMode)]
public sealed class MainActivity : SilkActivity
{
	/// <inheritdoc/>
	protected override void OnRun()
	{
		// Ion loads content from files; APK assets are not files, so they are unpacked into the app's files folder first.
		var root = Path.Combine(FilesDir!.AbsolutePath, "game");
		Unpack(Assets!, Path.Combine(root, "Assets"));
		BreakoutMobile.Run(root, []);
	}

	/// <summary>Copies the APK's top-level assets (the game's Assets folder) into <paramref name="destination"/>.</summary>
	private static void Unpack(AssetManager assets, string destination)
	{
		Directory.CreateDirectory(destination);
		foreach (var name in assets.List("") ?? [])
		{
			// Only files: the APK root also lists folders the platform adds (images, webkit).
			if (!Path.HasExtension(name)) continue;
			using var source = assets.Open(name);
			using var target = File.Create(Path.Combine(destination, name));
			source.CopyTo(target);
		}
	}
}
