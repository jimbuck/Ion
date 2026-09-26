using System.Diagnostics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

using Ion.Extensions.Graphics;
using Ion.Extensions.Http;
using Ion.Extensions.Metrics;

namespace Ion.Extensions.Remote;

/// <summary>
/// The remote system: starts the server in Init and applies queued requests at the end of every frame
/// (<see cref="StageOrder.Remote"/> in Last).
/// </summary>
public sealed class RemoteSystem(RemoteServer server)
{
	/// <summary>Starts the server (after every other Init step, so the game is loaded when the first request arrives).</summary>
	[StackTraceHidden]
	[Init(Order = StageOrder.Remote)]
	public void Start(GameTime dt) => server.Start();

	/// <summary>Applies the queued requests and re-evaluates watches; blocks here while the game is paused.</summary>
	[StackTraceHidden]
	[Last(Order = StageOrder.Remote)]
	public void Process(GameTime dt) => server.ProcessFrame();
}

/// <summary>Registration of the remote inspection protocol.</summary>
public static class RemoteBuilderExtensions
{
	/// <summary>The configuration key that turns the server on (<c>--remote</c> sets it).</summary>
	public const string EnabledKey = "Ion:Remote:Enabled";

	/// <summary>
	/// Registers the remote inspection server when <c>Ion:Remote:Enabled</c> is true (<c>--remote</c>): the
	/// <see cref="RemoteServer"/>, its <see cref="RemoteSystem"/>, scripted input for <c>input.send</c>, the log buffer for
	/// <c>log.tail</c>, the built-in resources (<c>Ion.GameTime</c>, <c>Ion.Metrics.Profiling</c>) and frame retention for
	/// windowed screenshots. Binds <see cref="RemoteOptions"/> from <c>Ion:Remote</c>. Does nothing when the server is not
	/// enabled, or when the module is compiled out (<see cref="RemoteFeature.IsSupported"/> is false: Release builds
	/// without <c>-p:IonRemote=true</c>). Add the system with <see cref="UseRemote"/>.
	/// </summary>
	public static IServiceCollection AddRemote(this IServiceCollection services, IConfiguration config)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(config);

		var enabled = bool.TryParse(config[EnabledKey], out var on) && on;
		if (!enabled) return services;

		if (!RemoteFeature.IsSupported)
		{
			Console.Error.WriteLine("Ion remote: --remote was requested but the remote module is compiled out of this build (Release without -p:IonRemote=true); the server does not run.");
			return services;
		}

		services.Configure<RemoteOptions>(config.GetSection("Ion:Remote"));
		services.TryAddSingleton<RemoteServer>();
		services.TryAddSingleton<RemoteSystem>();
		// The protocol as an HTTP endpoint, which the web module (Ion.Extensions.Web) mounts at /rpc when it runs too.
		services.AddSingleton<IHttpEndpoint>(static sp => sp.GetRequiredService<RemoteServer>().HttpEndpoint);
		services.AddScriptedInput();

		if (!services.Any(static d => d.ServiceType == typeof(RemoteLogBuffer)))
		{
			var logs = new RemoteLogBuffer();
			services.AddSingleton(logs);
			services.AddSingleton<ILoggerProvider>(logs);
		}

		if (Enum.TryParse<RemoteTransport>(config["Ion:Remote:Transport"], ignoreCase: true, out var transport) && transport is RemoteTransport.Stdio or RemoteTransport.Both)
		{
			// Standard output carries the protocol: console logs go to standard error.
			services.Configure<ConsoleLoggerOptions>(static o => o.LogToStandardErrorThreshold = LogLevel.Trace);
		}

		// Windowed screenshots read back the last presented frame.
		services.PostConfigure<GraphicsConfig>(static g => g.RetainLastFrame = true);

		services.AddRemoteResource("Ion.GameTime", "The loop's frame, elapsed time, last delta, fixed steps started and running stage.", RemoteJsonContext.Default.GameTimeInfo, static sp =>
		{
			var context = sp.GetRequiredService<GameLoopContext>();
			var loop = context.Loop;
			return new GameTimeInfo(context.Frame, loop?.GameTime.Elapsed.TotalSeconds ?? 0, loop?.GameTime.Delta ?? 0, context.FixedStepCount, context.Stage.ToString());
		});
		services.AddRemoteResource("Ion.Metrics.Profiling", "Whether span recording is on (the metrics module's runtime toggle).", RemoteJsonContext.Default.Boolean,
			static sp => sp.GetService<IMetrics>()?.IsProfiling ?? false,
			static (sp, value) =>
			{
				if (sp.GetService<IMetrics>() is { } metrics) metrics.IsProfiling = value;
			});

		return services;
	}

	/// <summary>Adds the <see cref="RemoteSystem"/> when <see cref="AddRemote"/> registered the server; otherwise does nothing.</summary>
	public static IIonApplication UseRemote(this IIonApplication app)
	{
		ArgumentNullException.ThrowIfNull(app);
		// One guarded registration (not early returns), so the schedule generator's summary marks it conditional.
		if (RemoteFeature.IsSupported && app.Services.GetService<RemoteServer>() is not null) app.UseSystem<RemoteSystem>();
		return app;
	}
}
