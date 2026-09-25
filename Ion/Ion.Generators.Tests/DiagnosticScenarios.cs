namespace Ion.Generators.Tests;

/// <summary>
/// One source per schedule diagnostic. Each has an application whose registrations trigger it, in two entry points:
/// <c>App.Run()</c> builds the game (the generator reports at compile time), and <c>App.Plan()</c> plans the same
/// registrations at run time and returns the runtime's diagnostics, so the two can be compared.
/// </summary>
internal static class DiagnosticScenarios
{
	public sealed record Scenario(string Id, string Code, string Systems, string Services, string Registrations)
	{
		/// <summary>The source the generator runs on (with location markup): the systems and <c>App.Run()</c>.</summary>
		public string Source => Preamble + Systems + "\n" + RunApp(Services, Registrations);

		/// <summary>The source the runtime plans (compiled without the generator): the systems and <c>App.Plan()</c>.</summary>
		public string RuntimeSource => Plain(Preamble + Systems + "\n" + PlanApp(Services, Registrations));

		public override string ToString() => Id;
	}

	private const string Preamble = """
		using System.Threading.Tasks;
		using Ion;
		using Microsoft.Extensions.DependencyInjection;

		""";

	private static string RunApp(string services, string registrations) => $$"""
		public static class App
		{
			public static void Run()
			{
				var builder = IonApplication.CreateBuilder();
				{{services}}
				var app = builder.Build();
				{{registrations}}
				app.Build();
			}
		}
		""";

	private static string PlanApp(string services, string registrations) => $$"""
		public static class App
		{
			public static object Plan()
			{
				var builder = IonApplication.CreateBuilder();
				{{services}}
				var app = builder.Build();
				{{registrations}}
				try
				{
					return app.Schedule.Plan(app.Services).Diagnostics;
				}
				catch (IonScheduleException ex)
				{
					return ex.Diagnostics;
				}
			}
		}
		""";

	public static readonly Scenario[] All =
	[
		new("UnknownStage", "ION001", """
			public sealed class Frame
			{
				[Begin((Stage)42)] public void {|#0:Open|}(GameTime dt) { }
				[End((Stage)42)] public void {|#1:Close|}(GameTime dt) { }
				[Update] public void Tick(GameTime dt) { }
			}

			""", "builder.Services.AddSingleton<Frame>();", "app.UseSystem<Frame>();"),

		new("OrderingCycle", "ION002", """
			public sealed class A { [Update, After<B>] public void Tick(GameTime dt) { } }
			public sealed class B { [Update, After<A>] public void Tick(GameTime dt) { } }

			""", "builder.Services.AddSingleton<A>().AddSingleton<B>();", "{|#0:app.UseSystem<A>().UseSystem<B>()|};"),

		new("UnpairedScope", "ION003", """
			public sealed class Frame
			{
				[Begin(Stage.Render)] public void {|#0:Open|}(GameTime dt) { }
				[Update] public void Tick(GameTime dt) { }
			}

			""", "builder.Services.AddSingleton<Frame>();", "app.UseSystem<Frame>();"),

		new("UnreachableStep", "ION004", """
			public sealed class Hidden
			{
				[Update] private void {|#0:Tick|}(GameTime dt) { }
				[Render] public void Draw(GameTime dt) { }
			}

			""", "builder.Services.AddSingleton<Hidden>();", "app.UseSystem<Hidden>();"),

		new("AsyncStep", "ION005", """
			public sealed class Loader
			{
				[Init] public async Task {|#0:Load|}(GameTime dt) => await Task.Yield();
				[Update] public void Tick(GameTime dt) { }
			}

			""", "builder.Services.AddSingleton<Loader>();", "app.UseSystem<Loader>();"),

		new("ScopedServiceInRoot", "ION006", """
			public sealed class Scoped { [Update] public void Tick(GameTime dt) { } }

			""", "builder.Services.AddScoped<Scoped>();", "{|#0:app.UseSystem<Scoped>()|};"),

		new("InvalidSignature", "ION007", """
			public sealed class Counter
			{
				[Update] public int {|#0:Tick|}(GameTime dt) => 0;
				[Render] public void {|#1:Draw|}(GameTime dt, GameTime again) { }
				[Last] public void Valid(GameTime dt) { }
			}

			""", "builder.Services.AddSingleton<Counter>();", "app.UseSystem<Counter>();"),

		new("UnresolvableParameter", "ION008", """
			public sealed class Missing { }
			public sealed class Needy { [Update] public void Tick(GameTime dt, Missing missing) { } }

			""", "builder.Services.AddSingleton<Needy>();", "{|#0:app.UseSystem<Needy>()|};"),

		new("UnregisteredSystem", "ION009", """
			public sealed class Lonely { [Update] public void Tick(GameTime dt) { } }

			""", "", "{|#0:app.UseSystem<Lonely>()|};"),

		new("LegacyMiddleware", "ION010", """
			public sealed class Legacy
			{
				[Update] public void {|#0:Wrap|}(GameTime dt, GameLoopDelegate next) => next(dt);
			}

			""", "builder.Services.AddSingleton<Legacy>();", "app.UseSystem<Legacy>();"),

		new("AmbiguousScope", "ION011", """
			public sealed class Frame
			{
				[Begin(Stage.Render)] public void {|#0:OpenA|}(GameTime dt) { }
				[Begin(Stage.Render)] public void OpenB(GameTime dt) { }
				[End(Stage.Render)] public void Close(GameTime dt) { }
				[Update] public void Tick(GameTime dt) { }
			}

			""", "builder.Services.AddSingleton<Frame>();", "app.UseSystem<Frame>();"),

		new("UnmatchedConstraint", "ION012", """
			public sealed class Elsewhere { }
			public sealed class Picky { [Update, After<Elsewhere>] public void Tick(GameTime dt) { } }

			""", "builder.Services.AddSingleton<Picky>();", "{|#0:app.UseSystem<Picky>()|};"),

		new("SystemWithoutSteps", "ION013", """
			public sealed class {|#0:Idle|} { public void Tick(GameTime dt) { } }

			""", "builder.Services.AddSingleton<Idle>();", "app.UseSystem<Idle>();"),
	];

	/// <summary>Removes the <c>{|#n:...|}</c> location markup.</summary>
	public static string Plain(string source) => System.Text.RegularExpressions.Regex.Replace(source, @"\{\|#\d+:(.*?)\|\}", "$1");
}
