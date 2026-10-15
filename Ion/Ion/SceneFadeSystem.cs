using System.Numerics;

using Ion.Extensions.Graphics;
using Ion.Extensions.Scenes;

namespace Ion;

/// <summary>
/// Draws the built-in <see cref="TransitionKind.Fade"/> scene transition: a rectangle of <see cref="Color"/> over the
/// whole window, at the transition's <see cref="SceneTransitionState.Coverage"/> as opacity, with the 2D renderer.
/// Draws nothing while no fade runs. Registered by <c>AddIon</c> and added by <c>UseIon()</c> (or
/// <see cref="BuilderExtensions.UseSceneFade"/> for games composed from parts); with the headless backends the rectangle
/// goes to the recording sprite batch.
/// </summary>
/// <remarks>
/// Its Render step runs at <see cref="StageOrder.SceneTransition"/> (750): inside the sprite batch scope, over the
/// scene, the game's own drawing and the UI, and under the metrics overlay.
/// </remarks>
public sealed class SceneFadeSystem(SceneSystem scenes, ISpriteBatch sprites, IWindow window)
{
	/// <summary>The color faded to (black by default). Its alpha scales the fade's opacity.</summary>
	public Color Color { get; set; } = Color.Black;

	/// <summary>Draws the running fade, if any.</summary>
	[Render(Order = StageOrder.SceneTransition)]
	public void Draw(GameTime dt)
	{
		var transition = scenes.Transition;
		if (transition.Transition.Kind != TransitionKind.Fade) return;

		var coverage = transition.Coverage;
		if (coverage <= 0f) return;

		sprites.DrawRect(new Color(Color, Color.A * coverage), Vector2.Zero, window.Size);
	}
}
