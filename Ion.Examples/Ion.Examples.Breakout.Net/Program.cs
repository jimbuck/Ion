using Ion;
using Ion.Examples.Breakout.Net;

// Breakout over the network. The game setup lives in BreakoutNetGame so the tests build exactly the same game.
//   dotnet run                                                     a listen server: the server and the first player
//   dotnet run -- --Ion:Headless=true --Ion:Network:Mode=Server    a dedicated server (no window, no GPU)
//   dotnet run -- --Ion:Network:Mode=Client                        a client of the server at Ion:Network:Connect
// Add --Ion:Network:JoinSecret=<secret> on both sides to require a secret.
var builder = IonApplication.CreateBuilder(args);
BreakoutNetGame.Configure(builder);

using var game = builder.Build();
BreakoutNetGame.Use(game);
game.Run();
