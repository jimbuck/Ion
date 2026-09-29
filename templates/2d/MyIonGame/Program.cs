using Microsoft.Extensions.DependencyInjection;

using Ion;
using Ion.Extensions.Graphics;
using Ion.Extensions.Remote;

using MyIonGame;

// The game's setup: the modules it uses and its systems, registered on builder (services) and added to game (the
// schedule). The tests run this file as it is (IonTestHost.RunEntryPoint<Program>(frames)), and the Ion schedule generator
// compiles the schedule it builds into direct calls: keep the registrations in this file (or in methods it calls), before
// builder.Build() and game.Run().
//
//   dotnet run                                   windowed
//   dotnet run -- --headless                     no window, GPU or audio device
//   ion run --headless --frames 600 --seed 1 --screenshot out/frame.png --summary out/run.json
//   dotnet run -- --remote-allow-mutations       inspect and change the running game (ion mcp, ion remote)
var builder = IonApplication.CreateBuilder(args);
builder.AddIon(graphics => graphics.ClearColor = new Color(0x1B, 0x26, 0x3B))
	.AddSystem<PaddleSystem>();
builder.Services.AddSingleton(GameSettings.From(builder.Configuration));
builder.Services.AddSingleton<PlayState>();

// The state is a remote resource: resources.get/set "Game.State" read and write it on a running game
// (--remote-allow-mutations), and the snapshot test serializes it with the same metadata.
builder.Services.AddRemoteResource("Game.State", "Paddle, ball, score and misses.", GameJson.Default.PlayState,
	static sp => sp.GetRequiredService<PlayState>(),
	static (sp, value) => sp.GetRequiredService<PlayState>().CopyFrom(value));

using var game = builder.Build();
game.UseIon()
	.UseSystem<PaddleSystem>();
game.Run();
