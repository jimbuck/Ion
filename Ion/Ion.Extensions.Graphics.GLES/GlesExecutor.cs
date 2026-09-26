using System.Runtime.InteropServices;

using Silk.NET.OpenGLES;

using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Graphics.GLES;

/// <summary>
/// Replays recorded command buffers on the GL context. Keeps the bindings of the pass being replayed (pipeline, bind
/// groups, vertex and index buffers) and applies them lazily at draw time, so bind groups and buffers may be set before
/// or after the pipeline, as in WebGPU.
/// </summary>
internal sealed unsafe class GlesExecutor(GlesDevice device)
{
	public const int MaxVertexBuffers = 8;

	private readonly GlesBindGroup?[] _groups = new GlesBindGroup?[GlesBindings.MaxGroups];
	private readonly (GlesBuffer? Buffer, ulong Offset)[] _vertexBuffers = new (GlesBuffer?, ulong)[MaxVertexBuffers];
	private readonly Dictionary<(uint Handle, int Level), uint> _readFramebuffers = [];
	private GlesPass? _pass;
	private GlesRenderPipeline? _pipeline;
	private GlesBuffer? _indexBuffer;
	private IndexFormat _indexFormat;
	private ulong _indexOffset;
	private uint _groupsDirty;
	private bool _samplersDirty;
	private bool _vertexDirty;
	private bool _indexDirty;
	private long _appliedVertexShift = long.MinValue;
	private long _appliedInstanceShift = long.MinValue;
	private int _appliedBaseInstance = int.MinValue;
	private byte[] _scratch = [];

	private GL Gl => device.Gl;

	/// <summary>Forgets the bound framebuffer (another path bound one).</summary>
	public void InvalidateFramebuffer() => _pass = null;

	/// <summary>Forgets the bound vertex array (pipeline creation binds its own).</summary>
	public void InvalidateVertexArray()
	{
		_pipeline = null;
		_vertexDirty = _indexDirty = true;
	}

	/// <summary>Replays <paramref name="encoder"/>'s commands.</summary>
	public void Execute(GlesCommandEncoder encoder)
	{
		device.Context.MakeCurrent();
		var commands = CollectionsMarshal.AsSpan(encoder.Commands);
		foreach (ref readonly var command in commands)
		{
			switch (command.Op)
			{
				case GlesOp.BeginPass: _beginPass((GlesPass)command.A!); break;
				case GlesOp.EndPass: _endPass(); break;
				case GlesOp.SetPipeline: _setPipeline((GlesRenderPipeline)command.A!); break;
				case GlesOp.SetBindGroup:
					_groups[command.U0] = (GlesBindGroup)command.A!;
					_groupsDirty |= 1u << (int)command.U0;
					_samplersDirty = true;
					break;
				case GlesOp.SetVertexBuffer:
					_vertexBuffers[command.U0] = ((GlesBuffer)command.A!, command.X);
					_vertexDirty = true;
					break;
				case GlesOp.SetIndexBuffer:
					_indexBuffer = (GlesBuffer)command.A!;
					_indexFormat = (IndexFormat)command.U0;
					_indexOffset = command.X;
					_indexDirty = true;
					break;
				case GlesOp.SetViewport:
					Gl.Viewport((int)MathF.Round(command.F0), (int)MathF.Round(command.F1), (uint)MathF.Round(command.F2), (uint)MathF.Round(command.F3));
					Gl.DepthRange(command.F4, command.F5);
					break;
				case GlesOp.SetScissor:
					Gl.Scissor((int)command.U0, (int)command.U1, command.U2, command.U3);
					break;
				case GlesOp.Draw: _draw(command.U0, command.U1, command.U2, command.U3); break;
				case GlesOp.DrawIndexed: _drawIndexed(command.U0, command.U1, command.U2, command.I0, command.U3); break;
				case GlesOp.CopyBufferToBuffer: _copyBuffer((GlesBuffer)command.A!, command.X, (GlesBuffer)command.B!, command.Y, command.Z); break;
				case GlesOp.CopyTextureToBuffer:
					_copyTextureToBuffer((GlesTexture)command.A!, new TextureRegion(command.U0, command.U1, command.U2, command.U3, (uint)command.I0), (GlesBuffer)command.B!, command.X, (uint)command.Y);
					break;
			}
		}

		device.CheckErrors("a command buffer submission");
	}

	public void Destroy()
	{
		foreach (var framebuffer in _readFramebuffers.Values) Gl.DeleteFramebuffer(framebuffer);
		_readFramebuffers.Clear();
	}

	/// <summary>Deletes the cached read framebuffers of a deleted texture.</summary>
	public void ForgetTexture(uint handle)
	{
		List<(uint, int)>? stale = null;
		foreach (var key in _readFramebuffers.Keys)
		{
			if (key.Handle == handle) (stale ??= []).Add(key);
		}

		if (stale is null) return;
		foreach (var key in stale)
		{
			Gl.DeleteFramebuffer(_readFramebuffers[key]);
			_readFramebuffers.Remove(key);
		}
	}

	private void _beginPass(GlesPass pass)
	{
		var framebuffer = device.GetFramebuffer(pass.Key);
		Gl.BindFramebuffer(GLEnum.Framebuffer, framebuffer);
		_pass = pass;

		// Clears ignore neither the scissor nor the write masks: open both.
		Gl.Disable(GLEnum.ScissorTest);
		Gl.ColorMask(true, true, true, true);
		Gl.DepthMask(true);
		for (var i = 0; i < pass.ColorCount; i++)
		{
			if (pass.ColorLoad[i] != LoadOp.Clear) continue;
			var clear = pass.Clear[i];
			Gl.ClearBuffer(GLEnum.Color, i, (float*)&clear);
		}

		if (pass.Depth is { } depth && pass.DepthLoad == LoadOp.Clear)
		{
			if (depth.Format.HasStencil())
			{
				Gl.ClearBuffer(GLEnum.DepthStencil, 0, pass.DepthClear, 0);
			}
			else
			{
				var value = pass.DepthClear;
				Gl.ClearBuffer(GLEnum.Depth, 0, &value);
			}
		}

		// WebGPU state at the start of a pass: full viewport and scissor, nothing bound.
		Gl.Viewport(0, 0, pass.Width, pass.Height);
		Gl.DepthRange(0f, 1f);
		Gl.Enable(GLEnum.ScissorTest);
		Gl.Scissor(0, 0, pass.Width, pass.Height);
		_pipeline = null;
		Array.Clear(_groups);
		Array.Clear(_vertexBuffers);
		_indexBuffer = null;
		_groupsDirty = 0;
		_samplersDirty = _vertexDirty = _indexDirty = true;
		_appliedVertexShift = _appliedInstanceShift = long.MinValue;
		_appliedBaseInstance = int.MinValue;
	}

	private void _endPass()
	{
		if (_pass is not { } pass) return;
		Span<GLEnum> discard = stackalloc GLEnum[FramebufferKey.MaxColorAttachments + 1];
		var count = 0;
		for (var i = 0; i < pass.ColorCount; i++)
		{
			if (pass.ColorStore[i] == StoreOp.Discard) discard[count++] = GLEnum.ColorAttachment0 + i;
		}

		if (pass.Depth is { } depth && pass.DepthStore == StoreOp.Discard)
		{
			discard[count++] = depth.Format.HasStencil() ? GLEnum.DepthStencilAttachment : GLEnum.DepthAttachment;
		}

		if (count > 0) Gl.InvalidateFramebuffer(GLEnum.Framebuffer, discard[..count]);
	}

	private void _setPipeline(GlesRenderPipeline pipeline)
	{
		if (ReferenceEquals(pipeline, _pipeline)) return;
		_pipeline = pipeline;
		Gl.UseProgram(pipeline.Program);
		Gl.BindVertexArray(pipeline.VertexArray);

		if (pipeline.CullMode == CullMode.None) Gl.Disable(GLEnum.CullFace);
		else
		{
			Gl.Enable(GLEnum.CullFace);
			Gl.CullFace(pipeline.CullMode == CullMode.Front ? GLEnum.Front : GLEnum.Back);
		}

		Gl.FrontFace(pipeline.FrontFace);

		if (pipeline.Depth is { } depth)
		{
			Gl.Enable(GLEnum.DepthTest);
			Gl.DepthFunc(depth.DepthCompare.ToGles());
			Gl.DepthMask(depth.DepthWriteEnabled);
		}
		else
		{
			Gl.Disable(GLEnum.DepthTest);
			Gl.DepthMask(false);
		}

		if (pipeline.Blend is { } blend)
		{
			Gl.Enable(GLEnum.Blend);
			Gl.BlendFuncSeparate(blend.Color.SrcFactor.ToGles(), blend.Color.DstFactor.ToGles(), blend.Alpha.SrcFactor.ToGles(), blend.Alpha.DstFactor.ToGles());
			Gl.BlendEquationSeparate(blend.Color.Operation.ToGles(), blend.Alpha.Operation.ToGles());
		}
		else
		{
			Gl.Disable(GLEnum.Blend);
		}

		var mask = pipeline.WriteMask;
		Gl.ColorMask((mask & ColorWriteMask.Red) != 0, (mask & ColorWriteMask.Green) != 0, (mask & ColorWriteMask.Blue) != 0, (mask & ColorWriteMask.Alpha) != 0);

		// The vertex array holds the element buffer binding and (ES 3.0) the attribute pointers; strides are per pipeline.
		_vertexDirty = _indexDirty = _samplersDirty = true;
		_appliedVertexShift = _appliedInstanceShift = long.MinValue;
		_appliedBaseInstance = int.MinValue;
	}

	private GlesRenderPipeline _prepareDraw(long vertexShift, long instanceShift, uint firstInstance)
	{
		if (_pass is null) throw new InvalidOperationException("Draws must be inside a render pass.");
		var pipeline = _pipeline ?? throw new InvalidOperationException("Set a pipeline before drawing.");

		if (_groupsDirty != 0)
		{
			for (var g = 0; g < _groups.Length; g++)
			{
				if ((_groupsDirty & (1u << g)) == 0 || _groups[g] is not { } group) continue;
				foreach (var binding in group.Uniforms)
				{
					Gl.BindBufferRange(GLEnum.UniformBuffer, (uint)GlesBindings.Slot((uint)g, binding.Binding), binding.Buffer.Handle, (nint)binding.Offset, (nuint)binding.Size);
				}

				foreach (var binding in group.Storage)
				{
					Gl.BindBufferRange(GLEnum.ShaderStorageBuffer, (uint)GlesBindings.Slot((uint)g, binding.Binding), binding.Buffer.Handle, (nint)binding.Offset, (nuint)binding.Size);
				}

				foreach (var binding in group.Textures)
				{
					var view = binding.View;
					var texture = view.TextureImpl;
					Gl.ActiveTexture(GLEnum.Texture0 + GlesBindings.Slot((uint)g, binding.Binding));
					Gl.BindTexture(texture.Target, texture.Handle);
					if (texture.AppliedMips != (view.BaseMip, view.MipCount))
					{
						Gl.TexParameter(texture.Target, GLEnum.TextureBaseLevel, (int)view.BaseMip);
						Gl.TexParameter(texture.Target, GLEnum.TextureMaxLevel, (int)(view.BaseMip + view.MipCount - 1));
						texture.AppliedMips = (view.BaseMip, view.MipCount);
					}
				}
			}

			_groupsDirty = 0;
		}

		if (_samplersDirty)
		{
			if (pipeline.HasBindingTable)
			{
				// The sampler objects the shaders combined with each texture (from the flattening table).
				foreach (var pair in pipeline.SamplerPairs)
				{
					var sampler = pair.SamplerGroup < _groups.Length ? _groups[pair.SamplerGroup]?.SamplerAt(pair.SamplerBinding) : null;
					Gl.BindSampler((uint)pair.Unit, sampler?.Handle ?? 0);
				}
			}
			else
			{
				// No table (shader not from Ion.Shaders): a group's only sampler samples all of its textures.
				for (var g = 0; g < _groups.Length; g++)
				{
					if (_groups[g] is not { Samplers.Length: 1 } group) continue;
					foreach (var binding in group.Textures) Gl.BindSampler((uint)GlesBindings.Slot((uint)g, binding.Binding), group.Samplers[0].Sampler.Handle);
				}
			}

			_samplersDirty = false;
		}

		if (_vertexDirty || vertexShift != _appliedVertexShift || instanceShift != _appliedInstanceShift)
		{
			_bindVertexBuffers(pipeline, vertexShift, instanceShift);
			_vertexDirty = false;
			_appliedVertexShift = vertexShift;
			_appliedInstanceShift = instanceShift;
		}

		if (_indexDirty)
		{
			Gl.BindBuffer(GLEnum.ElementArrayBuffer, _indexBuffer?.Handle ?? 0);
			_indexDirty = false;
		}

		if (pipeline.BaseInstanceLocation >= 0 && _appliedBaseInstance != (int)firstInstance)
		{
			Gl.Uniform1(pipeline.BaseInstanceLocation, (int)firstInstance);
			_appliedBaseInstance = (int)firstInstance;
		}

		return pipeline;
	}

	/// <summary>
	/// Binds the vertex buffers, shifting per-vertex buffers by <paramref name="vertexShift"/> elements (base vertex
	/// emulation) and per-instance buffers by <paramref name="instanceShift"/> elements (first instance emulation).
	/// </summary>
	private void _bindVertexBuffers(GlesRenderPipeline pipeline, long vertexShift, long instanceShift)
	{
		ulong Offset(int slot)
		{
			var shift = pipeline.StepModes[slot] == VertexStepMode.Instance ? instanceShift : vertexShift;
			var offset = (long)_vertexBuffers[slot].Offset + shift * pipeline.Strides[slot];
			if (offset < 0) throw new NotSupportedException("A negative base vertex below the start of the vertex buffer needs OpenGL ES 3.2 on the GLES backend.");
			return (ulong)offset;
		}

		if (device.VertexAttribBinding)
		{
			for (var slot = 0; slot < pipeline.Strides.Length; slot++)
			{
				var (buffer, _) = _vertexBuffers[slot];
				if (buffer is null) continue;
				Gl.BindVertexBuffer((uint)slot, buffer.Handle, (nint)Offset(slot), pipeline.Strides[slot]);
			}

			return;
		}

		// ES 3.0: attribute pointers into the buffer bound to GL_ARRAY_BUFFER.
		foreach (var attribute in pipeline.Attributes)
		{
			var (buffer, _) = _vertexBuffers[attribute.Slot];
			if (buffer is null) continue;
			Gl.BindBuffer(GLEnum.ArrayBuffer, buffer.Handle);
			var pointer = (void*)(nint)(Offset((int)attribute.Slot) + attribute.Offset);
			var stride = pipeline.Strides[attribute.Slot];
			var format = attribute.Format;
			if (format.Integer) Gl.VertexAttribIPointer(attribute.Location, format.Components, format.Type, stride, pointer);
			else Gl.VertexAttribPointer(attribute.Location, format.Components, format.Type, format.Normalized, stride, pointer);
		}

		Gl.BindBuffer(GLEnum.ArrayBuffer, 0);
	}

	private void _draw(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
	{
		if (vertexCount == 0 || instanceCount == 0) return;
		var pipeline = _prepareDraw(0, firstInstance, firstInstance);
		if (instanceCount == 1) Gl.DrawArrays(pipeline.Topology, (int)firstVertex, vertexCount);
		else Gl.DrawArraysInstanced(pipeline.Topology, (int)firstVertex, vertexCount, instanceCount);
	}

	private void _drawIndexed(uint indexCount, uint instanceCount, uint firstIndex, int baseVertex, uint firstInstance)
	{
		if (indexCount == 0 || instanceCount == 0) return;
		if (_indexBuffer is null) throw new InvalidOperationException("Set an index buffer before an indexed draw.");
		var native = device.NativeBaseVertex && baseVertex != 0;
		var pipeline = _prepareDraw(native ? 0 : baseVertex, firstInstance, firstInstance);
		var type = _indexFormat.ToGles();
		var offset = (void*)(nint)(_indexOffset + firstIndex * _indexFormat.IndexSize());
		if (native) Gl.DrawElementsInstancedBaseVertex(pipeline.Topology, indexCount, type, offset, instanceCount, baseVertex);
		else if (instanceCount == 1) Gl.DrawElements(pipeline.Topology, indexCount, type, offset);
		else Gl.DrawElementsInstanced(pipeline.Topology, indexCount, type, offset, instanceCount);
	}

	private void _copyBuffer(GlesBuffer source, ulong sourceOffset, GlesBuffer destination, ulong destinationOffset, ulong size)
	{
		Gl.BindBuffer(GLEnum.CopyReadBuffer, source.Handle);
		Gl.BindBuffer(GLEnum.CopyWriteBuffer, destination.Handle);
		Gl.CopyBufferSubData(GLEnum.CopyReadBuffer, GLEnum.CopyWriteBuffer, (nint)sourceOffset, (nint)destinationOffset, (nuint)size);
		Gl.BindBuffer(GLEnum.CopyReadBuffer, 0);
		Gl.BindBuffer(GLEnum.CopyWriteBuffer, 0);
	}

	/// <summary>
	/// Reads a texture region into a buffer. RGBA8 formats go straight into the buffer through <c>GL_PIXEL_PACK_BUFFER</c>
	/// (no CPU stall); the other color formats are read as RGBA to the CPU (the one format GLES guarantees), converted to
	/// the texel layout and written into the buffer.
	/// </summary>
	private void _copyTextureToBuffer(GlesTexture texture, TextureRegion region, GlesBuffer destination, ulong destinationOffset, uint bytesPerRow)
	{
		Gl.BindFramebuffer(GLEnum.ReadFramebuffer, _readFramebuffer(texture, (int)region.MipLevel));
		Gl.ReadBuffer(GLEnum.ColorAttachment0);
		Gl.PixelStore(GLEnum.PackAlignment, 1);
		var bpp = texture.Format.BytesPerPixel();

		if (texture.Gles is { Format: GLEnum.Rgba, Type: GLEnum.UnsignedByte, SwapRedBlue: false })
		{
			Gl.PixelStore(GLEnum.PackRowLength, (int)(bytesPerRow / (uint)bpp));
			Gl.BindBuffer(GLEnum.PixelPackBuffer, destination.Handle);
			Gl.ReadPixels((int)region.X, (int)region.Y, region.Width, region.Height, GLEnum.Rgba, GLEnum.UnsignedByte, (void*)(nint)destinationOffset);
			Gl.BindBuffer(GLEnum.PixelPackBuffer, 0);
			Gl.PixelStore(GLEnum.PackRowLength, 0);
		}
		else
		{
			var isFloat = texture.Format.IsFloat();
			var readSize = checked((int)(region.Width * region.Height * (isFloat ? 16u : 4u)));
			if (_scratch.Length < readSize) _scratch = new byte[readSize];
			fixed (byte* p = _scratch) Gl.ReadPixels((int)region.X, (int)region.Y, region.Width, region.Height, GLEnum.Rgba, isFloat ? GLEnum.Float : GLEnum.UnsignedByte, p);

			var rowBytes = (int)region.Width * bpp;
			var row = new byte[rowBytes];
			Gl.BindBuffer(GLEnum.CopyWriteBuffer, destination.Handle);
			for (var y = 0; y < region.Height; y++)
			{
				_convertRow(texture, _scratch.AsSpan(y * (int)region.Width * (isFloat ? 16 : 4)), row, (int)region.Width);
				fixed (byte* p = row) Gl.BufferSubData(GLEnum.CopyWriteBuffer, (nint)(destinationOffset + (ulong)y * bytesPerRow), (nuint)rowBytes, p);
			}

			Gl.BindBuffer(GLEnum.CopyWriteBuffer, 0);
		}

		Gl.BindFramebuffer(GLEnum.ReadFramebuffer, 0);
		_pass = null;
	}

	private static void _convertRow(GlesTexture texture, ReadOnlySpan<byte> rgba, Span<byte> row, int width)
	{
		var format = texture.Format;
		if (!format.IsFloat())
		{
			var channels = format.Channels();
			for (var x = 0; x < width; x++)
			{
				for (var c = 0; c < channels; c++) row[x * channels + c] = rgba[x * 4 + c];
				if (texture.Gles.SwapRedBlue) (row[x * 4], row[x * 4 + 2]) = (row[x * 4 + 2], row[x * 4]);
			}

			return;
		}

		var floats = MemoryMarshal.Cast<byte, float>(rgba);
		var channelCount = format.Channels();
		var half = format is TextureFormat.R16Float or TextureFormat.Rgba16Float;
		for (var x = 0; x < width; x++)
		{
			for (var c = 0; c < channelCount; c++)
			{
				var value = floats[x * 4 + c];
				if (half) MemoryMarshal.Write(row[((x * channelCount + c) * 2)..], (Half)value);
				else MemoryMarshal.Write(row[((x * channelCount + c) * 4)..], value);
			}
		}
	}

	private uint _readFramebuffer(GlesTexture texture, int level)
	{
		if (_readFramebuffers.TryGetValue((texture.Handle, level), out var framebuffer)) return framebuffer;
		framebuffer = Gl.GenFramebuffer();
		Gl.BindFramebuffer(GLEnum.ReadFramebuffer, framebuffer);
		Gl.FramebufferTexture2D(GLEnum.ReadFramebuffer, GLEnum.ColorAttachment0, GLEnum.Texture2D, texture.Handle, level);
		_readFramebuffers.Add((texture.Handle, level), framebuffer);
		return framebuffer;
	}
}
