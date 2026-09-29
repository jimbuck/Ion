using System.Numerics;

using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Graphics;

/// <summary>
/// Draws sprites, rectangles, lines, points and text in 2D.
/// </summary>
/// <remarks>
/// <para>
/// The sprite batch system opens a segment with <c>default</c> <see cref="SpriteBatchOptions"/> around every Render stage,
/// so a game can call the <c>Draw*</c> methods from any Render step without <see cref="Begin"/>. For other render state
/// (sort mode, blend, sampler, camera, scissor) wrap draws in <see cref="Begin"/> and <see cref="End"/>: segments nest,
/// and <see cref="End"/> returns to the enclosing segment's options. Segments are drawn in order at the end of the frame.
/// </para>
/// <para>
/// Depth is a sort key (see <see cref="SpriteSortMode"/>), not a depth test: in <see cref="SpriteSortMode.Deferred"/> (the
/// default) sprites are drawn in submission order.
/// </para>
/// </remarks>
public interface ISpriteBatch
{
	/// <summary>
	/// Starts a segment with <paramref name="options"/>, ending the current one. Pair every call with <see cref="End"/>.
	/// </summary>
	void Begin(SpriteBatchOptions options = default);

	/// <summary>
	/// Ends the segment started by the matching <see cref="Begin"/>; drawing continues with the enclosing segment's options.
	/// Ending the outermost segment submits the frame's sprites.
	/// </summary>
	/// <exception cref="InvalidOperationException">No segment is open.</exception>
	void End();

	/// <summary>
	/// Renders the following draws into <paramref name="target"/> (a texture created with
	/// <see cref="TextureUsage.RenderAttachment"/> and <see cref="TextureUsage.TextureBinding"/>), or back into the frame
	/// when null. Segments drawn into a target use its pixel space. The target stays set until changed or until the
	/// outermost <see cref="End"/>.
	/// </summary>
	/// <param name="target">The target texture, or null for the frame.</param>
	/// <param name="clearColor">Clears the target to this color before the first draw into it; null keeps its contents.</param>
	void SetRenderTarget(ITexture? target, Color? clearColor = null);

	void DrawRect(Color color, RectangleF destinationRectangle, Vector2 origin = default, float rotation = 0, float depth = 0);
	void DrawRect(Color color, Vector2 position, Vector2 size, Vector2 origin = default, float rotation = 0, float depth = 0);

	void DrawPoint(Color color, Vector2 position, float depth = 0);
	void DrawPoint(Color color, Vector2 position, Vector2 size, float depth = 0);

	void DrawLine(Color color, Vector2 pointA, Vector2 pointB, float thickness = 1f, float depth = 0);
	void DrawLine(Color color, Vector2 start, float length, float angle, float thickness = 1, float depth = 0);

	/// <summary>
	/// Draws <paramref name="text"/> with <paramref name="font"/> at <paramref name="textPosition"/>.
	/// </summary>
	/// <remarks>
	/// <see cref="Color"/> has no separate "unset" value: <c>default(Color)</c> is bitwise identical to <see cref="Color.Transparent"/>.
	/// An omitted or <c>default</c> <c>color</c> therefore means "no tint" and is drawn as <see cref="Color.White"/>,
	/// so passing <see cref="Color.Transparent"/> also draws untinted. To draw fully transparent, use a color whose alpha is 0
	/// and whose RGB is not all zero (for example <c>new Color(Color.White, 0f)</c>), or skip the draw call.
	/// </remarks>
	void DrawString(IFont font, string text, Vector2 textPosition, Color color = default, float depth = 0, Vector2 origin = default, float rotation = 0, float scale = 1, SpriteEffect options = SpriteEffect.None);

	/// <summary>
	/// Draws <paramref name="texture"/> (or the <paramref name="sourceRectangle"/> part of it; <c>default</c> means the whole texture) into <paramref name="destinationRectangle"/>.
	/// </summary>
	/// <remarks>
	/// <see cref="Color"/> has no separate "unset" value: <c>default(Color)</c> is bitwise identical to <see cref="Color.Transparent"/>.
	/// An omitted or <c>default</c> <c>color</c> therefore means "no tint" and is drawn as <see cref="Color.White"/>,
	/// so passing <see cref="Color.Transparent"/> also draws untinted. To draw fully transparent, use a color whose alpha is 0
	/// and whose RGB is not all zero (for example <c>new Color(Color.White, 0f)</c>), or skip the draw call.
	/// </remarks>
	void Draw(ITexture2D texture, RectangleF destinationRectangle, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None);
	/// <summary>
	/// Draws <paramref name="texture"/> (or the <paramref name="sourceRectangle"/> part of it; <c>default</c> means the whole texture) at <paramref name="position"/> with <paramref name="size"/>.
	/// </summary>
	/// <remarks>
	/// <see cref="Color"/> has no separate "unset" value: <c>default(Color)</c> is bitwise identical to <see cref="Color.Transparent"/>.
	/// An omitted or <c>default</c> <c>color</c> therefore means "no tint" and is drawn as <see cref="Color.White"/>,
	/// so passing <see cref="Color.Transparent"/> also draws untinted. To draw fully transparent, use a color whose alpha is 0
	/// and whose RGB is not all zero (for example <c>new Color(Color.White, 0f)</c>), or skip the draw call.
	/// </remarks>
	void Draw(ITexture2D texture, Vector2 position, Vector2 size, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None);
}
