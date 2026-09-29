using Microsoft.Extensions.DependencyInjection;

using Ion;
using Ion.Examples.Breakout.Net;
using Ion.Extensions.Ecs;
using Ion.Extensions.Ecs.Rendering;
using Ion.Extensions.Graphics;
using Ion.Extensions.Networking;
using Ion.Extensions.Networking.LiteNetLib;
using Ion.Extensions.Physics2D;

// Breakout over the network: the ECS module, 2D physics, the networking module on the LiteNetLib (UDP) transport, and the
// game's systems (Systems.cs). The components, messages and field are in Components.cs.
//   dotnet run                                                     a listen server: the server and the first player
//   dotnet run -- --Ion:Headless=true --Ion:Network:Mode=Server    a dedicated server (no window, no GPU)
//   dotnet run -- --Ion:Network:Mode=Client                        a client of the server at Ion:Network:Connect
// Add --Ion:Network:JoinSecret=<secret> on both sides to require a secret. Headless, an autopilot plays.
var builder = IonApplication.CreateBuilder(args);

// NativeAOT needs every stored component registered with Arch up front (the networking generator registers the
// replicated ones, AddPhysics2D the physics ones).
EcsComponents.Register<Wall>();

// Without a configured Ion:Network:Mode the game is a listen server.
var modeConfigured = builder.Configuration[$"{NetworkConfig.Section}:Mode"] is not null;
var headless = builder.Configuration.IsHeadless();

builder.AddIon(graphics => graphics.ClearColor = new Color(0x223))
	.AddEcsRendering()
	.AddPhysics2D(physics =>
	{
		physics.GravityY = 0;
		physics.UnitsPerMeter = Field.PixelsPerMeter;
	})
	.AddNetworking(network =>
	{
		if (!modeConfigured) network.Mode = NetworkMode.ListenServer;
		if (string.IsNullOrEmpty(network.GameId)) network.GameId = "ion-breakout-net";
	})
	.AddLiteNetLibTransport()
	.AddSystem<PaddlePredictionSystem>()
	.AddSystem<ServerGameSystem>()
	.AddSystem<PresentationSystem>()
	.AddSystem<PlayerInputSystem>();
builder.Services.AddSingleton<PaddleInputSource>();
if (headless) builder.AddSystem<NetAutopilotSystem>();

using var game = builder.Build();
game.UseIon()
	.UseEcsRendering()
	.UsePhysics2D()
	.UseNetworking()
	.UseSystem<PaddlePredictionSystem>()
	.UseSystem<ServerGameSystem>()
	.UseSystem<PresentationSystem>()
	.UseSystem<PlayerInputSystem>();
if (headless) game.UseSystem<NetAutopilotSystem>();
game.Run();
