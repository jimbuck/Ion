using System.Numerics;
using WebGPU;

using Ion.Extensions.Debug;
using Ion.Extensions.Assets;
using System.Runtime.InteropServices;


namespace Ion.Extensions.Graphics;

internal class SpriteRenderer(
	IGraphicsContext graphics,
	ITraceTimer<SpriteRenderer> trace
) : IDisposable
{
	private static readonly RectangleF _defaultScissor = new(-(1 << 22), -(1 << 22), 1 << 23, 1 << 23);
	private static readonly Vector2 VEC2_HALF = Vector2.One / 2f;
	private Texture2D _whitePixel = default!;

	private WGPUPipelineLayout _pipelineLayout;
	private WGPURenderPipeline _pipeline;
	private WGPUBuffer _vertexBuffer;
	private WGPUBuffer _indexBuffer;

	private bool _beginCalled = false;


	public unsafe void Initialize()
	{
		if (graphics.NoRender) return;

		var timer = trace.Start("SpriteRenderer::Initialize");

		_whitePixel = graphics.CreateTexture2D("SpriteRendererWhitePixel", 1, 1, WGPUTextureUsage.TextureBinding | WGPUTextureUsage.CopyDst, graphics.SwapChainFormat);
		graphics.WriteTexture2D(_whitePixel, new byte[] { 255, 255, 255, 255 });

		
		_pipelineLayout = graphics.Device.CreatePipelineLayout();

		string shaderSource = @"struct VertexInput {
    @location(0) position: vec3f,
    @location(1) color: vec3f,
};

/**
 * A structure with fields labeled with builtins and locations can also be used
 * as *output* of the vertex shader, which is also the input of the fragment
 * shader.
 */
struct VertexOutput {
    @builtin(position) position: vec4f,
    // The location here does not refer to a vertex attribute, it just means
    // that this field must be handled by the rasterizer.
    // (It can also refer to another field of another struct that would be used
    // as input to the fragment shader.)
    @location(0) color: vec3f,
};

@vertex
fn vertexMain(in: VertexInput) -> VertexOutput {
    var out: VertexOutput;
    out.position = vec4f(in.position, 1.0);
    out.color = in.color; // forward to the fragment shader
    return out;
}

@fragment
fn fragmentMain(in: VertexOutput) -> @location(0) vec4f {
    return vec4f(in.color, 1.0);
}";
		WGPUShaderModule shaderModule = graphics.Device.CreateShaderModule(shaderSource);

		// Vertex fetch
		WGPUVertexAttribute* vertexAttributes = stackalloc WGPUVertexAttribute[2] {
				new WGPUVertexAttribute(WGPUVertexFormat.Float32x3, 0, 0),
				new WGPUVertexAttribute(WGPUVertexFormat.Float32x4, 12, 1)
			};

		WGPUVertexBufferLayout vertexBufferLayout = new()
		{
			attributeCount = 2,
			attributes = vertexAttributes,
			arrayStride = (ulong)VertexPositionColor.SizeInBytes,
			stepMode = WGPUVertexStepMode.Vertex
		};

		fixed (sbyte* pVertexEntryPoint = "vertexMain".GetUtf8Span())
		fixed (sbyte* pFragmentEntryPoint = "fragmentMain".GetUtf8Span())
		{
			WGPURenderPipelineDescriptor pipelineDesc = new();
			pipelineDesc.layout = _pipelineLayout;

			pipelineDesc.vertex.bufferCount = 1;
			pipelineDesc.vertex.buffers = &vertexBufferLayout;

			// Vertex shader
			pipelineDesc.vertex.module = shaderModule;
			pipelineDesc.vertex.entryPoint = pVertexEntryPoint;
			pipelineDesc.vertex.constantCount = 0;
			pipelineDesc.vertex.constants = null;

			// Primitive assembly and rasterization
			// Each sequence of 3 vertices is considered as a triangle
			pipelineDesc.primitive.topology = WGPUPrimitiveTopology.TriangleList;
			// We'll see later how to specify the order in which vertices should be
			// connected. When not specified, vertices are considered sequentially.
			pipelineDesc.primitive.stripIndexFormat = WGPUIndexFormat.Undefined;
			// The face orientation is defined by assuming that when looking
			// from the front of the face, its corner vertices are enumerated
			// in the counter-clockwise (CCW) order.
			pipelineDesc.primitive.frontFace = WGPUFrontFace.CCW;
			// But the face orientation does not matter much because we do not
			// cull (i.e. "hide") the faces pointing away from us (which is often
			// used for optimization).
			pipelineDesc.primitive.cullMode = WGPUCullMode.None;

			// Fragment shader
			WGPUFragmentState fragmentState = new()
			{
				nextInChain = null,
				module = shaderModule,
				entryPoint = pFragmentEntryPoint,
				constantCount = 0,
				constants = null
			};
			pipelineDesc.fragment = &fragmentState;

			// Configure blend state
			WGPUBlendState blendState = new();
			// Usual alpha blending for the color:
			blendState.color.srcFactor = WGPUBlendFactor.SrcAlpha;
			blendState.color.dstFactor = WGPUBlendFactor.OneMinusSrcAlpha;
			blendState.color.operation = WGPUBlendOperation.Add;
			// We leave the target alpha untouched:
			blendState.alpha.srcFactor = WGPUBlendFactor.Zero;
			blendState.alpha.dstFactor = WGPUBlendFactor.One;
			blendState.alpha.operation = WGPUBlendOperation.Add;

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
			pipelineDesc.depthStencil = null;

			// Multi-sampling
			// Samples per pixel
			pipelineDesc.multisample.count = 1;
			// Default value for the mask, meaning "all bits on"
			pipelineDesc.multisample.mask = ~0u;
			// Default value as well (irrelevant for count = 1 anyways)
			pipelineDesc.multisample.alphaToCoverageEnabled = false;

			_pipeline = graphics.Device.CreateRenderPipeline(pipelineDesc);
		}

		shaderModule.Release();

		Span<VertexPositionColor> vertexData = [
			new(new Vector3(-0.5f, 0.5f, 0.5f), new Vector4(0.0f, 1.0f, 0.0f, 1.0f)),
			new(new Vector3(0.5f, 0.5f, 0.5f), new Vector4(1.0f, 0.0f, 0.0f, 1.0f)),
			new(new Vector3(0.5f, -0.5f, 0.5f), new Vector4(0.0f, 0.0f, 1.0f, 1.0f)),
			new(new Vector3(-0.5f, -0.5f, 0.5f), new Vector4(1.0f, 1.0f, 0.0f, 1.0f))
		];
		_vertexBuffer = graphics.Device.CreateBuffer(graphics.Queue, vertexData, WGPUBufferUsage.Vertex);

		// Index buffer
		Span<ushort> indices = [
			0,
			1,
			2,    // first triangle
			0,
			2,
			3,    // second triangle
		];
		_indexBuffer = graphics.Device.CreateBuffer(graphics.Queue, indices, WGPUBufferUsage.Index | WGPUBufferUsage.CopyDst);

		timer.Stop();
	}

	public void Begin(GameTime dt)
	{
		if (graphics.NoRender) return;
		if (graphics.Device.IsNull) throw new InvalidOperationException("Begin cannot be called until the GraphicsDevice has been initialized.");

		if (_beginCalled) throw new InvalidOperationException("Begin cannot be called again until End has been successfully called.");

		var timer = trace.Start("Begin");

		_beginCalled = true;

		timer.Stop();
	}

	public unsafe void End()
	{
		if (graphics.NoRender) return;
		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling End.");

		_beginCalled = false;

		var timer = trace.Start("End");

		var encoder = graphics.Device.CreateCommandEncoder("SpriteRenderer Command Encoder");

		encoder.PushDebugGroup("SpriteRenderer");

		WGPUTextureView targetView = graphics.RenderTarget.CreateView();

		WGPURenderPassColorAttachment renderPassColorAttachment = new()
		{
			// The attachment is tighed to the view returned by the swap chain, so that
			// the render pass draws directly on screen.
			view = targetView,
			// Not relevant here because we do not use multi-sampling
			resolveTarget = WGPUTextureView.Null,
			loadOp = WGPULoadOp.Load,
			storeOp = WGPUStoreOp.Store,
		};

		// Describe a render pass, which targets the texture view
		WGPURenderPassDescriptor renderPassDesc = new()
		{
			nextInChain = null,
			colorAttachmentCount = 1,
			colorAttachments = &renderPassColorAttachment,
			// No depth buffer for now
			depthStencilAttachment = null,

			// We do not use timers for now neither
			timestampWrites = null
		};

		// Create a render pass. We end it immediately because we use its built-in
		// mechanism for clearing the screen when it begins (see descriptor).
		var renderPass = encoder.BeginRenderPass(renderPassDesc);

		renderPass.SetPipeline(_pipeline);
		renderPass.SetVertexBuffer(0, _vertexBuffer);
		renderPass.SetIndexBuffer(_indexBuffer, WGPUIndexFormat.Uint16);

		renderPass.DrawIndexed(6);

		renderPass.End();
		//wgpuTextureViewReference(targetView);

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
		if (sourceRectangle.IsEmpty) sourceRectangle = new RectangleF(0f, 0f, texture.Size.X, texture.Size.Y);

		_addSprite(texture, color, sourceRectangle, destinationRectangle, origin, rotation, depth, _defaultScissor, options);
	}

	public void Draw(ITexture2D texture, Vector2 position, Vector2 scale, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None)
	{
		if (!_beginCalled) throw new InvalidOperationException("Begin must be called before calling Draw.");

		if (color == default) color = Color.White;
		if (sourceRectangle.IsEmpty) sourceRectangle = new RectangleF(0f, 0f, texture.Size.X, texture.Size.Y);

		_addSprite(texture, color, sourceRectangle, new RectangleF(position.X, position.Y, scale.X * sourceRectangle.Size.X, scale.Y * sourceRectangle.Size.Y), origin, rotation, depth, _defaultScissor, options);
	}


	public void Dispose()
	{
		_pipelineLayout.Release();
		_pipeline.Release();
		_vertexBuffer.Dispose();
		_indexBuffer.Dispose();
	}

	private void _addSprite(ITexture2D texture, Color color, RectangleF sourceRect, RectangleF destinationRect, Vector2 origin, float rotation, float depth, RectangleF scissor, SpriteEffect options)
	{
		//ref var instance = ref _batchManager.Add(texture);

		//instance.Update(texture.Size, destinationRect, sourceRect, color, rotation, origin, depth, _transformRectF(scissor, graphics.ProjectionMatrix), options);
	}

	private static RectangleF _transformRectF(RectangleF rect, Matrix4x4 matrix)
	{
		var pos = Vector4.Transform(new Vector4(rect.X, rect.Y, 0, 1), matrix);
		var size = Vector4.Transform(new Vector4(rect.X + rect.Width, rect.Y + rect.Height, 0, 1), matrix);
		return new(pos.X, pos.Y, size.X - pos.X, size.Y - pos.Y);
	}

	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public readonly struct VertexPositionColor(in Vector3 position, in Vector4 color)
	{
		public static unsafe readonly int SizeInBytes = sizeof(VertexPositionColor);

		public readonly Vector3 Position = position;
		public readonly Vector4 Color = color;
	}
}

