using System.Numerics;

using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Graphics.GLES;

internal enum GlesOp : byte
{
	BeginPass,
	EndPass,
	SetPipeline,
	SetBindGroup,
	SetVertexBuffer,
	SetIndexBuffer,
	SetViewport,
	SetScissor,
	Draw,
	DrawIndexed,
	CopyBufferToBuffer,
	CopyTextureToBuffer,
}

/// <summary>One recorded command. A plain struct in a reused list: recording allocates nothing in steady state.</summary>
internal struct GlesCommand
{
	public GlesOp Op;
	public object? A;
	public object? B;
	public ulong X;
	public ulong Y;
	public ulong Z;
	public uint U0;
	public uint U1;
	public uint U2;
	public uint U3;
	public int I0;
	public float F0;
	public float F1;
	public float F2;
	public float F3;
	public float F4;
	public float F5;
}

/// <summary>The attachments of a recorded render pass.</summary>
internal sealed class GlesPass
{
	public int ColorCount;
	public readonly GlesTextureView?[] Colors = new GlesTextureView?[FramebufferKey.MaxColorAttachments];
	public readonly LoadOp[] ColorLoad = new LoadOp[FramebufferKey.MaxColorAttachments];
	public readonly StoreOp[] ColorStore = new StoreOp[FramebufferKey.MaxColorAttachments];
	public readonly Vector4[] Clear = new Vector4[FramebufferKey.MaxColorAttachments];
	public GlesTextureView? Depth;
	public LoadOp DepthLoad;
	public StoreOp DepthStore;
	public float DepthClear;
	public uint Width;
	public uint Height;
	public FramebufferKey Key;

	public void Reset()
	{
		Array.Clear(Colors);
		Depth = null;
		ColorCount = 0;
	}
}

/// <summary>
/// The GLES <see cref="ICommandEncoder"/>, which is also the finished <see cref="ICommandBuffer"/> and, while a pass is
/// open, the <see cref="IRenderPassEncoder"/>. Commands are recorded into a list and replayed by the
/// <see cref="GlesExecutor"/> at submission. Encoders are pooled per frame slot.
/// </summary>
internal sealed class GlesCommandEncoder(GlesDevice device) : ICommandEncoder, ICommandBuffer, IRenderPassEncoder
{
	private readonly List<GlesCommand> _commands = [];
	private readonly List<GlesPass> _passes = [];
	private int _nextPass;
	private State _state = State.Submitted;
	private bool _inPass;

	private enum State
	{
		Recording,
		Finished,
		Submitted,
	}

	public GlesDevice Device => device;

	public List<GlesCommand> Commands => _commands;

	public void Begin()
	{
		_commands.Clear();
		_nextPass = 0;
		_inPass = false;
		_state = State.Recording;
	}

	/// <summary>Marks the buffer submitted (it can be submitted once).</summary>
	public void MarkSubmitted()
	{
		if (_state == State.Recording) throw new InvalidOperationException("Finish the command encoder before submitting it.");
		if (_state == State.Submitted) throw new InvalidOperationException("This command buffer was already submitted.");
		_state = State.Submitted;
	}

	public IRenderPassEncoder BeginRenderPass(in RenderPassDescriptor descriptor)
	{
		_ensureRecording();
		if (_inPass) throw new InvalidOperationException("End the current render pass first.");
		var colors = descriptor.ColorAttachments;
		if (colors.Length > FramebufferKey.MaxColorAttachments) throw new ArgumentException($"At most {FramebufferKey.MaxColorAttachments} color attachments.", nameof(descriptor));
		if (colors.Length == 0 && descriptor.DepthStencilAttachment is null) throw new ArgumentException("A render pass needs at least one attachment.", nameof(descriptor));

		if (_nextPass == _passes.Count) _passes.Add(new GlesPass());
		var pass = _passes[_nextPass++];
		pass.Reset();
		pass.ColorCount = colors.Length;
		var key = new FramebufferKey { ColorCount = colors.Length };
		for (var i = 0; i < colors.Length; i++)
		{
			var view = (GlesTextureView)colors[i].View;
			pass.Colors[i] = view;
			pass.ColorLoad[i] = colors[i].LoadOp;
			pass.ColorStore[i] = colors[i].StoreOp;
			pass.Clear[i] = colors[i].ClearValue;
			key[i] = view.Attachment;
			pass.Width = Math.Max(1, view.TextureImpl.Width >> (int)view.BaseMip);
			pass.Height = Math.Max(1, view.TextureImpl.Height >> (int)view.BaseMip);
		}

		if (descriptor.DepthStencilAttachment is { } depth)
		{
			var view = (GlesTextureView)depth.View;
			pass.Depth = view;
			pass.DepthLoad = depth.DepthLoadOp;
			pass.DepthStore = depth.DepthStoreOp;
			pass.DepthClear = depth.DepthClearValue;
			key.Depth = view.Attachment;
			key.DepthHasStencil = view.Format.HasStencil();
			if (colors.Length == 0)
			{
				pass.Width = Math.Max(1, view.TextureImpl.Width >> (int)view.BaseMip);
				pass.Height = Math.Max(1, view.TextureImpl.Height >> (int)view.BaseMip);
			}
		}

		pass.Key = key;
		_commands.Add(new GlesCommand { Op = GlesOp.BeginPass, A = pass });
		_inPass = true;
		return this;
	}

	public void CopyBufferToBuffer(IBuffer source, ulong sourceOffset, IBuffer destination, ulong destinationOffset, ulong size)
	{
		_ensureRecording();
		_ensureNoPass();
		if (sourceOffset + size > source.Size || destinationOffset + size > destination.Size) throw new ArgumentOutOfRangeException(nameof(size), "The copy overflows a buffer.");
		_commands.Add(new GlesCommand { Op = GlesOp.CopyBufferToBuffer, A = (GlesBuffer)source, B = (GlesBuffer)destination, X = sourceOffset, Y = destinationOffset, Z = size });
	}

	public void CopyTextureToBuffer(ITexture source, TextureRegion region, IBuffer destination, ulong destinationOffset, uint bytesPerRow)
	{
		_ensureRecording();
		_ensureNoPass();
		var texture = (GlesTexture)source;
		var bpp = (uint)texture.Format.BytesPerPixel();
		if (bpp == 0 || bytesPerRow % bpp != 0) throw new ArgumentException($"bytesPerRow ({bytesPerRow}) must be a multiple of the texel size ({bpp}).", nameof(bytesPerRow));
		if (texture.Format.IsDepth()) throw new NotSupportedException("OpenGL ES cannot read back depth textures.");
		if (texture.IsRenderbuffer) throw new NotSupportedException("Multisampled textures cannot be copied on the GLES backend.");
		if (region.Width == 0 || region.Height == 0) return;
		if (destinationOffset + (ulong)bytesPerRow * (region.Height - 1) + (ulong)region.Width * bpp > destination.Size) throw new ArgumentOutOfRangeException(nameof(destination), "The copy overflows the destination buffer.");
		_commands.Add(new GlesCommand
		{
			Op = GlesOp.CopyTextureToBuffer,
			A = texture,
			B = (GlesBuffer)destination,
			X = destinationOffset,
			U0 = region.X,
			U1 = region.Y,
			U2 = region.Width,
			U3 = region.Height,
			I0 = (int)region.MipLevel,
			Y = bytesPerRow,
		});
	}

	public ICommandBuffer Finish()
	{
		_ensureRecording();
		if (_inPass) throw new InvalidOperationException("End the render pass before finishing the encoder.");
		_state = State.Finished;
		return this;
	}

	// IRenderPassEncoder.

	public void SetPipeline(IRenderPipeline pipeline) =>
		_pass(new GlesCommand { Op = GlesOp.SetPipeline, A = (GlesRenderPipeline)pipeline });

	public void SetBindGroup(uint index, IBindGroup group)
	{
		if (index >= GlesBindings.MaxGroups) throw new ArgumentOutOfRangeException(nameof(index), index, $"At most {GlesBindings.MaxGroups} bind groups.");
		_pass(new GlesCommand { Op = GlesOp.SetBindGroup, A = (GlesBindGroup)group, U0 = index });
	}

	public void SetVertexBuffer(uint slot, IBuffer buffer, ulong offset = 0)
	{
		if (slot >= GlesExecutor.MaxVertexBuffers) throw new ArgumentOutOfRangeException(nameof(slot), slot, $"At most {GlesExecutor.MaxVertexBuffers} vertex buffers.");
		_pass(new GlesCommand { Op = GlesOp.SetVertexBuffer, A = (GlesBuffer)buffer, U0 = slot, X = offset });
	}

	public void SetIndexBuffer(IBuffer buffer, IndexFormat format, ulong offset = 0) =>
		_pass(new GlesCommand { Op = GlesOp.SetIndexBuffer, A = (GlesBuffer)buffer, U0 = (uint)format, X = offset });

	public void SetViewport(float x, float y, float width, float height, float minDepth = 0f, float maxDepth = 1f) =>
		_pass(new GlesCommand { Op = GlesOp.SetViewport, F0 = x, F1 = y, F2 = width, F3 = height, F4 = minDepth, F5 = maxDepth });

	public void SetScissorRect(uint x, uint y, uint width, uint height) =>
		_pass(new GlesCommand { Op = GlesOp.SetScissor, U0 = x, U1 = y, U2 = width, U3 = height });

	public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) =>
		_pass(new GlesCommand { Op = GlesOp.Draw, U0 = vertexCount, U1 = instanceCount, U2 = firstVertex, U3 = firstInstance });

	public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0) =>
		_pass(new GlesCommand { Op = GlesOp.DrawIndexed, U0 = indexCount, U1 = instanceCount, U2 = firstIndex, I0 = baseVertex, U3 = firstInstance });

	public void End()
	{
		if (!_inPass) throw new InvalidOperationException("The render pass has already ended.");
		_commands.Add(new GlesCommand { Op = GlesOp.EndPass });
		_inPass = false;
	}

	private void _pass(in GlesCommand command)
	{
		if (!_inPass) throw new InvalidOperationException("The render pass has ended.");
		_commands.Add(command);
	}

	private void _ensureRecording()
	{
		if (_state != State.Recording) throw new InvalidOperationException("The command encoder is finished; create a new one.");
	}

	private void _ensureNoPass()
	{
		if (_inPass) throw new InvalidOperationException("End the render pass before recording copies.");
	}
}
