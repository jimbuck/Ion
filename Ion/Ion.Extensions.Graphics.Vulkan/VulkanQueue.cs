using System.Runtime.InteropServices;

using Silk.NET.Vulkan;

using Ion.Extensions.Graphics.Rhi;

using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Ion.Extensions.Graphics.Vulkan;

/// <summary>
/// The Vulkan <see cref="IQueue"/>. Submissions are batched: <see cref="Submit(ICommandBuffer)"/> appends to a pending list
/// in program order (with the upload command buffer of any writes made before it), and <see cref="Flush"/> submits the
/// batch with one fence of the current frame slot. The batch is flushed at present, at the end of a frame, and before any
/// blocking read.
/// </summary>
internal sealed unsafe class VulkanQueue(VulkanDevice device) : IQueue
{
	private readonly List<CommandBuffer> _pending = [];
	private readonly HashSet<ulong> _uploadTargets = [];
	private CommandBuffer _upload;
	private VkSemaphore _wait;

	/// <summary>Makes the next flush wait on <paramref name="semaphore"/> (the swapchain image acquire).</summary>
	public void WaitOn(VkSemaphore semaphore) => _wait = semaphore;

	public void Submit(ICommandBuffer commandBuffer)
	{
		_closeUpload();
		_pending.Add(((VulkanCommandEncoder)commandBuffer).Finished());
	}

	public void Submit(ReadOnlySpan<ICommandBuffer> commandBuffers)
	{
		foreach (var commandBuffer in commandBuffers) Submit(commandBuffer);
	}

	public void WriteBuffer(IBuffer buffer, ulong offset, ReadOnlySpan<byte> data)
	{
		if (data.IsEmpty) return;
		var target = (VulkanBuffer)buffer;
		if (offset + (ulong)data.Length > target.Size) throw new ArgumentOutOfRangeException(nameof(data), $"Writing {data.Length} bytes at {offset} overflows a {target.Size}-byte buffer.");

		var staging = device.CurrentFrame.AllocateStaging((ulong)data.Length, 16);
		data.CopyTo(new Span<byte>(staging.Pointer, data.Length));

		var cmd = _ensureUpload();

		// Copies within one upload command buffer are unordered: a second write to the same buffer waits for the first.
		if (!_uploadTargets.Add(target.Handle.Handle))
		{
			VulkanCommandEncoder.GlobalBarrier(device.Vk, cmd, PipelineStageFlags.TransferBit, AccessFlags.TransferWriteBit, PipelineStageFlags.TransferBit, AccessFlags.TransferWriteBit);
			_uploadTargets.Clear();
			_uploadTargets.Add(target.Handle.Handle);
		}

		var region = new BufferCopy(staging.Offset, offset, (ulong)data.Length);
		device.Vk.CmdCopyBuffer(cmd, staging.Buffer, target.Handle, 1, in region);
	}

	public void WriteTexture(ITexture texture, ReadOnlySpan<byte> data, uint bytesPerRow, TextureRegion region)
	{
		var target = (VulkanTexture)texture;
		var bpp = (uint)target.Format.BytesPerPixel();
		if (bytesPerRow % bpp != 0) throw new ArgumentException($"bytesPerRow ({bytesPerRow}) must be a multiple of the texel size ({bpp}).", nameof(bytesPerRow));
		var size = (ulong)bytesPerRow * region.Height;
		if ((ulong)data.Length < size) throw new ArgumentException($"Expected at least {size} bytes, got {data.Length}.", nameof(data));

		var staging = device.CurrentFrame.AllocateStaging(size, Math.Max(16u, bpp));
		data[..(int)size].CopyTo(new Span<byte>(staging.Pointer, (int)size));

		var cmd = _ensureUpload();
		var range = new ImageSubresourceRange(target.Format.Aspect(), region.MipLevel, 1, 0, 1);
		var final = (target.Usage & TextureUsage.TextureBinding) != 0 ? ImageLayout.ShaderReadOnlyOptimal : ImageLayout.TransferDstOptimal;

		// Layouts are tracked per image: before the first write to a mipmapped image, bring every level to the final
		// layout, so a write to one level leaves the others in the tracked layout too.
		if (target.Layout == ImageLayout.Undefined && target.MipLevelCount > 1)
		{
			var all = new ImageSubresourceRange(target.Format.Aspect(), 0, target.MipLevelCount, 0, 1);
			VulkanCommandEncoder.Transition(device.Vk, cmd, target.Image, ImageLayout.Undefined, final, all);
			target.Layout = final;
		}

		VulkanCommandEncoder.Transition(device.Vk, cmd, target.Image, target.Layout, ImageLayout.TransferDstOptimal, range);
		var copy = new BufferImageCopy
		{
			BufferOffset = staging.Offset,
			BufferRowLength = bytesPerRow / bpp,
			BufferImageHeight = region.Height,
			ImageSubresource = new ImageSubresourceLayers(target.Format.Aspect(), region.MipLevel, 0, 1),
			ImageOffset = new Offset3D((int)region.X, (int)region.Y, 0),
			ImageExtent = new Extent3D(region.Width, region.Height, 1),
		};
		device.Vk.CmdCopyBufferToImage(cmd, staging.Buffer, target.Image, ImageLayout.TransferDstOptimal, 1, in copy);
		VulkanCommandEncoder.Transition(device.Vk, cmd, target.Image, ImageLayout.TransferDstOptimal, final, range);
		target.Layout = final;
	}

	public void WaitIdle()
	{
		Flush();
		VulkanDevice.Check(device.Vk.QueueWaitIdle(device.VkQueue), "vkQueueWaitIdle");
	}

	/// <summary>
	/// Submits the pending command buffers (waiting on the acquire semaphore if one is set, signalling
	/// <paramref name="signal"/> if given) with a fence of the current frame slot.
	/// </summary>
	public void Flush(VkSemaphore signal = default)
	{
		_closeUpload();
		if (_pending.Count == 0 && signal.Handle == 0 && _wait.Handle == 0) return;

		var fence = device.RentFence();
		var cmds = CollectionsMarshal.AsSpan(_pending);
		var wait = _wait;
		var waitStage = PipelineStageFlags.AllCommandsBit;
		fixed (CommandBuffer* pCmds = cmds)
		{
			var submit = new SubmitInfo
			{
				SType = StructureType.SubmitInfo,
				CommandBufferCount = (uint)cmds.Length,
				PCommandBuffers = pCmds,
				WaitSemaphoreCount = wait.Handle != 0 ? 1u : 0u,
				PWaitSemaphores = &wait,
				PWaitDstStageMask = &waitStage,
				SignalSemaphoreCount = signal.Handle != 0 ? 1u : 0u,
				PSignalSemaphores = &signal,
			};
			VulkanDevice.Check(device.Vk.QueueSubmit(device.VkQueue, 1, in submit, fence), "vkQueueSubmit");
		}

		device.CurrentFrame.Fences.Add(fence);
		_pending.Clear();
		_wait = default;
	}

	private CommandBuffer _ensureUpload()
	{
		if (_upload.Handle != 0) return _upload;
		_upload = device.CurrentFrame.BeginCommandBuffer();
		// Earlier GPU work that reads or writes the destinations completes before the copies.
		VulkanCommandEncoder.GlobalBarrier(device.Vk, _upload, PipelineStageFlags.AllCommandsBit, AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit, PipelineStageFlags.TransferBit, AccessFlags.TransferWriteBit);
		return _upload;
	}

	private void _closeUpload()
	{
		if (_upload.Handle == 0) return;
		// The copies are visible to everything submitted after them.
		VulkanCommandEncoder.GlobalBarrier(device.Vk, _upload, PipelineStageFlags.TransferBit, AccessFlags.TransferWriteBit, PipelineStageFlags.AllCommandsBit, AccessFlags.MemoryReadBit | AccessFlags.MemoryWriteBit);
		VulkanDevice.Check(device.Vk.EndCommandBuffer(_upload), "vkEndCommandBuffer");
		_pending.Add(_upload);
		_upload = default;
		_uploadTargets.Clear();
	}
}
