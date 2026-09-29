namespace Ion.Extensions.Graphics.Tests;

public class NullSpriteBatchTests
{
	private readonly NullSpriteBatch _batch = new();
	private readonly NullTexture2D _texture = new("tex", 16, 8);
	private readonly IFont _font = new NullFontSet("font", ["font.ttf"]).CreateStyle(20);

	[Fact, Trait(CATEGORY, UNIT)]
	public void CountsDrawsAndStringsPerFrame()
	{
		_batch.Begin();
		_batch.Draw(_texture, new Vector2(1, 2), new Vector2(16, 8));
		_batch.Draw(_texture, new RectangleF(0, 0, 4, 4));
		_batch.DrawString(_font, "hi", new Vector2(5, 5));
		_batch.DrawRect(Color.Red, new RectangleF(0, 0, 1, 1));
		_batch.DrawLine(Color.Red, Vector2.Zero, Vector2.One);
		_batch.DrawPoint(Color.Red, Vector2.One);
		_batch.End();

		var frame = _batch.LastFrame;
		Assert.Equal(0, frame.Frame);
		Assert.Equal(6, frame.DrawCalls);
		Assert.Equal(2, frame.Sprites);
		Assert.Equal(1, frame.Strings);
		Assert.Equal(1, frame.Rects);
		Assert.Equal(1, frame.Lines);
		Assert.Equal(1, frame.Points);
		Assert.Equal(1, _batch.FramesCompleted);

		_batch.Begin();
		_batch.DrawString(_font, "again", Vector2.Zero);
		_batch.End();

		frame = _batch.LastFrame;
		Assert.Equal(1, frame.Frame);
		Assert.Equal(1, frame.DrawCalls);
		Assert.Equal(0, frame.Sprites);
		Assert.Equal(1, frame.Strings);
		Assert.Equal(2, _batch.FramesCompleted);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void RecordsCommandDetails()
	{
		_batch.Begin();
		_batch.Draw(_texture, new Vector2(1, 2), new Vector2(16, 8), color: Color.Blue, depth: 0.5f);
		_batch.DrawString(_font, "abc", new Vector2(3, 4), scale: 2f);
		_batch.End();

		var commands = _batch.LastFrame.Commands;
		Assert.Equal(2, commands.Count);

		Assert.Equal(SpriteBatchCommandKind.Sprite, commands[0].Kind);
		Assert.Same(_texture, commands[0].Texture);
		Assert.Equal(new Vector2(1, 2), commands[0].Position);
		Assert.Equal(Color.Blue, commands[0].Color);
		Assert.Equal(0.5f, commands[0].Depth);

		Assert.Equal(SpriteBatchCommandKind.String, commands[1].Kind);
		Assert.Equal("abc", commands[1].Text);
		Assert.Same(_font, commands[1].Font);
		Assert.Equal(new Vector2(3 * 10 * 2, 20 * 2), commands[1].Size); // 3 glyphs of 10px, 20px line, scale 2
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void KeepsOnlyTheLastCommandCapacityCommands()
	{
		_batch.CommandCapacity = 3;

		_batch.Begin();
		for (var i = 0; i < 5; i++) _batch.DrawRect(Color.White, new Vector2(i, 0), Vector2.One);
		_batch.End();

		Assert.Equal(5, _batch.LastFrame.DrawCalls);
		Assert.Equal([2f, 3f, 4f], _batch.LastFrame.Commands.Select(c => c.Position.X));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void BeginDiscardsDrawsOutsideAFrame_AndLastFrameIsEmptyBeforeTheFirstEnd()
	{
		Assert.Equal(-1, _batch.LastFrame.Frame);
		Assert.Equal(0, _batch.LastFrame.DrawCalls);

		_batch.DrawRect(Color.White, Vector2.Zero, Vector2.One);
		Assert.Equal(1, _batch.CurrentFrame.DrawCalls);

		_batch.Begin();
		_batch.End();
		Assert.Equal(0, _batch.LastFrame.DrawCalls);
	}
}
