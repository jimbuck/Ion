namespace Kyber.Graphics;

internal class SpriteBatchManager
{
	public const int BATCH_STEP_SIZE = 64;
	public const int BATCH_STEP_SIZE_MINUS_ONE = BATCH_STEP_SIZE - 1;
	public const int BATCH_STEP_SIZE_BIT_COMP = ~BATCH_STEP_SIZE_MINUS_ONE;

	public static int INSTANCE_SIZE = (int)MemUtils.SizeOf<Instance>();

	private readonly Stack<SpriteBatch> _batchPool;
	private readonly Dictionary<Assets.Texture, SpriteBatch> _batches;

	public bool IsEmpty => !_batches.Any(b => b.Value.Count > 0);

	public SpriteBatchManager()
	{
		_batches = new();
		_batchPool = new();
	}

	public ref Instance Add(Assets.Texture texture)
	{
		if (!_batches.TryGetValue(texture, out var group))
		{
			group = _rentSpriteBatch();
			group.Clear();
			_batches[texture] = group;
		}

		return ref group.Add();
	}

	public void Clear()
	{
		foreach (var group in this) _releaseSpriteBatch(group.Value);
		_batches.Clear();
	}

	public Dictionary<Assets.Texture, SpriteBatch>.Enumerator GetEnumerator() => _batches.GetEnumerator();

	private SpriteBatch _rentSpriteBatch()
	{
		if (!_batchPool.TryPop(out var group)) group = new();
		return group;
	}

	private void _releaseSpriteBatch(SpriteBatch group) => _batchPool.Push(group);

	public static uint GetBatchSize(int count)
	{
		return (uint)(((count + BATCH_STEP_SIZE_MINUS_ONE) & BATCH_STEP_SIZE_BIT_COMP) * Instance.SizeInBytes);
	}

	public class SpriteBatch
	{
		internal Instance[] _items;

		public int Count { get; private set; }

		public SpriteBatch()
		{
			_items = new Instance[BATCH_STEP_SIZE];
		}

		public ref Instance Add()
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

		public ReadOnlySpan<Instance> GetSpan()
		{
			//Array.Sort(_items, 0, Count);
			return new(_items, 0, Count);
		}
	}

	public struct Instance : IComparable<Instance>
	{
		public Vector4 UV;
		public Color Color;
		public Vector2 Scale;
		public Vector2 Origin;
		public Vector3 Location;
		public float Rotation;
		public RectangleF Scissor;

		public static uint SizeInBytes => MemUtils.SizeOf<Instance>();

		public void Update(Vector2 textureSize, RectangleF destinationRectangle, RectangleF sourceRectangle, Color color, float rotation, Vector2 origin, float layerDepth, RectangleF scissor, SpriteOptions options)
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
		private static Vector4 _createUV(SpriteOptions options, Vector2 sourceSize, Vector2 sourceLocation)
		{
			if (options != SpriteOptions.None)
			{
				// flipX
				if (options.HasFlag(SpriteOptions.FlipHorizontally))
				{
					sourceLocation.X += sourceSize.X;
					sourceSize.X *= -1;
				}

				// flipY
				if (options.HasFlag(SpriteOptions.FlipVertically))
				{
					sourceLocation.Y += sourceSize.Y;
					sourceSize.Y *= -1;
				}
			}

			return new(sourceLocation.X, sourceLocation.Y, sourceSize.X, sourceSize.Y);
		}

		public int CompareTo(Instance other)
		{
			return (int)(other.Location.Z - this.Location.Z);
		}
	}
}

