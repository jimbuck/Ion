using System.Numerics;
using System.Runtime.InteropServices;

using Hexa.NET.ImGui;

using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.ImGui;

/// <summary>
/// Draws Dear ImGui's draw data on the RHI: one pipeline per target format (straight alpha blending, no depth), a linear
/// sampler, and per frame slot a vertex, an index and a uniform buffer (a ring of <see cref="IGraphicsDevice.FramesInFlight"/>,
/// so a frame never writes what the GPU may still read). Every draw list is uploaded into the shared buffers once; each
/// command binds its list's range by buffer offsets, so no base-vertex draw is needed (GLES 3.1 has none). It also serves
/// ImGui's texture requests (<see cref="ImGuiBackendFlags.RendererHasTextures"/>): the font atlas and anything else ImGui
/// creates, grows or destroys.
/// </summary>
internal sealed unsafe class ImGuiRenderer : IDisposable
{
	private const int UniformSize = 64; // mat4 (std140)
	private static readonly int VertexSize = sizeof(ImDrawVert);

	private readonly IGraphicsDevice _device;
	private readonly IShaderModule _vertex;
	private readonly IShaderModule _fragment;
	private readonly IBindGroupLayout _projectionLayout;
	private readonly IBindGroupLayout _textureLayout;
	private readonly IPipelineLayout _pipelineLayout;
	private readonly ISampler _sampler;
	private readonly Dictionary<TextureFormat, IRenderPipeline> _pipelines = [];
	private readonly FrameResources[] _frames;
	private readonly ImGuiTextures _textures;
	private byte[] _vertexScratch = [];
	private byte[] _indexScratch = [];

	private sealed class FrameResources
	{
		public IBuffer? Vertices;
		public IBuffer? Indices;
		public IBuffer? Uniforms;
		public IBindGroup? Projection;
	}

	public ImGuiRenderer(IGraphicsDevice device, ImGuiTextures textures)
	{
		_device = device;
		_textures = textures;
		var assembly = typeof(ImGuiRenderer).Assembly;
		_vertex = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, assembly, "imgui.vert"));
		_fragment = device.CreateShaderModule(EmbeddedShaders.Descriptor(device, assembly, "imgui.frag"));

		_projectionLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor([new(0, ShaderStage.Vertex, BindingType.UniformBuffer)], "ImGui projection"));
		_textureLayout = device.CreateBindGroupLayout(new BindGroupLayoutDescriptor(
		[
			new(0, ShaderStage.Fragment, BindingType.Texture),
			new(1, ShaderStage.Fragment, BindingType.Sampler),
		], "ImGui texture"));
		_pipelineLayout = device.CreatePipelineLayout(new PipelineLayoutDescriptor([_projectionLayout, _textureLayout], "ImGui"));
		_sampler = device.CreateSampler(SamplerDescriptor.LinearClamp with { Label = "ImGui linear clamp" });

		_frames = new FrameResources[Math.Max(1, device.FramesInFlight)];
		for (var i = 0; i < _frames.Length; i++) _frames[i] = new FrameResources();
	}

	/// <summary>The draw calls issued by the last <see cref="Draw"/>.</summary>
	public int LastDrawCalls { get; private set; }

	/// <summary>Creates the bind group that draws <paramref name="view"/> (for <see cref="ImGuiTextures"/>).</summary>
	public IBindGroup CreateBindGroup(ITextureView view, string? label) => _device.CreateBindGroup(new BindGroupDescriptor(_textureLayout,
	[
		BindGroupEntry.ForTexture(0, view),
		BindGroupEntry.ForSampler(1, _sampler),
	], label));

	/// <summary>Serves ImGui's texture requests: creates, updates and destroys the textures it owns (the font atlas).</summary>
	public void UpdateTextures(ImDrawDataPtr drawData)
	{
		var list = drawData.Handle->Textures; // null before the first frame with textures
		if (list is null) return;
		for (var i = 0; i < list->Size; i++)
		{
			var texture = list->Data[i];
			switch (texture.Status)
			{
				case ImTextureStatus.WantCreate:
					Create(texture);
					break;
				case ImTextureStatus.WantUpdates:
					Update(texture);
					break;
				case ImTextureStatus.WantDestroy when texture.UnusedFrames > 0:
					_textures.Release((nint)texture.TexID.Handle);
					texture.SetTexID(ImTextureID.Null);
					texture.SetStatus(ImTextureStatus.Destroyed);
					break;
			}
		}
	}

	private void Create(ImTextureDataPtr data)
	{
		var width = (uint)data.Width;
		var height = (uint)data.Height;
		var texture = _device.CreateTexture(new TextureDescriptor(width, height, TextureFormat.Rgba8Unorm, TextureUsage.TextureBinding | TextureUsage.CopyDst, Label: "ImGui texture"));
		var id = _textures.Add(texture, owned: true, "ImGui texture");
		Upload(data, texture, 0, 0, width, height);
		data.SetTexID(new ImTextureID(id));
		data.SetStatus(ImTextureStatus.Ok);
	}

	private void Update(ImTextureDataPtr data)
	{
		if (_textures.Find((nint)data.TexID.Handle) is not { } texture)
		{
			Create(data);
			return;
		}

		var updates = data.Updates;
		if (updates.Size == 0)
		{
			var rect = data.UpdateRect;
			Upload(data, texture, rect.X, rect.Y, rect.W, rect.H);
		}
		else
		{
			for (var i = 0; i < updates.Size; i++)
			{
				var rect = updates.Data[i];
				Upload(data, texture, rect.X, rect.Y, rect.W, rect.H);
			}
		}

		data.SetStatus(ImTextureStatus.Ok);
	}

	/// <summary>Writes a rectangle of ImGui's pixels into <paramref name="texture"/> (alpha-only atlases become white with that alpha).</summary>
	private void Upload(ImTextureDataPtr data, ITexture texture, uint x, uint y, uint width, uint height)
	{
		if (width == 0 || height == 0) return;
		var pitch = data.Width * data.BytesPerPixel;
		var pixels = (byte*)data.Pixels;
		var bytes = new byte[width * height * 4];
		for (var row = 0u; row < height; row++)
		{
			var source = pixels + (y + row) * pitch + x * data.BytesPerPixel;
			var target = bytes.AsSpan((int)(row * width * 4), (int)(width * 4));
			if (data.Format == ImTextureFormat.Rgba32)
			{
				new ReadOnlySpan<byte>(source, (int)(width * 4)).CopyTo(target);
			}
			else
			{
				for (var column = 0; column < width; column++)
				{
					target[column * 4] = target[column * 4 + 1] = target[column * 4 + 2] = 255;
					target[column * 4 + 3] = source[column];
				}
			}
		}

		_device.Queue.WriteTexture(texture, bytes, width * 4, new TextureRegion(x, y, width, height));
	}

	/// <summary>Draws <paramref name="drawData"/> into the frame's color target, over what the frame already holds.</summary>
	public void Draw(ImDrawDataPtr drawData, IGraphicsFrame frame)
	{
		LastDrawCalls = 0;
		if (!frame.IsRendering || frame.ColorTarget is not { } target) return;
		if (drawData.TotalVtxCount == 0 || drawData.CmdListsCount == 0) return;

		var resources = _frames[_device.FrameIndex % _frames.Length];
		var queue = _device.Queue;

		// Every list into one vertex and one index upload.
		var vertexBytes = drawData.TotalVtxCount * VertexSize;
		var indexBytes = drawData.TotalIdxCount * sizeof(ushort);
		var indexBytesAligned = (indexBytes + 3) & ~3;
		if (_vertexScratch.Length < vertexBytes) _vertexScratch = new byte[(int)BitOperations.RoundUpToPowerOf2((uint)vertexBytes)];
		if (_indexScratch.Length < indexBytesAligned) _indexScratch = new byte[(int)BitOperations.RoundUpToPowerOf2((uint)indexBytesAligned)];
		var vertexOffset = 0;
		var indexOffset = 0;
		for (var n = 0; n < drawData.CmdListsCount; n++)
		{
			var list = drawData.CmdLists[n];
			var vertices = list.VtxBuffer.Size * VertexSize;
			var indices = list.IdxBuffer.Size * sizeof(ushort);
			new ReadOnlySpan<byte>(list.VtxBuffer.Data, vertices).CopyTo(_vertexScratch.AsSpan(vertexOffset));
			new ReadOnlySpan<byte>(list.IdxBuffer.Data, indices).CopyTo(_indexScratch.AsSpan(indexOffset));
			vertexOffset += vertices;
			indexOffset += indices;
		}

		Ensure(ref resources.Vertices, (ulong)vertexBytes, BufferUsage.Vertex | BufferUsage.CopyDst, "ImGui vertices");
		Ensure(ref resources.Indices, (ulong)indexBytesAligned, BufferUsage.Index | BufferUsage.CopyDst, "ImGui indices");
		queue.WriteBuffer(resources.Vertices!, 0, _vertexScratch.AsSpan(0, vertexBytes));
		queue.WriteBuffer(resources.Indices!, 0, _indexScratch.AsSpan(0, indexBytesAligned));

		if (resources.Uniforms is null)
		{
			resources.Uniforms = _device.CreateBuffer(new BufferDescriptor(UniformSize, BufferUsage.Uniform | BufferUsage.CopyDst, "ImGui projection"));
			resources.Projection = _device.CreateBindGroup(new BindGroupDescriptor(_projectionLayout, [BindGroupEntry.ForBuffer(0, resources.Uniforms, 0, UniformSize)]));
		}

		var position = drawData.DisplayPos;
		var size = drawData.DisplaySize;
		var projection = Matrix4x4.CreateOrthographicOffCenter(position.X, position.X + size.X, position.Y + size.Y, position.Y, 0, 1);
		Span<byte> uniform = stackalloc byte[UniformSize];
		MemoryMarshal.Write(uniform, in projection);
		queue.WriteBuffer(resources.Uniforms, 0, uniform);

		var encoder = _device.CreateCommandEncoder("Ion ImGui");
		var pass = encoder.BeginRenderPass(new RenderPassDescriptor([frame.ColorAttachment()], label: "ImGui"));
		pass.SetPipeline(GetPipeline(target.Format));
		pass.SetBindGroup(0, resources.Projection!);
		pass.SetViewport(0, 0, target.Width, target.Height);

		// The display may be scaled to the target (a framebuffer larger than the window).
		var scale = new Vector2(target.Width / Math.Max(1f, size.X), target.Height / Math.Max(1f, size.Y));
		var listVertex = 0;
		var listIndex = 0;
		IBindGroup? bound = null;
		for (var n = 0; n < drawData.CmdListsCount; n++)
		{
			var list = drawData.CmdLists[n];
			for (var c = 0; c < list.CmdBuffer.Size; c++)
			{
				ref var command = ref list.CmdBuffer.Data[c];
				if (command.UserCallback != null || command.ElemCount == 0) continue;

				var clip = command.ClipRect;
				var x0 = Math.Clamp((clip.X - position.X) * scale.X, 0, target.Width);
				var y0 = Math.Clamp((clip.Y - position.Y) * scale.Y, 0, target.Height);
				var x1 = Math.Clamp((clip.Z - position.X) * scale.X, 0, target.Width);
				var y1 = Math.Clamp((clip.W - position.Y) * scale.Y, 0, target.Height);
				if (x1 <= x0 || y1 <= y0) continue;

				if (_textures.BindGroup((nint)command.GetTexID().Handle) is not { } group) continue;
				if (!ReferenceEquals(group, bound))
				{
					pass.SetBindGroup(1, group);
					bound = group;
				}

				pass.SetScissorRect((uint)x0, (uint)y0, (uint)(x1 - x0), (uint)(y1 - y0));
				pass.SetVertexBuffer(0, resources.Vertices!, (ulong)(listVertex + (int)command.VtxOffset) * (ulong)VertexSize);
				pass.SetIndexBuffer(resources.Indices!, IndexFormat.Uint16, (ulong)(listIndex + (int)command.IdxOffset) * sizeof(ushort));
				pass.DrawIndexed(command.ElemCount);
				LastDrawCalls++;
			}

			listVertex += list.VtxBuffer.Size;
			listIndex += list.IdxBuffer.Size;
		}

		pass.End();
		queue.Submit(encoder.Finish());
	}

	private IRenderPipeline GetPipeline(TextureFormat format)
	{
		if (_pipelines.TryGetValue(format, out var pipeline)) return pipeline;
		pipeline = _device.CreateRenderPipeline(new RenderPipelineDescriptor
		{
			Label = $"ImGui {format}",
			Layout = _pipelineLayout,
			Vertex = new VertexState(_vertex,
			[
				new VertexBufferLayout((uint)VertexSize, VertexStepMode.Vertex,
				[
					new VertexAttribute(VertexFormat.Float32x2, 0, 0),
					new VertexAttribute(VertexFormat.Float32x2, 8, 1),
					new VertexAttribute(VertexFormat.Unorm8x4, 16, 2),
				]),
			]),
			Primitive = new PrimitiveState(),
			Fragment = new FragmentState(_fragment, [new ColorTargetState(format, BlendState.AlphaBlend)]),
		});
		_pipelines[format] = pipeline;
		return pipeline;
	}

	private void Ensure(ref IBuffer? buffer, ulong bytes, BufferUsage usage, string label)
	{
		if (buffer is not null && buffer.Size >= bytes) return;
		buffer?.Dispose();
		buffer = _device.CreateBuffer(new BufferDescriptor(BitOperations.RoundUpToPowerOf2(Math.Max(bytes, 16 * 1024ul)), usage, label));
	}

	/// <summary>Releases every GPU resource of the renderer (the textures are <see cref="ImGuiTextures"/>').</summary>
	public void Dispose()
	{
		foreach (var frame in _frames)
		{
			frame.Projection?.Dispose();
			frame.Uniforms?.Dispose();
			frame.Vertices?.Dispose();
			frame.Indices?.Dispose();
			frame.Projection = null;
			frame.Uniforms = frame.Vertices = frame.Indices = null;
		}

		foreach (var pipeline in _pipelines.Values) pipeline.Dispose();
		_pipelines.Clear();
		_sampler.Dispose();
		_pipelineLayout.Dispose();
		_textureLayout.Dispose();
		_projectionLayout.Dispose();
		_fragment.Dispose();
		_vertex.Dispose();
	}
}
