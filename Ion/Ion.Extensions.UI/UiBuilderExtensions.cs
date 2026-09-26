using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.UI;

/// <summary>Registration of the UI module.</summary>
public static class UiBuilderExtensions
{
	/// <summary>
	/// Registers the UI module: the <see cref="Ui"/> context (a singleton reading <see cref="IInputState"/>), the same
	/// instance's tree as <see cref="IUiTree"/>, and its system. Needs <see cref="IInputState"/>, <see cref="IWindow"/> and
	/// <see cref="ISpriteBatch"/> (every graphics backend registers them; call it after <c>AddIon</c> or on its own).
	/// Add the system with <see cref="UseUi"/>.
	/// </summary>
	public static IServiceCollection AddUi(this IServiceCollection services, Action<UiOptions>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		var options = new UiOptions();
		configure?.Invoke(options);

		services.TryAddSingleton(sp => new Ui(sp.GetRequiredService<IInputState>(), options, sp.GetService<ILogger<Ui>>()));
		services.TryAddSingleton(static sp => sp.GetRequiredService<Ui>().Tree);
		services.TryAddSingleton(static sp => new UiSystem(sp.GetRequiredService<Ui>(), sp.GetRequiredService<IWindow>(), sp.GetRequiredService<ISpriteBatch>()));
		return services;
	}

	/// <summary>
	/// Adds the UI system: a scope around Update at <see cref="StageOrder.UiFrame"/> (<see cref="Ui.BeginFrame"/> before the
	/// scene and game steps, <see cref="Ui.EndFrame"/> after all of them) and the drawing step in Render at
	/// <see cref="StageOrder.Ui"/> (after the game's drawing, before the metrics overlay).
	/// </summary>
	public static IIonApplication UseUi(this IIonApplication app) => app.UseSystem<UiSystem>();
}

/// <summary>
/// The UI module's system: <see cref="Ui.BeginFrame"/>/<see cref="Ui.EndFrame"/> around Update at
/// <see cref="StageOrder.UiFrame"/> (the viewport is the window size) and <see cref="Ui.Draw"/> in Render at
/// <see cref="StageOrder.Ui"/>.
/// </summary>
internal sealed class UiSystem(Ui ui, IWindow window, ISpriteBatch spriteBatch)
{
	[Begin(Stage.Update, Order = StageOrder.UiFrame)]
	public void BeginFrame(GameTime dt) => ui.BeginFrame(dt.Delta, window.Size);

	[End(Stage.Update, Order = StageOrder.UiFrame)]
	public void EndFrame(GameTime dt) => ui.EndFrame();

	[Render(Order = StageOrder.Ui)]
	public void Draw(GameTime dt) => ui.Draw(spriteBatch);
}
