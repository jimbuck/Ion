using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

using Xunit.Abstractions;

namespace Ion.Generators.Tests;

/// <summary>
/// The generated event bus: its golden output, the same behaviour as the runtime bus for the same emits and reads, the
/// runtime fallback for types the generator cannot see, library summaries, and the event diagnostics ION101 to ION106.
/// </summary>
public class EventGeneratorTests(ITestOutputHelper output)
{
	[Fact]
	public void GeneratesTheGoldenEventBus()
	{
		var result = GeneratorHarness.Run(EventScenarios.Game, "EventsGame");

		Assert.Empty(result.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning));
		result.AssertCompiles();
		Assert.Empty(result.Output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Warning));
		GeneratorHarness.AssertGolden("Events", result.GeneratedEvents);

		// Scored is emitted in Update and read in First: one frame of latency, reported as information.
		var latency = Assert.Single(result.Diagnostics, d => d.Id == "ION105");
		Assert.Equal(DiagnosticSeverity.Info, latency.Severity);
		Assert.Contains("'Scored'", latency.GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
	}

	[Fact]
	public void TheGeneratedBusBehavesLikeTheRuntimeBus()
	{
		var generated = Run(GeneratorHarness.Run(EventScenarios.Game, "GeneratedEvents").Output);
		var runtime = Run(GeneratorHarness.Compile(EventScenarios.Game, "RuntimeEvents"));

		output.WriteLine(generated.Log);

		Assert.EndsWith("GeneratedEventBus", generated.Bus, StringComparison.Ordinal);
		Assert.True(generated.HitIdIsGenerated);
		Assert.Equal("EventBus", runtime.Bus);
		Assert.False(runtime.HitIdIsGenerated);

		// The same emits and reads give the same results, frame by frame.
		Assert.Equal(runtime.Log, generated.Log);

		// Every hit was seen once by the fixed-step reader and once by the scorer, including those of frames without a
		// fixed step; the plugin type fell back to a runtime channel on the generated bus.
		var lines = generated.Log.Split('\n');
		var hits = lines.Where(l => l.StartsWith("hit ", StringComparison.Ordinal)).Select(l => l.Split(' ')[1]).ToList();
		var fixedHits = lines.Where(l => l.StartsWith("fixed saw ", StringComparison.Ordinal)).Select(l => l.Split(' ')[2]).ToList();
		Assert.NotEmpty(hits);
		Assert.Equal(hits.Distinct(), hits);
		Assert.Equal(hits.Count, hits.Distinct().Count());
		Assert.True(fixedHits.Count >= hits.Count - 3, $"{fixedHits.Count} fixed reads for {hits.Count} hits");
		Assert.Equal(fixedHits.Distinct(), fixedHits);
		Assert.Contains("plugin 0", lines);
		Assert.Contains("plugin 9", lines);
		Assert.Contains("plugin channel runtime id True", lines);
	}

	[Fact]
	public void LibrarySummariesPutLibraryEventsOnTheApplicationBus()
	{
		var library = GeneratorHarness.Run("""
			using Ion;

			namespace Plugin;

			public record struct PluginScored(int Points);
			internal record struct PluginInternal(int Value);

			public sealed class PluginSystem(IEvents events)
			{
				private EventReader<PluginInternal> _internal = events.Reader<PluginInternal>();

				[Update]
				public void Tick(GameTime dt)
				{
					for (var i = 0; i < 4; i++) events.Emit(new PluginScored(i));
					events.Emit(new PluginInternal(1));
					_internal.Read();
				}
			}
			""", "EventPlugin");
		library.AssertCompiles();
		Assert.Empty(library.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning));
		GeneratorHarness.AssertGolden("EventsLibrary", library.GeneratedEvents);

		using var stream = new MemoryStream();
		Assert.True(library.Output.Emit(stream).Success);
		var reference = MetadataReference.CreateFromImage(stream.ToArray());

		var app = GeneratorHarness.Run("using Plugin;\n" + EventScenarios.Preamble + """
			public sealed class Hud(IEvents events)
			{
				private EventReader<PluginScored> _scored = events.Reader<PluginScored>();

				[Render] public void Draw(GameTime dt) { foreach (var s in _scored.Read()) Log.Calls.Add("scored " + s.Points); }
			}

			public static class App
			{
				public static void Run() => IonApplication.CreateBuilder().Build();
			}
			""", "EventPluginApp", [reference]);
		app.AssertCompiles();

		// Read here, emitted by the library: no ION102. The library's public type is on the bus (a per-frame emit in a loop
		// gets the largest channel); its internal type is not (it stays on the runtime path).
		Assert.Empty(app.Diagnostics.Where(d => d.Severity >= DiagnosticSeverity.Warning));
		Assert.Contains("global::Ion.EventChannel<global::Plugin.PluginScored>", app.GeneratedEvents, StringComparison.Ordinal);
		Assert.Contains("Register<global::Plugin.PluginScored>(256)", app.GeneratedEvents, StringComparison.Ordinal);
		Assert.DoesNotContain("PluginInternal", app.GeneratedEvents, StringComparison.Ordinal);
	}

	[Fact]
	public void ALibraryGetsASummaryButNoBus()
	{
		var library = GeneratorHarness.Run("""
			using Ion;

			public record struct Ping;

			public sealed class Pinger(IEvents events)
			{
				[Update] public void Tick(GameTime dt) => events.Emit(new Ping());
			}
			""", "EventLibraryOnly");

		// Emitted but never read is only reported for applications (the game may read it).
		Assert.Empty(library.Diagnostics);
		Assert.Contains("EventUsageAttribute(typeof(global::Ping)", library.GeneratedEvents, StringComparison.Ordinal);
		Assert.DoesNotContain("GeneratedEventBus", library.GeneratedEvents, StringComparison.Ordinal);
	}

	private static (string Log, string Bus, bool HitIdIsGenerated) Run(Compilation compilation)
	{
		var assembly = GeneratorHarness.Load(compilation);
		try
		{
			return ((string, string, bool))assembly.GetType("App")!.GetMethod("Run")!.Invoke(null, null)!;
		}
		catch (TargetInvocationException ex) when (ex.InnerException is not null)
		{
			throw ex.InnerException;
		}
	}

	#region Diagnostics

	private const string Usings = """
		using Ion;

		""";

	private const string Application = """

		public static class App
		{
			public static void Run() => IonApplication.CreateBuilder().Build();
		}
		""";

	public static TheoryData<string> DiagnosticScenarios => [.. Scenarios.Keys];

	private static readonly Dictionary<string, (string Code, string Source)> Scenarios = new()
	{
		["ION101 emitted but never read"] = ("ION101", Usings + """
			public record struct Unheard(int Value);

			public sealed class Shouter(IEvents events)
			{
				[Update] public void Shout(GameTime dt) => {|#0:events.Emit(new Unheard(1))|};
				[Render] public void Again(GameTime dt) => {|#1:events.Emit<Unheard>()|};
			}
			""" + Application),

		["ION102 read but never emitted"] = ("ION102", Usings + """
			public record struct Silent;

			public sealed class Waiter(IEvents events)
			{
				private EventReader<Silent> _silent = {|#0:events.Reader<Silent>()|};

				[Update] public void Tick(GameTime dt) => _silent.TryRead(out _);
			}
			""" + Application),

		["ION103 reader created in a stage method"] = ("ION103", Usings + """
			public record struct Ping;

			public sealed class Forgetful(IEvents events)
			{
				[Update]
				public void Tick(GameTime dt)
				{
					var reader = {|#0:events.Reader<Ping>()|};
					if (reader.Any()) events.Emit(new Ping());
				}

				// Once, in Init: fine.
				[Init] public void Init(GameTime dt) => events.Reader<Ping>().Skip();
			}
			"""),

		["ION104 payload is not unmanaged"] = ("ION104", Usings + """
			public record struct Named(string Name);
			public sealed class Holder;

			public static class Raiser
			{
				[EmitsEvent(typeof(Holder))]
				public static void RaiseHolder(IEvents events) { }

				public static void Run(IEvents events)
				{
					{|#0:events.Emit(new Named("x"))|};
					{|#1:RaiseHolder(events)|};
				}
			}
			"""),

		["ION105 read before it is emitted in the frame"] = ("ION105", Usings + """
			public record struct Late;

			public sealed class Early(IEvents events)
			{
				private EventReader<Late> _late = events.Reader<Late>();

				[First] public void Check(GameTime dt) { if ({|#0:_late.Any()|}) _late.Skip(); }
				[Last] public void After(GameTime dt) => _late.Skip();
			}

			public sealed class Sender(IEvents events)
			{
				[Render] public void Send(GameTime dt) => events.Emit(new Late());
			}
			"""),

		["ION106 reader in a readonly field or a property"] = ("ION106", Usings + """
			public record struct Ping;

			public sealed class Frozen(IEvents events)
			{
				private readonly EventReader<Ping> {|#0:_pings|} = events.Reader<Ping>();

				public EventReader<Ping> {|#1:Pings|} => _pings;

				[Update]
				public void Tick(GameTime dt)
				{
					events.Emit(new Ping());
					_pings.TryRead(out _);
				}
			}
			"""),
	};

	[Theory]
	[MemberData(nameof(DiagnosticScenarios))]
	public async Task ReportsTheEventDiagnosticAtTheMarkedLocations(string scenario)
	{
		var (code, source) = Scenarios[scenario];
		var descriptor = code switch
		{
			"ION101" => Diagnostics.EventNeverRead,
			"ION102" => Diagnostics.EventNeverEmitted,
			"ION103" => Diagnostics.ReaderInStage,
			"ION104" => Diagnostics.NotUnmanaged,
			"ION105" => Diagnostics.ReadBeforeEmit,
			_ => Diagnostics.ReadonlyReader,
		};

		var markers = System.Text.RegularExpressions.Regex.Matches(source, @"\{\|#(\d+):").Count;
		var test = new EventGeneratorTest { TestCode = source };
		for (var i = 0; i < markers; i++) test.ExpectedDiagnostics.Add(new DiagnosticResult(descriptor.Id, descriptor.DefaultSeverity).WithLocation(i));

		// The payload scenario is also a compiler error (CS8377), which is not what this test is about.
		if (code == "ION104") test.CompilerDiagnostics = CompilerDiagnostics.None;

		await test.RunAsync();
	}

	[Fact]
	public void DiagnosticMessagesSayWhatToDo()
	{
		string Message(string code)
		{
			var source = System.Text.RegularExpressions.Regex.Replace(Scenarios.Values.Single(s => s.Code == code).Source, @"\{\|#\d+:(.*?)\|\}", "$1");
			var result = GeneratorHarness.Run(source, "Messages" + code);
			return result.Diagnostics.First(d => d.Id == code).GetMessage(System.Globalization.CultureInfo.InvariantCulture);
		}

		Assert.Equal("'Unheard' is emitted here but never read: nothing in this application or the Ion assemblies it references reads it. Read it (a reader from IEvents.Reader<Unheard>() created in a constructor), or stop emitting it.", Message("ION101"));
		Assert.Equal("'Silent' is read here but never emitted: nothing in this application or the Ion assemblies it references emits it, so this reader never has events. Emit it, or remove the reader.", Message("ION102"));
		Assert.Equal("Forgetful.Tick creates a reader of 'Ping' every time it runs (Update). A new reader starts at the oldest visible event, so it sees the previous frame's events again. Create the reader once, in the constructor or a field initializer, and keep it in a field that is not readonly.", Message("ION103"));
		Assert.Equal("'Named' cannot be an event: events are stored unboxed in typed channels, so the payload must be an unmanaged struct, and its field 'Name' is a 'string' (a reference). Use ids, indices or handles instead of references.", Message("ION104"));
		Assert.Equal("Early.Check reads 'Late' in First, but it is only emitted in Render (Sender.Send), later in the frame, so it sees each event one frame after it was emitted. Read it in a later stage, or emit it earlier, if that latency is not intended.", Message("ION105"));
		Assert.Equal("'_pings' is a readonly EventReader<Ping>: reading through it advances a copy of the reader, so it returns the same events every time. Remove 'readonly'.", Message("ION106"));
	}

	/// <summary>The Roslyn SDK test, with the engine assemblies referenced and interceptors enabled.</summary>
	private sealed class EventGeneratorTest : CSharpSourceGeneratorTest<ScheduleGenerator, DefaultVerifier>
	{
		public EventGeneratorTest()
		{
			ReferenceAssemblies = new ReferenceAssemblies("net10.0");
			TestState.AdditionalReferences.AddRange(GeneratorHarness.References);
			TestBehaviors |= TestBehaviors.SkipGeneratedSourcesCheck;
		}

		protected override ParseOptions CreateParseOptions() =>
			((CSharpParseOptions)base.CreateParseOptions()).WithLanguageVersion(LanguageVersion.Latest).WithFeatures([new("InterceptorsNamespaces", "Ion.Generated")]);

		protected override CompilationOptions CreateCompilationOptions() =>
			((CSharpCompilationOptions)base.CreateCompilationOptions()).WithNullableContextOptions(NullableContextOptions.Enable);
	}

	#endregion
}
