namespace Ion.Examples.Breakout.ECS;

/// <summary>
/// The entry point of the phone and tablet heads (<c>Ion.Examples.Breakout.ECS.Android</c> and <c>.iOS</c>): the same
/// game as <c>Program.cs</c>, on the SDL windowing platform (a full-screen view), with the graphics backend picked by
/// <see cref="Ion.Extensions.Graphics.GraphicsBackend.Auto"/> (Vulkan, then OpenGL ES), and touch driving the paddle.
/// Compiled into the desktop sample too, so the tests run it headless.
/// </summary>
public static class BreakoutMobile
{
	/// <summary>
	/// The configuration of a mobile run: the game's content under <paramref name="contentRoot"/> (where the head put the
	/// Assets folder), SDL, full screen, no cursor; <paramref name="extra"/> is appended (later values win).
	/// </summary>
	public static string[] Arguments(string contentRoot, IReadOnlyList<string> extra)
	{
		ArgumentNullException.ThrowIfNull(contentRoot);
		ArgumentNullException.ThrowIfNull(extra);
		return
		[
			$"--Ion:Storage:GamePath={contentRoot}",
			"--Ion:Title=Ion Breakout",
			"--Ion:Window:Platform=Sdl",
			"--Ion:Window:Fullscreen=true",
			"--Ion:Window:ShowCursor=false",
			.. extra,
		];
	}

	/// <summary>
	/// Builds and runs the game until it exits (or for <c>Ion:Run:Frames</c> frames when configured). Called on the thread
	/// SDL runs the app's main function on.
	/// </summary>
	public static void Run(string contentRoot, IReadOnlyList<string> extra)
	{
		var builder = IonApplication.CreateBuilder(Arguments(contentRoot, extra));
		BreakoutGame.Configure(builder);

		using var game = builder.Build();
		BreakoutGame.Use(game);

		game.Run();
	}
}
