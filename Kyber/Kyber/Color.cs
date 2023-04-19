using System.Runtime.Serialization;

namespace Kyber;

/// <summary>
/// An RGBA color value backed by a Vector4 (16 bytes).
/// Based MonoGame's Color struct but modified to work with System.Numerics Vector4.
/// </summary>
[DataContract]
public struct Color : IEquatable<Color>
{
	// X-------Y-------Z-------W-------
	// R       G       B       A
	private readonly Vector4 _channels;

	/// <summary>
	/// 16 bytes.
	/// </summary>
	public static readonly uint SizeInBytes = 16;	

	/// <summary>
	/// The red channel.
	/// </summary>
	public float R => _channels.X;

	/// <summary>
	/// The green channel.
	/// </summary>
	public float G => _channels.Y;

	/// <summary>
	/// The blue channel.
	/// </summary>
	public float B => _channels.Z;

	/// <summary>
	/// The alpha channel.
	/// </summary>
	public float A => _channels.W;

	/// <summary>
	/// Gets or sets packed value of this <see cref="Color"/>.
	/// </summary>
	public uint PackedValue => ((uint)(R * 255) << 24) | ((uint)(G * 255) << 16) | ((uint)(B * 255) << 8) | (uint)(A * 255);

	/// <summary>
	/// Constructs an RGBA color from a packed value.
	/// The value is a 32-bit unsigned integer, with A in the least significant octet.
	/// </summary>
	/// <param name="packedValue">The packed value.</param>
	public Color(uint packedValue)
	{
		float r, g, b, a;
		unchecked
		{
			if ((packedValue & 0xff000000) != 0)
			{
				r = (byte)packedValue >> 24;
				g = (byte)packedValue >> 16;
				b = (byte)packedValue >> 8;
				a = (byte)packedValue;
			} else {
				r = (byte)packedValue >> 16;
				g = (byte)packedValue >> 8;
				b = (byte)packedValue;
				a = 0;
			}
			
		}

		_channels = new Vector4(r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f);
	}

	/// <summary>
	/// Constructs an RGBA color from the XYZW unit length components of a vector.
	/// </summary>
	/// <param name="color">A <see cref="Vector4"/> representing color.</param>
	public Color(Vector4 color) {
		_channels = color;
	}

	/// <summary>
	/// Constructs an RGBA color from the XYZ unit length components of a vector. Alpha value will be opaque.
	/// </summary>
	/// <param name="color">A <see cref="Vector3"/> representing color.</param>
	public Color(Vector3 color) : this(color.X, color.Y, color.Z, 1f) { }

	/// <summary>
	/// Constructs an RGBA color from a <see cref="Color"/> and an alpha value.
	/// </summary>
	/// <param name="color">A <see cref="Color"/> for RGB values of new <see cref="Color"/> instance.</param>
	/// <param name="alpha">The alpha component value from 0 to 255.</param>
	public Color(Color color, int alpha) : this(color.R, color.G, color.B, alpha / 255f) { }

	/// <summary>
	/// Constructs an RGBA color from color and alpha value.
	/// </summary>
	/// <param name="color">A <see cref="Color"/> for RGB values of new <see cref="Color"/> instance.</param>
	/// <param name="alpha">Alpha component value from 0.0f to 1.0f.</param>
	public Color(Color color, float alpha) : this(color.R, color.G, color.B, alpha) { }

	/// <summary>
	/// Constructs an RGBA color from scalars representing red, green and blue values. Alpha value will be opaque.
	/// </summary>
	/// <param name="r">Red component value from 0.0f to 1.0f.</param>
	/// <param name="g">Green component value from 0.0f to 1.0f.</param>
	/// <param name="b">Blue component value from 0.0f to 1.0f.</param>
	public Color(float r, float g, float b) : this(r,g, b, 1f) { }

	/// <summary>
	/// Constructs an RGBA color from scalars representing red, green, blue and alpha values.
	/// </summary>
	/// <param name="r">Red component value from 0.0f to 1.0f.</param>
	/// <param name="g">Green component value from 0.0f to 1.0f.</param>
	/// <param name="b">Blue component value from 0.0f to 1.0f.</param>
	/// <param name="alpha">Alpha component value from 0.0f to 1.0f.</param>
	public Color(float r, float g, float b, float alpha) {
		_channels = new Vector4(r, g, b, alpha);
	}

	/// <summary>
	/// Constructs an RGBA color from scalars representing red, green and blue values. Alpha value will be opaque.
	/// </summary>
	/// <param name="r">Red component value from 0 to 255.</param>
	/// <param name="g">Green component value from 0 to 255.</param>
	/// <param name="b">Blue component value from 0 to 255.</param>
	public Color(int r, int g, int b) : this(r / 255f, g / 255f, b / 255f, 1f) { }

	/// <summary>
	/// Constructs an RGBA color from scalars representing red, green, blue and alpha values.
	/// </summary>
	/// <param name="r">Red component value from 0 to 255.</param>
	/// <param name="g">Green component value from 0 to 255.</param>
	/// <param name="b">Blue component value from 0 to 255.</param>
	/// <param name="alpha">Alpha component value from 0 to 255.</param>
	public Color(int r, int g, int b, int alpha) : this(r / 255f, g / 255f, b / 255f, alpha/255f) { }

	/// <summary>
	/// Compares whether two <see cref="Color"/> instances are equal.
	/// </summary>
	/// <param name="a"><see cref="Color"/> instance on the left of the equal sign.</param>
	/// <param name="b"><see cref="Color"/> instance on the right of the equal sign.</param>
	/// <returns><c>true</c> if the instances are equal; <c>false</c> otherwise.</returns>
	public static bool operator ==(Color a, Color b)
	{
		return a._channels == b._channels;
	}

	/// <summary>
	/// Compares whether two <see cref="Color"/> instances are not equal.
	/// </summary>
	/// <param name="a"><see cref="Color"/> instance on the left of the not equal sign.</param>
	/// <param name="b"><see cref="Color"/> instance on the right of the not equal sign.</param>
	/// <returns><c>true</c> if the instances are not equal; <c>false</c> otherwise.</returns>	
	public static bool operator !=(Color a, Color b)
	{
		return a._channels != b._channels;
	}

	/// <summary>
	/// Gets the hash code of this <see cref="Color"/>.
	/// </summary>
	/// <returns>Hash code of this <see cref="Color"/>.</returns>
	public override int GetHashCode()
	{
		return _channels.GetHashCode();
	}

	/// <summary>
	/// Compares whether current instance is equal to specified object.
	/// </summary>
	/// <param name="obj">The <see cref="Color"/> to compare.</param>
	/// <returns><c>true</c> if the instances are equal; <c>false</c> otherwise.</returns>
	public override bool Equals(object? obj)
	{
		return (obj is Color color) && Equals(color);
	}

	/// <summary>
	/// Performs linear interpolation of <see cref="Color"/>.
	/// </summary>
	/// <param name="value1">Source <see cref="Color"/>.</param>
	/// <param name="value2">Destination <see cref="Color"/>.</param>
	/// <param name="amount">Interpolation factor.</param>
	/// <returns>Interpolated <see cref="Color"/>.</returns>
	public static Color Lerp(Color value1, Color value2, float amount)
	{
		amount = MathHelper.Clamp(amount, 0, 1);
		return new Color(
			MathHelper.Lerp(value1.R, value2.R, amount),
			MathHelper.Lerp(value1.G, value2.G, amount),
			MathHelper.Lerp(value1.B, value2.B, amount),
			MathHelper.Lerp(value1.A, value2.A, amount));
	}

	/// <summary>
	/// Multiply <see cref="Color"/> by value.
	/// </summary>
	/// <param name="value">Source <see cref="Color"/>.</param>
	/// <param name="scale">Multiplicator.</param>
	/// <returns>Multiplication result.</returns>
	public static Color Multiply(Color value, float scale)
	{
		return new Color(value.R * scale, value.G * scale, value.B * scale, value.A * scale);
	}

	public static Color FromHSV(float hue, float saturation, float value)
	{
		int hi = Convert.ToInt32(MathF.Floor(hue / 60)) % 6;
		double f = hue / 60 - MathF.Floor(hue / 60);

		value *= 255;
		int v = Convert.ToInt32(value);
		int p = Convert.ToInt32(value * (1 - saturation));
		int q = Convert.ToInt32(value * (1 - f * saturation));
		int t = Convert.ToInt32(value * (1 - (1 - f) * saturation));

		if (hi == 0)
			return new Color(v, t, p);
		else if (hi == 1)
			return new Color(q, v, p);
		else if (hi == 2)
			return new Color(p, v, t);
		else if (hi == 3)
			return new Color(p, q, v);
		else if (hi == 4)
			return new Color(t, p, v);
		else
			return new Color(v, p, q);
	}

	/// <summary>
	/// Multiply <see cref="Color"/> by value.
	/// </summary>
	/// <param name="value">Source <see cref="Color"/>.</param>
	/// <param name="scale">Multiplicator.</param>
	/// <returns>Multiplication result.</returns>
	public static Color operator *(Color value, float scale)
	{
		return new Color(value.R * scale, value.G * scale, value.B * scale, value.A * scale);
	}

	public static Color operator *(float scale, Color value)
	{
		return new Color(value.R * scale, value.G * scale, value.B * scale, value.A * scale);
	}

	/// <summary>
	/// Gets a <see cref="Vector3"/> representation for this object.
	/// </summary>
	/// <returns>A <see cref="Vector3"/> representation for this object.</returns>
	public Vector3 ToVector3()
	{
		return new Vector3(_channels.X, _channels.Y, _channels.Z);
	}

	/// <summary>
	/// Gets a <see cref="Vector4"/> representation for this object.
	/// </summary>
	/// <returns>A <see cref="Vector4"/> representation for this object.</returns>
	public Vector4 ToVector4()
	{
		return _channels;
	}

	/// <summary>
	/// Converts Color to RgbaFloat.
	/// </summary>
	/// <param name="color">The color to convert.</param>
	public static implicit operator Veldrid.RgbaFloat(Color color) => new(color._channels);

	internal string DebugDisplayString
	{
		get
		{
			return string.Concat(
				R.ToString(), "  ",
				G.ToString(), "  ",
				B.ToString(), "  ",
				A.ToString()
			);
		}
	}

	/// <summary>
	/// Returns a <see cref="string"/> representation of this <see cref="Color"/> in the format:
	/// {R:[red] G:[green] B:[blue] A:[alpha]}
	/// </summary>
	/// <returns><see cref="string"/> representation of this <see cref="Color"/>.</returns>
	public override string ToString()
	{
		return $"#{(uint)(R * 255):X2}{(uint)(G * 255):X2}{(uint)(B * 255):X2} {A:0.##}";
	}

	#region IEquatable<Color> Members

	/// <summary>
	/// Compares whether current instance is equal to specified <see cref="Color"/>.
	/// </summary>
	/// <param name="other">The <see cref="Color"/> to compare.</param>
	/// <returns><c>true</c> if the instances are equal; <c>false</c> otherwise.</returns>
	public bool Equals(Color other)
	{
		return _channels == other._channels;
	}

	#endregion

	/// <summary>
	/// Deconstruction method for <see cref="Color"/>.
	/// </summary>
	/// <param name="r">Red component value from 0 to 255.</param>
	/// <param name="g">Green component value from 0 to 255.</param>
	/// <param name="b">Blue component value from 0 to 255.</param>
	public void Deconstruct(out byte r, out byte g, out byte b)
	{
		r = (byte)(R * 255);
		g = (byte)(G * 255);
		b = (byte)(B * 255);
	}

	/// <summary>
	/// Deconstruction method for <see cref="Color"/> with Alpha.
	/// </summary>
	/// <param name="r">Red component value from 0 to 255.</param>
	/// <param name="g">Green component value from 0 to 255.</param>
	/// <param name="b">Blue component value from 0 to 255.</param>
	/// <param name="a">Alpha component value from 0 to 255.</param>
	public void Deconstruct(out byte r, out byte g, out byte b, out byte a)
	{
		r = (byte)(R * 255);
		g = (byte)(G * 255);
		b = (byte)(B * 255);
		a = (byte)(A * 255);
	}

	/// <summary>
	/// Deconstruction method for <see cref="Color"/>.
	/// </summary>
	/// <param name="r">Red component value from 0.0f to 1.0f.</param>
	/// <param name="g">Green component value from 0.0f to 1.0f.</param>
	/// <param name="b">Blue component value from 0.0f to 1.0f.</param>
	public void Deconstruct(out float r, out float g, out float b)
	{
		r = R;
		g = G;
		b = B;
	}

	/// <summary>
	/// Deconstruction method for <see cref="Color"/> with Alpha.
	/// </summary>
	/// <param name="r">Red component value from 0.0f to 1.0f.</param>
	/// <param name="g">Green component value from 0.0f to 1.0f.</param>
	/// <param name="b">Blue component value from 0.0f to 1.0f.</param>
	/// <param name="a">Alpha component value from 0.0f to 1.0f.</param>
	public void Deconstruct(out float r, out float g, out float b, out float a)
	{
		r = R / 255f;
		g = G / 255f;
		b = B / 255f;
		a = A / 255f;
	}

	#region Named Colors

	public static readonly Color Transparent = new(0x00, 0x00, 0x00, 0x00);
	public static readonly Color AliceBlue = new(0xf0, 0xf8, 0xff, 0xff);
	public static readonly Color AntiqueWhite = new(0xfa, 0xeb, 0xd7, 0xff);
	public static readonly Color Aqua = new(0x00, 0xff, 0xff, 0xff);
	public static readonly Color Aquamarine = new(0x7f, 0xff, 0xd4, 0xff);
	public static readonly Color Azure = new(0xf0, 0xff, 0xff, 0xff);
	public static readonly Color Beige = new(0xf5, 0xf5, 0xdc, 0xff);
	public static readonly Color Bisque = new(0xff, 0xe4, 0xc4, 0xff);
	public static readonly Color Black = new(0x00, 0x00, 0x00, 0xff);
	public static readonly Color BlanchedAlmond = new(0xff, 0xeb, 0xcd, 0xff);
	public static readonly Color Blue = new(0x00, 0x00, 0xff, 0xff);
	public static readonly Color BlueViolet = new(0x8a, 0x2b, 0xe2, 0xff);
	public static readonly Color Brown = new(0xa5, 0x2a, 0x2a, 0xff);
	public static readonly Color BurlyWood = new(0xde, 0xb8, 0x87, 0xff);
	public static readonly Color CadetBlue = new(0x5f, 0x9e, 0xa0, 0xff);
	public static readonly Color Chartreuse = new(0x7f, 0xff, 0x00, 0xff);
	public static readonly Color Chocolate = new(0xd2, 0x69, 0x1e, 0xff);
	public static readonly Color Coral = new(0xff, 0x7f, 0x50, 0xff);
	public static readonly Color CornflowerBlue = new(0x64, 0x95, 0xed, 0xff);
	public static readonly Color Cornsilk = new(0xff, 0xf8, 0xdc, 0xff);
	public static readonly Color Crimson = new(0xdc, 0x14, 0x3c, 0xff);
	public static readonly Color Cyan = new(0x00, 0xff, 0xff, 0xff);
	public static readonly Color DarkBlue = new(0x00, 0x00, 0x8b, 0xff);
	public static readonly Color DarkCyan = new(0x00, 0x8b, 0x8b, 0xff);
	public static readonly Color DarkGoldenrod = new(0xb8, 0x86, 0x0b, 0xff);
	public static readonly Color DarkGray = new(0xa9, 0xa9, 0xa9, 0xff);
	public static readonly Color DarkGreen = new(0x00, 0x64, 0x00, 0xff);
	public static readonly Color DarkKhaki = new(0xbd, 0xb7, 0x6b, 0xff);
	public static readonly Color DarkMagenta = new(0x8b, 0x00, 0x8b, 0xff);
	public static readonly Color DarkOliveGreen = new(0x55, 0x6b, 0x2f, 0xff);
	public static readonly Color DarkOrange = new(0xff, 0x8c, 0x00, 0xff);
	public static readonly Color DarkOrchid = new(0x99, 0x32, 0xcc, 0xff);
	public static readonly Color DarkRed = new(0x8b, 0x00, 0x00, 0xff);
	public static readonly Color DarkSalmon = new(0xe9, 0x96, 0x7a, 0xff);
	public static readonly Color DarkSeaGreen = new(0x8f, 0xbc, 0x8b, 0xff);
	public static readonly Color DarkSlateBlue = new(0x48, 0x3d, 0x8b, 0xff);
	public static readonly Color DarkSlateGray = new(0x2f, 0x4f, 0x4f, 0xff);
	public static readonly Color DarkTurquoise = new(0x00, 0xce, 0xd1, 0xff);
	public static readonly Color DarkViolet = new(0x94, 0x00, 0xd3, 0xff);
	public static readonly Color DeepPink = new(0xff, 0x14, 0x93, 0xff);
	public static readonly Color DeepSkyBlue = new(0x00, 0xbf, 0xff, 0xff);
	public static readonly Color DimGray = new(0x69, 0x69, 0x69, 0xff);
	public static readonly Color DodgerBlue = new(0x1e, 0x90, 0xff, 0xff);
	public static readonly Color Firebrick = new(0xb2, 0x22, 0x22, 0xff);
	public static readonly Color FloralWhite = new(0xff, 0xfa, 0xf0, 0xff);
	public static readonly Color ForestGreen = new(0x22, 0x8b, 0x22, 0xff);
	public static readonly Color Fuchsia = new(0xff, 0x00, 0xff, 0xff);
	public static readonly Color Gainsboro = new(0xdc, 0xdc, 0xdc, 0xff);
	public static readonly Color GhostWhite = new(0xf8, 0xf8, 0xff, 0xff);
	public static readonly Color Gold = new(0xff, 0xd7, 0x00, 0xff);
	public static readonly Color Goldenrod = new(0xda, 0xa5, 0x20, 0xff);
	public static readonly Color Gray = new(0x80, 0x80, 0x80, 0xff);
	public static readonly Color Green = new(0x00, 0x80, 0x00, 0xff);
	public static readonly Color GreenYellow = new(0xad, 0xff, 0x2f, 0xff);
	public static readonly Color Honeydew = new(0xf0, 0xff, 0xf0, 0xff);
	public static readonly Color HotPink = new(0xff, 0x69, 0xb4, 0xff);
	public static readonly Color IndianRed = new(0xcd, 0x5c, 0x5c, 0xff);
	public static readonly Color Indigo = new(0x4b, 0x00, 0x82, 0xff);
	public static readonly Color Ivory = new(0xff, 0xff, 0xf0, 0xff);
	public static readonly Color Khaki = new(0xf0, 0xe6, 0x8c, 0xff);
	public static readonly Color Lavender = new(0xe6, 0xe6, 0xfa, 0xff);
	public static readonly Color LavenderBlush = new(0xff, 0xf0, 0xf5, 0xff);
	public static readonly Color LawnGreen = new(0x7c, 0xfc, 0x00, 0xff);
	public static readonly Color LemonChiffon = new(0xff, 0xfa, 0xcd, 0xff);
	public static readonly Color LightBlue = new(0xad, 0xd8, 0xe6, 0xff);
	public static readonly Color LightCoral = new(0xf0, 0x80, 0x80, 0xff);
	public static readonly Color LightCyan = new(0xe0, 0xff, 0xff, 0xff);
	public static readonly Color LightGoldenrodYellow = new(0xfa, 0xfa, 0xd2, 0xff);
	public static readonly Color LightGray = new(0xd3, 0xd3, 0xd3, 0xff);
	public static readonly Color LightGreen = new(0x90, 0xee, 0x90, 0xff);
	public static readonly Color LightPink = new(0xff, 0xb6, 0xc1, 0xff);
	public static readonly Color LightSalmon = new(0xff, 0xa0, 0x7a, 0xff);
	public static readonly Color LightSeaGreen = new(0x20, 0xb2, 0xaa, 0xff);
	public static readonly Color LightSkyBlue = new(0x87, 0xce, 0xfa, 0xff);
	public static readonly Color LightSlateGray = new(0x77, 0x88, 0x99, 0xff);
	public static readonly Color LightSteelBlue = new(0xb0, 0xc4, 0xde, 0xff);
	public static readonly Color LightYellow = new(0xff, 0xff, 0xe0, 0xff);
	public static readonly Color Lime = new(0x00, 0xff, 0x00, 0xff);
	public static readonly Color LimeGreen = new(0x32, 0xcd, 0x32, 0xff);
	public static readonly Color Linen = new(0xfa, 0xf0, 0xe6, 0xff);
	public static readonly Color Magenta = new(0xff, 0x00, 0xff, 0xff);
	public static readonly Color Maroon = new(0x80, 0x00, 0x00, 0xff);
	public static readonly Color MediumAquamarine = new(0x66, 0xcd, 0xaa, 0xff);
	public static readonly Color MediumBlue = new(0x00, 0x00, 0xcd, 0xff);
	public static readonly Color MediumOrchid = new(0xba, 0x55, 0xd3, 0xff);
	public static readonly Color MediumPurple = new(0x93, 0x70, 0xdb, 0xff);
	public static readonly Color MediumSeaGreen = new(0x3c, 0xb3, 0x71, 0xff);
	public static readonly Color MediumSlateBlue = new(0x7b, 0x68, 0xee, 0xff);
	public static readonly Color MediumSpringGreen = new(0x00, 0xfa, 0x9a, 0xff);
	public static readonly Color MediumTurquoise = new(0x48, 0xd1, 0xcc, 0xff);
	public static readonly Color MediumVioletRed = new(0xc7, 0x15, 0x85, 0xff);
	public static readonly Color MidnightBlue = new(0x19, 0x19, 0x70, 0xff);
	public static readonly Color MintCream = new(0xf5, 0xff, 0xfa, 0xff);
	public static readonly Color MistyRose = new(0xff, 0xe4, 0xe1, 0xff);
	public static readonly Color Moccasin = new(0xff, 0xe4, 0xb5, 0xff);
	public static readonly Color MonoGameOrange = new(0xe7, 0x3c, 0x00, 0xff);
	public static readonly Color NavajoWhite = new(0xff, 0xde, 0xad, 0xff);
	public static readonly Color Navy = new(0x00, 0x00, 0x80, 0xff);
	public static readonly Color OldLace = new(0xfd, 0xf5, 0xe6, 0xff);
	public static readonly Color Olive = new(0x80, 0x80, 0x00, 0xff);
	public static readonly Color OliveDrab = new(0x6b, 0x8e, 0x23, 0xff);
	public static readonly Color Orange = new(0xff, 0xa5, 0x00, 0xff);
	public static readonly Color OrangeRed = new(0xff, 0x45, 0x00, 0xff);
	public static readonly Color Orchid = new(0xda, 0x70, 0xd6, 0xff);
	public static readonly Color PaleGoldenrod = new(0xee, 0xe8, 0xaa, 0xff);
	public static readonly Color PaleGreen = new(0x98, 0xfb, 0x98, 0xff);
	public static readonly Color PaleTurquoise = new(0xaf, 0xee, 0xee, 0xff);
	public static readonly Color PaleVioletRed = new(0xdb, 0x70, 0x93, 0xff);
	public static readonly Color PapayaWhip = new(0xff, 0xef, 0xd5, 0xff);
	public static readonly Color PeachPuff = new(0xff, 0xda, 0xb9, 0xff);
	public static readonly Color Peru = new(0xcd, 0x85, 0x3f, 0xff);
	public static readonly Color Pink = new(0xff, 0xc0, 0xcb, 0xff);
	public static readonly Color Plum = new(0xdd, 0xa0, 0xdd, 0xff);
	public static readonly Color PowderBlue = new(0xb0, 0xe0, 0xe6, 0xff);
	public static readonly Color Purple = new(0x80, 0x00, 0x80, 0xff);
	public static readonly Color Red = new(0xff, 0x00, 0x00, 0xff);
	public static readonly Color RosyBrown = new(0xbc, 0x8f, 0x8f, 0xff);
	public static readonly Color RoyalBlue = new(0x41, 0x69, 0xe1, 0xff);
	public static readonly Color SaddleBrown = new(0x8b, 0x45, 0x13, 0xff);
	public static readonly Color Salmon = new(0xfa, 0x80, 0x72, 0xff);
	public static readonly Color SandyBrown = new(0xf4, 0xa4, 0x60, 0xff);
	public static readonly Color SeaGreen = new(0x2e, 0x8b, 0x57, 0xff);
	public static readonly Color SeaShell = new(0xff, 0xf5, 0xee, 0xff);
	public static readonly Color Sienna = new(0xa0, 0x52, 0x2d, 0xff);
	public static readonly Color Silver = new(0xc0, 0xc0, 0xc0, 0xff);
	public static readonly Color SkyBlue = new(0x87, 0xce, 0xeb, 0xff);
	public static readonly Color SlateBlue = new(0x6a, 0x5a, 0xcd, 0xff);
	public static readonly Color SlateGray = new(0x70, 0x80, 0x90, 0xff);
	public static readonly Color Snow = new(0xff, 0xfa, 0xfa, 0xff);
	public static readonly Color SpringGreen = new(0x00, 0xff, 0x7f, 0xff);
	public static readonly Color SteelBlue = new(0x46, 0x82, 0xb4, 0xff);
	public static readonly Color Tan = new(0xd2, 0xb4, 0x8c, 0xff);
	public static readonly Color Teal = new(0x00, 0x80, 0x80, 0xff);
	public static readonly Color Thistle = new(0xd8, 0xbf, 0xd8, 0xff);
	public static readonly Color Tomato = new(0xff, 0x63, 0x47, 0xff);
	public static readonly Color Turquoise = new(0x40, 0xe0, 0xd0, 0xff);
	public static readonly Color Violet = new(0xee, 0x82, 0xee, 0xff);
	public static readonly Color Wheat = new(0xf5, 0xde, 0xb3, 0xff);
	public static readonly Color White = new(0xff, 0xff, 0xff, 0xff);
	public static readonly Color WhiteSmoke = new(0xf5, 0xf5, 0xf5, 0xff);
	public static readonly Color Yellow = new(0xff, 0xff, 0x00, 0xff);
	public static readonly Color YellowGreen = new(0x9a, 0xcd, 0x32, 0xff);

	#endregion
}