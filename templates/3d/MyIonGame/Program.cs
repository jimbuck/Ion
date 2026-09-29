using Microsoft.Extensions.DependencyInjection;

using Ion;
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
builder.AddIon()
	.AddRendering3D()
	.AddSystem<SceneSystem3D>();
builder.Services.AddSingleton(GameSettings.From(builder.Configuration));
builder.Services.AddSingleton<SpinState>();

// Readable and writable on a running game (--remote-allow-mutations): resources.get/set "Game.Spin".
builder.Services.AddRemoteResource("Game.Spin", "The cubes' spin angle and speed.", GameJson.Default.SpinState,
	static sp => sp.GetRequiredService<SpinState>(),
	static (sp, value) => sp.GetRequiredService<SpinState>().CopyFrom(value));

using var game = builder.Build();
game.UseIon()
	.UseRendering3D()
	.UseSystem<SceneSystem3D>();
game.Run();
