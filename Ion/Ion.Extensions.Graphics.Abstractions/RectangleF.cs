﻿using System.Numerics;
using System.Diagnostics;

namespace Ion;

[DebuggerDisplay("{DebugDisplayString,nq}")]
public struct RectangleF : IEquatable<RectangleF>
{
	#region Private Fields

	private static RectangleF emptyRectangle = new();

	#endregion

	#region Public Fields

	/// <summary>
	/// The x coordinate of the top-left corner of this <see cref="RectangleF"/>.
	/// </summary>
	public float X;

	/// <summary>
	/// The y coordinate of the top-left corner of this <see cref="RectangleF"/>.
	/// </summary>
	public float Y;

	/// <summary>
	/// The width of this <see cref="RectangleF"/>.
	/// </summary>
	public float Width;

	/// <summary>
	/// The height of this <see cref="RectangleF"/>.
	/// </summary>
	public float Height;

	#endregion

	#region Public Properties

	/// <summary>
	/// Returns a <see cref="RectangleF"/> with X=0, Y=0, Width=0, Height=0.
	/// </summary>
	public static RectangleF Empty => emptyRectangle;

	/// <summary>
	/// Returns the x coordinate of the left edge of this <see cref="RectangleF"/>.
	/// </summary>
	public float Left => X;

	/// <summary>
	/// Returns the x coordinate of the right edge of this <see cref="RectangleF"/>.
	/// </summary>
	public float Right => X + Width;

	/// <summary>
	/// Returns the y coordinate of the top edge of this <see cref="RectangleF"/>.
	/// </summary>
	public float Top => Y;

	/// <summary>
	/// Returns the y coordinate of the bottom edge of this <see cref="RectangleF"/>.
	/// </summary>
	public float Bottom => Y + Height;

	/// <summary>
	/// Whether or not this <see cref="Rectangle"/> has a <see cref="Width"/> and
	/// <see cref="Height"/> of 0, and a <see cref="Location"/> of (0, 0).
	/// </summary>
	public bool IsEmpty => Width == 0 && Height == 0 && X == 0 && Y == 0;

	/// <summary>
	/// The top-left coordinates of this <see cref="Rectangle"/>.
	/// </summary>
	public Vector2 Location
	{
		get => new(X, Y);
		set
		{
			X = value.X;
			Y = value.Y;
		}
	}

	/// <summary>
	/// The width-height coordinates of this <see cref="Rectangle"/>.
	/// </summary>
	public Vector2 Size => new(Width, Height);

	/// <summary>
	/// A <see cref="Point"/> located in the center of this <see cref="Rectangle"/>.
	/// </summary>
	/// <remarks>
	/// If <see cref="Width"/> or <see cref="Height"/> is an odd number,
	/// the center point will be rounded down.
	/// </remarks>
	public Vector2 Center => new(X + (Width / 2f), Y + (Height / 2f));

	#endregion

	#region Internal Properties

	internal string DebugDisplayString => ToString();

	#endregion

	#region Constructors

	/// <summary>
	/// Creates a new instance of <see cref="Rectangle"/> struct, with the specified
	/// position, width, and height.
	/// </summary>
	/// <param name="x">The x coordinate of the top-left corner of the created <see cref="Rectangle"/>.</param>
	/// <param name="y">The y coordinate of the top-left corner of the created <see cref="Rectangle"/>.</param>
	/// <param name="width">The width of the created <see cref="Rectangle"/>.</param>
	/// <param name="height">The height of the created <see cref="Rectangle"/>.</param>
	public RectangleF(float x, float y, float width, float height)
	{
		X = x;
		Y = y;
		Width = width;
		Height = height;
	}

	/// <summary>
	/// Creates a new instance of <see cref="Rectangle"/> struct, with the specified
	/// location and size.
	/// </summary>
	/// <param name="location">The x and y coordinates of the top-left corner of the created <see cref="Rectangle"/>.</param>
	/// <param name="size">The width and height of the created <see cref="Rectangle"/>.</param>
	public RectangleF(Vector2 location, Vector2 size)
	{
		X = location.X;
		Y = location.Y;
		Width = size.X;
		Height = size.Y;
	}

	#endregion

	#region Operators

	/// <summary>
	/// Compares whether two <see cref="RectangleF"/> instances are equal.
	/// </summary>
	/// <param name="a"><see cref="RectangleF"/> instance on the left of the equal sign.</param>
	/// <param name="b"><see cref="RectangleF"/> instance on the right of the equal sign.</param>
	/// <returns><c>true</c> if the instances are equal; <c>false</c> otherwise.</returns>
	public static bool operator ==(RectangleF a, RectangleF b)
	{
		return (a.X == b.X) && (a.Y == b.Y) && (a.Width == b.Width) && (a.Height == b.Height);
	}

	/// <summary>
	/// Compares whether two <see cref="RectangleF"/> instances are not equal.
	/// </summary>
	/// <param name="a"><see cref="RectangleF"/> instance on the left of the not equal sign.</param>
	/// <param name="b"><see cref="RectangleF"/> instance on the right of the not equal sign.</param>
	/// <returns><c>true</c> if the instances are not equal; <c>false</c> otherwise.</returns>
	public static bool operator !=(RectangleF a, RectangleF b)
	{
		return !(a == b);
	}

	public static implicit operator System.Drawing.Rectangle(RectangleF src) => new((int)src.X, (int)src.Y, (int)src.Width, (int)src.Height);
	public static explicit operator RectangleF(System.Drawing.Rectangle src) => new(src.X, src.Y, src.Width, src.Height);

	#endregion
	#region Public Methods
	/// <summary>
	/// Gets whether or not the provided coordinates lie within the bounds of this <see cref="Rectangle"/>.
	/// </summary>
	/// <param name="x">The x coordinate of the point to check for containment.</param>
	/// <param name="y">The y coordinate of the point to check for containment.</param>
	/// <returns><c>true</c> if the provided coordinates lie inside this <see cref="Rectangle"/>; <c>false</c> otherwise.</returns>
	public bool Contains(int x, int y)
	{
		return (X <= x) && (x < (X + Width)) && (Y <= y) && (y < (Y + Height));
	}

	/// <summary>
	/// Gets whether or not the provided coordinates lie within the bounds of this <see cref="Rectangle"/>.
	/// </summary>
	/// <param name="x">The x coordinate of the point to check for containment.</param>
	/// <param name="y">The y coordinate of the point to check for containment.</param>
	/// <returns><c>true</c> if the provided coordinates lie inside this <see cref="Rectangle"/>; <c>false</c> otherwise.</returns>
	public bool Contains(float x, float y)
	{
		return (X <= x) && (x < (X + Width)) && (Y <= y) && (y < (Y + Height));
	}

	/// <summary>
	/// Gets whether or not the provided <see cref="Point"/> lies within the bounds of this <see cref="Rectangle"/>.
	/// </summary>
	/// <param name="value">The coordinates to check for inclusion in this <see cref="Rectangle"/>.</param>
	/// <returns><c>true</c> if the provided <see cref="Point"/> lies inside this <see cref="Rectangle"/>; <c>false</c> otherwise.</returns>
	public bool Contains(Point value)
	{
		return (X <= value.X) && (value.X < (X + Width)) && (Y <= value.Y) && (value.Y < (Y + Height));
	}

	/// <summary>
	/// Gets whether or not the provided <see cref="Point"/> lies within the bounds of this <see cref="Rectangle"/>.
	/// </summary>
	/// <param name="value">The coordinates to check for inclusion in this <see cref="Rectangle"/>.</param>
	/// <param name="result"><c>true</c> if the provided <see cref="Point"/> lies inside this <see cref="Rectangle"/>; <c>false</c> otherwise. As an output parameter.</param>
	public void Contains(ref Point value, out bool result)
	{
		result = (X <= value.X) && (value.X < (X + Width)) && (Y <= value.Y) && (value.Y < (Y + Height));
	}

	/// <summary>
	/// Gets whether or not the provided <see cref="Vector2"/> lies within the bounds of this <see cref="Rectangle"/>.
	/// </summary>
	/// <param name="value">The coordinates to check for inclusion in this <see cref="Rectangle"/>.</param>
	/// <returns><c>true</c> if the provided <see cref="Vector2"/> lies inside this <see cref="Rectangle"/>; <c>false</c> otherwise.</returns>
	public bool Contains(Vector2 value)
	{
		return (X <= value.X) && (value.X < (X + Width)) && (Y <= value.Y) && (value.Y < (Y + Height));
	}

	/// <summary>
	/// Gets whether or not the provided <see cref="Vector2"/> lies within the bounds of this <see cref="Rectangle"/>.
	/// </summary>
	/// <param name="value">The coordinates to check for inclusion in this <see cref="Rectangle"/>.</param>
	/// <param name="result"><c>true</c> if the provided <see cref="Vector2"/> lies inside this <see cref="Rectangle"/>; <c>false</c> otherwise. As an output parameter.</param>
	public void Contains(ref Vector2 value, out bool result)
	{
		result = (X <= value.X) && (value.X < (X + Width)) && (Y <= value.Y) && (value.Y < (Y + Height));
	}

	/// <summary>
	/// Gets whether or not the provided <see cref="Rectangle"/> lies within the bounds of this <see cref="RectangleF"/>.
	/// </summary>
	/// <param name="value">The <see cref="Rectangle"/> to check for inclusion in this <see cref="RectangleF"/>.</param>
	/// <returns><c>true</c> if the provided <see cref="Rectangle"/>'s bounds lie entirely inside this <see cref="RectangleF"/>; <c>false</c> otherwise.</returns>
	public bool Contains(Rectangle value)
	{
		return (X <= value.X) && ((value.X + value.Width) <= (X + Width)) && (Y <= value.Y) && ((value.Y + value.Height) <= (Y + Height));
	}

	/// <summary>
	/// Gets whether or not the provided <see cref="Rectangle"/> lies within the bounds of this <see cref="RectangleF"/>.
	/// </summary>
	/// <param name="value">The <see cref="Rectangle"/> to check for inclusion in this <see cref="RectangleF"/>.</param>
	/// <param name="result"><c>true</c> if the provided <see cref="Rectangle"/>'s bounds lie entirely inside this <see cref="RectangleF"/>; <c>false</c> otherwise. As an output parameter.</param>
	public void Contains(ref Rectangle value, out bool result)
	{
		result = (X <= value.X) && ((value.X + value.Width) <= (X + Width)) && (Y <= value.Y) && ((value.Y + value.Height) <= (Y + Height));
	}

	/// <summary>
	/// Gets whether or not the provided <see cref="RectangleF"/> lies within the bounds of this <see cref="RectangleF"/>.
	/// </summary>
	/// <param name="value">The <see cref="RectangleF"/> to check for inclusion in this <see cref="RectangleF"/>.</param>
	/// <returns><c>true</c> if the provided <see cref="RectangleF"/>'s bounds lie entirely inside this <see cref="RectangleF"/>; <c>false</c> otherwise.</returns>
	public bool Contains(RectangleF value)
	{
		return (X <= value.X) && ((value.X + value.Width) <= (X + Width)) && (Y <= value.Y) && ((value.Y + value.Height) <= (Y + Height));
	}

	/// <summary>
	/// Gets whether or not the provided <see cref="RectangleF"/> lies within the bounds of this <see cref="RectangleF"/>.
	/// </summary>
	/// <param name="value">The <see cref="RectangleF"/> to check for inclusion in this <see cref="RectangleF"/>.</param>
	/// <param name="result"><c>true</c> if the provided <see cref="RectangleF"/>'s bounds lie entirely inside this <see cref="RectangleF"/>; <c>false</c> otherwise. As an output parameter.</param>
	public void Contains(ref RectangleF value, out bool result)
	{
		result = (X <= value.X) && ((value.X + value.Width) <= (X + Width)) && (Y <= value.Y) && ((value.Y + value.Height) <= (Y + Height));
	}

	/// <summary>
	/// Compares whether current instance is equal to specified <see cref="Object"/>.
	/// </summary>
	/// <param name="obj">The <see cref="Object"/> to compare.</param>
	/// <returns><c>true</c> if the instances are equal; <c>false</c> otherwise.</returns>
	public override bool Equals(object? obj)
	{
		return (obj is RectangleF rect) && this == rect;
	}

	/// <summary>
	/// Compares whether current instance is equal to specified <see cref="RectangleF"/>.
	/// </summary>
	/// <param name="other">The <see cref="RectangleF"/> to compare.</param>
	/// <returns><c>true</c> if the instances are equal; <c>false</c> otherwise.</returns>
	public bool Equals(RectangleF other)
	{
		return this == other;
	}

	/// <summary>
	/// Gets the hash code of this <see cref="RectangleF"/>.
	/// </summary>
	/// <returns>Hash code of this <see cref="RectangleF"/>.</returns>
	public override int GetHashCode()
	{
		return HashCode.Combine(X, Y, Width, Height);
	}

	/// <summary>
	/// Adjusts the edges of this <see cref="Rectangle"/> by specified horizontal and vertical amounts. 
	/// </summary>
	/// <param name="horizontalAmount">Value to adjust the left and right edges.</param>
	/// <param name="verticalAmount">Value to adjust the top and bottom edges.</param>
	public void Inflate(int horizontalAmount, int verticalAmount)
	{
		X -= horizontalAmount;
		Y -= verticalAmount;
		Width += horizontalAmount * 2;
		Height += verticalAmount * 2;
	}

	/// <summary>
	/// Adjusts the edges of this <see cref="RectangleF"/> by specified horizontal and vertical amounts. 
	/// </summary>
	/// <param name="horizontalAmount">Value to adjust the left and right edges.</param>
	/// <param name="verticalAmount">Value to adjust the top and bottom edges.</param>
	public void Inflate(float horizontalAmount, float verticalAmount)
	{
		X -= (int)horizontalAmount;
		Y -= (int)verticalAmount;
		Width += (int)horizontalAmount * 2;
		Height += (int)verticalAmount * 2;
	}

	/// <summary>
	/// Gets whether or not the other <see cref="RectangleF"/> intersects with this rectangle.
	/// </summary>
	/// <param name="value">The other rectangle for testing.</param>
	/// <returns><c>true</c> if other <see cref="RectangleF"/> intersects with this rectangle; <c>false</c> otherwise.</returns>
	public bool Intersects(RectangleF value)
	{
		return value.Left < Right &&
			   Left < value.Right &&
			   value.Top < Bottom &&
			   Top < value.Bottom;
	}


	/// <summary>
	/// Gets whether or not the other <see cref="Rectangle"/> intersects with this rectangle.
	/// </summary>
	/// <param name="value">The other rectangle for testing.</param>
	/// <param name="result"><c>true</c> if other <see cref="Rectangle"/> intersects with this rectangle; <c>false</c> otherwise. As an output parameter.</param>
	public void Intersects(ref RectangleF value, out bool result)
	{
		result = value.Left < Right &&
				 Left < value.Right &&
				 value.Top < Bottom &&
				 Top < value.Bottom;
	}

	/// <summary>
	/// Creates a new <see cref="Rectangle"/> that contains overlapping region of two other rectangles.
	/// </summary>
	/// <param name="value1">The first <see cref="Rectangle"/>.</param>
	/// <param name="value2">The second <see cref="Rectangle"/>.</param>
	/// <returns>Overlapping region of the two rectangles.</returns>
	public static RectangleF Intersect(RectangleF value1, RectangleF value2)
	{
		Intersect(ref value1, ref value2, out RectangleF rectangle);
		return rectangle;
	}

	/// <summary>
	/// Creates a new <see cref="RectangleF"/> that contains overlapping region of two other rectangles.
	/// </summary>
	/// <param name="value1">The first <see cref="RectangleF"/>.</param>
	/// <param name="value2">The second <see cref="RectangleF"/>.</param>
	/// <param name="result">Overlapping region of the two rectangles as an output parameter.</param>
	public static void Intersect(ref RectangleF value1, ref RectangleF value2, out RectangleF result)
	{
		if (value1.Intersects(value2))
		{
			float right_side = Math.Min(value1.X + value1.Width, value2.X + value2.Width);
			float left_side = Math.Max(value1.X, value2.X);
			float top_side = Math.Max(value1.Y, value2.Y);
			float bottom_side = Math.Min(value1.Y + value1.Height, value2.Y + value2.Height);
			result = new RectangleF(left_side, top_side, right_side - left_side, bottom_side - top_side);
		}
		else
		{
			result = new RectangleF(0, 0, 0, 0);
		}
	}

	/// <summary>
	/// Changes the <see cref="Location"/> of this <see cref="Rectangle"/>.
	/// </summary>
	/// <param name="offsetX">The x coordinate to add to this <see cref="Rectangle"/>.</param>
	/// <param name="offsetY">The y coordinate to add to this <see cref="Rectangle"/>.</param>
	public void Offset(int offsetX, int offsetY)
	{
		X += offsetX;
		Y += offsetY;
	}

	/// <summary>
	/// Changes the <see cref="Location"/> of this <see cref="RectangleF"/>.
	/// </summary>
	/// <param name="offsetX">The x coordinate to add to this <see cref="RectangleF"/>.</param>
	/// <param name="offsetY">The y coordinate to add to this <see cref="RectangleF"/>.</param>
	public void Offset(float offsetX, float offsetY)
	{
		X += (int)offsetX;
		Y += (int)offsetY;
	}

	/// <summary>
	/// Changes the <see cref="Location"/> of this <see cref="RectangleF"/>.
	/// </summary>
	/// <param name="amount">The x and y components to add to this <see cref="RectangleF"/>.</param>
	public void Offset(Point amount)
	{
		X += amount.X;
		Y += amount.Y;
	}

	/// <summary>
	/// Changes the <see cref="Location"/> of this <see cref="RectangleF"/>.
	/// </summary>
	/// <param name="amount">The x and y components to add to this <see cref="RectangleF"/>.</param>
	public void Offset(Vector2 amount)
	{
		X += (int)amount.X;
		Y += (int)amount.Y;
	}

	/// <summary>
	/// Returns a <see cref="String"/> representation of this <see cref="RectangleF"/> in the format:
	/// {X:[<see cref="X"/>] Y:[<see cref="Y"/>] Width:[<see cref="Width"/>] Height:[<see cref="Height"/>]}
	/// </summary>
	/// <returns><see cref="String"/> representation of this <see cref="RectangleF"/>.</returns>
	public override string ToString() => $"{{X: {X}  Y: {Y}  W: {Width}  H: {Height}}}";

	/// <summary>
	/// Creates a new <see cref="RectangleF"/> that completely contains two other rectangles.
	/// </summary>
	/// <param name="value1">The first <see cref="RectangleF"/>.</param>
	/// <param name="value2">The second <see cref="RectangleF"/>.</param>
	/// <returns>The union of the two rectangles.</returns>
	public static RectangleF Union(RectangleF value1, RectangleF value2)
	{
		float x = Math.Min(value1.X, value2.X);
		float y = Math.Min(value1.Y, value2.Y);
		return new RectangleF(x, y,
							 Math.Max(value1.Right, value2.Right) - x,
								 Math.Max(value1.Bottom, value2.Bottom) - y);
	}

	/// <summary>
	/// Creates a new <see cref="RectangleF"/> that completely contains two other rectangles.
	/// </summary>
	/// <param name="value1">The first <see cref="RectangleF"/>.</param>
	/// <param name="value2">The second <see cref="RectangleF"/>.</param>
	/// <param name="result">The union of the two rectangles as an output parameter.</param>
	public static void Union(ref RectangleF value1, ref RectangleF value2, out RectangleF result)
	{
		result.X = Math.Min(value1.X, value2.X);
		result.Y = Math.Min(value1.Y, value2.Y);
		result.Width = Math.Max(value1.Right, value2.Right) - result.X;
		result.Height = Math.Max(value1.Bottom, value2.Bottom) - result.Y;
	}

	/// <summary>
	/// Deconstruction method for <see cref="RectangleF"/>.
	/// </summary>
	/// <param name="x"></param>
	/// <param name="y"></param>
	/// <param name="width"></param>
	/// <param name="height"></param>
	public void Deconstruct(out float x, out float y, out float width, out float height)
	{
		x = X;
		y = Y;
		width = Width;
		height = Height;
	}

	#endregion
}
