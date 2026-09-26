using Ion;
using Ion.Examples.Breakout.ECS;

// The game setup lives in BreakoutGame and the game's systems in BreakoutSystems.cs, so the tests
// (Ion.Examples.Breakout.ECS.Tests) and the mobile heads (Ion.Examples.Breakout.ECS.Android and .iOS) build exactly the
// same game.
// Run with --Ion:Headless=true to use the headless graphics and audio backends (no GPU, window or audio device), and
// --Ion:Seed=<n> to change the random seed.
var builder = IonApplication.CreateBuilder(args);
BreakoutGame.Configure(builder);

using var game = builder.Build();
BreakoutGame.Use(game);

#if TRACY
// Built with -p:IonTracy=true: stream every span, frame mark and counter to a Tracy server.
Ion.Extensions.Metrics.Tracy.TracyExtensions.UseMetricsTracy(game);
#endif

// --Ion:Run:Frames=<n> runs n frames and exits (the publish size and startup measurement runs one headless frame).
game.Run();
