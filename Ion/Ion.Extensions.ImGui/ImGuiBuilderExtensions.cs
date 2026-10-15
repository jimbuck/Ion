using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.ImGui;

/// <summary>Registration of the Dear ImGui module.</summary>
public static class ImGuiBuilderExtensions
{
	/// <summary>
	/// Registers Dear ImGui: <see cref="ImGuiHost"/> (the context, created when first resolved) with
	/// <see cref="ImGuiOptions"/> bound from <c>Ion:ImGui</c> when <paramref name="configuration"/> is given, then
	/// <paramref name="configure"/>. Add its system with <see cref="UseImGui"/>.
	/// </summary>
	public static IServiceCollection AddImGui(this IServiceCollection services, IConfiguration? configuration = null, Action<ImGuiOptions>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		var options = services.AddOptions<ImGuiOptions>();
		if (configuration is not null) options.Bind(configuration.GetSection("Ion:ImGui"));
		if (configure is not null) options.Configure(configure);

		services.TryAddSingleton(static sp => new ImGuiHost(
			sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ImGuiOptions>>(),
			sp.GetService<IInputState>(),
			sp.GetService<IWindow>(),
			sp.GetService<IGraphicsFrame>()));
		services.TryAddSingleton(static sp => new ImGuiSystem(sp.GetRequiredService<ImGuiHost>()));
		return services;
	}

	/// <summary>
	/// Adds the ImGui system: the renderer is created at Init (after the graphics device), a frame opens in First (after
	/// input, at <see cref="StageOrder.ImGui"/>) and is drawn when Render ends, after the 3D and 2D renderers, so ImGui is
	/// on top of the game, and the GPU resources are released in Destroy before the device.
	/// </summary>
	public static IIonApplication UseImGui(this IIonApplication app) => app.UseSystem<ImGuiSystem>();
}

/// <summary>Drives <see cref="ImGuiHost"/> from the schedule (see <see cref="ImGuiBuilderExtensions.UseImGui"/>).</summary>
public sealed class ImGuiSystem(ImGuiHost host)
{
	/// <summary>Before the RHI backends destroy the device (<c>StageOrder.WindowClose - 50</c>), after the game's Destroy steps.</summary>
	public const int DestroyOrder = StageOrder.WindowClose - 100;

	/// <summary>Creates the renderer (Init).</summary>
	[Init(Order = StageOrder.ImGui)]
	public void Init(GameTime dt) => host.Initialize();

	/// <summary>Feeds input and opens the frame (First).</summary>
	[First(Order = StageOrder.ImGui)]
	public void NewFrame(GameTime dt) => host.BeginFrame((float)dt.Elapsed.TotalSeconds);

	/// <summary>Opens the Render scope (nothing to do: the scope exists so the end runs after the renderers').</summary>
	[Begin(Stage.Render, Order = StageOrder.ImGui)]
	public void BeginRender(GameTime dt) { }

	/// <summary>Closes the frame and draws it (end of Render).</summary>
	[End(Stage.Render, Order = StageOrder.ImGui)]
	public void Draw(GameTime dt) => host.EndFrame();

	/// <summary>Releases the GPU resources (Destroy).</summary>
	[Destroy(Order = DestroyOrder)]
	public void Destroy(GameTime dt) => host.ReleaseGpu();
}
