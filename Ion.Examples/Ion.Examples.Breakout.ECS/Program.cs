using Ion;
using Ion.Examples.Breakout.ECS;

// The game is a module (AddBreakout/UseBreakout in BreakoutGame.cs, its systems in BreakoutSystems.cs) because the mobile
// heads (Ion.Examples.Breakout.ECS.Android and .iOS) run it from their own entry point; the tests run this one.
// Run with --Ion:Headless=true to use the headless graphics and audio backends (no GPU, window or audio device), and
// --Ion:Seed=<n> to change the random seed.
var builder = IonApplication.CreateBuilder(args);
builder.AddBreakout();

using var game = builder.Build();
game.UseBreakout();

#if TRACY
// Built with -p:IonTracy=true: stream every span, frame mark and counter to a Tracy server.
Ion.Extensions.Metrics.Tracy.TracyExtensions.UseMetricsTracy(game);
#endif

// --Ion:Run:Frames=<n> runs n frames and exits (the publish size and startup measurement runs one headless frame).
game.Run();
