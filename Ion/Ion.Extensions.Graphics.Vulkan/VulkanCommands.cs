using Silk.NET.Vulkan;

using Ion.Extensions.Graphics.Rhi;

using VkBuffer = Silk.NET.Vulkan.Buffer;
using VkImage = Silk.NET.Vulkan.Image;

namespace Ion.Extensions.Graphics.Vulkan;

/// <summary>
/// The Vulkan <see cref="ICommandEncoder"/>, which is also the finished <see cref="ICommandBuffer"/>. Encoders are pooled per
/// frame slot and reused once their frame has completed, so recording allocates nothing in steady state.
/// </summary>
internal sealed unsafe class VulkanCommandEncoder(VulkanDevice device) : ICommandEncoder, ICommandBuffer
{
	private readonly VulkanRenderPassEncoder _pass = new(device);
	private CommandBuffer _cmd;
	private bool _recording;

	public void Begin(CommandBuffer cmd)
	{
		_cmd = cmd;
		_recording = true;
	}

	/// <summary>The command buffer to submit. The encoder must be finished.</summary>
	public CommandBuffer Finished()
	{
		if (_recording) throw new InvalidOperationException("Finish the command encoder before submitting it.");
		if (_cmd.Handle == 0) throw new InvalidOperationException("This command buffer was already submitted.");
		var cmd = _cmd;
		_cmd = default;
		return cmd;
	}

	public IRenderPassEncoder BeginRenderPass(in RenderPassDescriptor descriptor)
	{
		_ensureRecording();
		if (_pass.IsActive) throw new InvalidOperationException("End the current render pass first.");
		var colors = descriptor.ColorAttachments;
		if (colors.Length > RenderPassKey.MaxColorAttachments) throw new ArgumentException($"At most {RenderPassKey.MaxColorAttachments} color attachments.", nameof(descriptor));
		if (colors.Length == 0 && descriptor.DepthStencilAttachment is null) throw new ArgumentException("A render pass needs at least one attachment.", nameof(descriptor));

		var key = new RenderPassKey { ColorCount = colors.Length };
		var views = stackalloc ImageView[RenderPassKey.MaxColorAttachments + 1];
		var clears = stackalloc ClearValue[RenderPassKey.MaxColorAttachments + 1];
		uint width = 0, height = 0;
		var samples = SampleCountFlags.Count1Bit;

		for (var i = 0; i < colors.Length; i++)
		{
			var attachment = colors[i];
			var view = (VulkanTextureView)attachment.View;
			var texture = view.TextureImpl;
			if (texture.Dimension == TextureDimension.Cube) throw new NotSupportedException("A cube map cannot be a render attachment.");
			width = texture.Width;
			height = texture.Height;
			samples = VulkanFormats.ToVkSamples(texture.SampleCount);

			var final = texture.IsSwapchainImage ? ImageLayout.PresentSrcKhr
				: (texture.Usage & TextureUsage.TextureBinding) != 0 ? ImageLayout.ShaderReadOnlyOptimal
				: ImageLayout.ColorAttachmentOptimal;
			key[i] = new AttachmentKey(
				texture.VkFormat,
				attachment.LoadOp == LoadOp.Clear ? AttachmentLoadOp.Clear : AttachmentLoadOp.Load,
				attachment.StoreOp == StoreOp.Store ? AttachmentStoreOp.Store : AttachmentStoreOp.DontCare,
				attachment.LoadOp == LoadOp.Load ? texture.Layout : ImageLayout.Undefined,
				final);
			texture.Layout = final;
			views[i] = view.Handle;
			var c = attachment.ClearValue;
			clears[i] = new ClearValue(new ClearColorValue(c.X, c.Y, c.Z, c.W));
		}

		var attachmentCount = colors.Length;
		if (descriptor.DepthStencilAttachment is { } depth)
		{
			var view = (VulkanTextureView)depth.View;
			var texture = view.TextureImpl;
			if (width == 0)
			{
				width = texture.Width;
				height = texture.Height;
				samples = VulkanFormats.ToVkSamples(texture.SampleCount);
			}

			var final = (texture.Usage & TextureUsage.TextureBinding) != 0 ? ImageLayout.ShaderReadOnlyOptimal : ImageLayout.DepthStencilAttachmentOptimal;
			key.HasDepth = true;
			key.Depth = new AttachmentKey(
				texture.VkFormat,
				depth.DepthLoadOp == LoadOp.Clear ? AttachmentLoadOp.Clear : AttachmentLoadOp.Load,
				depth.DepthStoreOp == StoreOp.Store ? AttachmentStoreOp.Store : AttachmentStoreOp.DontCare,
				depth.DepthLoadOp == LoadOp.Load ? texture.Layout : ImageLayout.Undefined,
				final);
			texture.Layout = final;
			views[attachmentCount] = view.Handle;
			clears[attachmentCount] = new ClearValue(depthStencil: new ClearDepthStencilValue(depth.DepthClearValue, 0));
			attachmentCount++;
		}

		key.Samples = samples;
		var renderPass = device.GetRenderPass(key);

		var framebufferInfo = new FramebufferCreateInfo
		{
			SType = StructureType.FramebufferCreateInfo,
			RenderPass = renderPass,
			AttachmentCount = (uint)attachmentCount,
			PAttachments = views,
			Width = width,
			Height = height,
			Layers = 1,
		};
		VulkanDevice.Check(device.Vk.CreateFramebuffer(device.Handle, in framebufferInfo, null, out var framebuffer), "vkCreateFramebuffer");
		device.Defer(() => device.Vk.DestroyFramebuffer(device.Handle, framebuffer, null));

		var begin = new RenderPassBeginInfo
		{
			SType = StructureType.RenderPassBeginInfo,
			RenderPass = renderPass,
			Framebuffer = framebuffer,
			RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(width, height)),
			ClearValueCount = (uint)attachmentCount,
			PClearValues = clears,
		};
		device.Vk.CmdBeginRenderPass(_cmd, in begin, SubpassContents.Inline);
		_pass.Begin(_cmd, width, height);
		return _pass;
	}

	public void CopyBufferToBuffer(IBuffer source, ulong sourceOffset, IBuffer destination, ulong destinationOffset, ulong size)
	{
		_ensureRecording();
		var vk = device.Vk;
		GlobalBarrier(vk, _cmd, PipelineStageFlags.AllCommandsBit, AccessFlags.MemoryWriteBit, PipelineStageFlags.TransferBit, AccessFlags.TransferReadBit | AccessFlags.TransferWriteBit);
		var region = new BufferCopy(sourceOffset, destinationOffset, size);
		vk.CmdCopyBuffer(_cmd, ((VulkanBuffer)source).Handle, ((VulkanBuffer)destination).Handle, 1, in region);
		GlobalBarrier(vk, _cmd, PipelineStageFlags.TransferBit, AccessFlags.TransferWriteBit, PipelineStageFlags.AllCommandsBit | PipelineStageFlags.HostBit, AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit | AccessFlags.HostReadBit);
	}

	public void CopyTextureToBuffer(ITexture source, TextureRegion region, IBuffer destination, ulong destinationOffset, uint bytesPerRow)
	{
		_ensureRecording();
		var texture = (VulkanTexture)source;
		if (texture.Dimension == TextureDimension.Cube) throw new NotSupportedException("A cube map cannot be a copy source.");
		var bpp = (uint)texture.Format.BytesPerPixel();
		if (bpp == 0 || bytesPerRow % bpp != 0) throw new ArgumentException($"bytesPerRow ({bytesPerRow}) must be a multiple of the texel size ({bpp}).", nameof(bytesPerRow));

		var vk = device.Vk;
		var range = new ImageSubresourceRange(texture.Format.Aspect(), region.MipLevel, 1, 0, 1);
		// Earlier writes to the destination buffer (a readback reused every frame) complete before this copy writes it.
		GlobalBarrier(vk, _cmd, PipelineStageFlags.AllCommandsBit, AccessFlags.MemoryWriteBit, PipelineStageFlags.TransferBit, AccessFlags.TransferReadBit | AccessFlags.TransferWriteBit);
		var previous = texture.Layout;
		Transition(vk, _cmd, texture.Image, previous, ImageLayout.TransferSrcOptimal, range);
		var copy = new BufferImageCopy
		{
			BufferOffset = destinationOffset,
			BufferRowLength = bytesPerRow / bpp,
			BufferImageHeight = region.Height,
			ImageSubresource = new ImageSubresourceLayers(texture.Format.Aspect(), region.MipLevel, 0, 1),
			ImageOffset = new Offset3D((int)region.X, (int)region.Y, 0),
			ImageExtent = new Extent3D(region.Width, region.Height, 1),
		};
		vk.CmdCopyImageToBuffer(_cmd, texture.Image, ImageLayout.TransferSrcOptimal, ((VulkanBuffer)destination).Handle, 1, in copy);

		if (previous == ImageLayout.Undefined)
		{
			texture.Layout = ImageLayout.TransferSrcOptimal;
		}
		else
		{
			Transition(vk, _cmd, texture.Image, ImageLayout.TransferSrcOptimal, previous, range);
		}

		GlobalBarrier(vk, _cmd, PipelineStageFlags.TransferBit, AccessFlags.TransferWriteBit, PipelineStageFlags.HostBit | PipelineStageFlags.AllCommandsBit, AccessFlags.HostReadBit | AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit);
	}

	public ICommandBuffer Finish()
	{
		_ensureRecording();
		if (_pass.IsActive) throw new InvalidOperationException("End the render pass before finishing the encoder.");
		VulkanDevice.Check(device.Vk.EndCommandBuffer(_cmd), "vkEndCommandBuffer");
		_recording = false;
		return this;
	}

	private void _ensureRecording()
	{
		if (!_recording) throw new InvalidOperationException("The command encoder is finished; create a new one.");
	}

	/// <summary>A full image layout transition (conservative: all commands before, all commands after).</summary>
	public static void Transition(Vk vk, CommandBuffer cmd, VkImage image, ImageLayout from, ImageLayout to, ImageSubresourceRange range)
	{
		// Same-layout barriers are kept: they order writes (two uploads to one image) as well as transitions.
		var barrier = new ImageMemoryBarrier
		{
			SType = StructureType.ImageMemoryBarrier,
			OldLayout = from,
			NewLayout = to,
			SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
			DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
			Image = image,
			SubresourceRange = range,
			SrcAccessMask = AccessFlags.MemoryWriteBit,
			DstAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
		};
		vk.CmdPipelineBarrier(cmd, PipelineStageFlags.AllCommandsBit, PipelineStageFlags.AllCommandsBit, 0, 0, null, 0, null, 1, &barrier);
	}

	/// <summary>A global memory barrier.</summary>
	public static void GlobalBarrier(Vk vk, CommandBuffer cmd, PipelineStageFlags srcStage, AccessFlags srcAccess, PipelineStageFlags dstStage, AccessFlags dstAccess)
	{
		var barrier = new MemoryBarrier { SType = StructureType.MemoryBarrier, SrcAccessMask = srcAccess, DstAccessMask = dstAccess };
		vk.CmdPipelineBarrier(cmd, srcStage, dstStage, 0, 1, &barrier, 0, null, 0, null);
	}
}

/// <summary>
/// The Vulkan <see cref="IRenderPassEncoder"/>, one per command encoder. Bind groups are applied lazily at draw time with
/// the current pipeline's layout, so they can be set before or after the pipeline (as in WebGPU).
/// </summary>
internal sealed unsafe class VulkanRenderPassEncoder(VulkanDevice device) : IRenderPassEncoder
{
	private const int MaxGroups = 4;

	private readonly VulkanBindGroup?[] _groups = new VulkanBindGroup?[MaxGroups];
	private CommandBuffer _cmd;
	private PipelineLayout _layout;
	private uint _dirty;

	public bool IsActive { get; private set; }

	public void Begin(CommandBuffer cmd, uint width, uint height)
	{
		_cmd = cmd;
		_layout = default;
		_dirty = 0;
		Array.Clear(_groups);
		IsActive = true;
		SetViewport(0, 0, width, height);
		SetScissorRect(0, 0, width, height);
	}

	public void SetPipeline(IRenderPipeline pipeline)
	{
		var p = (VulkanRenderPipeline)pipeline;
		device.Vk.CmdBindPipeline(_cmd, PipelineBindPoint.Graphics, p.Handle);
		if (_layout.Handle != p.Layout.Handle.Handle)
		{
			_layout = p.Layout.Handle;
			_dirty = (1u << MaxGroups) - 1;
		}
	}

	public void SetBindGroup(uint index, IBindGroup group)
	{
		if (index >= MaxGroups) throw new ArgumentOutOfRangeException(nameof(index), index, $"At most {MaxGroups} bind groups.");
		_groups[index] = (VulkanBindGroup)group;
		_dirty |= 1u << (int)index;
	}

	public void SetVertexBuffer(uint slot, IBuffer buffer, ulong offset = 0)
	{
		var handle = ((VulkanBuffer)buffer).Handle;
		device.Vk.CmdBindVertexBuffers(_cmd, slot, 1, &handle, &offset);
	}

	public void SetIndexBuffer(IBuffer buffer, IndexFormat format, ulong offset = 0)
	{
		device.Vk.CmdBindIndexBuffer(_cmd, ((VulkanBuffer)buffer).Handle, offset, format.ToVk());
	}

	public void SetViewport(float x, float y, float width, float height, float minDepth = 0f, float maxDepth = 1f)
	{
		// Negative height: clip space y points up (WebGPU/GL convention) while the origin stays top-left.
		var viewport = new Viewport(x, y + height, width, -height, minDepth, maxDepth);
		device.Vk.CmdSetViewport(_cmd, 0, 1, in viewport);
	}

	public void SetScissorRect(uint x, uint y, uint width, uint height)
	{
		var scissor = new Rect2D(new Offset2D((int)x, (int)y), new Extent2D(width, height));
		device.Vk.CmdSetScissor(_cmd, 0, 1, in scissor);
	}

	public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
	{
		_flushBindGroups();
		device.Vk.CmdDraw(_cmd, vertexCount, instanceCount, firstVertex, firstInstance);
	}

	public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
	{
		_flushBindGroups();
		device.Vk.CmdDrawIndexed(_cmd, indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
	}

	public void End()
	{
		if (!IsActive) throw new InvalidOperationException("The render pass has already ended.");
		device.Vk.CmdEndRenderPass(_cmd);
		IsActive = false;
	}

	private void _flushBindGroups()
	{
		if (_dirty == 0) return;
		if (_layout.Handle == 0) throw new InvalidOperationException("Set a pipeline before drawing.");
		for (var i = 0; i < MaxGroups; i++)
		{
			if ((_dirty & (1u << i)) == 0 || _groups[i] is not { } group) continue;
			var set = group.Handle;
			device.Vk.CmdBindDescriptorSets(_cmd, PipelineBindPoint.Graphics, _layout, (uint)i, 1, &set, 0, null);
		}

		_dirty = 0;
	}
}

internal readonly record struct AttachmentKey(Format Format, AttachmentLoadOp Load, AttachmentStoreOp Store, ImageLayout Initial, ImageLayout Final);

/// <summary>
/// Everything that distinguishes one <c>VkRenderPass</c> from another in this backend. Render passes are cached by key and
/// live as long as the device.
/// </summary>
internal unsafe struct RenderPassKey : IEquatable<RenderPassKey>
{
	public const int MaxColorAttachments = 4;

	public int ColorCount;
	public AttachmentKey Color0;
	public AttachmentKey Color1;
	public AttachmentKey Color2;
	public AttachmentKey Color3;
	public bool HasDepth;
	public AttachmentKey Depth;
	public SampleCountFlags Samples;

	public AttachmentKey this[int index]
	{
		readonly get => index switch { 0 => Color0, 1 => Color1, 2 => Color2, _ => Color3 };
		set
		{
			switch (index)
			{
				case 0: Color0 = value; break;
				case 1: Color1 = value; break;
				case 2: Color2 = value; break;
				default: Color3 = value; break;
			}
		}
	}

	/// <summary>A key for pipeline creation: only formats and sample count matter for render pass compatibility.</summary>
	public static RenderPassKey Compatible(VulkanDevice device, ColorTargetState[] targets, TextureFormat depth, uint samples)
	{
		if (targets.Length > MaxColorAttachments) throw new ArgumentException($"At most {MaxColorAttachments} color targets.");
		var key = new RenderPassKey { ColorCount = targets.Length, Samples = VulkanFormats.ToVkSamples(samples) };
		for (var i = 0; i < targets.Length; i++)
		{
			key[i] = new AttachmentKey(targets[i].Format.ToVk(), AttachmentLoadOp.Clear, AttachmentStoreOp.Store, ImageLayout.Undefined, ImageLayout.ColorAttachmentOptimal);
		}

		if (depth != TextureFormat.Undefined)
		{
			key.HasDepth = true;
			key.Depth = new AttachmentKey(device.PickDepthFormat(depth), AttachmentLoadOp.Clear, AttachmentStoreOp.DontCare, ImageLayout.Undefined, ImageLayout.DepthStencilAttachmentOptimal);
		}

		return key;
	}

	public readonly RenderPass Create(VulkanDevice device)
	{
		var attachments = stackalloc AttachmentDescription[MaxColorAttachments + 1];
		var colorRefs = stackalloc AttachmentReference[MaxColorAttachments];
		for (var i = 0; i < ColorCount; i++)
		{
			var a = this[i];
			attachments[i] = new AttachmentDescription
			{
				Format = a.Format,
				Samples = Samples,
				LoadOp = a.Load,
				StoreOp = a.Store,
				StencilLoadOp = AttachmentLoadOp.DontCare,
				StencilStoreOp = AttachmentStoreOp.DontCare,
				InitialLayout = a.Initial,
				FinalLayout = a.Final,
			};
			colorRefs[i] = new AttachmentReference((uint)i, ImageLayout.ColorAttachmentOptimal);
		}

		var count = ColorCount;
		var depthRef = new AttachmentReference((uint)count, ImageLayout.DepthStencilAttachmentOptimal);
		if (HasDepth)
		{
			attachments[count++] = new AttachmentDescription
			{
				Format = Depth.Format,
				Samples = Samples,
				LoadOp = Depth.Load,
				StoreOp = Depth.Store,
				StencilLoadOp = Depth.Load,
				StencilStoreOp = Depth.Store,
				InitialLayout = Depth.Initial,
				FinalLayout = Depth.Final,
			};
		}

		var subpass = new SubpassDescription
		{
			PipelineBindPoint = PipelineBindPoint.Graphics,
			ColorAttachmentCount = (uint)ColorCount,
			PColorAttachments = colorRefs,
			PDepthStencilAttachment = HasDepth ? &depthRef : null,
		};

		// Conservative external dependencies: everything before the pass completes before it, and the pass completes
		// before anything after it (including the layout transitions to the final layouts).
		var dependencies = stackalloc SubpassDependency[2];
		dependencies[0] = new SubpassDependency
		{
			SrcSubpass = Vk.SubpassExternal,
			DstSubpass = 0,
			SrcStageMask = PipelineStageFlags.AllCommandsBit,
			DstStageMask = PipelineStageFlags.AllGraphicsBit,
			SrcAccessMask = AccessFlags.MemoryWriteBit,
			DstAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
		};
		dependencies[1] = new SubpassDependency
		{
			SrcSubpass = 0,
			DstSubpass = Vk.SubpassExternal,
			SrcStageMask = PipelineStageFlags.AllGraphicsBit,
			DstStageMask = PipelineStageFlags.AllCommandsBit,
			SrcAccessMask = AccessFlags.MemoryWriteBit,
			DstAccessMask = AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit,
		};

		var info = new RenderPassCreateInfo
		{
			SType = StructureType.RenderPassCreateInfo,
			AttachmentCount = (uint)count,
			PAttachments = attachments,
			SubpassCount = 1,
			PSubpasses = &subpass,
			DependencyCount = 2,
			PDependencies = dependencies,
		};
		VulkanDevice.Check(device.Vk.CreateRenderPass(device.Handle, in info, null, out var pass), "vkCreateRenderPass");
		return pass;
	}

	public readonly bool Equals(RenderPassKey other) =>
		ColorCount == other.ColorCount && Color0 == other.Color0 && Color1 == other.Color1 && Color2 == other.Color2 && Color3 == other.Color3
		&& HasDepth == other.HasDepth && Depth == other.Depth && Samples == other.Samples;

	public override readonly bool Equals(object? obj) => obj is RenderPassKey other && Equals(other);

	public override readonly int GetHashCode() => HashCode.Combine(ColorCount, Color0, Color1, Color2, Color3, HasDepth, Depth, Samples);
}
