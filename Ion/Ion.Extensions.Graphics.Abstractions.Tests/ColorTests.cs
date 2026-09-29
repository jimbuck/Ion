namespace Ion.Extensions.Graphics.Abstractions.Tests;

public class ColorTests
{
	private static void AssertBytes(Color color, byte r, byte g, byte b, byte a)
	{
		color.Deconstruct(out byte cr, out byte cg, out byte cb, out byte ca);
		Assert.Equal((r, g, b, a), (cr, cg, cb, ca));
	}

	[Theory]
	[InlineData(0x123u, 0x11, 0x22, 0x33, 0xFF)]
	[InlineData(0xF0Au, 0xFF, 0x00, 0xAA, 0xFF)]
	[InlineData(0xFFFu, 0xFF, 0xFF, 0xFF, 0xFF)]
	public void Constructor_ThreeDigitHex_IsRgbOpaque(uint hex, int r, int g, int b, int a)
	{
		AssertBytes(new Color(hex), (byte)r, (byte)g, (byte)b, (byte)a);
	}

	[Theory]
	[InlineData(0x1234u, 0x11, 0x22, 0x33, 0x44)]
	[InlineData(0xF00Fu, 0xFF, 0x00, 0x00, 0xFF)]
	[InlineData(0xFFF0u, 0xFF, 0xFF, 0xFF, 0x00)]
	[InlineData(0xABC8u, 0xAA, 0xBB, 0xCC, 0x88)]
	public void Constructor_FourDigitHex_IsRgba(uint hex, int r, int g, int b, int a)
	{
		AssertBytes(new Color(hex), (byte)r, (byte)g, (byte)b, (byte)a);
	}

	[Theory]
	[InlineData(0x123456u, 0x12, 0x34, 0x56, 0xFF)]
	[InlineData(0xFF8000u, 0xFF, 0x80, 0x00, 0xFF)]
	[InlineData(0x010203u, 0x01, 0x02, 0x03, 0xFF)]
	public void Constructor_SixDigitHex_IsRrggbbOpaque(uint hex, int r, int g, int b, int a)
	{
		AssertBytes(new Color(hex), (byte)r, (byte)g, (byte)b, (byte)a);
	}

	[Theory]
	[InlineData(0x12345678u, 0x12, 0x34, 0x56, 0x78)]
	[InlineData(0xFF000080u, 0xFF, 0x00, 0x00, 0x80)]
	[InlineData(0xFFFFFFFFu, 0xFF, 0xFF, 0xFF, 0xFF)]
	public void Constructor_EightDigitHex_IsRrggbbaa(uint hex, int r, int g, int b, int a)
	{
		AssertBytes(new Color(hex), (byte)r, (byte)g, (byte)b, (byte)a);
	}

	[Theory]
	[InlineData(0x12345678u)]
	[InlineData(0xFF000080u)]
	[InlineData(0x80808080u)]
	[InlineData(0x01020304u)]
	[InlineData(0xFFFFFFFFu)]
	[InlineData(0xFEFDFCFBu)]
	public void PackedValue_RoundTripsEightDigitHex(uint hex)
	{
		Assert.Equal(hex, new Color(hex).PackedValue);
	}

	[Theory]
	[InlineData(0x123u, 0x112233FFu)]
	[InlineData(0x1234u, 0x11223344u)]
	[InlineData(0x123456u, 0x123456FFu)]
	public void PackedValue_ExpandsShortForms(uint hex, uint packed)
	{
		Assert.Equal(packed, new Color(hex).PackedValue);
	}

	[Fact]
	public void PackedValue_RoundTripsEveryChannelValue()
	{
		for (var v = 0; v <= 255; v++)
		{
			var color = new Color(v, 255 - v, v, 255 - v);
			var expected = ((uint)v << 24) | ((uint)(255 - v) << 16) | ((uint)v << 8) | (uint)(255 - v);
			Assert.Equal(expected, color.PackedValue);
			// A zero red channel makes the packed value read back as a shorter form (see Color(uint) remarks).
			if (v > 0) Assert.Equal(color, new Color(color.PackedValue));
		}
	}

	[Fact]
	public void PackedValue_ClampsOutOfRangeChannels()
	{
		Assert.Equal(0xFF0000FFu, new Color(2f, -1f, 0f, 1f).PackedValue);
	}

	[Fact]
	public void Default_EqualsTransparent()
	{
		// Documented contract: SpriteBatch treats default (== Transparent) as "no tint".
		Assert.Equal(default, Color.Transparent);
		Assert.NotEqual(default, new Color(Color.White, 0f));
	}

	[Fact]
	public void FloatDeconstruct_ReturnsUnitChannels()
	{
		new Color(0.25f, 0.5f, 0.75f, 1f).Deconstruct(out float r, out float g, out float b, out float a);
		Assert.Equal((0.25f, 0.5f, 0.75f, 1f), (r, g, b, a));
	}

	[Fact]
	public void ToString_UsesRoundedBytes()
	{
		Assert.StartsWith("#FF8000", new Color(0xFF8000u).ToString());
	}
}
