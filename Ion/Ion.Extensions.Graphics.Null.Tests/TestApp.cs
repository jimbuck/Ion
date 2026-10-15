namespace Ion.Extensions.Graphics.Tests;

/// <summary>
/// Builds headless <see cref="IonApplication"/>s for tests.
/// </summary>
internal static class TestApp
{
	public static readonly GameTime FrameTime = new() { Frame = 0, Delta = 1f / 60f, Alpha = 1f, Elapsed = TimeSpan.Zero };

	/// <summary>
	/// Builds an application with <c>AddIon</c> in headless mode plus whatever <paramref name="configure"/> adds, and wires
	/// <c>UseIon</c> followed by <paramref name="use"/>.
	/// </summary>
	public static IonApplication CreateHeadless(Action<IServiceCollection>? configure = null, Action<IIonApplication>? use = null, Dictionary<string, string?>? settings = null)
	{
		var builder = IonApplication.CreateBuilder();
		builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Ion:Headless"] = "true" });
		if (settings is not null) builder.Configuration.AddInMemoryCollection(settings);

		builder.Services.AddIon(builder.Configuration);
		configure?.Invoke(builder.Services);

		var app = builder.Build();
		app.UseIon();
		use?.Invoke(app);

		return app;
	}

	/// <summary>
	/// Builds an application with only the null graphics backend and the event system.
	/// </summary>
	public static IonApplication CreateNullGraphics(Dictionary<string, string?>? settings = null, Action<IServiceCollection>? configure = null, Action<IIonApplication>? use = null)
	{
		var builder = IonApplication.CreateBuilder();
		if (settings is not null) builder.Configuration.AddInMemoryCollection(settings);

		builder.Services.AddNullGraphics(builder.Configuration);
		configure?.Invoke(builder.Services);

		var app = builder.Build();
		app.UseEvents();
		app.UseNullGraphics();
		use?.Invoke(app);

		return app;
	}
}
