using Ion;

using MyIonGame;

// The entry point stays this small: the setup lives in Game (Game.cs) so tests build the same game
// (IonTestHost.Run<Game>(frames)). Keep these four calls here, in this form: the Ion schedule generator reads them and
// compiles the schedule into direct calls.
//
//   dotnet run                                   windowed
//   dotnet run -- --headless                     no window, GPU or audio device
//   ion run --headless --frames 600 --seed 1 --screenshot out/frame.png --summary out/run.json
//   dotnet run -- --remote-allow-mutations       inspect and change the running game (ion mcp, ion remote)
var builder = IonApplication.CreateBuilder(args);
Game.Configure(builder);

using var app = builder.Build();
Game.Use(app);

app.Run();
