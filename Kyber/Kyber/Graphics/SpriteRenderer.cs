//using Veldrid;
using Veldrid.SPIRV;

namespace Kyber.Graphics;

public interface ISpriteRenderer
{
	void Begin();

	void Draw(Color color, Rectangle destinationRectangle, Vector2? origin, float rotation = 0, float layerDepth = 1);
	void Draw(Color color, Vector2 position, Vector2 size, Vector2? origin, float rotation = 0, float layerDepth = 1);

	void Draw(Texture texture, Rectangle destinationRectangle, Rectangle? sourceRectangle, Color? color, Vector2? origin, SpriteOptions options = SpriteOptions.None, float rotation = 0, float layerDepth = 1);
	void Draw(Texture texture, Vector2 position, Vector2 scale, Rectangle? sourceRectangle, Color? color, Vector2? origin, SpriteOptions options = SpriteOptions.None, float rotation = 0, float layerDepth = 1);

	void End();
}

public class SpriteRenderer : ISpriteRenderer
{
	private readonly GraphicsDevice _graphicsDevice;
	private readonly ILogger _logger;

	private Veldrid.CommandList? _commandList;
	private Veldrid.DeviceBuffer? _vertexBuffer;
	private Veldrid.DeviceBuffer? _indexBuffer;
	private Veldrid.Shader[]? _shaders;
	private Veldrid.Pipeline? _pipeline;

	private bool _beginCalled = false;

	private const string VertexCode = @"
#version 450

layout(location = 0) in vec2 Position;
layout(location = 1) in vec4 Color;

layout(location = 0) out vec4 fsin_Color;

void main()
{
    gl_Position = vec4(Position, 0, 1);
    fsin_Color = Color;
}";

	private const string FragmentCode = @"
#version 450

layout(location = 0) in vec4 fsin_Color;
layout(location = 0) out vec4 fsout_Color;

void main()
{
    fsout_Color = fsin_Color;
}";

	private record struct VertexPositionColor(Vector2 Position, Color Color)
	{
		public const uint SizeInBytes = 24;

		public VertexPositionColor(float x, float y, Color color) : this(new Vector2(x, y), color) { }
	}

	public bool IsEnabled { get; set; } = true;

	public SpriteRenderer(IGraphicsDevice graphicsDevice, ILogger<SpriteRenderer> logger)
	{
		_graphicsDevice = (GraphicsDevice)graphicsDevice;
		_logger = logger;
	}

	public void Initialize()
	{
		if (_graphicsDevice.Internal == null)
		{
			IsEnabled = false;
			_logger.LogWarning($"{nameof(SpriteRenderer)} automically disabled due to GraphicsDevice not being set.");
			return;
		}

		Veldrid.ResourceFactory factory = _graphicsDevice.Internal.ResourceFactory;

		VertexPositionColor[] quadVertices = {
			new(-0.75f, 0.75f, Color.Red),
			new(0.75f, 0.75f, Color.Green),
			new(-0.75f, -0.75f, Color.Blue),
			new(0.75f, -0.75f, Color.Yellow)
		};

		ushort[] quadIndices = { 0, 1, 2, 3 };

		_vertexBuffer = factory.CreateBuffer(new Veldrid.BufferDescription(4 * VertexPositionColor.SizeInBytes, Veldrid.BufferUsage.VertexBuffer));
		_indexBuffer = factory.CreateBuffer(new Veldrid.BufferDescription(4 * sizeof(ushort), Veldrid.BufferUsage.IndexBuffer));

		_graphicsDevice.Internal.UpdateBuffer(_vertexBuffer, 0, quadVertices);
		_graphicsDevice.Internal.UpdateBuffer(_indexBuffer, 0, quadIndices);

		Veldrid.VertexLayoutDescription vertexLayout = new(
			new Veldrid.VertexElementDescription("Position", Veldrid.VertexElementSemantic.TextureCoordinate, Veldrid.VertexElementFormat.Float2),
			new Veldrid.VertexElementDescription("Color", Veldrid.VertexElementSemantic.TextureCoordinate, Veldrid.VertexElementFormat.Float4));

		Veldrid.ShaderDescription vertexShaderDesc = new(Veldrid.ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(VertexCode), "main");
		Veldrid.ShaderDescription fragmentShaderDesc = new(Veldrid.ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(FragmentCode), "main");

		_shaders = factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);

		_pipeline = factory.CreateGraphicsPipeline(new()
		{
			BlendState = Veldrid.BlendStateDescription.SingleOverrideBlend,
			DepthStencilState = new Veldrid.DepthStencilStateDescription(depthTestEnabled: true, depthWriteEnabled: true, comparisonKind: Veldrid.ComparisonKind.LessEqual),
			RasterizerState = new Veldrid.RasterizerStateDescription(cullMode: Veldrid.FaceCullMode.Back, fillMode: Veldrid.PolygonFillMode.Solid, frontFace: Veldrid.FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
			PrimitiveTopology = Veldrid.PrimitiveTopology.TriangleStrip,
			ResourceLayouts = Array.Empty<Veldrid.ResourceLayout>(),
			ShaderSet = new Veldrid.ShaderSetDescription(new Veldrid.VertexLayoutDescription[] { vertexLayout }, _shaders),
			Outputs = _graphicsDevice.Internal.SwapchainFramebuffer.OutputDescription
		});

		_commandList = factory.CreateCommandList();
	}

	public void Begin()
	{
		if (_graphicsDevice.Internal == null || _commandList == null) throw new InvalidOperationException("Begin cannot be called until the GraphicsDevice has been initialized.");

		if (_beginCalled) throw new InvalidOperationException("Begin cannot be called again until End has been successfully called.");

		_commandList.Begin();
		_beginCalled = true;
	}

	public void Draw(Color color, Rectangle destinationRectangle, Vector2? origin, float rotation = 0, float layerDepth = 1)
	{
		throw new NotImplementedException();
	}

	public void Draw(Color color, Vector2 position, Vector2 size, Vector2? origin, float rotation = 0, float layerDepth = 1)
	{
		throw new NotImplementedException();
	}

	public void Draw(Texture texture, Rectangle destinationRectangle, Rectangle? sourceRectangle, Color? color, Vector2? origin, SpriteOptions options = SpriteOptions.None, float rotation = 0, float layerDepth = 1)
	{
		throw new NotImplementedException();
	}

	public void Draw(Texture texture, Vector2 position, Vector2 scale, Rectangle? sourceRectangle, Color? color, Vector2? origin, SpriteOptions options = SpriteOptions.None, float rotation = 0, float layerDepth = 1)
	{
		throw new NotImplementedException();
	}

	public void End()
	{
		if (_graphicsDevice.Internal == null || _commandList == null) return;

		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling End.");

		_beginCalled = false;

		_commandList.End();
		_graphicsDevice.Internal.SubmitCommands(_commandList);
	}
}
