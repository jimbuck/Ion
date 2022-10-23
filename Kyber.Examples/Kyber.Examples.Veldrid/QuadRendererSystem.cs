using System.Numerics;
using System.Text;
using Kyber.Graphics;

using Veldrid;
using Veldrid.SPIRV;


namespace Kyber.Examples.Veldrid;

public class QuadRendererSystem : IInitializeSystem, IPreRenderSystem, IRenderSystem, IPostRenderSystem, IDisposable
{
    private readonly IGraphicsDevice _graphicsDevice;
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

    public QuadRendererSystem(IGraphicsDevice graphicsDevice, ILogger<QuadRendererSystem> logger)
    {
        _graphicsDevice = graphicsDevice;
        _logger = logger;
    }

    public void Initialize()
    {
		if (_graphicsDevice.Internal == null)
		{
			IsEnabled = false;
			_logger.LogWarning($"{nameof(QuadRendererSystem)} automically disabled due to GraphicsDevice not being set.");
			return;
		}

        ResourceFactory factory = _graphicsDevice.Internal.ResourceFactory;

        VertexPositionColor[] quadVertices = {
            new(-0.75f, 0.75f, RgbaFloat.Red),
            new(0.75f, 0.75f, RgbaFloat.Green),
            new(-0.75f, -0.75f, RgbaFloat.Blue),
            new(0.75f, -0.75f, RgbaFloat.Yellow)
        };

        ushort[] quadIndices = { 0, 1, 2, 3 };

        _vertexBuffer = factory.CreateBuffer(new BufferDescription(4 * VertexPositionColor.SizeInBytes, BufferUsage.VertexBuffer));
        _indexBuffer = factory.CreateBuffer(new BufferDescription(4 * sizeof(ushort), BufferUsage.IndexBuffer));

        _graphicsDevice.Internal.UpdateBuffer(_vertexBuffer, 0, quadVertices);
        _graphicsDevice.Internal.UpdateBuffer(_indexBuffer, 0, quadIndices);

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
			Outputs = _graphicsDevice.Internal.SwapchainFramebuffer.OutputDescription
		});

        _commandList = factory.CreateCommandList();
    }


    public void PreRender(float dt)
    {
        if (_graphicsDevice.Internal == null || _commandList == null) return;

        _commandList.Begin();
    }

    public void Render(float dt)
    {
        if (_graphicsDevice.Internal == null || _commandList == null) return;

        _commandList.SetFramebuffer(_graphicsDevice.Internal.SwapchainFramebuffer);

        _commandList.ClearColorTarget(0, RgbaFloat.Black);
        _commandList.SetVertexBuffer(0, _vertexBuffer);
        _commandList.SetIndexBuffer(_indexBuffer, IndexFormat.UInt16);

        _commandList.SetPipeline(_pipeline);
        _commandList.DrawIndexed(4, 1, 0, 0, 0);
    }

    public void PostRender(float dt)
    {
        if (_graphicsDevice.Internal == null || _commandList == null) return;

        _commandList.End();
        _graphicsDevice.Internal.SubmitCommands(_commandList);
        _graphicsDevice.Internal.SwapBuffers();
    }

	public void Dispose()
	{
		_pipeline?.Dispose();
		if (_shaders != null) foreach (var shader in _shaders) shader.Dispose();

		_commandList?.Dispose();
		_vertexBuffer?.Dispose();
		_indexBuffer?.Dispose();
	}
}

record struct VertexPositionColor(Vector2 Position, RgbaFloat Color)
{
	public const uint SizeInBytes = 24;

	public VertexPositionColor(float x, float y, RgbaFloat color) : this(new Vector2(x, y), color) { }
}