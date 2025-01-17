﻿using System.Numerics;

using Ion.Extensions.Assets;

namespace Ion.Extensions.Graphics;

internal class SpriteBatchManager
{
	public const int BATCH_STEP_SIZE = 64;
	public const int BATCH_STEP_SIZE_MINUS_ONE = BATCH_STEP_SIZE - 1;
	public const int BATCH_STEP_SIZE_BIT_COMP = ~BATCH_STEP_SIZE_MINUS_ONE;

	public static int INSTANCE_SIZE = (int)MemUtils.SizeOf<SpriteInstance>();

	private readonly Stack<SpriteBatch> _batchPool;
	private readonly Dictionary<ITexture2D, SpriteBatch> _batches;

	public bool IsEmpty { get; private set; }

	public SpriteBatchManager()
	{
		_batches = new();
		_batchPool = new();
	}

	public ref SpriteInstance Add(ITexture2D texture)
	{
		if (!_batches.TryGetValue(texture, out var group))
		{
			group = _rentSpriteBatch();
			group.Clear();
			_batches[texture] = group;
		}

		IsEmpty = false;
		return ref group.Add();
	}

	public void Clear()
	{
		foreach (var group in _batches.Values) _releaseSpriteBatch(group);
		_batches.Clear();
		IsEmpty = true;
	}

	public Dictionary<ITexture2D, SpriteBatch>.Enumerator GetEnumerator() => _batches.GetEnumerator();

	private SpriteBatch _rentSpriteBatch()
	{
		if (!_batchPool.TryPop(out var group)) group = new();
		return group;
	}

	private void _releaseSpriteBatch(SpriteBatch group) => _batchPool.Push(group);

	public static uint GetBatchSize(int count)
	{
		return (uint)(((count + BATCH_STEP_SIZE_MINUS_ONE) & BATCH_STEP_SIZE_BIT_COMP) * SpriteInstance.SizeInBytes);
	}

	public class SpriteBatch
	{
		internal SpriteInstance[] _items;

		public int Count { get; private set; }

		public SpriteBatch()
		{
			_items = new SpriteInstance[BATCH_STEP_SIZE];
		}

		public ref SpriteInstance Add()
		{
			if (Count >= _items.Length)
			{
				var lastSize = _items.Length;
				var newSize = (lastSize + lastSize / 2 + 63) & (~63);
				Array.Resize(ref _items, newSize);
			}

			return ref _items[Count++];
		}

		public void Clear()
		{
			Count = 0;
		}

		public ReadOnlySpan<SpriteInstance> GetSpan()
		{
			return new(_items, 0, Count);
		}
	}

	public struct SpriteInstance : IComparable<SpriteInstance>
	{
		public Vector4 UV;
		public Color Color;
		public Vector2 Scale;
		public Vector2 Origin;
		public Vector3 Location;
		public float Rotation;
		public RectangleF Scissor;

		public static uint SizeInBytes => MemUtils.SizeOf<SpriteInstance>();

		public void Update(Vector2 textureSize, RectangleF destinationRectangle, RectangleF sourceRectangle, Color color, float rotation, Vector2 origin, float layerDepth, RectangleF scissor, SpriteEffect options)
		{
			var sourceSize = new Vector2(sourceRectangle.Width, sourceRectangle.Height) / textureSize;
			var pos = new Vector2(sourceRectangle.X, sourceRectangle.Y) / textureSize;

			UV = _createUV(options, sourceSize, pos);
			Color = color;
			Scale = destinationRectangle.Size;
			Origin = origin * Scale;
			Location = new(destinationRectangle.Location, layerDepth);
			Rotation = rotation;
			Scissor = scissor;
		}
		private static Vector4 _createUV(SpriteEffect options, Vector2 sourceSize, Vector2 sourceLocation)
		{
			if (options != SpriteEffect.None)
			{
				// flipX
				if (options.HasFlag(SpriteEffect.FlipHorizontally))
				{
					sourceLocation.X += sourceSize.X;
					sourceSize.X *= -1;
				}

				// flipY
				if (options.HasFlag(SpriteEffect.FlipVertically))
				{
					sourceLocation.Y += sourceSize.Y;
					sourceSize.Y *= -1;
				}
			}

			return new(sourceLocation.X, sourceLocation.Y, sourceSize.X, sourceSize.Y);
		}

		public int CompareTo(SpriteInstance other)
		{
			return (int)(other.Location.Z - this.Location.Z);
		}
	}
}

