using Microsoft.Extensions.DependencyInjection;

using Ion;
using Ion.Examples.Breakout.Net;
using Ion.Extensions.Ecs;
using Ion.Extensions.Ecs.Rendering;
using Ion.Extensions.Graphics;
using Ion.Extensions.Networking;
using Ion.Extensions.Networking.LiteNetLib;
using Ion.Extensions.Physics2D;
using Ion.Extensions.Scenes;

// Breakout over the network: the ECS module, 2D physics, the networking module on the LiteNetLib (UDP) transport, and the
// game's systems (Systems.cs). The components, messages and field are in Components.cs.
//   dotnet run                                                     a listen server: the server and the first player
//   dotnet run -- --Ion:Headless=true --Ion:Network:Mode=Server    a dedicated server (no window, no GPU)
//   dotnet run -- --Ion:Network:Mode=Client                        a client of the server at Ion:Network:Connect
// Add --Ion:Network:JoinSecret=<secret> on both sides to require a secret. Headless, an autopilot plays.
//
// The game goes menu, level, menu, level...: the root world keeps the score and the stage (both replicated), and each
// level is a scene whose world is networked as "level", so a client gets the level only while its own level scene is
// loaded. The server decides the stage; every peer follows it with its own scene change.
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
	.AddSystem<FlowSystem>()
	.AddSystem<HudSystem>()
	.AddSystem<PlayerInputSystem>()
	.AddSystem<ServerGameSystem>()
	.AddSystem<PresentationSystem>();
builder.Services.AddSingleton<PaddleInputSource>();
builder.Services.AddSingleton(BreakoutSettings.From(builder.Configuration));
if (headless) builder.AddSystem<NetAutopilotSystem>();

using var game = builder.Build();
game.UseIon()
	.UseNetworking()
	.UseSystem<PaddlePredictionSystem>()
	.UseSystem<FlowSystem>()
	.UseSystem<HudSystem>()
	.UseSystem<PlayerInputSystem>();
if (headless) game.UseSystem<NetAutopilotSystem>();

// The menu has nothing networked of its own (the stage and the score are in the root world).
game.UseScene(GameScene.Menu, scene => scene.UseEcs());

// The level: its world is networked as "level", with its own physics and sprites.
game.UseScene(GameScene.Level, scene => scene
	.UseNetworking("level")
	.UsePhysics2D()
	.UseEcsRendering()
	.UseSystem<ServerGameSystem>()
	.UseSystem<PresentationSystem>());
game.Run();
