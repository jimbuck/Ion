using Microsoft.Extensions.DependencyInjection;

using Ion;
using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;
using Ion.Extensions.UI;

// Run with --Ion:Headless=true to use the headless backends (no GPU, window or audio device), and add
// --Ion:Headless:Render=true to render offscreen.
var builder = IonApplication.CreateBuilder(args);
MenuApp.Configure(builder);

var game = builder.Build();
MenuApp.Use(game);

game.Run();

/// <summary>The game setup, shared with the tests (Ion.Examples.Menu.Tests).</summary>
public static class MenuApp
{
	/// <summary>Registers the engine (<c>AddIon</c>), the UI module and the menu.</summary>
	public static IonApplicationBuilder Configure(IonApplicationBuilder builder)
	{
		builder.Services.AddIon(builder.Configuration, graphics => graphics.ClearColor = new Color(0x14, 0x17, 0x20, 0xFF));
		builder.Services.AddUi();
		builder.Services.AddSingleton<MenuSettings>();
		builder.Services.AddSingleton<MenuSystem>();
		return builder;
	}

	/// <summary>Adds the engine's systems (<c>UseIon</c>), the UI system and the menu.</summary>
	public static IIonApplication Use(IIonApplication game) => game.UseIon().UseUi().UseSystem<MenuSystem>();
}

/// <summary>The screens of the sample.</summary>
public enum MenuScreen
{
	/// <summary>Play, Options, Quit.</summary>
	Main,
	/// <summary>The settings.</summary>
	Options,
	/// <summary>A stand-in for the game.</summary>
	Play,
}

/// <summary>What the options screen edits.</summary>
public sealed class MenuSettings
{
	/// <summary>The difficulty names, in order.</summary>
	public static readonly string[] Difficulties = ["Easy", "Normal", "Hard"];

	/// <summary>Whether the window is fullscreen.</summary>
	public bool Fullscreen;

	/// <summary>The master volume, 0 to 1.</summary>
	public float Volume = 0.8f;

	/// <summary>The index into <see cref="Difficulties"/>.</summary>
	public int Difficulty = 1;

	/// <summary>The player's name.</summary>
	public string PlayerName = "Player";
}

/// <summary>
/// The menu: one Update step describes the current screen with the <see cref="Ui"/> every frame. Back (gamepad B, Escape)
/// returns to the main menu; the first widget of every screen has the focus, so the D-pad alone drives it.
/// </summary>
public sealed class MenuSystem(Ui ui, MenuSettings settings, IAssetManager assets, IEvents events, IWindow window)
{
	/// <summary>The screen shown.</summary>
	public MenuScreen Screen { get; private set; }

	/// <summary>Loads the font and sets the theme and the centered root.</summary>
	[Init]
	public void Load(GameTime dt)
	{
		var font = assets.Load<IFontSet>("Bungee-Regular.ttf").CreateStyle(20);
		ui.Theme = UiTheme.Default with { Font = font, LabelWidth = 140, InputWidth = 200 };
		ui.RootStyle = new UiStyle { Justify = UiJustify.Center, AlignItems = UiAlign.Center };
	}

	/// <summary>Describes the current screen.</summary>
	[Update]
	public void Build(GameTime dt)
	{
		switch (Screen)
		{
			case MenuScreen.Main: Main(); break;
			case MenuScreen.Options: Options(); break;
			default: Play(); break;
		}
	}

	private void Main()
	{
		using (ui.Panel("main", new UiStyle { Width = 360, Gap = 12 }))
		{
			ui.Label("Ion Menu", "title", scale: 1.6f);
			Span<char> greeting = stackalloc char[48];
			var length = Concat(greeting, "Welcome, ", settings.PlayerName);
			ui.Label(greeting[..length], "greeting", color: UiTheme.Default.TextDisabled);
			if (ui.Button("Play")) Screen = MenuScreen.Play;
			if (ui.Button("Options")) Screen = MenuScreen.Options;
			if (ui.Button("Quit")) events.Emit(new ExitGameEvent());
		}
	}

	private void Options()
	{
		using (ui.Panel("options", new UiStyle { Width = 560, Gap = 10 }))
		{
			ui.Label("Options", "title", scale: 1.6f);
			if (ui.Toggle("Fullscreen", ref settings.Fullscreen)) window.IsFullscreen = settings.Fullscreen;
			ui.Slider("Volume", ref settings.Volume, 0f, 1f, 0.05f);
			ui.List("Difficulty", MenuSettings.Difficulties, ref settings.Difficulty);

			ui.TextInput("Name", ref settings.PlayerName, maxLength: 16);
			if (ui.Button("Back") || ui.BackPressed) Screen = MenuScreen.Main;
		}
	}

	private void Play()
	{
		using (ui.Panel("play", new UiStyle { Width = 420, Gap = 12 }))
		{
			ui.Label("Playing", "title", scale: 1.6f);
			Span<char> line = stackalloc char[64];
			var length = Concat(line, settings.PlayerName, " on ");
			length += Concat(line[length..], MenuSettings.Difficulties[settings.Difficulty], "");
			ui.Label(line[..length], "status");
			if (ui.Button("Back") || ui.BackPressed) Screen = MenuScreen.Main;
		}
	}

	private static int Concat(Span<char> destination, ReadOnlySpan<char> a, ReadOnlySpan<char> b)
	{
		var n = Math.Min(a.Length, destination.Length);
		a[..n].CopyTo(destination);
		var m = Math.Min(b.Length, destination.Length - n);
		b[..m].CopyTo(destination[n..]);
		return n + m;
	}
}
