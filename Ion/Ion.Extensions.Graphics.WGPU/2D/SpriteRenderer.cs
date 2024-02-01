using System.Numerics;
using Microsoft.Extensions.Logging;

using Ion.Extensions.Debug;
using Ion.Extensions.Assets;
using SixLabors.ImageSharp.PixelFormats;

namespace Ion.Extensions.Graphics;

internal class SpriteRenderer(
	IGraphicsContext graphicsContext,
	ILogger<SpriteRenderer> logger,
	ITraceTimer<SpriteRenderer> trace
) : IDisposable
{
	//	private static readonly RectangleF _defaultScissor = new(-(1 << 22), -(1 << 22), 1 << 23, 1 << 23);
	//	private static readonly Vector2 VEC2_HALF = Vector2.One / 2f;
	//	private readonly SpriteBatchManager _batchManager = new();
	//	private BaseTexture? _whitePixel;

	//	private CommandEncoder _commandEncoder = default!;
	//	private WGPU.NET.Buffer _vertexBuffer = default!;
	//	private RenderPipeline _pipeline = default!;
	//	private BindGroup _bindGroup = default!;

	//	private bool _beginCalled = false;

	//	private Vertex[] vertices = [
	//		new Vertex(new ( -1,-1,0), new (1,1,0,1), new (-.2f,1.0f)),
	//		new Vertex(new (  1,-1,0), new (0,1,1,1), new (1.2f,1.0f)),
	//		new Vertex(new (  0, 1,0), new (1,0,1,1), new (0.5f,-.5f)),
	//	];

	//	struct Vertex
	//	{
	//		public Vector3 Position;
	//		public Vector4 Color;
	//		public Vector2 UV;

	//		public Vertex(Vector3 position, Vector4 color, Vector2 uv)
	//		{
	//			Position = position;
	//			Color = color;
	//			UV = uv;
	//		}
	//	}

	//	private class BufferContainer(object Buffer, object InstanceSet, object TextureSet)
	//	{
	//		public void Dispose()
	//		{
	//			//Buffer.Dispose();
	//			//InstanceSet.Dispose();
	//			//TextureSet.Dispose(); // TODO: Check if this is needed...
	//		}
	//	}

	//	private struct UniformBuffer
	//	{
	//		public Matrix4x4 Transform;
	//	}

	//	private readonly Dictionary<ITexture2D, BufferContainer> _buffers = new();

	//	private const string VertexCode = @"
	//#version 450

	//layout(location = 0) in vec2 Position;
	//layout(location = 0) out vec4 fsin_Color;
	//layout(location = 1) out vec2 tex_coord;
	//layout(location = 2) out vec4 bounds;
	//layout(location = 3) out vec2 pos;

	//struct Instance 
	//{
	//	vec4 UV;
	//	vec4 Color;
	//	vec2 Scale;
	//	vec2 Origin;
	//	vec4 Location;
	//	vec4 Scissor;
	//};

	//layout(std430, binding = 0) readonly buffer Instances
	//{
	//	mat4 projection;
	//	vec4 padding;
	//    Instance instances[];
	//};

	//mat2 makeRotation(float angle)
	//{
	//    float c = cos(angle);
	//    float s = sin(angle);
	//    return mat2(c, -s, s, c);
	//}

	//void main()
	//{
	//    Instance item = instances[gl_InstanceIndex];

	//    pos = Position * item.Scale;
	//    pos -= item.Origin;
	//    pos *= makeRotation(item.Location.w);
	//    pos += item.Location.xy;

	//    tex_coord = Position * item.UV.zw + item.UV.xy;

	//    // scissor bounds
	//    vec2 start = item.Scissor.xy;
	//    vec2 end = start + item.Scissor.zw;
	//    bounds = vec4(start, end);

	//    gl_Position = projection * vec4(pos, item.Location.z, 1);
	//	pos = gl_Position.xy;  

	//    fsin_Color = item.Color;
	//}";

	//	private const string FragmentCode = @"
	//#version 450

	//layout(location = 0) in vec4 fsin_Color;
	//layout(location = 1) in vec2 tex_coord;
	//layout(location = 2) in vec4 bounds;
	//layout(location = 3) in vec2 pos;
	//layout(location = 0) out vec4 fsout_Color;

	//layout(set = 1, binding = 0) uniform texture2D Tex;
	//layout(set = 1, binding = 1) uniform sampler Sampler;

	//void main()
	//{
	//	float left = bounds.x;
	//    float top = bounds.y;
	//    float right = bounds.z;
	//    float bottom = bounds.w;

	//    if(!(left <= pos.x && right >= pos.x &&
	//        top <= pos.y && bottom >= pos.y))
	//        discard;

	//    fsout_Color = fsin_Color * texture(sampler2D(Tex, Sampler), tex_coord);
	//}";

	//	public unsafe void Initialize()
	//	{
	//		if (graphicsContext.NoRender) return;

	//		var device = graphicsContext.Device;

	//		var timer = trace.Start("SpriteRenderer::Initialize");

	//		_commandEncoder = graphicsContext.Device.CreateCommandEncoder("SpriteRendererEncoder");

	//		timer.Then("CreateWhitePixel");

	//		_whitePixel = _createWhitePixel();

	//		timer.Then("CreateShaders");


	//		UniformBuffer uniformBufferData = new() { Transform = Matrix4x4.Identity };

	//		var uniformBuffer = device.CreateBuffer("UniformBuffer", false, (ulong)sizeof(UniformBuffer), Wgpu.BufferUsage.Uniform | Wgpu.BufferUsage.CopyDst);

	//		var imageSampler = graphicsContext.Device.CreateSampler("ImageSampler",
	//				addressModeU: Wgpu.AddressMode.ClampToEdge,
	//				addressModeV: Wgpu.AddressMode.ClampToEdge,
	//				addressModeW: default,

	//				magFilter: Wgpu.FilterMode.Linear,
	//				minFilter: Wgpu.FilterMode.Linear,
	//				mipmapFilter: Wgpu.MipmapFilterMode.Linear,

	//				lodMinClamp: 0,
	//				lodMaxClamp: 1,
	//				compare: default,

	//				maxAnisotropy: 1
	//			);

	//		var bindGroupLayout = device.CreateBindgroupLayout(null, [
	//				new() {
	//					binding = 0,
	//					buffer = new Wgpu.BufferBindingLayout
	//					{
	//						type = Wgpu.BufferBindingType.Uniform,
	//						minBindingSize = (ulong)sizeof(UniformBuffer)
	//					},
	//					visibility = (uint)Wgpu.ShaderStage.Vertex
	//				},
	//				new() {
	//					binding = 1,
	//					sampler = new Wgpu.SamplerBindingLayout
	//					{
	//						type = Wgpu.SamplerBindingType.Filtering,
	//					},
	//					visibility = (uint)Wgpu.ShaderStage.Fragment
	//				},
	//				new() {
	//					binding = 2,
	//					texture = new Wgpu.TextureBindingLayout
	//					{
	//						viewDimension = Wgpu.TextureViewDimension.TwoDimensions,
	//						sampleType = Wgpu.TextureSampleType.Float,
	//					},
	//					visibility = (uint)Wgpu.ShaderStage.Fragment
	//				}
	//			]);

	//		_bindGroup = device.CreateBindGroup(null, bindGroupLayout, [
	//			new BindGroupEntry
	//			{
	//				Binding = 0,
	//				Buffer = uniformBuffer,
	//				Offset = 0,
	//				Size = (ulong)sizeof(UniformBuffer)
	//			},
	//			new BindGroupEntry
	//			{
	//				Binding = 1,
	//				Sampler = imageSampler
	//			},
	//			new BindGroupEntry
	//			{
	//				Binding = 2,
	//				TextureView = ((Texture)_whitePixel).CreateTextureView()
	//			}
	//		]);

	//		_vertexBuffer = device.CreateBuffer("VertexBuffer", true, (ulong)(vertices.Length * sizeof(Vertex)), Wgpu.BufferUsage.Vertex);

	//		{
	//			Span<Vertex> mapped = _vertexBuffer.GetMappedRange<Vertex>(0, vertices.Length);

	//			vertices.CopyTo(mapped);

	//			_vertexBuffer.Unmap();
	//		}


	//		var shader = device.CreateWgslShaderModule(
	//				label: "shader.wgsl",
	//				wgslCode: @"
	//struct UniformBuffer {
	//    mdlMat : mat4x4<f32>
	//};

	//struct VOut {
	//    @builtin(position) pos : vec4<f32>,
	//    @location(1) col : vec4<f32>,
	//    @location(2) uv : vec2<f32>
	//};

	//@group(0)
	//@binding(0)
	//var<uniform> ub : UniformBuffer;

	//@vertex
	//fn vs_main(@location(0) pos: vec3<f32>, @location(1) col: vec4<f32>, @location(2) uv: vec2<f32>) -> VOut {


	//    return VOut(ub.mdlMat*vec4<f32>(pos, 1.0), col, uv);
	//}



	//@group(0)
	//@binding(1)
	//var samp : sampler;

	//@group(0)
	//@binding(2)
	//var tex : texture_2d<f32>;

	//@fragment
	//fn fs_main(in : VOut) -> @location(0) vec4<f32> {
	//    let rpos = vec2<f32>(
	//        floor(in.uv.x*10.0),
	//        floor(in.uv.y*10.0)
	//    );

	//    let texCol = textureSample(tex,samp,in.uv);

	//    let col = mix(in.col, vec4<f32>(texCol.rgb,1.0), texCol.a);

	//    return col * mix(1.0, 0.9, f32((rpos.x%2.0+2.0)%2.0 == (rpos.y%2.0+2.0)%2.0));
	//}"
	//			);

	//		var pipelineLayout = device.CreatePipelineLayout(
	//			label: null,
	//			new BindGroupLayout[]
	//			{
	//				bindGroupLayout
	//			}
	//		);

	//		var colorTargets = new ColorTargetState[] {
	//			new ColorTargetState()
	//			{
	//				Format = graphicsContext.SwapchainFormat,
	//				BlendState = new Wgpu.BlendState()
	//				{
	//					color = new Wgpu.BlendComponent()
	//					{
	//						srcFactor = Wgpu.BlendFactor.One,
	//						dstFactor = Wgpu.BlendFactor.Zero,
	//						operation = Wgpu.BlendOperation.Add
	//					},
	//					alpha = new Wgpu.BlendComponent()
	//					{
	//						srcFactor = Wgpu.BlendFactor.One,
	//						dstFactor = Wgpu.BlendFactor.Zero,
	//						operation = Wgpu.BlendOperation.Add
	//					}
	//				},
	//				WriteMask = (uint)Wgpu.ColorWriteMask.All
	//			}
	//		};

	//		var vertexState = new VertexState() {
	//			Module = shader,
	//			EntryPoint = "vs_main",
	//			bufferLayouts = [
	//				new VertexBufferLayout
	//				{
	//					ArrayStride = (ulong)sizeof(Vertex),
	//					Attributes = [
	//						//position
	//						new Wgpu.VertexAttribute
	//						{
	//							format = Wgpu.VertexFormat.Float32x3,
	//							offset = 0,
	//							shaderLocation = 0
	//						},
	//						//color
	//						new Wgpu.VertexAttribute
	//						{
	//							format = Wgpu.VertexFormat.Float32x4,
	//							offset = (ulong)sizeof(Vector3), //right after positon
	//							shaderLocation = 1
	//						},
	//						//uv
	//						new Wgpu.VertexAttribute
	//						{
	//							format = Wgpu.VertexFormat.Float32x2,
	//							offset = (ulong)(sizeof(Vector3)+sizeof(Vector4)), //right after color
	//							shaderLocation = 2
	//						}
	//					]
	//				}
	//			]
	//		};

	//		var fragmentState = new FragmentState()
	//		{
	//			Module = shader,
	//			EntryPoint = "fs_main",
	//			colorTargets = colorTargets
	//		};

	//		_pipeline = device.CreateRenderPipeline(
	//			label: "Render pipeline",
	//			layout: pipelineLayout,
	//			vertexState: vertexState,
	//			primitiveState: new Wgpu.PrimitiveState()
	//			{
	//				topology = Wgpu.PrimitiveTopology.TriangleList,
	//				stripIndexFormat = Wgpu.IndexFormat.Undefined,
	//				frontFace = Wgpu.FrontFace.CCW,
	//				cullMode = Wgpu.CullMode.None
	//			},
	//			multisampleState: new Wgpu.MultisampleState()
	//			{
	//				count = 1,
	//				mask = uint.MaxValue,
	//				alphaToCoverageEnabled = false
	//			},
	//			depthStencilState: new Wgpu.DepthStencilState()
	//			{
	//				format = Wgpu.TextureFormat.Depth32Float,
	//				depthCompare = Wgpu.CompareFunction.Always,
	//				stencilBack = new Wgpu.StencilFaceState
	//				{
	//					depthFailOp = Wgpu.StencilOperation.Keep,
	//					failOp = Wgpu.StencilOperation.Keep,
	//					passOp = Wgpu.StencilOperation.Keep,
	//					compare = Wgpu.CompareFunction.Always
	//				},
	//				stencilFront = new Wgpu.StencilFaceState
	//				{
	//					depthFailOp = Wgpu.StencilOperation.Keep,
	//					failOp = Wgpu.StencilOperation.Keep,
	//					passOp = Wgpu.StencilOperation.Keep,
	//					compare = Wgpu.CompareFunction.Always
	//				}
	//			},
	//			fragmentState: fragmentState
	//		);

	//		timer.Stop();
	//	}

	//	public void Begin(GameTime dt)
	//	{
	//		if (graphicsContext.NoRender) return;
	//		if (graphicsContext.Device == null) throw new InvalidOperationException("Begin cannot be called until the GraphicsDevice has been initialized.");

	//		if (_beginCalled) throw new InvalidOperationException("Begin cannot be called again until End has been successfully called.");

	//		var timer = trace.Start("Begin");

	//		_batchManager.Clear();
	//		_beginCalled = true;

	//		timer.Stop();
	//	}

	//	public void DrawRect(Color color, RectangleF destinationRectangle, Vector2 origin = default, float rotation = 0, float depth = 0f)
	//	{
	//		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling Draw.");

	//		_addSprite(_whitePixel!, color, new RectangleF(0, 0, 1, 1), destinationRectangle, origin, rotation, depth, _defaultScissor, SpriteEffect.None);
	//	}

	//	public void DrawRect(Color color, Vector2 position, Vector2 size, Vector2 origin = default, float rotation = 0, float depth = 0f)
	//	{
	//		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling Draw.");

	//		_addSprite(_whitePixel!, color, new RectangleF(0, 0, 1, 1), new RectangleF(position.X, position.Y, size.X, size.Y), origin, rotation, depth, _defaultScissor, SpriteEffect.None);
	//	}

	//	public void DrawPoint(Color color, Vector2 position, Vector2 size, float depth = 0)
	//	{
	//		DrawRect(color, position, size, origin: VEC2_HALF, depth: depth);
	//	}

	//	public void DrawPoint(Color color, Vector2 position, float depth = 0)
	//	{
	//		DrawRect(color, position, Vector2.One, origin: VEC2_HALF, depth: depth);
	//	}

	//	public void DrawLine(Color color, Vector2 pointA, Vector2 pointB, float thickness = 1f, float depth = 0)
	//	{
	//		var diff = pointB - pointA;
	//		var length = MathF.Sqrt(Vector2.Dot(diff, diff));
	//		var angle = MathF.Atan2(diff.Y, diff.X);
	//		DrawLine(color, pointA, length: length, angle: angle, thickness: thickness, depth: depth);
	//	}

	//	public void DrawLine(Color color, Vector2 start, float length, float angle, float thickness = 1, float depth = 0)
	//	{
	//		var rect = new RectangleF(start.X, start.Y, length, thickness);
	//		DrawRect(color, rect, new Vector2(0, 0.5f), rotation: angle, depth: depth);
	//	}

	//	public void Draw(ITexture2D texture, RectangleF destinationRectangle, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None)
	//	{
	//		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling Draw.");

	//		if (color == default) color = Color.White;
	//		if (sourceRectangle.IsEmpty) sourceRectangle = new RectangleF(0f, 0f, texture.Size.X, texture.Size.Y);

	//		_addSprite(texture, color, sourceRectangle, destinationRectangle, origin, rotation, depth, _defaultScissor, options);
	//	}

	//	public void Draw(ITexture2D texture, Vector2 position, Vector2 scale, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None)
	//	{
	//		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling Draw.");

	//		if (color == default) color = Color.White;
	//		if (sourceRectangle.IsEmpty) sourceRectangle = new RectangleF(0f, 0f, texture.Size.X, texture.Size.Y);

	//		_addSprite(texture, color, sourceRectangle, new RectangleF(position.X, position.Y, scale.X * sourceRectangle.Size.X, scale.Y * sourceRectangle.Size.Y), origin, rotation, depth, _defaultScissor, options);
	//	}

	//	public unsafe void End()
	//	{
	//		if (graphicsContext.NoRender) return;
	//		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling End.");

	//		_beginCalled = false;

	//		if (_batchManager.IsEmpty || graphicsContext.Device is null || _commandEncoder is null) return;

	//		var timer = trace.Start("End");

	//		var renderPass = graphicsContext.RenderPass;

	//		renderPass.SetPipeline(_pipeline);

	//		renderPass.SetBindGroup(0, _bindGroup, Array.Empty<uint>());
	//		renderPass.SetVertexBuffer(0, _vertexBuffer, 0, (ulong)(vertices.Length * sizeof(Vertex)));
	//		renderPass.Draw(3, 1, 0, 0);
	//		renderPass.End();

	//		//foreach (var (texture, group) in _batchManager)
	//		//{
	//		//	var pair = _getBuffer(texture, group.Count + 1);
	//		//	var innerTimer = trace.Start("End::Texture::Map");

	//		//	var mapped = graphicsContext.Device.Map(pair.Buffer, MapMode.Write);

	//		//	innerTimer.Then("End::Texture::Copy");
	//		//	MemUtils.Set(mapped.Data, graphicsContext.ProjectionMatrix, 1);
	//		//	MemUtils.Copy(mapped.Data + SpriteBatchManager.INSTANCE_SIZE, group.GetSpan());

	//		//	innerTimer.Then("End::Texture::Unmap");
	//		//	graphicsContext.Device.Unmap(pair.Buffer);
	//		//	innerTimer.Then("End::Texture::Commands");
	//		//	_commandEncoder.SetVertexBuffer(0, _vertexBuffer);
	//		//	_commandEncoder.SetGraphicsResourceSet(0, pair.InstanceSet);
	//		//	_commandEncoder.SetGraphicsResourceSet(1, pair.TextureSet);
	//		//	_commandEncoder.Draw(4, (uint)group.Count, 0, 0);
	//		//	innerTimer.Stop();
	//		//}

	//		var queue = graphicsContext.Device.Queue;

	//		//uniformBufferSpan[0] = uniformBufferData;

	//		//queue.WriteBuffer<UniformBuffer>(uniformBuffer, 0, uniformBufferSpan);


	//		var commands = _commandEncoder.Finish(null);

	//		graphicsContext.SubmitCommands(commands);

	//		timer.Stop();
	//	}

	//	//private BufferContainer _getBuffer(ITexture2D texture, int count)
	//	//{
	//	//	var timer = trace.Start("_getBuffer");

	//	//	var size = SpriteBatchManager.GetBatchSize(count);
	//	//	var bci = new BufferDescription(size, BufferUsage.StructuredBufferReadOnly | BufferUsage.Dynamic, SpriteBatchManager.SpriteInstance.SizeInBytes);

	//	//	if (!_buffers.TryGetValue(texture, out var pair))
	//	//	{
	//	//		var nb = trace.Start("_getBuffer::NewBuffer");
	//	//		var buffer = graphicsContext.Factory.CreateBuffer(bci);
	//	//		pair = new(buffer,
	//	//			graphicsContext.Factory.CreateResourceSet(new ResourceSetDescription(_instanceResourceLayout, buffer)),
	//	//			graphicsContext.Factory.CreateResourceSet(new ResourceSetDescription(_fragmentResourceLayout, (Veldrid.Texture)(Texture2D)texture, graphicsContext.Device!.LinearSampler))
	//	//			);

	//	//		_buffers[texture] = pair;
	//	//		nb.Stop();
	//	//	}
	//	//	else if (size > pair.Buffer.SizeInBytes)
	//	//	{
	//	//		var rb = trace.Start("_getBuffer::ResizeBuffer");
	//	//		pair.Dispose();

	//	//		pair.Buffer = graphicsContext.Factory.CreateBuffer(bci);
	//	//		pair.InstanceSet = graphicsContext.Factory.CreateResourceSet(new ResourceSetDescription(_instanceResourceLayout, pair.Buffer));
	//	//		pair.TextureSet = graphicsContext.Factory.CreateResourceSet(new ResourceSetDescription(_fragmentResourceLayout, (Veldrid.Texture)(Texture2D)texture, graphicsContext.Device!.LinearSampler));
	//	//		_buffers[texture] = pair;
	//	//		rb.Stop();
	//	//	}

	//	//	timer.Stop();
	//	//	return pair;
	//	//}

	public void Dispose()
	{
		//_pipeline?.Dispose();
		////if (_shaders != null) foreach (var shader in _shaders) shader.Dispose();

		//_vertexBuffer?.Dispose();
	}

	//	private unsafe Texture2D _createWhitePixel()
	//	{
	//		var whitePixel = graphicsContext.CreateTexture2D(new Wgpu.TextureDescriptor()
	//		{
	//			label = "WhitePixel",
	//			dimension = Wgpu.TextureDimension.TwoDimensions,
	//			size = new Wgpu.Extent3D() { width = 1, height = 1, depthOrArrayLayers = 1 },
	//			format = Wgpu.TextureFormat.RGBA8Unorm,
	//			usage = (uint)(Wgpu.TextureUsage.TextureBinding | Wgpu.TextureUsage.CopyDst),
	//			mipLevelCount = 1,
	//			sampleCount = 1
	//		});

	//		graphicsContext.Device.Queue.WriteTexture<byte>(
	//			destination: new ImageCopyTexture
	//			{
	//				Aspect = Wgpu.TextureAspect.All,
	//				MipLevel = 0,
	//				Origin = default,
	//				Texture = whitePixel
	//			},
	//			data: new byte[] { 255, 255, 255, 255 },
	//			dataLayout: new Wgpu.TextureDataLayout
	//			{
	//				bytesPerRow = (uint)(sizeof(Bgra32)),
	//				offset = 0,
	//				rowsPerImage = 1
	//			},
	//			writeSize: new Wgpu.Extent3D { width = 1, height = 1, depthOrArrayLayers = 1 }
	//		);

	//		return whitePixel;
	//	}

	//	private void _addSprite(ITexture2D texture, Color color, RectangleF sourceRect, RectangleF destinationRect, Vector2 origin, float rotation, float depth, RectangleF scissor, SpriteEffect options)
	//	{
	//		ref var instance = ref _batchManager.Add(texture);

	//		instance.Update(texture.Size, destinationRect, sourceRect, color, rotation, origin, depth, _transformRectF(scissor, graphicsContext.ProjectionMatrix), options);
	//	}

	//	private static RectangleF _transformRectF(RectangleF rect, Matrix4x4 matrix)
	//	{
	//		var pos = Vector4.Transform(new Vector4(rect.X, rect.Y, 0, 1), matrix);
	//		var size = Vector4.Transform(new Vector4(rect.X + rect.Width, rect.Y + rect.Height, 0, 1), matrix);
	//		return new(pos.X, pos.Y, size.X - pos.X, size.Y - pos.Y);
	//	}
}

