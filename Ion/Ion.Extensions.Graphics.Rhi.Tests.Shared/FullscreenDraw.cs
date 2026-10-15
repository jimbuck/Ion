namespace Ion.Extensions.Graphics.Rhi.Tests;

/// <summary>A full-target triangle with <c>fullscreen.vert</c> and a fragment shader reading one bind group.</summary>
internal sealed class FullscreenDraw : IDisposable
{
	private readonly IGraphicsDevice _device;
	private readonly IShaderModule _vertex;
	private readonly IShaderModule _fragment;
	private readonly IBindGroupLayout _layout;
	private readonly IPipelineLayout _pipelineLayout;
	private readonly IRenderPipeline _pipeline;
	private readonly IBindGroup _group;

	public FullscreenDraw(IGraphicsDevice device, string fragment, BindGroupLayoutEntry[] layout, BindGroupEntry[] entries)
	{
		_device = device;
		var assembly = typeof(TextureContractTests).Assembly;
		_vertex = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, assembly, "fullscreen.vert"));
		_fragment = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, assembly, fragment));
		_layout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor(layout));
		_pipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor([_layout]));
		_pipeline = device.CreateRenderPipeline(new RenderPipelineDescriptor
		{
			Label = fragment,
			Layout = _pipelineLayout,
			Vertex = new VertexState(_vertex, []),
			Fragment = new FragmentState(_fragment, [new ColorTargetState(TextureFormat.Rgba8Unorm)]),
		});
		_group = device.CreateBindGroup(new BindGroupDescriptor(_layout, entries));
	}

	public void Draw(ITexture target)
	{
		var encoder = _device.CreateCommandEncoder();
		var pass = encoder.BeginRenderPass(new RenderPassDescriptor([new RenderPassColorAttachment(target.DefaultView, LoadOp.Clear, StoreOp.Store, new Vector4(0, 0, 0, 1))]));
		pass.SetPipeline(_pipeline);
		pass.SetBindGroup(0, _group);
		pass.Draw(3);
		pass.End();
		_device.Queue.Submit(encoder.Finish());
	}

	public void Dispose()
	{
		_group.Dispose();
		_pipeline.Dispose();
		_pipelineLayout.Dispose();
		_layout.Dispose();
		_fragment.Dispose();
		_vertex.Dispose();
	}
}
