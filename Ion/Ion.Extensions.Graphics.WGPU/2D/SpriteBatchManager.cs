using System.Numerics;
using System.Runtime.InteropServices;

using Ion.Extensions.Assets;

using WebGPU;

namespace Ion.Extensions.Graphics;

internal class SpriteBatchManager
{
	public const int BATCH_STEP_SIZE = 64;
	public const int BATCH_STEP_SIZE_MINUS_ONE = BATCH_STEP_SIZE - 1;
	public const int BATCH_STEP_SIZE_BIT_COMP = ~BATCH_STEP_SIZE_MINUS_ONE;

	public static int INSTANCE_SIZE = (int)MemUtils.SizeOf<SpriteInstance>();

	private readonly Stack<SpriteBatch> _batchPool = new();
	private readonly Dictionary<Texture2D, SpriteBatch> _batches = [];

	public bool IsEmpty { get; private set; }

	public ref SpriteInstance Add(Texture2D texture)
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

	public Dictionary<Texture2D, SpriteBatch>.Enumerator GetEnumerator() => _batches.GetEnumerator();

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

	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public struct SpriteInstance(in Vector3 position, in float rotation, in Vector2 origin, in Vector4 uv, in Vector2 scale, in Color color, in Vector4 scissor) : IComparable<SpriteInstance>
	{
		public static unsafe readonly int SizeInBytes = sizeof(SpriteInstance);
		public static WGPUVertexAttribute[] VertexAttributes => [
			new WGPUVertexAttribute(WGPUVertexFormat.Float32x4, 0, 0),
			new WGPUVertexAttribute(WGPUVertexFormat.Float32x4, 16, 1),
			new WGPUVertexAttribute(WGPUVertexFormat.Float32x4, 32, 2),
			new WGPUVertexAttribute(WGPUVertexFormat.Float32x3, 48, 3),
			new WGPUVertexAttribute(WGPUVertexFormat.Float32, 60, 4),
			new WGPUVertexAttribute(WGPUVertexFormat.Float32x2, 64, 5),
			new WGPUVertexAttribute(WGPUVertexFormat.Float32x2, 72, 6),
		];

		public Color Color = color;
		public Vector4 UV = uv;
		public Vector4 Scissor = scissor;
		public Vector3 Position = position;
		public float Rotation = rotation;
		public Vector2 Origin = origin;
		public Vector2 Scale = scale;

		public void Update(Vector2 textureSize, RectangleF destinationRectangle, RectangleF sourceRectangle, Color color, float rotation, Vector2 origin, float layerDepth, Vector4 scissor, SpriteEffect options)
		{
			var sourceSize = new Vector2(sourceRectangle.Width, sourceRectangle.Height) / textureSize;
			var pos = new Vector2(sourceRectangle.X, sourceRectangle.Y) / textureSize;

			UV = _createUV(options, sourceSize, pos);
			Color = color;
			Scale = destinationRectangle.Size;
			Origin = origin * Scale;
			Position = new(destinationRectangle.Location, layerDepth);
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
			return (int)(other.Position.Z - this.Position.Z);
		}
	}
}


