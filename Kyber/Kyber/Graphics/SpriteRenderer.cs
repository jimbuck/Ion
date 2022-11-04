using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Kyber.Assets;
using Veldrid;
using Veldrid.SPIRV;

namespace Kyber.Graphics;

public interface ISpriteRenderer
{
	void Begin();

	void DrawRect(Color color, RectangleF destinationRectangle, Vector2 origin = default, float rotation = 0, float depth = 0);
	void DrawRect(Color color, Vector2 position, Vector2 size, Vector2 origin = default, float rotation = 0, float depth = 0);

	void DrawPoint(Color color, Vector2 position, float depth = 0);
	void DrawPoint(Color color, Vector2 position, Vector2 size, float depth = 0);

	void DrawLine(Color color, Vector2 pointA, Vector2 pointB, float thickness = 1f, float depth = 0);
	void DrawLine(Color color, Vector2 start, float length, float angle, float thickness = 1, float depth = 0);

	void Draw(Assets.Texture texture, Rectangle destinationRectangle, Rectangle? sourceRectangle, Color? color, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteOptions options = SpriteOptions.None);
	void Draw(Assets.Texture texture, Vector2 position, Vector2 scale, Rectangle? sourceRectangle, Color? color, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteOptions options = SpriteOptions.None);

	void End();
}

internal class SpriteRenderer : ISpriteRenderer, IDisposable
{
	private const int MAX_SPRITE_COUNT = 2048;
	private static readonly Vector2 VEC2_HALF = Vector2.One / 2f;

	private readonly IWindow _window;
	private readonly GraphicsDevice _graphicsDevice;
	private readonly ILogger _logger;
	private readonly IEventListener _events;

	private DeviceBuffer? _matrixBuffer;
	private DeviceBuffer? _vertexBuffer;
	private DeviceBuffer? _instanceBuffer;

	private ResourceSet? _instanceResourceSet;
	private ResourceLayout? _instanceResourceLayout;
	private ResourceSet? _viewProjResourceSet;
	private ResourceLayout? _viewProjResourceLayout;

	private Shader[]? _shaders;
	private Pipeline? _pipeline;

	private readonly int _batchStepSize = 256;

	private Instance[] _instances;

	private int _instanceCount = 0;
	private bool _beginCalled = false;

	private const string VertexCode = @"
#version 450

layout (constant_id = 0) const bool InvertY = false;

layout(location = 0) in vec2 Position;
layout(location = 0) out vec4 fsin_Color;
layout(location = 1) out vec2 tex_coord;
layout(location = 2) out vec4 bounds;
layout(location = 3) out vec2 pos;

layout(set = 0, binding = 0) uniform MVP
{
    mat4 projection;
};

struct Instance 
{
	vec4 UV;
	vec4 Color;
	vec2 Scale;
	vec2 Origin;
	vec4 Location;
	vec4 Scissor;
};

layout(std430, binding = 1) readonly buffer Instances
{
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

void main()
{
	float left = bounds.x;
    float top = bounds.y;
    float right = bounds.z;
    float bottom = bounds.w;

    //if(!(left <= pos.x && right >= pos.x &&
    //    top <= pos.y && bottom >= pos.y))
    //    discard;

    fsout_Color = fsin_Color;
}";

	public bool IsEnabled { get; set; } = true;

	private static readonly RectangleF _defaultScissor = new(-(1 << 22), -(1 << 22), 1 << 23, 1 << 23);

	public SpriteRenderer(IWindow window, IGraphicsDevice graphicsDevice, ILogger<SpriteRenderer> logger, IEventListener events)
	{
		_window = window;
		_graphicsDevice = (GraphicsDevice)graphicsDevice;
		_logger = logger;
		_events = events;

		_instances = new Instance[_batchStepSize];
	}

	public void Initialize()
	{
		if (_graphicsDevice.Internal == null)
		{
			IsEnabled = false;
			_logger.LogWarning($"{nameof(SpriteRenderer)} automically disabled due to GraphicsDevice not being set.");
			return;
		}

		var factory = _graphicsDevice.Factory;

		VertexLayoutDescription vertexLayout = new(new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));

		ShaderDescription vertexShaderDesc = new(ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(VertexCode), "main");
		ShaderDescription fragmentShaderDesc = new(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(FragmentCode), "main");

		_matrixBuffer = factory.CreateBuffer(new(64, BufferUsage.UniformBuffer));
		_viewProjResourceLayout = factory.CreateResourceLayout(new(new ResourceLayoutElementDescription("MVP", ResourceKind.UniformBuffer, ShaderStages.Vertex)));
		_viewProjResourceSet = factory.CreateResourceSet(new(_viewProjResourceLayout, _matrixBuffer));

		_instanceResourceLayout = factory.CreateResourceLayout(new(new ResourceLayoutElementDescription[] { new("Instances", ResourceKind.StructuredBufferReadOnly, ShaderStages.Vertex) }));
		_setupInstanceBuffer();

		_shaders = factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);

		_pipeline = factory.CreateGraphicsPipeline(new()
		{
			BlendState = BlendStateDescription.SingleAlphaBlend,
			DepthStencilState = new DepthStencilStateDescription(depthTestEnabled: true, depthWriteEnabled: true, comparisonKind: ComparisonKind.GreaterEqual),
			RasterizerState = new RasterizerStateDescription(cullMode: FaceCullMode.None, fillMode: PolygonFillMode.Solid, frontFace: FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
			PrimitiveTopology = PrimitiveTopology.TriangleStrip,
			ResourceLayouts = new ResourceLayout[] { _viewProjResourceLayout, _instanceResourceLayout },
			ShaderSet = new(vertexLayouts: new [] { vertexLayout }, shaders: _shaders, specializations: new[] { new SpecializationConstant(0, _graphicsDevice.Internal.IsClipSpaceYInverted) }),
			Outputs = _graphicsDevice.Internal.SwapchainFramebuffer.OutputDescription
		});
		
		_vertexBuffer = factory.CreateBuffer(new(4 * MemUtils.SizeOf<Vector2>(), BufferUsage.VertexBuffer));
		_graphicsDevice.Internal.UpdateBuffer(_vertexBuffer, 0, new Vector2[] { 
			new( 0,  0),
			new( 1,  0),
			new( 0,  1),
			new( 1,  1),
		});
	}

	public void Begin()
	{
		if (_graphicsDevice.Internal == null || _graphicsDevice.CommandList == null) throw new InvalidOperationException("Begin cannot be called until the GraphicsDevice has been initialized.");

		if (_beginCalled) throw new InvalidOperationException("Begin cannot be called again until End has been successfully called.");

		_updateMatricies();

		_instanceCount = 0;
		_beginCalled = true;
	}

	public void DrawRect(Color color, RectangleF destinationRectangle, Vector2 origin = default, float rotation = 0, float depth = 0f)
	{
		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling Draw.");

		_addSprite(color, new RectangleF(0, 0, 1, 1), destinationRectangle, origin, rotation, depth, _defaultScissor, SpriteOptions.None);
	}

	public void DrawRect(Color color, Vector2 position, Vector2 size, Vector2 origin = default, float rotation = 0, float depth = 0f)
	{
		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling Draw.");

		_addSprite(color, new RectangleF(0, 0, 1, 1), new RectangleF(position.X, position.Y, size.X, size.Y), origin, rotation, depth, _defaultScissor, SpriteOptions.None);
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

	public void Draw(Assets.Texture texture, Rectangle destinationRectangle, Rectangle? sourceRectangle, Color? color, Vector2 origin = default, float rotation = 0, float layerDepth = 0, SpriteOptions options = SpriteOptions.None)
	{
		throw new NotImplementedException();
	}

	public void Draw(Assets.Texture texture, Vector2 position, Vector2 scale, Rectangle? sourceRectangle, Color? color, Vector2 origin = default, float rotation = 0, float layerDepth = 0, SpriteOptions options = SpriteOptions.None)
	{
		throw new NotImplementedException();
	}

	public void End()
	{
		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling End.");

		_beginCalled = false;

		if (_instanceCount == 0) return;

		_graphicsDevice.CommandList.SetPipeline(_pipeline);
		_updateInstanceBuffer();
		_graphicsDevice.CommandList.SetVertexBuffer(0, _vertexBuffer);
		_graphicsDevice.CommandList.SetGraphicsResourceSet(0, _viewProjResourceSet);
		_graphicsDevice.CommandList.SetGraphicsResourceSet(1, _instanceResourceSet);
		_graphicsDevice.CommandList.Draw(4, (uint)_instanceCount, 0, 0);
		_instanceCount = 0;
	}

	public void Dispose()
	{
		_pipeline?.Dispose();
		if (_shaders != null) foreach (var shader in _shaders) shader.Dispose();

		_vertexBuffer?.Dispose();
	}

	private void _updateMatricies()
	{
		_graphicsDevice.CommandList.UpdateBuffer(_matrixBuffer, 0, _graphicsDevice.ProjectionMatrix);
	}

	private void _addSprite(Color color, RectangleF sourceRect, RectangleF destinationRect, Vector2 origin, float rotation, float depth, RectangleF scissor, SpriteOptions options)
	{
		if (_instanceCount >= _instances.Length)
		{
			Array.Resize(ref _instances, _instances.Length + _batchStepSize);
			_setupInstanceBuffer();
		}
		
		_instances[_instanceCount].Update(Vector2.One, destinationRect, sourceRect, color, rotation, origin, depth, _transformRectF(scissor, _graphicsDevice.ProjectionMatrix), options);

		_instanceCount++;
	}

	private void _setupInstanceBuffer()
	{
		var factory = _graphicsDevice.Factory;

		_instanceBuffer?.Dispose();
		_instanceResourceSet?.Dispose();

		_instanceBuffer = factory.CreateBuffer(new((uint)_instances.Length * Instance.SizeInBytes, BufferUsage.StructuredBufferReadOnly | BufferUsage.Dynamic, Instance.SizeInBytes));
		_instanceResourceSet = factory.CreateResourceSet(new(_instanceResourceLayout, _instanceBuffer));
	}

	private unsafe void _updateInstanceBuffer()
	{
		var mapped = _graphicsDevice.Internal.Map(_instanceBuffer, MapMode.Write);
		var sizeInBytes = (uint)_instanceCount * MemUtils.SizeOf<Instance>();

		fixed (Instance* instances = &_instances[0])
		{
			MemUtils.Copy(mapped.Data, (IntPtr)instances, _instanceCount * MemUtils.SizeOf<Instance>());
		}	

		_graphicsDevice.Internal.Unmap(_instanceBuffer);
	}

	private struct Instance
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
			Origin = origin;
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
	}

	private static RectangleF _transformRectF(RectangleF rect, Matrix4x4 matrix)
	{
		var pos = Vector4.Transform(new Vector4(rect.X, rect.Y, 0, 1), matrix);
		var size = Vector4.Transform(new Vector4(rect.X + rect.Width, rect.Y + rect.Height, 0, 1), matrix);
		return new(pos.X, pos.Y, size.X - pos.X, size.Y - pos.Y);
	}
}

