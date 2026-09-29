using Microsoft.Extensions.DependencyInjection;

using Ion;
using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;

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
	.AddEcs()
	// Components registered here are saved by the world serializers (world snapshots in tests) and are readable and
	// writable over the remote protocol (world.query, world.mutate_components). Register every component you add.
	.AddEcsSerialization(components => components
		.AddUnmanaged("Velocity", GameJson.Default.Velocity)
		.AddTag<Ball>("Ball"))
	.AddSystem<BallSystem>()
	.AddSystem<DrawSystem>();
builder.Services.AddSingleton(GameSettings.From(builder.Configuration));

using var game = builder.Build();
game.UseIon()
	.UseEcs()
	.UseSystem<BallSystem>()
	.UseSystem<DrawSystem>();
game.Run();
