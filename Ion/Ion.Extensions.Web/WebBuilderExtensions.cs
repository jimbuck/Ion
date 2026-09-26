using System.Diagnostics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ion.Extensions.Web;

/// <summary>
/// The web system: starts the server in Init, hands queued requests and messages to the handlers at the end of every
/// frame (<see cref="StageOrder.Web"/> in Last), and stops the server in Destroy.
/// </summary>
public sealed class WebSystem(WebServer server)
{
	/// <summary>Starts the server (after the game's own Init steps, so its systems are ready for the first request).</summary>
	[StackTraceHidden]
	[Init(Order = StageOrder.Web)]
	public void Start(GameTime dt) => server.Start();

	/// <summary>Runs the queued handlers on the game thread.</summary>
	[StackTraceHidden]
	[Last(Order = StageOrder.Web)]
	public void Process(GameTime dt) => server.ProcessFrame();

	/// <summary>Stops the server.</summary>
	[StackTraceHidden]
	[Destroy(Order = StageOrder.Web)]
	public void Stop(GameTime dt) => server.Dispose();
}

/// <summary>Registration of the web server module.</summary>
public static class WebBuilderExtensions
{
	/// <summary>The configuration section of <see cref="WebOptions"/>.</summary>
	public const string Section = "Ion:Web";

	/// <summary>The configuration key that turns the server on.</summary>
	public const string EnabledKey = "Ion:Web:Enabled";

	/// <summary>
	/// Registers the web server when it is enabled (<c>Ion:Web:Enabled</c>, or <see cref="WebOptions.Enabled"/> set by
	/// <paramref name="configure"/>): the <see cref="WebServer"/> (also as <see cref="IWebServer"/>) and its
	/// <see cref="WebSystem"/>, with <see cref="WebOptions"/> bound from <c>Ion:Web</c> and then <paramref name="configure"/>.
	/// Does nothing otherwise: nothing listens unless asked. Add the system with <see cref="UseWeb"/>. The routes are the
	/// game's <see cref="HttpAttribute"/> and <see cref="WebSocketAttribute"/> methods (the routing generator's tables),
	/// plus the tables added with <see cref="AddWebRoutes"/>; their systems must be registered as singletons.
	/// </summary>
	public static IServiceCollection AddWeb(this IServiceCollection services, IConfiguration config, Action<WebOptions>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(config);

		var section = config.GetSection(Section);
		var probe = new WebOptions();
		section.Bind(probe);
		configure?.Invoke(probe);
		if (!probe.Enabled) return services;

		services.AddOptions<WebOptions>();
		services.Configure<WebOptions>(section);
		if (configure is not null) services.Configure(configure);
		services.TryAddSingleton<WebServer>();
		services.TryAddSingleton<IWebServer>(static sp => sp.GetRequiredService<WebServer>());
		services.TryAddSingleton<WebSystem>();
		return services;
	}

	/// <summary>Adds a route table built by hand (or by another generator) to the server's routes.</summary>
	public static IServiceCollection AddWebRoutes(this IServiceCollection services, WebRouteTable table)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(table);
		services.AddSingleton(table);
		return services;
	}

	/// <summary>Adds the <see cref="WebSystem"/> when <see cref="AddWeb"/> registered the server; otherwise does nothing.</summary>
	public static IIonApplication UseWeb(this IIonApplication app)
	{
		ArgumentNullException.ThrowIfNull(app);
		// One guarded registration (not early returns), so the schedule generator's summary marks it conditional.
		if (app.Services.GetService<WebServer>() is not null) app.UseSystem<WebSystem>();
		return app;
	}
}
