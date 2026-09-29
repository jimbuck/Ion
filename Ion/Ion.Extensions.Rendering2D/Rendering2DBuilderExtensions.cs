using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;

namespace Ion.Extensions.Rendering2D;

/// <summary>
/// Registration of the 2D renderer.
/// </summary>
public static class Rendering2DBuilderExtensions
{
	/// <summary>
	/// Registers the 2D renderer on the RHI backend registered before it (which provides <see cref="IGraphicsFrame"/>):
	/// <see cref="SpriteBatch"/> as <see cref="ISpriteBatch"/>, <see cref="TextureFactory"/>, the <see cref="ITexture2D"/> and <see cref="IFontSet"/>
	/// loaders and the glyph atlas. Any <see cref="ISpriteBatch"/> or texture and font loader registered earlier (the null
	/// backend's) is replaced. Call <see cref="UseRendering2D"/> to add its system.
	/// </summary>
	public static IServiceCollection AddRendering2D(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		// Replace a null backend's sprite batch mapping and loaders (their concrete types stay registered for tests).
		services.RemoveAll<ISpriteBatch>();
		for (var i = services.Count - 1; i >= 0; i--)
		{
			var descriptor = services[i];
			if (descriptor.ServiceType != typeof(IAssetLoader) || descriptor.ImplementationType is not { } type) continue;
			if (type.Name is "NullTexture2DLoader" or "NullFontLoader") services.RemoveAt(i);
		}

		services
			.AddSingleton<GpuResourceTracker>()
			.AddSingleton(static sp => new GlyphAtlas(sp.GetRequiredService<IGraphicsFrame>(), sp.GetRequiredService<GpuResourceTracker>()))
			.AddSingleton(static sp => new SpriteBatch(sp.GetRequiredService<IGraphicsFrame>(), sp.GetService<ILogger<SpriteBatch>>()))
			.AddSingleton<ISpriteBatch>(static sp => sp.GetRequiredService<SpriteBatch>())
			.AddSingleton<IAssetLoader>(static sp => new Texture2DLoader(sp.GetRequiredService<IGraphicsFrame>(), sp.GetRequiredService<IPersistentStorage>(), sp.GetRequiredService<GpuResourceTracker>()))
			.AddSingleton<IAssetLoader>(static sp => new FontSetLoader(sp.GetRequiredService<IPersistentStorage>(), sp.GetRequiredService<GlyphAtlas>()))
			.AddSingleton(static sp => new TextureFactory(sp.GetRequiredService<IGraphicsFrame>(), sp.GetRequiredService<GpuResourceTracker>()))
			.AddSingleton<SpriteBatchSystem>();
		return services;
	}

	/// <summary>
	/// Adds the sprite batch system: creates the batch's GPU resources at Init (<see cref="StageOrder.SpriteBatch"/>,
	/// after the device), opens and submits a segment around every Render stage, and releases the renderer's GPU
	/// resources (its own and every texture and glyph page it loaded) in Destroy before the device is destroyed.
	/// </summary>
	public static IIonApplication UseRendering2D(this IIonApplication app) => app.UseSystem<SpriteBatchSystem>();
}

/// <summary>
/// The 2D renderer's system: Init at <see cref="StageOrder.SpriteBatch"/>, a Begin/End scope around the Render stage at
/// <see cref="StageOrder.SpriteBatch"/> (inside the graphics frame scope), and teardown at <see cref="DestroyOrder"/>.
/// </summary>
internal sealed class SpriteBatchSystem(SpriteBatch spriteBatch, GpuResourceTracker tracker)
{
	/// <summary>
	/// The order of the teardown in the Destroy stage: after the game's Destroy steps (order 0) and before the RHI
	/// backends destroy the device (<c>StageOrder.WindowClose - 50</c>).
	/// </summary>
	public const int DestroyOrder = StageOrder.WindowClose - 100;

	[Init(Order = StageOrder.SpriteBatch)]
	public void Init(GameTime dt) => spriteBatch.Initialize();

	[Begin(Stage.Render, Order = StageOrder.SpriteBatch)]
	public void Begin(GameTime dt) => spriteBatch.Begin();

	[End(Stage.Render, Order = StageOrder.SpriteBatch)]
	public void End(GameTime dt) => spriteBatch.End();

	[Destroy(Order = DestroyOrder)]
	public void Destroy(GameTime dt)
	{
		tracker.ReleaseAll();
		spriteBatch.Dispose();
	}
}
