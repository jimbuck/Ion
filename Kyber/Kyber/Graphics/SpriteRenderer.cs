using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Assimp;

using Kyber.Assets;

using Veldrid;
using Veldrid.SPIRV;

namespace Kyber.Graphics;

public interface ISpriteRenderer
{
	void DrawRect(Color color, RectangleF destinationRectangle, Vector2 origin = default, float rotation = 0, float depth = 0);
	void DrawRect(Color color, Vector2 position, Vector2 size, Vector2 origin = default, float rotation = 0, float depth = 0);

	void DrawPoint(Color color, Vector2 position, float depth = 0);
	void DrawPoint(Color color, Vector2 position, Vector2 size, float depth = 0);

	void DrawLine(Color color, Vector2 pointA, Vector2 pointB, float thickness = 1f, float depth = 0);
	void DrawLine(Color color, Vector2 start, float length, float angle, float thickness = 1, float depth = 0);

	void Draw(Texture2D texture, RectangleF destinationRectangle, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteOptions options = SpriteOptions.None);
	void Draw(Texture2D texture, Vector2 position, Vector2 scale, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteOptions options = SpriteOptions.None);
}

internal class SpriteRenderer : ISpriteRenderer, IDisposable
{
	private const int BATCH_STEP_SIZE = 64;
	private const int BATCH_STEP_SIZE_MINUS_ONE = BATCH_STEP_SIZE - 1;
	private const int BATCH_STEP_SIZE_BIT_COMP = ~BATCH_STEP_SIZE_MINUS_ONE;
	private static readonly RectangleF _defaultScissor = new(-(1 << 22), -(1 << 22), 1 << 23, 1 << 23);
	private static readonly Vector2 VEC2_HALF = Vector2.One / 2f;

	private readonly IWindow _window;
	private readonly GraphicsDevice _graphicsDevice;
	private readonly ILogger _logger;
	private readonly IEventListener _events;

	private readonly SpriteBatchManager _batchManager;
	private Assets.Texture? _whitePixel;

	private DeviceBuffer? _vertexBuffer;

	private ResourceLayout? _instanceResourceLayout;
	private ResourceLayout? _fragmentResourceLayout;

	private Shader[]? _shaders;
	private Pipeline? _pipeline;

	private bool _beginCalled = false;

	private class SpriteBatchManager
	{
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
	}

	private class SpriteBatch
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

		public ReadOnlySpan<Instance> GetSpan() {
			Array.Sort(_items, 0, Count);
			return new(_items, 0, Count);
		}
	}

	private struct Instance : IComparable<Instance>
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
			Scale = destinationRectangle.Size.ToVector2();
			Origin = origin * Scale;
			Location = new(destinationRectangle.Location.ToVector2(), layerDepth);
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

	private class BufferContainer
	{
		public DeviceBuffer Buffer { get; set; }
		public ResourceSet InstanceSet { get; set; }
		public ResourceSet TextureSet { get; set; }

		public BufferContainer(DeviceBuffer buffer, ResourceSet instanceSet, ResourceSet textureSet)
		{
			Buffer = buffer;
			InstanceSet = instanceSet;
			TextureSet = textureSet;
		}

		public void Dispose()
		{
			Buffer.Dispose();
			InstanceSet.Dispose();
			TextureSet.Dispose(); // TODO: Check if this is needed...
		}
	}
	private readonly Dictionary<Assets.Texture, BufferContainer> _buffers;

	private const string VertexCode = @"
#version 450

layout (constant_id = 0) const bool InvertY = false;

layout(location = 0) in vec2 Position;
layout(location = 0) out vec4 fsin_Color;
layout(location = 1) out vec2 tex_coord;
layout(location = 2) out vec4 bounds;
layout(location = 3) out vec2 pos;

struct Instance 
{
	vec4 UV;
	vec4 Color;
	vec2 Scale;
	vec2 Origin;
	vec4 Location;
	vec4 Scissor;
};

layout(std430, binding = 0) readonly buffer Instances
{
	mat4 projection;
    Instance instances[];
};

mat2 makeRotation(float angle)
{
    float c = cos(angle);
    float s = sin(angle);
    return mat2(c, -s, s, c);
}

void main()
{
    Instance item = instances[gl_InstanceIndex];

    pos = Position * item.Scale;
    pos -= item.Origin;
    pos *= makeRotation(item.Location.w);
    pos += item.Location.xy;

    tex_coord = Position * item.UV.zw + item.UV.xy;
    
    // scissor bounds
    vec2 start = item.Scissor.xy;
    vec2 end = start + item.Scissor.zw;
    bounds = vec4(start, end);

    gl_Position = projection * vec4(pos, item.Location.z, 1);
    pos = gl_Position.xy;

    if(!InvertY)
        gl_Position.y = -gl_Position.y;

    fsin_Color = item.Color;
}";

	private const string FragmentCode = @"
#version 450

layout(location = 0) in vec4 fsin_Color;
layout(location = 1) in vec2 tex_coord;
layout(location = 2) in vec4 bounds;
layout(location = 3) in vec2 pos;
layout(location = 0) out vec4 fsout_Color;

layout(set = 1, binding = 0) uniform texture2D Tex;
layout(set = 1, binding = 1) uniform sampler Sampler;

void main()
{
	float left = bounds.x;
    float top = bounds.y;
    float right = bounds.z;
    float bottom = bounds.w;

    if(!(left <= pos.x && right >= pos.x &&
        top <= pos.y && bottom >= pos.y))
        discard;

    fsout_Color = fsin_Color * texture(sampler2D(Tex, Sampler), tex_coord);
}";

	public SpriteRenderer(IWindow window, IGraphicsDevice graphicsDevice, ILogger<SpriteRenderer> logger, IEventListener events)
	{
		_window = window;
		_graphicsDevice = (GraphicsDevice)graphicsDevice;
		_logger = logger;
		_events = events;

		_batchManager = new();
		_buffers = new();
	}

	public void Initialize()
	{
		if (_graphicsDevice.NoRender) return;
		if (_graphicsDevice.Internal == null)
		{
			_logger.LogWarning($"{nameof(SpriteRenderer)} automically disabled due to GraphicsDevice not being set.");
			return;
		}

		var factory = _graphicsDevice.Factory;

		TextureDescription desc = new(1, 1, 1, 1, 1, Veldrid.PixelFormat.B8_G8_R8_A8_UNorm, TextureUsage.Sampled, Veldrid.TextureType.Texture2D);
		_whitePixel = factory.CreateTexture2D(desc, "white-pixel");
		_graphicsDevice.Internal.UpdateTexture(_whitePixel, new byte[] { 255, 255, 255, 255 }, 0, 0, 0, 1, 1, 1, 0, 0);

		VertexLayoutDescription vertexLayout = new(new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));

		ShaderDescription vertexShaderDesc = new(ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(VertexCode), "main");
		ShaderDescription fragmentShaderDesc = new(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(FragmentCode), "main");

		_logger.LogInformation("Created shaders");
		_shaders = factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);

		_logger.LogInformation("Created _vertexBuffer");
		_vertexBuffer = factory.CreateBuffer(new(4 * MemUtils.SizeOf<Vector2>(), BufferUsage.VertexBuffer));
		_logger.LogInformation("Updated _vertexBuffer");
		_graphicsDevice.Internal.UpdateBuffer(_vertexBuffer, 0, new Vector2[] {
			new( 0,  0),
			new( 1,  0),
			new( 0,  1),
			new( 1,  1),
		});

		_instanceResourceLayout = factory.CreateResourceLayout(new(new ResourceLayoutElementDescription[] {
			new("Instances", ResourceKind.StructuredBufferReadOnly, ShaderStages.Vertex)
		}));

		_fragmentResourceLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(new ResourceLayoutElementDescription[] {
			new("Tex", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
			new("Sampler", ResourceKind.Sampler, ShaderStages.Fragment)
		}));

		_pipeline = factory.CreateGraphicsPipeline(new()
		{
			BlendState = BlendStateDescription.SingleAlphaBlend,
			DepthStencilState = new DepthStencilStateDescription(depthTestEnabled: true, depthWriteEnabled: true, comparisonKind: ComparisonKind.GreaterEqual),
			RasterizerState = new RasterizerStateDescription(cullMode: FaceCullMode.Back, fillMode: PolygonFillMode.Solid, frontFace: FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
			PrimitiveTopology = PrimitiveTopology.TriangleStrip,
			ResourceLayouts = new ResourceLayout[] { _instanceResourceLayout, _fragmentResourceLayout },
			ShaderSet = new(vertexLayouts: new[] { vertexLayout }, shaders: _shaders, specializations: new[] {
				new SpecializationConstant(0, _graphicsDevice.Internal.IsClipSpaceYInverted)
			}),
			Outputs = _graphicsDevice.Internal.MainSwapchain.Framebuffer.OutputDescription
		});
	}

	public void Begin(GameTime dt)
	{
		if (_graphicsDevice.NoRender) return;
		if (_graphicsDevice.Internal == null || _graphicsDevice.CommandList == null) throw new InvalidOperationException("Begin cannot be called until the GraphicsDevice has been initialized.");

		if (_beginCalled) throw new InvalidOperationException("Begin cannot be called again until End has been successfully called.");

		_batchManager.Clear();
		_beginCalled = true;
	}

	public void DrawRect(Color color, RectangleF destinationRectangle, Vector2 origin = default, float rotation = 0, float depth = 0f)
	{
		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling Draw.");

		_addSprite(_whitePixel!, color, new RectangleF(0, 0, 1, 1), destinationRectangle, origin, rotation, depth, _defaultScissor, SpriteOptions.None);
	}

	public void DrawRect(Color color, Vector2 position, Vector2 size, Vector2 origin = default, float rotation = 0, float depth = 0f)
	{
		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling Draw.");

		_addSprite(_whitePixel!, color, new RectangleF(0, 0, 1, 1), new RectangleF(position.X, position.Y, size.X, size.Y), origin, rotation, depth, _defaultScissor, SpriteOptions.None);
	}

	public void DrawPoint(Color color, Vector2 position, Vector2 size, float depth = 0)
	{
		DrawRect(color, position, size, origin: VEC2_HALF, depth: depth);
	}

	public void DrawPoint(Color color, Vector2 position, float depth = 0)
	{
		DrawRect(color, position, Vector2.One, origin: VEC2_HALF, depth: depth);
	}

	public void DrawLine(Color color, Vector2 pointA, Vector2 pointB, float thickness = 1f, float depth = 0)
	{
		var diff = pointB - pointA;
		var length = MathF.Sqrt(Vector2.Dot(diff, diff));
		var angle = MathF.Atan2(diff.Y, diff.X);
		DrawLine(color, pointA, length: length, angle: angle, thickness: thickness, depth: depth);
	}

	public void DrawLine(Color color, Vector2 start, float length, float angle, float thickness = 1, float depth = 0)
	{
		var rect = new RectangleF(start.X, start.Y, length, thickness);
		DrawRect(color, rect, new Vector2(0, 0.5f), rotation: angle, depth: depth);
	}

	public void Draw(Texture2D texture, RectangleF destinationRectangle, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteOptions options = SpriteOptions.None)
	{
		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling Draw.");

		if (color == default) color = Color.White;
		if (sourceRectangle.Size.IsEmpty) sourceRectangle = new RectangleF(0f, 0f, texture.Width, texture.Height);

		_addSprite(texture, color, sourceRectangle, destinationRectangle, origin, rotation, depth, _defaultScissor, options);
	}

	public void Draw(Texture2D texture, Vector2 position, Vector2 scale, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteOptions options = SpriteOptions.None)
	{
		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling Draw.");

		if (color == default) color = Color.White;
		if (sourceRectangle.Size.IsEmpty) sourceRectangle = new RectangleF(0f, 0f, texture.Width, texture.Height);

		_addSprite(texture, color, sourceRectangle, new RectangleF(position.X, position.Y, scale.X * texture.Width, scale.Y * texture.Height), origin, rotation, depth, _defaultScissor, options);
	}

	public unsafe void End()
	{
		if (_graphicsDevice.NoRender) return;
		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling End.");

		_beginCalled = false;

		if (_batchManager.IsEmpty) return;

		var matrixSize = (int)MemUtils.SizeOf<System.Numerics.Matrix4x4>();

		_graphicsDevice.CommandList.SetPipeline(_pipeline);
		foreach (var (texture, group) in _batchManager)
		{
			var pair = _getBuffer(texture, group.Count + matrixSize);
			var mapped = _graphicsDevice.Internal.Map(pair.Buffer, MapMode.Write);
			MemUtils.Set(mapped.Data, _graphicsDevice.ProjectionMatrix, 1);
			MemUtils.Copy(mapped.Data + matrixSize, group.GetSpan());

			_graphicsDevice.Internal.Unmap(pair.Buffer);

			_graphicsDevice.CommandList.SetVertexBuffer(0, _vertexBuffer);
			_graphicsDevice.CommandList.SetGraphicsResourceSet(0, pair.InstanceSet);
			_graphicsDevice.CommandList.SetGraphicsResourceSet(1, pair.TextureSet);
			_graphicsDevice.CommandList.Draw(4, (uint)group.Count, 0, 0);
		}
	}

	private BufferContainer _getBuffer(Assets.Texture texture, int count)
	{
		var structSize = MemUtils.SizeOf<Instance>();
		var size = ((count + BATCH_STEP_SIZE_MINUS_ONE) & BATCH_STEP_SIZE_BIT_COMP) * structSize;
		var bci = new BufferDescription((uint)size, BufferUsage.StructuredBufferReadOnly | BufferUsage.Dynamic, structSize);

		if (!_buffers.TryGetValue(texture, out var pair))
		{
			var buffer = _graphicsDevice.Factory.CreateBuffer(bci);
			pair = new(buffer,
				_graphicsDevice.Factory.CreateResourceSet(new ResourceSetDescription(_instanceResourceLayout, buffer)),
				_graphicsDevice.Factory.CreateResourceSet(new ResourceSetDescription(_fragmentResourceLayout, (Veldrid.Texture)texture, _graphicsDevice.Internal.LinearSampler))
				);

			_buffers[texture] = pair;
		}
		else if (size > pair.Buffer.SizeInBytes)
		{
			pair.Dispose();

			pair.Buffer = _graphicsDevice.Factory.CreateBuffer(bci);
			pair.InstanceSet = _graphicsDevice.Factory.CreateResourceSet(new ResourceSetDescription(_instanceResourceLayout, pair.Buffer));
			pair.TextureSet = _graphicsDevice.Factory.CreateResourceSet(new ResourceSetDescription(_fragmentResourceLayout, (Veldrid.Texture)texture, _graphicsDevice.Internal.LinearSampler));
			_buffers[texture] = pair;
		}

		return pair;
	}

	public void Dispose()
	{
		_pipeline?.Dispose();
		if (_shaders != null) foreach (var shader in _shaders) shader.Dispose();

		_vertexBuffer?.Dispose();
	}

	private void _addSprite(Assets.Texture texture, Color color, RectangleF sourceRect, RectangleF destinationRect, Vector2 origin, float rotation, float depth, RectangleF scissor, SpriteOptions options)
	{
		ref var instance = ref _batchManager.Add(texture);

		instance.Update(texture.Size, destinationRect, sourceRect, color, rotation, origin, depth, _transformRectF(scissor, _graphicsDevice.ProjectionMatrix), options);
	}

	private static RectangleF _transformRectF(RectangleF rect, System.Numerics.Matrix4x4 matrix)
	{
		var pos = Vector4.Transform(new Vector4(rect.X, rect.Y, 0, 1), matrix);
		var size = Vector4.Transform(new Vector4(rect.X + rect.Width, rect.Y + rect.Height, 0, 1), matrix);
		return new(pos.X, pos.Y, size.X - pos.X, size.Y - pos.Y);
	}
}

