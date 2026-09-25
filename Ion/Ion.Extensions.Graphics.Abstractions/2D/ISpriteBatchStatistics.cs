namespace Ion.Extensions.Graphics;

/// <summary>
/// What a sprite batch drew in one frame, for metrics (<c>FrameStats.DrawCalls</c>, <c>Sprites</c>, <c>Triangles</c>).
/// </summary>
/// <param name="Frame">The 0-based index of the frame (the number of frames the batch completed before it); -1 before the first.</param>
/// <param name="DrawCalls">Draw calls submitted to the GPU (a backend that does not batch counts every draw).</param>
/// <param name="Sprites">Quads drawn (sprites, rectangles, lines, points and glyphs).</param>
/// <param name="Triangles">Triangles drawn.</param>
public readonly record struct SpriteBatchStatistics(long Frame, int DrawCalls, int Sprites, int Triangles);

/// <summary>
/// Implemented by sprite batches that count what they draw. The metrics module reads <see cref="LastFrameStatistics"/>
/// from the registered <see cref="ISpriteBatch"/> at the end of every frame.
/// </summary>
public interface ISpriteBatchStatistics
{
	/// <summary>The statistics of the last completed frame (after the batch's end of frame).</summary>
	SpriteBatchStatistics LastFrameStatistics { get; }
}
