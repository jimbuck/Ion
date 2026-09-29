
using Microsoft.Extensions.DependencyInjection.Extensions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ion;

public class IonApplicationBuilder : IIonApplicationBuilder
{
	private readonly HostApplicationBuilder _hostBuilder;

	public ConfigurationManager Configuration => _hostBuilder.Configuration;
	public IServiceCollection Services => _hostBuilder.Services;

	internal IonApplicationBuilder(string[] args)
	{
		_hostBuilder = Host.CreateApplicationBuilder(CreateHostSettings(IonCommandLine.Normalize(args)));

		Services.AddLogging(config =>
		{
			config
				.ClearProviders()
				.AddSimpleConsole(options =>
				{
					options.TimestampFormat = "[HH:mm:ss] ";
					options.SingleLine = true;
				})
				.AddDebug();
		});

		Services.Configure<GameConfig>(Configuration.GetSection("Ion"));
		Services.Configure<StorageConfig>(Configuration.GetSection("Ion:Storage"));
		Services.Configure<InputConfig>(Configuration.GetSection("Ion:Input"));

		// Metrics: a profiler without history until AddMetrics replaces it (the loop then opens a profile per frame).
		Services.TryAddSingleton(FrameProfiler.Disabled);
		Services.AddSingleton<IStepProfiler>(static sp => sp.GetRequiredService<FrameProfiler>());
		Services.AddSingleton<IFrameStatsSource, EventStatsSource>();
		Services.AddSingleton<IClock, StopwatchClock>();
		Services.AddSingleton<GameLoopContext>();
		Services.AddSingleton<ILoopContext>(static sp => sp.GetRequiredService<GameLoopContext>());

		// The event bus: the generated bus when the application installed one (UseEventBus, called by the generated
		// CreateBuilder interceptor), otherwise the runtime bus. IEvents forwards to it and can be replaced by a fake; the
		// engine's own plumbing (the loop's fixed-step marks, EventSystem) keeps using the concrete bus.
		Services.AddSingleton<EventBus>(static sp => sp.GetService<EventBusFactory>() is { } factory
			? factory.Create(sp.GetRequiredService<ILoopContext>())
			: new EventBus(sp.GetRequiredService<ILoopContext>()));
		Services.AddSingleton<IEvents>(static sp => sp.GetRequiredService<EventBus>());
		Services.AddSingleton<EventSystem>();

		Services.AddSingleton<IPersistentStorage, PersistentStorage>();
	}

	/// <summary>
	/// The host settings of <see cref="IonApplication.CreateBuilder(string[])"/>: the content root, where
	/// <c>appsettings.json</c> and <c>appsettings.{Environment}.json</c> are read from and what relative <c>Ion:Storage</c>
	/// paths resolve against, is the folder of the game's executable (<see cref="AppContext.BaseDirectory"/>) rather than
	/// the working directory, unless it is set explicitly with <c>--contentRoot</c> on the command line or the
	/// <c>DOTNET_CONTENTROOT</c> environment variable.
	/// </summary>
	internal static HostApplicationBuilderSettings CreateHostSettings(string[] args)
	{
		var settings = new HostApplicationBuilderSettings { Args = args };
		if (FindExplicitContentRoot(args) is null && DefaultContentRoot() is { } root) settings.ContentRootPath = root;
		return settings;
	}

	/// <summary>
	/// The content root given on the command line or in <c>DOTNET_CONTENTROOT</c>, read the way the host reads it, or null.
	/// </summary>
	internal static string? FindExplicitContentRoot(string[] args)
	{
		var config = new ConfigurationBuilder()
			.AddEnvironmentVariables(prefix: "DOTNET_")
			.AddCommandLine(args)
			.Build();
		var value = config[HostDefaults.ContentRootKey];
		return string.IsNullOrWhiteSpace(value) ? null : value;
	}

	/// <summary>
	/// <see cref="AppContext.BaseDirectory"/>, or null where it is not a folder (some mobile runtimes), which keeps the
	/// host's default (the working directory).
	/// </summary>
	internal static string? DefaultContentRoot()
	{
		var baseDirectory = AppContext.BaseDirectory;
		return !string.IsNullOrEmpty(baseDirectory) && Directory.Exists(baseDirectory) ? baseDirectory : null;
	}

	/// <summary>
	/// Uses the event bus created by <paramref name="factory"/> (given the loop context) instead of the runtime
	/// <see cref="EventBus"/>. The Ion source generator calls this from the application's <c>CreateBuilder</c> call to
	/// install its generated bus.
	/// </summary>
	public IonApplicationBuilder UseEventBus(Func<ILoopContext?, EventBus> factory)
	{
		ArgumentNullException.ThrowIfNull(factory);
		Services.AddSingleton(new EventBusFactory(factory));
		return this;
	}

	/// <summary>
	/// Registers the system <typeparamref name="TSystem"/> as a singleton (unless it is already registered). Add it to the
	/// schedule with <c>app.UseSystem&lt;TSystem&gt;()</c> once the application is built.
	/// </summary>
	public IonApplicationBuilder AddSystem<[DynamicallyAccessedMembers(SystemAccessibility.Members)] TSystem>() where TSystem : class =>
		AddSystem(typeof(TSystem));

	/// <summary>
	/// Registers the system <paramref name="systemType"/> as a singleton (unless it is already registered), like
	/// <see cref="AddSystem{TSystem}"/>.
	/// </summary>
	public IonApplicationBuilder AddSystem([DynamicallyAccessedMembers(SystemAccessibility.Members)] Type systemType)
	{
		ArgumentNullException.ThrowIfNull(systemType);
		Services.TryAddSingleton(systemType);
		return this;
	}

	/// <summary>The hook of a host running the program's entry point (see <see cref="IonApplicationHook"/>), if any.</summary>
	internal IonApplicationHook? Hook { get; set; }

	public IonApplication Build()
	{
		// A test host running the program's entry point adds its registrations after the program's.
		Hook?.OnBuilding(this);

		// Captured last, so the root schedule can reject scoped systems and scoped step parameters (ION006), and scenes can
		// create the systems registered as singletons from their own scope (and dispose them with it).
		Services.AddScoped<ScopeOwnedInstances>();
		Services.AddSingleton(new ServiceLifetimeIndex(Services));

		var host = _hostBuilder.Build();
		var game = new IonApplication(host, Hook);

		return game;
	}
}

/// <summary>Creates the application event bus (see <see cref="IonApplicationBuilder.UseEventBus"/>).</summary>
internal sealed record EventBusFactory(Func<ILoopContext?, EventBus> Create);
