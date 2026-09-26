using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering2D;

namespace Ion.Extensions.Rendering3D;

/// <summary>
/// Registration of the 3D renderer.
/// </summary>
public static class Rendering3DBuilderExtensions
{
	/// <summary>
	/// Registers the 3D renderer: <see cref="Renderer3D"/> as <see cref="IRenderer3D"/> and <see cref="IMeshBatch"/>, the
	/// <see cref="IModel"/> (glTF 2.0) and <see cref="ICubemap"/> loaders, and its frame statistics. Register it after the
	/// graphics backend (<c>AddIon</c>): with an RHI backend (<see cref="IGraphicsFrame"/>) it draws, with the headless
	/// null backend it runs its CPU pipeline only. Binds <see cref="Rendering3DOptions"/> from <c>Ion:Rendering3D</c>.
	/// Add its system with <see cref="UseRendering3D"/>.
	/// </summary>
	public static IServiceCollection AddRendering3D(this IServiceCollection services, IConfiguration? config = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		var options = services.AddOptions<Rendering3DOptions>();
		if (config is not null) options.Bind(config.GetSection("Ion:Rendering3D"));

		services
			.AddSingleton(static sp => new Renderer3D(
				sp.GetService<IGraphicsFrame>(),
				sp.GetRequiredService<IOptions<Rendering3DOptions>>().Value,
				sp.GetService<SpriteBatch>(),
				sp.GetService<ILogger<Renderer3D>>()))
			.AddSingleton<IRenderer3D>(static sp => sp.GetRequiredService<Renderer3D>())
			.AddSingleton<IMeshBatch>(static sp => sp.GetRequiredService<Renderer3D>())
			.AddSingleton<IAssetLoader>(static sp => new ModelLoader(sp.GetRequiredService<Renderer3D>(), sp.GetRequiredService<IPersistentStorage>(), sp.GetService<TextureFactory>()))
			.AddSingleton<IAssetLoader>(static sp => new CubemapLoader(sp.GetRequiredService<Renderer3D>(), sp.GetRequiredService<IPersistentStorage>()))
			.AddSingleton<Rendering3DSystem>();
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IFrameStatsSource, Rendering3DStatsSource>());
		return services;
	}

	/// <summary>
	/// Adds the 3D renderer's system: GPU resources at Init (<see cref="StageOrder.Rendering3D"/>, after the device), a
	/// scope around every Render stage (submissions are collected while it is open; when it closes the renderer culls,
	/// sorts, batches and executes its render graph, the 2D overlay last), and teardown in Destroy before the device.
	/// </summary>
	public static IIonApplication UseRendering3D(this IIonApplication app) => app.UseSystem<Rendering3DSystem>();
}

/// <summary>
/// The 3D renderer's system: Init at <see cref="StageOrder.Rendering3D"/>, a Begin/End scope around the Render stage at
/// <see cref="StageOrder.Rendering3D"/> (it encloses the sprite batch's scope), and teardown at <see cref="DestroyOrder"/>.
/// </summary>
internal sealed class Rendering3DSystem(Renderer3D renderer)
{
	/// <summary>The order of the teardown in Destroy: after the game's steps, before the 2D renderer and the device.</summary>
	public const int DestroyOrder = StageOrder.WindowClose - 120;

	[Init(Order = StageOrder.Rendering3D)]
	public void Init(GameTime dt) => renderer.Initialize();

	[Begin(Stage.Render, Order = StageOrder.Rendering3D)]
	public void Begin(GameTime dt)
	{
		renderer.Time = (float)dt.Elapsed.TotalSeconds;
		renderer.BeginFrame();
	}

	[End(Stage.Render, Order = StageOrder.Rendering3D)]
	public void End(GameTime dt) => renderer.EndFrame();

	[Destroy(Order = DestroyOrder)]
	public void Destroy(GameTime dt) => renderer.Dispose();
}

/// <summary>Adds the 3D renderer's draw calls and triangles to the frame's <see cref="FrameStats"/>.</summary>
internal sealed class Rendering3DStatsSource(Renderer3D renderer) : IFrameStatsSource
{
	public void Collect(ref FrameStats stats)
	{
		var last = renderer.LastFrameStatistics;
		stats.DrawCalls += last.DrawCalls;
		stats.Triangles += last.Triangles;
	}
}
