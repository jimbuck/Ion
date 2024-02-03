using System.Numerics;
using System.Runtime.InteropServices;

using WebGPU;

namespace Ion.Extensions.Graphics;

public unsafe class TriangleRenderer(IGraphicsContext graphics)
{
	private WGPUPipelineLayout _pipelineLayout;
	private WGPURenderPipeline _pipeline;
	private WGPUBuffer _vertexBuffer;

	[Init]
	public void Initialize(GameTime dt, GameLoopDelegate next)
	{
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
			WGPURenderPipelineDescriptor pipelineDesc = new()
			{
				layout = _pipelineLayout
			};

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

		ReadOnlySpan<VertexPositionColor> vertexData = [
			new(new Vector3(0.0f, 0.75f, 0.5f), new Vector4(1.0f, 0.0f, 0.0f, 1.0f)),
			new(new Vector3(0.5f, -0.5f, 0.5f), new Vector4(0.0f, 1.0f, 0.0f, 1.0f)),
			new(new Vector3(-0.5f, -0.5f, 0.5f), new Vector4(0.0f, 0.0f, 1.0f, 1.0f)),
		];
		_vertexBuffer = graphics.Device.CreateBuffer(WGPUBufferUsage.Vertex | WGPUBufferUsage.CopyDst, vertexData.Length * VertexPositionColor.SizeInBytes);
		graphics.Queue.WriteBuffer(_vertexBuffer, vertexData);

		next(dt);
	}

	[Render]
	public void OnDraw(GameTime dt, GameLoopDelegate next)
	{
		next(dt);

		var encoder = graphics.Device.CreateCommandEncoder("TriangleRendererCommandEncoder");

		encoder.PushDebugGroup("TriangleRenderer");

		WGPUTextureView targetView = graphics.RenderTarget.CreateView();

		WGPURenderPassColorAttachment renderPassColorAttachment = new()
		{
			view = targetView,
			// Not relevant here because we do not use multi-sampling
			resolveTarget = WGPUTextureView.Null,
			loadOp = WGPULoadOp.Load,
			storeOp = WGPUStoreOp.Store
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

		renderPass.Draw(3);

		renderPass.End();

		//wgpuTextureViewReference(targetView);

		encoder.PopDebugGroup();

		var commandBuffer = encoder.Finish("TriangleRendererCommandBuffer");
		graphics.Queue.Submit(commandBuffer);

		encoder.Release();
	}

	public void Dispose()
	{
		_pipelineLayout.Release();
		_pipeline.Release();
		_vertexBuffer.Dispose();
	}

	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	public readonly struct VertexPositionColor(in Vector3 position, in Vector4 color)
	{
		public static unsafe readonly int SizeInBytes = sizeof(VertexPositionColor);

		public readonly Vector3 Position = position;
		public readonly Vector4 Color = color;
	}
}
