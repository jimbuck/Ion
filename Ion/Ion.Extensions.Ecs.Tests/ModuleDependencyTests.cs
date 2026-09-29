using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Ion.Extensions.Ecs.Rendering;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering3D;
using Ion.Extensions.Scenes;


namespace Ion.Extensions.Ecs.Tests;

/// <summary>
/// The modules on the application builder register (and add to the schedule) what they depend on, once, whatever the
/// order of the calls: <c>AddEcsRendering3D</c> alone is a working 3D ECS game, and options passed to a module pulled in
/// by another one still apply. This project is compiled with the schedule generator, so the applications built here run
/// the generated schedule, which must match the reflection-bound runtime.
/// </summary>
public class ModuleDependencyTests
{
	private static readonly string[] HeadlessArgs = ["--Ion:Headless=true"];

	private static IonApplicationBuilder Builder()
	{
		var builder = IonApplication.CreateBuilder(HeadlessArgs);
		builder.Services.AddLogging(logging => logging.ClearProviders());
		return builder;
	}

	[Fact]
	public void TheExtractionAloneRegistersAndUsesEverythingItNeeds()
	{
		var builder = Builder();
		builder.AddEcsRendering3D();

		using var app = builder.Build();
		app.UseEcsRendering3D();
		var loop = app.Build();

		Assert.True(loop.Schedule!.IsGenerated, "The generated schedule did not run.");
		var systems = app.Schedule.Entries.OfType<SystemEntry>().Select(e => e.ImplementationType.Name).ToList();
		Assert.Contains("Scene3DExtractionSystem", systems);
		Assert.Contains("Rendering3DSystem", systems);
		Assert.Contains("EcsCommandsSystem", systems);
		Assert.Contains("NullWindowSystem", systems);
		Assert.Contains("EventSystem", systems);

		loop.Initialize();
		loop.Step();
		loop.Shutdown();
		Assert.Equal(0, app.Services.GetRequiredService<IRenderer3D>().LastFrameStatistics.Submitted);
	}

	[Fact]
	public void EveryModuleIsRegisteredAndAddedOnceWhateverTheOrder()
	{
		var listed = Builder();
		listed.AddIon().AddRendering3D().AddEcs().AddEcsRendering3D().AddEcsRendering();
		var reversed = Builder();
		reversed.AddEcsRendering().AddEcsRendering3D().AddEcs().AddRendering3D().AddIon().AddIon();

		foreach (var builder in new[] { listed, reversed })
		{
			// One engine: AddIon registers the scene system once.
			Assert.Single(builder.Services, d => d.ServiceType == typeof(SceneSystem));
			Assert.Single(builder.Services, d => d.ServiceType == typeof(Renderer3D));
			Assert.True(builder.Services.HasIonModule("Ion"));
		}

		using var first = listed.Build();
		first.UseIon().UseRendering3D().UseEcs().UseEcsRendering3D().UseEcsRendering();
		using var second = reversed.Build();
		second.UseEcsRendering().UseEcsRendering3D().UseEcs().UseRendering3D().UseIon().UseIon();

		var firstSystems = first.Schedule.Entries.OfType<SystemEntry>().Select(e => e.ImplementationType).ToList();
		var secondSystems = second.Schedule.Entries.OfType<SystemEntry>().Select(e => e.ImplementationType).ToList();
		Assert.Equal(firstSystems.Count, firstSystems.Distinct().Count());
		Assert.Equal(firstSystems.OrderBy(t => t.FullName), secondSystems.OrderBy(t => t.FullName));

		// Registration order only breaks ties between steps of equal order: both plans have the same steps at the same orders.
		static string[] Steps(IonApplication app) => [.. app.PrintSchedule().Split('\n').Select(l => l.Trim()).Order(StringComparer.Ordinal)];
		Assert.Equal(Steps(first), Steps(second));
		Assert.True(first.Build().Schedule!.IsGenerated);
		Assert.True(second.Build().Schedule!.IsGenerated);
	}

	[Fact]
	public void OptionsOfAModulePulledInByAnotherStillApply()
	{
		var before = Builder();
		before.AddRendering3D(o => o.Shadows = false).AddIon(g => g.ClearColor = Color.Red).AddEcsRendering3D(o => o.RequireVisible = true);
		var after = Builder();
		after.AddEcsRendering3D().AddRendering3D(o => o.Shadows = false).AddIon(g => g.ClearColor = Color.Red).AddEcsRendering3D(o => o.RequireVisible = true);

		foreach (var builder in new[] { before, after })
		{
			using var app = builder.Build();
			Assert.False(app.Services.GetRequiredService<IOptions<Rendering3DOptions>>().Value.Shadows);
			Assert.Equal(Color.Red, app.Services.GetRequiredService<IOptions<GraphicsConfig>>().Value.ClearColor);
			Assert.True(app.Services.GetRequiredService<Scene3DExtractionOptions>().RequireVisible);
		}
	}

	[Fact]
	public void TheGeneratedScheduleMatchesTheReflectionBoundRuntime()
	{
		// UseEcsRendering3D, UseEcsRendering and UseIon add systems already added: the generator drops the repeats like the
		// runtime does, so the generated schedule is used and plans exactly like reflection.
		var builder = Builder();
		builder.AddEcsRendering3D().AddEcsRendering().AddSystem<Spinner>();

		using var app = builder.Build();
		app.UseIon().UseEcsRendering3D().UseEcsRendering().UseRendering3D().UseSystem<Spinner>().UseEcs();

		Assert.All(app.Schedule.Entries, entry => Assert.NotNull(entry.Site));
		var reflection = new ScheduleModel(app.Schedule.Name, app.Schedule.IsRoot);
		foreach (var system in app.Schedule.Entries.OfType<SystemEntry>()) reflection.AddSystem(system.ServiceType, system.ImplementationType);
		Assert.Equal(reflection.Plan(app.Services).Print(), app.PrintSchedule());

		var loop = app.Build();
		Assert.True(loop.Schedule!.IsGenerated);
		loop.Initialize();
		loop.Step();
		loop.Shutdown();
		Assert.Equal(1, app.Services.GetRequiredService<Spinner>().Updates);
	}

	[Fact]
	public void AddSystemRegistersASingletonOnce()
	{
		var builder = Builder();
		builder.AddSystem<Spinner>().AddSystem(typeof(Spinner));

		Assert.Single(builder.Services, d => d.ServiceType == typeof(Spinner) && d.Lifetime == ServiceLifetime.Singleton);
	}

	/// <summary>A game system registered with <c>AddSystem</c>.</summary>
	public sealed class Spinner
	{
		public int Updates { get; private set; }

		[Update]
		public void Spin(GameTime dt) => Updates++;
	}
}
