using System.Numerics;
using System.Text;
using Ion.Graphics;

using Veldrid;
using Veldrid.SPIRV;


namespace Ion.Examples.Veldrid;

public class QuadRendererSystem : IInitializeSystem, IRenderSystem, IDisposable
{
	private readonly IWindow _window;
    private readonly IGraphicsContext _graphicsContext;
    private readonly ILogger _logger;

	private CommandList? _commandList;
    private DeviceBuffer? _vertexBuffer;
    private DeviceBuffer? _indexBuffer;
    private Shader[]? _shaders;
    private Pipeline? _pipeline;

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

    public bool IsEnabled { get; set; } = true;

    public QuadRendererSystem(IWindow window, IGraphicsContext graphicsDevice, ILogger<QuadRendererSystem> logger)
    {
		_window = window;
		_graphicsContext = graphicsDevice;
        _logger = logger;
    }

    public void Initialize()
    {
		if (_graphicsContext.GraphicsDevice is null) return;

		ResourceFactory factory = _graphicsContext.GraphicsDevice.ResourceFactory;

		_commandList = factory.CreateCommandList();

		(Vector2 position, Color color)[] points = {
			(new(100f, 800f), Color.Red),   // TOP LEFT
			(new(800f, 800f), Color.Yellow),   // TOP RIGHT
			(new(100f, 100f), Color.Blue),   // BOTTOM LEFT
			(new(800f, 100f), Color.Green)    // BOTTOM RIGHT
		};

		VertexPositionColor[] quadVertices = points.Select(p => new VertexPositionColor((2 * p.position / _window.Size) - Vector2.One, p.color)).ToArray();

        ushort[] quadIndices = { 0, 1, 2, 3 };

        _vertexBuffer = factory.CreateBuffer(new BufferDescription(4 * VertexPositionColor.SizeInBytes, BufferUsage.VertexBuffer));
        _indexBuffer = factory.CreateBuffer(new BufferDescription(4 * sizeof(ushort), BufferUsage.IndexBuffer));

        _graphicsContext.GraphicsDevice.UpdateBuffer(_vertexBuffer, 0, quadVertices);
        _graphicsContext.GraphicsDevice.UpdateBuffer(_indexBuffer, 0, quadIndices);

        VertexLayoutDescription vertexLayout = new(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4));

        ShaderDescription vertexShaderDesc = new(ShaderStages.Vertex, Encoding.UTF8.GetBytes(VertexCode), "main");
        ShaderDescription fragmentShaderDesc = new(ShaderStages.Fragment, Encoding.UTF8.GetBytes(FragmentCode), "main");

        _shaders = factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);

        _pipeline = factory.CreateGraphicsPipeline(new()
		{
			BlendState = BlendStateDescription.SingleOverrideBlend,
			DepthStencilState = new DepthStencilStateDescription(depthTestEnabled: true, depthWriteEnabled: true, comparisonKind: ComparisonKind.LessEqual),
			RasterizerState = new RasterizerStateDescription(cullMode: FaceCullMode.Back, fillMode: PolygonFillMode.Solid, frontFace: FrontFace.Clockwise, depthClipEnabled: true, scissorTestEnabled: false),
			PrimitiveTopology = PrimitiveTopology.TriangleStrip,
			ResourceLayouts = Array.Empty<ResourceLayout>(),
			ShaderSet = new ShaderSetDescription(new VertexLayoutDescription[] { vertexLayout }, _shaders),
			Outputs = _graphicsContext.GraphicsDevice.SwapchainFramebuffer.OutputDescription
		});
    }

    public void Render(GameTime dt)
    {
		if (_commandList is null || _graphicsContext.GraphicsDevice is null) return;

		_commandList.Begin();
		_commandList.SetFramebuffer(_graphicsContext.GraphicsDevice.MainSwapchain.Framebuffer);
		_commandList.SetFullViewports();

		_commandList.SetVertexBuffer(0, _vertexBuffer);
		_commandList.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);

		_commandList.SetPipeline(_pipeline);
		_commandList.DrawIndexed(4, 1, 0, 0, 0);
		_commandList.End();
		_graphicsContext.SubmitCommands(_commandList);
    }

	public void Dispose()
	{
		_pipeline?.Dispose();
		if (_shaders != null) foreach (var shader in _shaders) shader.Dispose();

		_vertexBuffer?.Dispose();
		_indexBuffer?.Dispose();
	}
}

record struct VertexPositionColor(Vector2 Position, RgbaFloat Color)
{
	public const uint SizeInBytes = 24;

	public VertexPositionColor(float x, float y, RgbaFloat color) : this(new Vector2(x, y), color) { }
}