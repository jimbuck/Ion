using System.Runtime.CompilerServices;

namespace Ion.Extensions.Rendering2D.Tests;

public class SpriteBatcherTests
{
	private readonly SpriteTexture _a = CpuTexture.Create("a", 64, 32);
	private readonly SpriteTexture _b = CpuTexture.Create("b", 16, 16);

	private static SpriteBatch NewBatch() => new(new NoGpuFrame());

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheInstanceIs40Bytes()
	{
		Assert.Equal(40, Unsafe.SizeOf<SpriteInstance>());
		Assert.Equal(SpriteInstance.SizeInBytes, Unsafe.SizeOf<SpriteInstance>());
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void PacksColorsAsRgba8AndUvsAsUnorm16()
	{
		Assert.Equal(0xFF00_80FFu, SpriteInstance.PackColor(new Vector4(1f, 0.5f, 0f, 1f)));
		Assert.Equal(0xFFFF_FFFFu, SpriteInstance.PackColor(new Vector4(2f, 1f, 1.5f, 1f)));
		Assert.Equal(0u, SpriteInstance.PackColor(new Vector4(-1f, 0f, 0f, 0f)));

		var uv = SpriteInstance.PackUv(0f, 0.5f, 1f, 0.25f);
		Assert.Equal(0, (int)(uv & 0xFFFF));
		Assert.Equal(32768, (int)((uv >> 16) & 0xFFFF));
		Assert.Equal(65535, (int)((uv >> 32) & 0xFFFF));
		Assert.Equal(16384, (int)((uv >> 48) & 0xFFFF));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void DeferredKeepsSubmissionOrderAndMergesRunsOfOneTexture()
	{
		var batch = NewBatch();
		batch.Begin();
		batch.Draw(_a, new Vector2(0, 0), new Vector2(1, 1));
		batch.Draw(_a, new Vector2(1, 0), new Vector2(1, 1));
		batch.Draw(_b, new Vector2(2, 0), new Vector2(1, 1));
		batch.Draw(_a, new Vector2(3, 0), new Vector2(1, 1));
		var batcher = batch.Batcher;
		batch.End();

		Assert.Equal([0f, 1f, 2f, 3f], batcher.Instances.ToArray().Select(i => i.Position.X));
		var draws = batcher.Draws.ToArray();
		Assert.Equal([2, 1, 1], draws.Select(d => d.Count));
		Assert.Same(_a, batcher.Textures[draws[0].Slot]);
		Assert.Same(_b, batcher.Textures[draws[1].Slot]);
		Assert.Same(_a, batcher.Textures[draws[2].Slot]);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TextureSortIssuesOneDrawPerTextureKeepingOrderInsideATexture()
	{
		var batch = NewBatch();
		batch.Begin(new SpriteBatchOptions { SortMode = SpriteSortMode.Texture });
		for (var i = 0; i < 10; i++) batch.Draw(i % 2 == 0 ? _a : _b, new Vector2(i, 0), Vector2.One);
		batch.End();

		var batcher = batch.Batcher;
		Assert.Equal(2, batcher.Draws.Length);
		Assert.Equal([0f, 2f, 4f, 6f, 8f, 1f, 3f, 5f, 7f, 9f], batcher.Instances.ToArray().Select(i => i.Position.X));
	}

	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData(SpriteSortMode.FrontToBack, new[] { 0.1f, 0.2f, 0.2f, 0.5f })]
	[InlineData(SpriteSortMode.BackToFront, new[] { 0.5f, 0.2f, 0.2f, 0.1f })]
	public void DepthSortsAreStable(SpriteSortMode mode, float[] expected)
	{
		var batch = NewBatch();
		batch.Begin(new SpriteBatchOptions { SortMode = mode });
		batch.Draw(_a, new Vector2(0, 0), Vector2.One, depth: 0.2f);
		batch.Draw(_a, new Vector2(1, 0), Vector2.One, depth: 0.5f);
		batch.Draw(_a, new Vector2(2, 0), Vector2.One, depth: 0.1f);
		batch.Draw(_a, new Vector2(3, 0), Vector2.One, depth: 0.2f);
		batch.End();

		var instances = batch.Batcher.Instances.ToArray();
		Assert.Equal(expected, instances.Select(i => i.Depth));
		// The two sprites at depth 0.2 keep their submission order.
		var twins = instances.Where(i => i.Depth == 0.2f).Select(i => i.Position.X).ToArray();
		Assert.Equal([0f, 3f], twins);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void NestedSegmentsSplitTheFrameAndRestoreTheOuterOptions()
	{
		var batch = NewBatch();
		batch.Begin();
		batch.DrawRect(Color.Red, new RectangleF(0, 0, 1, 1));
		batch.Begin(new SpriteBatchOptions { BlendMode = SpriteBlendMode.Additive, Scissor = new Rectangle(1, 2, 3, 4) });
		batch.DrawRect(Color.Red, new RectangleF(1, 0, 1, 1));
		batch.End();
		batch.DrawRect(Color.Red, new RectangleF(2, 0, 1, 1));
		var batcher = batch.Batcher;
		batch.End();

		var segments = batcher.Segments.ToArray();
		Assert.Equal(3, segments.Length);
		Assert.Equal(SpriteBlendMode.AlphaBlend, segments[0].Options.BlendMode);
		Assert.Equal(SpriteBlendMode.Additive, segments[1].Options.BlendMode);
		Assert.Equal(new Rectangle(1, 2, 3, 4), segments[1].Options.Scissor);
		Assert.Equal(SpriteBlendMode.AlphaBlend, segments[2].Options.BlendMode);
		Assert.All(segments, s => Assert.Equal(1, s.End - s.Start));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void EndWithoutBeginAndDrawOutsideASegmentThrow()
	{
		var batch = NewBatch();
		Assert.Throws<InvalidOperationException>(() => batch.End());
		Assert.Throws<InvalidOperationException>(() => batch.DrawRect(Color.Red, new RectangleF(0, 0, 1, 1)));

		// The cached fast path (same texture, source and tint as the last sprite) still refuses to draw after End.
		batch.Begin();
		batch.Draw(_a, Vector2.Zero, Vector2.One);
		batch.End();
		Assert.Throws<InvalidOperationException>(() => batch.Draw(_a, Vector2.Zero, Vector2.One));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void CachedUvsAndTintsFollowChanges()
	{
		var batch = NewBatch();
		batch.Begin();
		batch.Draw(_a, Vector2.Zero, Vector2.One, new RectangleF(0, 0, 32, 32), Color.Red);
		batch.Draw(_a, Vector2.Zero, Vector2.One, new RectangleF(0, 0, 32, 32), Color.Red);
		batch.Draw(_a, Vector2.Zero, Vector2.One, new RectangleF(32, 0, 32, 32), Color.Blue);
		batch.Draw(_a, Vector2.Zero, Vector2.One, color: Color.Blue);
		batch.Draw(_a, Vector2.Zero, Vector2.One);
		var instances = batch.Batcher.Instances.ToArray();
		batch.End();

		Assert.Equal(SpriteInstance.PackUv(0, 0, 0.5f, 1), instances[0].Uv);
		Assert.Equal(instances[0].Uv, instances[1].Uv);
		Assert.Equal(SpriteInstance.PackUv(0.5f, 0, 1, 1), instances[2].Uv);
		Assert.Equal(SpriteInstance.FullUv, instances[3].Uv);
		Assert.Equal(SpriteInstance.PackColor(Color.Red.ToVector4()), instances[1].Color);
		Assert.Equal(SpriteInstance.PackColor(Color.Blue.ToVector4()), instances[2].Color);
		Assert.Equal(0xFFFF_FFFFu, instances[4].Color);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void RejectsTexturesFromOtherBackends()
	{
		var batch = NewBatch();
		batch.Begin();
		Assert.Throws<ArgumentException>(() => batch.Draw(new NullTexture2D("null", 4, 4), Vector2.Zero, Vector2.One));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void FoldsOriginRotationAndFlipsIntoTheInstance()
	{
		var batch = NewBatch();
		batch.Begin();
		// A 64x32 source half of the texture, flipped horizontally, 10x20 on screen, centered on (100, 50).
		batch.Draw(_a, new RectangleF(100, 50, 10, 20), new RectangleF(0, 0, 32, 32), origin: new Vector2(0.5f, 0.5f), options: SpriteEffect.FlipHorizontally);
		// Rotated a quarter turn around its top-left corner.
		batch.Draw(_a, new RectangleF(0, 0, 10, 20), rotation: MathF.PI / 2);
		var instances = batch.Batcher.Instances.ToArray();
		batch.End();

		Assert.Equal(new Vector2(95, 40), instances[0].Position);
		Assert.Equal(new Vector2(10, 0), instances[0].AxisX);
		Assert.Equal(new Vector2(0, 20), instances[0].AxisY);
		Assert.Equal(SpriteInstance.PackUv(0.5f, 0f, 0f, 1f), instances[0].Uv);
		Assert.Equal(0xFFFF_FFFFu, instances[0].Color);

		Assert.Equal(0f, instances[1].AxisX.X, 4);
		Assert.Equal(10f, instances[1].AxisX.Y, 4);
		Assert.Equal(-20f, instances[1].AxisY.X, 4);
		Assert.Equal(0f, instances[1].AxisY.Y, 4);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void RecordingAFrameAllocatesNothingAfterWarmUp()
	{
		var batch = NewBatch();
		void Frame()
		{
			batch.Begin(new SpriteBatchOptions { SortMode = SpriteSortMode.BackToFront });
			for (var i = 0; i < 1000; i++) batch.Draw(i % 3 == 0 ? _a : _b, new Vector2(i, i), new Vector2(4, 4), color: Color.Red, rotation: i, depth: i % 7 / 7f);
			batch.DrawLine(Color.Blue, Vector2.Zero, new Vector2(10, 10));
			batch.End();
		}

		Frame();
		Frame();
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 10; i++) Frame();
		Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
	}
}
