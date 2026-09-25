using Microsoft.CodeAnalysis;

namespace Ion.Generators;

/// <summary>
/// The Ion types the generator recognizes, resolved once per compilation. <see cref="TryCreate"/> fails when the
/// compilation does not reference <c>Ion.Core.Abstractions</c> (nothing to generate).
/// </summary>
internal sealed class KnownSymbols
{
	public static readonly string[] StageNames = ["Init", "First", "FixedUpdate", "Update", "Render", "Last", "Destroy"];

	private KnownSymbols(Compilation compilation)
	{
		Compilation = compilation;
	}

	public Compilation Compilation { get; }

	public INamedTypeSymbol GameTime { get; private set; } = null!;
	public INamedTypeSymbol GameLoopDelegate { get; private set; } = null!;
	public INamedTypeSymbol StageAttribute { get; private set; } = null!;
	public INamedTypeSymbol BeginAttribute { get; private set; } = null!;
	public INamedTypeSymbol EndAttribute { get; private set; } = null!;
	public INamedTypeSymbol ScopeAttribute { get; private set; } = null!;
	public INamedTypeSymbol OrderingAttribute { get; private set; } = null!;
	public INamedTypeSymbol AfterAttribute { get; private set; } = null!;
	public INamedTypeSymbol BeforeAttribute { get; private set; } = null!;
	public INamedTypeSymbol IonApplicationInterface { get; private set; } = null!;
	public INamedTypeSymbol ScheduleBuilderInterface { get; private set; } = null!;
	public INamedTypeSymbol? IonApplicationClass { get; private set; }
	public INamedTypeSymbol? SceneBuilderInterface { get; private set; }
	public INamedTypeSymbol UseSystemExtensions { get; private set; } = null!;
	public INamedTypeSymbol? SceneUseSystemExtensions { get; private set; }
	public INamedTypeSymbol? StageStepExtensions { get; private set; }
	public INamedTypeSymbol? SceneStageStepExtensions { get; private set; }
	public INamedTypeSymbol? UseDelegateServiceExtensions { get; private set; }
	public INamedTypeSymbol? ScenesBuilderExtensions { get; private set; }
	public INamedTypeSymbol? ScheduleRegistrationsAttribute { get; private set; }
	public INamedTypeSymbol? ServiceCollection { get; private set; }
	public INamedTypeSymbol? ServiceProvider { get; private set; }
	public INamedTypeSymbol? AsyncStateMachineAttribute { get; private set; }

	// Events v2.
	public INamedTypeSymbol? Events { get; private set; }
	public INamedTypeSymbol? EventBus { get; private set; }
	public INamedTypeSymbol? EventReader { get; private set; }
	public INamedTypeSymbol? EventsExtensions { get; private set; }
	public INamedTypeSymbol? EmitsEventAttribute { get; private set; }
	public INamedTypeSymbol? ReadsEventAttribute { get; private set; }
	public INamedTypeSymbol? EventUsageAttribute { get; private set; }
	public INamedTypeSymbol? IonApplicationBuilder { get; private set; }

	/// <summary>Whether the compilation references the Events v2 API.</summary>
	public bool HasEvents => Events is not null && EventBus is not null && EventReader is not null && EmitsEventAttribute is not null && ReadsEventAttribute is not null;

	/// <summary>The stage attribute classes by stage (1 = Init ... 7 = Destroy).</summary>
	public Dictionary<INamedTypeSymbol, int> StageAttributes { get; } = new(SymbolEqualityComparer.Default);

	public static KnownSymbols? TryCreate(Compilation compilation)
	{
		var known = new KnownSymbols(compilation);
		INamedTypeSymbol? Get(string name) => compilation.GetTypeByMetadataName(name);

		if (Get("Ion.GameTime") is not { } gameTime
			|| Get("Ion.GameLoopDelegate") is not { } gameLoopDelegate
			|| Get("Ion.StageAttribute") is not { } stageAttribute
			|| Get("Ion.BeginAttribute") is not { } begin
			|| Get("Ion.EndAttribute") is not { } end
			|| Get("Ion.ScopeAttribute") is not { } scope
			|| Get("Ion.OrderingAttribute") is not { } ordering
			|| Get("Ion.AfterAttribute`1") is not { } after
			|| Get("Ion.BeforeAttribute`1") is not { } before
			|| Get("Ion.IIonApplication") is not { } app
			|| Get("Ion.IScheduleBuilder") is not { } scheduleBuilder
			|| Get("Ion.UseSystemExtensions") is not { } useSystem
			|| Get("Ion.GeneratedSystem") is null)
		{
			return null;
		}

		known.GameTime = gameTime;
		known.GameLoopDelegate = gameLoopDelegate;
		known.StageAttribute = stageAttribute;
		known.BeginAttribute = begin;
		known.EndAttribute = end;
		known.ScopeAttribute = scope;
		known.OrderingAttribute = ordering;
		known.AfterAttribute = after;
		known.BeforeAttribute = before;
		known.IonApplicationInterface = app;
		known.ScheduleBuilderInterface = scheduleBuilder;
		known.UseSystemExtensions = useSystem;
		known.IonApplicationClass = Get("Ion.IonApplication");
		known.SceneBuilderInterface = Get("Ion.Extensions.Scenes.ISceneBuilder");
		known.SceneUseSystemExtensions = Get("Ion.Extensions.Scenes.UseSystemExtensions");
		known.StageStepExtensions = Get("Ion.StageStepExtensions");
		known.SceneStageStepExtensions = Get("Ion.Extensions.Scenes.SceneStageStepExtensions");
		known.UseDelegateServiceExtensions = Get("Ion.UseDelegateServiceExtensions");
		known.ScenesBuilderExtensions = Get("Ion.Extensions.Scenes.BuilderExtensions");
		known.ScheduleRegistrationsAttribute = Get("Ion.ScheduleRegistrationsAttribute");
		known.ServiceCollection = Get("Microsoft.Extensions.DependencyInjection.IServiceCollection");
		known.ServiceProvider = Get("System.IServiceProvider");
		known.AsyncStateMachineAttribute = Get("System.Runtime.CompilerServices.AsyncStateMachineAttribute");
		known.Events = Get("Ion.IEvents");
		known.EventBus = Get("Ion.EventBus");
		known.EventReader = Get("Ion.EventReader`1");
		known.EventsExtensions = Get("Ion.EventsExtensions");
		known.EmitsEventAttribute = Get("Ion.EmitsEventAttribute");
		known.ReadsEventAttribute = Get("Ion.ReadsEventAttribute");
		known.EventUsageAttribute = Get("Ion.EventUsageAttribute");
		known.IonApplicationBuilder = Get("Ion.IonApplicationBuilder");

		for (var i = 0; i < StageNames.Length; i++)
		{
			if (Get("Ion." + StageNames[i] + "Attribute") is { } attribute) known.StageAttributes[attribute] = i + 1;
		}

		return known;
	}

	/// <summary>Whether <paramref name="type"/> is a type a schedule is built on (an application or scene builder).</summary>
	public bool IsBuilderType(ITypeSymbol? type)
	{
		if (type is null) return false;
		return Is(type, IonApplicationInterface) || Is(type, ScheduleBuilderInterface) || Is(type, IonApplicationClass) || Is(type, SceneBuilderInterface);
	}

	public static bool Is(ITypeSymbol? type, ITypeSymbol? other) => type is not null && other is not null && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, other.OriginalDefinition);

	public static bool IsStage(int stage) => stage is >= 1 and <= 7;

	public static string StageName(int stage) => IsStage(stage) ? StageNames[stage - 1] : stage.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
