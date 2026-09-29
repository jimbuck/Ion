using System.Numerics;

namespace Ion.Extensions.Graphics.Abstractions.Tests;

public class RectangleFTests
{
	[Fact]
	public void Inflate_Float_KeepsFractions()
	{
		var rect = new RectangleF(10f, 20f, 30f, 40f);
		rect.Inflate(0.5f, 1.25f);
		Assert.Equal(new RectangleF(9.5f, 18.75f, 31f, 42.5f), rect);
	}

	[Fact]
	public void Offset_Float_KeepsFractions()
	{
		var rect = new RectangleF(1f, 2f, 3f, 4f);
		rect.Offset(0.25f, 0.75f);
		Assert.Equal(new RectangleF(1.25f, 2.75f, 3f, 4f), rect);
	}

	[Fact]
	public void Offset_Vector2_KeepsFractions()
	{
		var rect = new RectangleF(1f, 2f, 3f, 4f);
		rect.Offset(new Vector2(-0.5f, 0.5f));
		Assert.Equal(new RectangleF(0.5f, 2.5f, 3f, 4f), rect);
	}

	[Fact]
	public void Intersect_ReturnsOverlap()
	{
		var a = new RectangleF(0f, 0f, 10f, 10f);
		var b = new RectangleF(5f, 5f, 10f, 10f);
		Assert.Equal(new RectangleF(5f, 5f, 5f, 5f), RectangleF.Intersect(a, b));
		Assert.Equal(RectangleF.Empty, RectangleF.Intersect(a, new RectangleF(20f, 20f, 1f, 1f)));
	}

	[Fact]
	public void Contains_IsHalfOpen()
	{
		var rect = new RectangleF(0f, 0f, 10f, 10f);
		Assert.True(rect.Contains(0f, 0f));
		Assert.True(rect.Contains(9.99f, 9.99f));
		Assert.False(rect.Contains(10f, 5f));
	}

	[Fact]
	public void IsEmpty_OnlyForDefault()
	{
		Assert.True(default(RectangleF).IsEmpty);
		Assert.False(new RectangleF(0f, 0f, 1f, 0f).IsEmpty);
	}
}
