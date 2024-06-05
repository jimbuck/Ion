using System.Numerics;
using WebGPU;

using Ion.Extensions.Debug;
using SixLabors.ImageSharp.PixelFormats;


namespace Ion.Extensions.Graphics;

internal unsafe class SpriteRenderer(
	IGraphicsContext graphics,
	ITraceTimer<SpriteRenderer> trace
) : IDisposable
{
	private static readonly RectangleF _defaultScissor = new(-(1 << 22), -(1 << 22), 1 << 23, 1 << 23);
	private static readonly Vector2 VEC2_HALF = Vector2.One / 2f;
	private readonly SpriteBatchManager _batchManager = new();

	private Texture2D _whitePixel = default!;

	private WGPUPipelineLayout _pipelineLayout;
	private WGPURenderPipeline _pipeline;
	private WGPURenderPassDescriptor _renderPassDesc;
	private WGPUSampler _sampler;
	private WGPUTexture _depthTexture;
	private WGPUTextureView _depthTextureView;
	private WGPUBindGroup _uniformsBindGroup;

	private WGPUBuffer _vertexBuffer;
	private WGPUBuffer _indexBuffer;

	private bool _beginCalled = false;
	
	private struct Uniforms
	{
		public Matrix4x4 ProjectionMatrix;
	}

	private struct BindGroupContainer(WGPUBindGroup bindGroup, WGPUBuffer buffer, int size)
	{
		public WGPUBindGroup BindGroup { get; set; } = bindGroup;
		public WGPUBuffer Buffer { get; set; } = buffer;
		public int InstanceSize { get; set; } = size;

		public void Dispose()
		{
			BindGroup.Release();
			Buffer.Dispose();
			InstanceSize = 0;
		}
	}

	private readonly Dictionary<Texture2D, BindGroupContainer> _bindGroupCache = [];


	public unsafe void Initialize()
	{
		var timer = trace.Start("SpriteRenderer::Initialize");

		_whitePixel = graphics.CreateTexture2D("SpriteRendererWhitePixel", 1, 1, WGPUTextureUsage.TextureBinding | WGPUTextureUsage.CopyDst, graphics.SwapChainFormat);
		graphics.WriteTexture2D(_whitePixel, [new Bgra32(255, 255, 255, 255)]);

		WGPUBindGroupLayoutEntry[] bindGroup0LayoutEntries = [
				new WGPUBindGroupLayoutEntry()
				{
					binding = 0,
					visibility = WGPUShaderStage.Vertex,
					buffer = new WGPUBufferBindingLayout()
					{
						type = WGPUBufferBindingType.Uniform,
						minBindingSize = (ulong)sizeof(Uniforms)
					}
				}];

		WGPUBindGroupLayoutEntry[] bindGroup1LayoutEntries = [
				new WGPUBindGroupLayoutEntry()
				{
					binding = 0,
					visibility = WGPUShaderStage.Vertex,
					texture = new WGPUTextureBindingLayout()
					{
						sampleType = WGPUTextureSampleType.Float,
						viewDimension = WGPUTextureViewDimension._2D,
						multisampled = false
					},
				},
			new WGPUBindGroupLayoutEntry()
			{
				binding = 1,
				visibility = WGPUShaderStage.Vertex,
				sampler = new WGPUSamplerBindingLayout()
				{
					type = WGPUSamplerBindingType.Filtering
				}
			},
			new WGPUBindGroupLayoutEntry()
			{
				binding = 2,
				visibility = WGPUShaderStage.Vertex | WGPUShaderStage.Fragment,
				buffer = new WGPUBufferBindingLayout()
				{
					type = WGPUBufferBindingType.Uniform,
					minBindingSize = (ulong)sizeof(SpriteBatchManager.SpriteInstance)
				}
			}];

		fixed (WGPUBindGroupLayoutEntry* pBindGroup0LayoutEntries = bindGroup0LayoutEntries)
		fixed (WGPUBindGroupLayoutEntry* pBindGroup1LayoutEntries = bindGroup1LayoutEntries)
		{
			var bindGroup0Layout = graphics.Device.CreateBindGroupLayout(new WGPUBindGroupLayoutDescriptor()
			{
				entries = pBindGroup0LayoutEntries,
				entryCount = (uint)bindGroup0LayoutEntries.Length
			});

			var bindGroup1Layout = graphics.Device.CreateBindGroupLayout(new WGPUBindGroupLayoutDescriptor()
			{
				entries = pBindGroup1LayoutEntries,
				entryCount = (uint)bindGroup1LayoutEntries.Length
			});

			WGPUBindGroupLayout[] bindGroupLayouts = [bindGroup0Layout, bindGroup1Layout];

			fixed(WGPUBindGroupLayout* pBindGroupLayouts = bindGroupLayouts)
			{
				_pipelineLayout = graphics.Device.CreatePipelineLayout(new WGPUPipelineLayoutDescriptor()
				{
					bindGroupLayouts = pBindGroupLayouts,
					bindGroupLayoutCount = (uint)bindGroupLayouts.Length
				});
			}
		}
		

		

		_sampler = graphics.Device.CreateSampler(new WGPUSamplerDescriptor()
		{
			addressModeU = WGPUAddressMode.ClampToEdge,
			addressModeV = WGPUAddressMode.ClampToEdge,
			magFilter = WGPUFilterMode.Nearest,
			minFilter = WGPUFilterMode.Nearest,
			mipmapFilter = WGPUMipmapFilterMode.Nearest,
			maxAnisotropy = 1,
			lodMaxClamp = 1.0f,
			lodMinClamp = 0.0f,
		});

		WGPURenderPassColorAttachment renderPassColorAttachment = new()
		{
			view = default,
			resolveTarget = WGPUTextureView.Null,
			loadOp = WGPULoadOp.Load,
			storeOp = WGPUStoreOp.Store,
		};

		static void setDefaultStencilFace(ref WGPUStencilFaceState stencilFaceState)
		{
			stencilFaceState.compare = WGPUCompareFunction.Always;
			stencilFaceState.failOp = WGPUStencilOperation.Keep;
			stencilFaceState.depthFailOp = WGPUStencilOperation.Keep;
			stencilFaceState.passOp = WGPUStencilOperation.Keep;
		}

		var depthTextureFormat = WGPUTextureFormat.Depth24Plus;

		WGPUDepthStencilState depthStencilState = new()
		{
			format = depthTextureFormat,
			depthWriteEnabled = true,
			depthCompare = WGPUCompareFunction.Less,
			stencilReadMask = 0,
			stencilWriteMask = 0,
			depthBias = 0,
			depthBiasSlopeScale = 0,
			depthBiasClamp = 0
		};
		setDefaultStencilFace(ref depthStencilState.stencilFront);
		setDefaultStencilFace(ref depthStencilState.stencilBack);

		WGPUTextureDescriptor depthTextureDesc = new()
		{
			dimension = WGPUTextureDimension._2D,
			format = depthTextureFormat,
			mipLevelCount = 1,
			sampleCount = 1,
			size = new WGPUExtent3D(graphics.RenderTarget.Width, graphics.RenderTarget.Height, 1),
			usage = WGPUTextureUsage.RenderAttachment,
			viewFormatCount = 1,
			viewFormats = &depthTextureFormat,
		};
		_depthTexture = graphics.CreateTexture2D("SpriteRendererDepthTexture", depthTextureDesc);

		WGPUTextureViewDescriptor depthTextureViewDesc = new()
		{
			aspect = WGPUTextureAspect.DepthOnly,
			baseArrayLayer = 0,
			arrayLayerCount = 1,
			baseMipLevel = 0,
			mipLevelCount = 1,
			dimension = WGPUTextureViewDimension._2D,
			format = depthStencilState.format
		};
		_depthTextureView = _depthTexture.CreateView(depthTextureViewDesc);

		WGPURenderPassDepthStencilAttachment depthStencilAttachment = new()
		{
			view = _depthTextureView,
			depthLoadOp = WGPULoadOp.Clear,
			depthStoreOp = WGPUStoreOp.Store,
			depthClearValue = 1,
			depthReadOnly = false,

			stencilClearValue = 0,
			stencilLoadOp = WGPULoadOp.Clear,
			stencilStoreOp = WGPUStoreOp.Store,
			stencilReadOnly = true
		};

		// Describe a render pass, which targets the texture view
		_renderPassDesc = new()
		{
			nextInChain = null,
			colorAttachmentCount = 1,
			colorAttachments = &renderPassColorAttachment,
			// No depth buffer for now
			depthStencilAttachment = &depthStencilAttachment,

			// We do not use timers for now neither
			timestampWrites = null
		};

		string shaderSource = @"

struct Instance {
    @location(0) color: vec4f,
    @location(1) uv: vec4f,
    @location(2) scissor: vec4f,
    @location(3) position: vec3f,
    @location(4) rotation: f32,
    @location(5) origin: vec2f,
    @location(6) scale: vec2f,
};

struct VertexOutput {
	@builtin(position) position: vec4f,
	@location(0) color: vec3f,
	@location(1) bounds: vec4f,
	@location(2) tex_coord: vec2f,
};

struct SpriteInstances {
    @location(0) instances: array<Instance>,
};

@group(0) @binding(0) var<uniform> projectionMatrix: mat4x4f;
@group(1) @binding(0) var gradientTexture: texture_2d<f32>;
@group(1) @binding(1) var gradientSampler: sampler;
@group(1) @binding(2) var<uniform> instances: SpriteInstances;

fn makeRotation(angle: f32) -> mat2x2<f32> {
    let c = cos(angle);
    let s = sin(angle);
    return mat2x2<f32>(c, -s, s, c);
}

@vertex
fn vs_main(
	@builtin(instance_index) instance_index: u32,
	@builtin(position) position: vec4<f32>,
) -> VertexOutput {
	var out: VertexOutput;

	var item = instances.instances[instance_index];

    out.position = position * item.scale;
    out.position -= item.origin;
    out.position *= makeRotation(item.rotation);
    out.position += item.position.xy;

    out.tex_coord = position * item.uv.zw + item.uv.xy;
    
    // scissor bounds
    var start = item.scissor.xy;
    var end = start + item.scissor.zw;
    out.bounds = vec4(start, end);

    out.position = projectionMatrix * vec4(out.position, item.position.z, 1);
	out.color = item.color;
	return out;
}

@fragment
fn fs_main(in: VertexOutput) -> @location(0) vec4f {
	
	let left = in.bounds.x;
    let top = in.bounds.y;
    let right = in.bounds.z;
    let bottom = in.bounds.w;

	if(!(left <= in.position.x && right >= in.position.x && top <= in.position.y && bottom >= in.position.y)) {
		discard;
    }

    // Fetch a texel from the texture
	let color = textureSample(gradientTexture, gradientSampler, in.tex_coord);
	// let color = textureLoad(gradientTexture, vec2<i32>(in.position.xy), 0).rgba;

	// Gamma-correction
	//color.rgb = pow(in.color.rgb, vec3f(2.2));

	return in.color * color;
}
";
		WGPUShaderModule shaderModule = graphics.Device.CreateShaderModule(shaderSource);


		fixed (WGPUVertexAttribute* pVertexAttributes = SpriteBatchManager.SpriteInstance.VertexAttributes)
		fixed (sbyte* pVertexEntryPoint = "vertexMain".GetUtf8Span())
		fixed (sbyte* pFragmentEntryPoint = "fragmentMain".GetUtf8Span())
		{
			WGPUVertexBufferLayout[] vertexBufferLayouts = [
				new()
				{
					attributeCount = (uint)SpriteBatchManager.SpriteInstance.VertexAttributes.Length,
					attributes = pVertexAttributes,
					arrayStride = (ulong)SpriteBatchManager.SpriteInstance.SizeInBytes,
					stepMode = WGPUVertexStepMode.Instance
				}
			];

			fixed (WGPUVertexBufferLayout* pVertexBufferLayouts = vertexBufferLayouts)
			{
				WGPURenderPipelineDescriptor pipelineDesc = new()
				{
					layout = _pipelineLayout,
					vertex = new()
					{
						bufferCount = (uint)vertexBufferLayouts.Length,
						buffers = pVertexBufferLayouts,

						// Vertex shader
						module = shaderModule,
						entryPoint = pVertexEntryPoint,
					},
					primitive = new()
					{
						// Primitive assembly and rasterization
						topology = WGPUPrimitiveTopology.TriangleList,
						stripIndexFormat = WGPUIndexFormat.Undefined,
						frontFace = WGPUFrontFace.CW,
						cullMode = WGPUCullMode.Back,
					}
				};

				var fragmentTargetState = new WGPUColorTargetState
				{
					format = graphics.SwapChainFormat
				};

				// Fragment shader
				WGPUFragmentState fragmentState = new()
				{
					nextInChain = null,
					module = shaderModule,
					entryPoint = pFragmentEntryPoint,
					targetCount = 1,
					targets = &fragmentTargetState
				};
				pipelineDesc.fragment = &fragmentState;

				// Configure blend state
				WGPUBlendState blendState = new()
				{
					color = new()
					{
						// Usual alpha blending for the color:
						srcFactor = WGPUBlendFactor.SrcAlpha,
						dstFactor = WGPUBlendFactor.OneMinusSrcAlpha,
						operation = WGPUBlendOperation.Add,
					},
					alpha = new()
					{
						// We leave the target alpha untouched:
						srcFactor = WGPUBlendFactor.Zero,
						dstFactor = WGPUBlendFactor.One,
						operation = WGPUBlendOperation.Add,
					}
				};

				WGPUColorTargetState colorTarget = new()
				{
					nextInChain = null,
					format = graphics.SwapChainFormat,
					blend = &blendState,
					writeMask = WGPUColorWriteMask.All // We could write to only some of the color channels.
				};

				// We have only one target because our render pass has only one output color
				// attachment.
				fragmentState.targetCount = 1;
				fragmentState.targets = &colorTarget;

				// Depth and stencil tests are not used here
				pipelineDesc.depthStencil = &depthStencilState;

				// Multi-sampling
				// Samples per pixel
				pipelineDesc.multisample.count = 1;
				// Default value for the mask, meaning "all bits on"
				pipelineDesc.multisample.mask = ~0u;
				// Default value as well (irrelevant for count = 1 anyways)
				pipelineDesc.multisample.alphaToCoverageEnabled = false;

				_pipeline = graphics.Device.CreateRenderPipeline(pipelineDesc);
			}
		}

		_uniformsBindGroup = graphics.Device.CreateBindGroup(_pipeline.GetBindGroupLayout(0), new WGPUBindGroupEntry
		{
			binding = 0,
			buffer = graphics.Device.CreateBuffer(WGPUBufferUsage.Uniform | WGPUBufferUsage.CopyDst, sizeof(Uniforms))
		});

		shaderModule.Release();

		Span<Vector2> vertexData = [
			new(0, 0), new(1, 0),
			new(0, 1), new(1, 1),
		];
		_vertexBuffer = graphics.Device.CreateBuffer(graphics.Queue, vertexData, WGPUBufferUsage.Vertex);

		// Index buffer
		Span<ushort> indices = [
			0, 1, 2,    // first triangle
			0, 2, 3,    // second triangle
		];
		_indexBuffer = graphics.Device.CreateBuffer(graphics.Queue, indices, WGPUBufferUsage.Index | WGPUBufferUsage.CopyDst);

		timer.Stop();
	}

	public void Begin(GameTime dt)
	{
		if (_beginCalled) throw new InvalidOperationException("Begin cannot be called again until End has been successfully called.");

		var timer = trace.Start("Begin");

		_batchManager.Clear();
		_beginCalled = true;

		timer.Stop();
	}

	public unsafe void End()
	{
		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling End.");

		_beginCalled = false;

		if (_batchManager.IsEmpty) return;

		var timer = trace.Start("End");

		var encoder = graphics.Device.CreateCommandEncoder("SpriteRenderer Command Encoder");

		encoder.PushDebugGroup("SpriteRenderer");

		var renderTarget = _updateRenderPassDesc();

		var renderPass = encoder.BeginRenderPass(_renderPassDesc);
		renderPass.SetPipeline(_pipeline);

		foreach (var (texture, batch) in _batchManager)
		{
			var bindGroup = _getBindGroup(texture, batch.Count + 1);
			var innerTimer = trace.Start("End::Texture::Map");

			innerTimer.Then("End::Texture::Commands");
			renderPass.SetVertexBuffer(0, _vertexBuffer);
			renderPass.SetIndexBuffer(_indexBuffer, WGPUIndexFormat.Uint16);
			renderPass.SetBindGroup(0, bindGroup.BindGroup);
			renderPass.DrawIndexed(6);

			innerTimer.Stop();
		}

		renderPass.End();
		renderTarget.Release();

		encoder.PopDebugGroup();

		var command = encoder.Finish("Sprite Renderer Command Buffer");
		graphics.Queue.Submit(command);

		encoder.Release();

		timer.Stop();
	}

	public void DrawRect(Color color, RectangleF destinationRectangle, Vector2 origin = default, float rotation = 0, float depth = 0f)
	{
		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling Draw.");

		_addSprite(_whitePixel, color, new RectangleF(0, 0, 1, 1), destinationRectangle, origin, rotation, depth, _defaultScissor, SpriteEffect.None);
	}

	public void DrawRect(Color color, Vector2 position, Vector2 size, Vector2 origin = default, float rotation = 0, float depth = 0f)
	{
		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling Draw.");

		_addSprite(_whitePixel, color, new RectangleF(0, 0, 1, 1), new RectangleF(position.X, position.Y, size.X, size.Y), origin, rotation, depth, _defaultScissor, SpriteEffect.None);
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

	public void Draw(ITexture2D texture, RectangleF destinationRectangle, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None)
	{
		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling Draw.");

		if (color == default) color = Color.White;
		if (sourceRectangle.IsEmpty) sourceRectangle = new RectangleF(0f, 0f, texture.Width, texture.Height);

		_addSprite(texture, color, sourceRectangle, destinationRectangle, origin, rotation, depth, _defaultScissor, options);
	}

	public void Draw(ITexture2D texture, Vector2 position, Vector2 scale, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None)
	{
		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling Draw.");

		if (color == default) color = Color.White;
		if (sourceRectangle.IsEmpty) sourceRectangle = new RectangleF(0f, 0f, texture.Width, texture.Height);

		_addSprite(texture, color, sourceRectangle, new RectangleF(position.X, position.Y, scale.X * sourceRectangle.Size.X, scale.Y * sourceRectangle.Size.Y), origin, rotation, depth, _defaultScissor, options);
	}


	public void Dispose()
	{
		_depthTextureView.Release();
		_depthTexture.Dispose();

		_pipelineLayout.Release();
		_pipeline.Release();

		_vertexBuffer.Dispose();
		_indexBuffer.Dispose();
	}

	private void _addSprite(ITexture2D texture, Color color, RectangleF sourceRect, RectangleF destinationRect, Vector2 origin, float rotation, float depth, RectangleF scissor, SpriteEffect options)
	{
		ref var instance = ref _batchManager.Add((Texture2D)texture);

		instance.Update(new Vector2(texture.Width, texture.Height), destinationRect, sourceRect, color, rotation, origin, depth, _transformRectF(scissor, graphics.ProjectionMatrix), options);
	}

	private static Vector4 _transformRectF(RectangleF rect, Matrix4x4 matrix)
	{
		var pos = Vector4.Transform(new Vector4(rect.X, rect.Y, 0, 1), matrix);
		var size = Vector4.Transform(new Vector4(rect.X + rect.Width, rect.Y + rect.Height, 0, 1), matrix);
		return new(pos.X, pos.Y, size.X - pos.X, size.Y - pos.Y);
	}

	private WGPUTextureView _updateRenderPassDesc()
	{
		WGPUTextureView targetView = graphics.RenderTarget.CreateView();

		_renderPassDesc.colorAttachments[0].view = targetView;

		return targetView;
	}

	private BindGroupContainer _getBindGroup(Texture2D texture, int count)
	{
		var timer = trace.Start("_getBindGroup");

		var size = SpriteBatchManager.GetBatchSize(count);

		if (!_bindGroupCache.TryGetValue(texture, out var cachedResources))
		{
			var nb = trace.Start("_getBindGroup::NewBindGroup");
			var instanceBuffer = graphics.Device.CreateBuffer(WGPUBufferUsage.Uniform | WGPUBufferUsage.CopyDst, (int)size);

			WGPUBindGroupEntry[] bindGroupEntries = [
				new WGPUBindGroupEntry() {
					binding = 0,
					textureView = texture.CreateView(),
					sampler = _sampler,
				},
				new WGPUBindGroupEntry()
				{
					binding = 1,
					buffer = instanceBuffer,
					size = size,
				}
			];

			WGPUBindGroup bindGroup = graphics.Device.CreateBindGroup(_pipeline.GetBindGroupLayout(1), bindGroupEntries);

			cachedResources = new BindGroupContainer(bindGroup, instanceBuffer, (int)size);

			_bindGroupCache[texture] = cachedResources;

			nb.Stop();
		}
		else if (size > cachedResources.InstanceSize)
		{
			var rb = trace.Start("_getBindGroup::ResizeBindGroup");
			cachedResources.Dispose();

			var instanceBuffer = graphics.Device.CreateBuffer(WGPUBufferUsage.Uniform | WGPUBufferUsage.CopyDst, (int)size);
			WGPUBindGroupEntry[] bindGroupEntries = [
				new WGPUBindGroupEntry()
				{
					binding = 0,
					sampler = _sampler,
				},
				new WGPUBindGroupEntry()
				{
					binding = 1,
					textureView = texture.CreateView(),
				},
				new WGPUBindGroupEntry()
				{
					binding = 2,
					buffer = instanceBuffer,
					size = size,
				}
			];

			WGPUBindGroup bindGroup = graphics.Device.CreateBindGroup(_pipeline.GetBindGroupLayout(1), bindGroupEntries);

			cachedResources.BindGroup = bindGroup;
			cachedResources.Buffer = instanceBuffer;
			cachedResources.InstanceSize = (int)size;
			_bindGroupCache[texture] = cachedResources;
			rb.Stop();
		}

		timer.Stop();
		return cachedResources;
	}
}

