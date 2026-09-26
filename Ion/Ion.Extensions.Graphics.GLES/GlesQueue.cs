using Silk.NET.OpenGLES;

using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Graphics.GLES;

/// <summary>
/// The GLES <see cref="IQueue"/>. <see cref="Submit(ICommandBuffer)"/> replays the command buffer on the context at once;
/// writes run at the call. GL executes in call order, so a write is seen by every command buffer submitted after it and by
/// none submitted before it, as the RHI requires.
/// </summary>
/// <remarks>
/// Per-frame data: a write that covers a whole buffer re-specifies its storage (<c>glBufferData</c>, orphaning), so the
/// driver never waits for draws still reading the old contents; partial writes use <c>glBufferSubData</c>. Rings indexed by
/// <see cref="IGraphicsDevice.FrameIndex"/> write regions the GPU is done with, because the device waits on the frame
/// slot's fence before reusing it.
/// </remarks>
internal sealed unsafe class GlesQueue(GlesDevice device) : IQueue
{
	private byte[] _swizzled = [];

	public void Submit(ICommandBuffer commandBuffer)
	{
		var encoder = (GlesCommandEncoder)commandBuffer;
		if (!ReferenceEquals(encoder.Device, device)) throw new ArgumentException("The command buffer belongs to another device.", nameof(commandBuffer));
		encoder.MarkSubmitted();
		device.Executor.Execute(encoder);
	}

	public void Submit(ReadOnlySpan<ICommandBuffer> commandBuffers)
	{
		foreach (var commandBuffer in commandBuffers) Submit(commandBuffer);
	}

	public void WriteBuffer(IBuffer buffer, ulong offset, ReadOnlySpan<byte> data)
	{
		if (data.IsEmpty) return;
		var target = (GlesBuffer)buffer;
		if (offset + (ulong)data.Length > target.Size) throw new ArgumentOutOfRangeException(nameof(data), $"Writing {data.Length} bytes at {offset} overflows a {target.Size}-byte buffer.");

		device.Context.MakeCurrent();
		var gl = device.Gl;
		gl.BindBuffer(GLEnum.CopyWriteBuffer, target.Handle);
		fixed (byte* p = data)
		{
			if (offset == 0 && (ulong)data.Length == target.Size) gl.BufferData(GLEnum.CopyWriteBuffer, (nuint)data.Length, p, target.GlUsage);
			else gl.BufferSubData(GLEnum.CopyWriteBuffer, (nint)offset, (nuint)data.Length, p);
		}

		gl.BindBuffer(GLEnum.CopyWriteBuffer, 0);
		device.CheckErrors("WriteBuffer");
	}

	public void WriteTexture(ITexture texture, ReadOnlySpan<byte> data, uint bytesPerRow, TextureRegion region)
	{
		var target = (GlesTexture)texture;
		var bpp = (uint)target.Format.BytesPerPixel();
		if (target.Format.IsDepth()) throw new NotSupportedException("OpenGL ES cannot upload depth textures.");
		if (target.IsRenderbuffer) throw new NotSupportedException("Multisampled textures cannot be written on the GLES backend.");
		if (region.ArrayLayer >= target.Dimension.ArrayLayerCount()) throw new ArgumentOutOfRangeException(nameof(region), region.ArrayLayer, $"The texture has {target.Dimension.ArrayLayerCount()} array layer(s).");
		if (bytesPerRow % bpp != 0) throw new ArgumentException($"bytesPerRow ({bytesPerRow}) must be a multiple of the texel size ({bpp}).", nameof(bytesPerRow));
		if (region.Width == 0 || region.Height == 0) return;
		var size = (ulong)bytesPerRow * (region.Height - 1) + region.Width * bpp;
		if ((ulong)data.Length < size) throw new ArgumentException($"Expected at least {size} bytes, got {data.Length}.", nameof(data));

		device.Context.MakeCurrent();
		var gl = device.Gl;
		var format = target.Gles;
		var rowLength = (int)(bytesPerRow / bpp);
		if (format.SwapRedBlue)
		{
			// BGRA is stored as RGBA: swap red and blue into a tightly packed copy.
			var packed = (int)(region.Width * region.Height * 4);
			if (_swizzled.Length < packed) _swizzled = new byte[packed];
			for (var y = 0; y < region.Height; y++)
			{
				var source = data.Slice((int)(y * bytesPerRow), (int)region.Width * 4);
				var destination = _swizzled.AsSpan((int)(y * region.Width * 4), (int)region.Width * 4);
				for (var x = 0; x < source.Length; x += 4)
				{
					destination[x] = source[x + 2];
					destination[x + 1] = source[x + 1];
					destination[x + 2] = source[x];
					destination[x + 3] = source[x + 3];
				}
			}

			data = _swizzled.AsSpan(0, packed);
			rowLength = (int)region.Width;
		}

		// A cube map face is its own 2D image target (+X, -X, +Y, -Y, +Z, -Z are consecutive enum values).
		var image = target.Dimension == TextureDimension.Cube ? GLEnum.TextureCubeMapPositiveX + (int)region.ArrayLayer : GLEnum.Texture2D;
		gl.BindTexture(target.Target, target.Handle);
		gl.PixelStore(GLEnum.UnpackAlignment, 1);
		gl.PixelStore(GLEnum.UnpackRowLength, rowLength);
		fixed (byte* p = data)
		{
			gl.TexSubImage2D(image, (int)region.MipLevel, (int)region.X, (int)region.Y, region.Width, region.Height, format.Format, format.Type, p);
		}

		gl.PixelStore(GLEnum.UnpackRowLength, 0);
		gl.PixelStore(GLEnum.UnpackAlignment, 4);
		gl.BindTexture(target.Target, 0);
		device.CheckErrors("WriteTexture");
	}

	public void WaitIdle()
	{
		device.Context.MakeCurrent();
		device.Gl.Finish();
	}
}
