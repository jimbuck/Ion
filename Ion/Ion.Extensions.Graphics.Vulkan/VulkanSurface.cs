using Silk.NET.Vulkan;

using Ion.Extensions.Graphics.Rhi;

using VkImage = Silk.NET.Vulkan.Image;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Ion.Extensions.Graphics.Vulkan;

/// <summary>
/// The Vulkan <see cref="ISurface"/>: a <c>VkSurfaceKHR</c> and its swapchain. <see cref="Configure"/> (re)creates the
/// swapchain (after waiting for the device to be idle; resizes are rare). One acquire semaphore per frame slot and one
/// render-finished semaphore per swapchain image.
/// </summary>
internal sealed unsafe class VulkanSurface : ISurface
{
	private readonly VulkanDevice _device;
	private readonly SurfaceKHR _surface;
	private SwapchainKHR _swapchain;
	private VulkanTexture[] _images = [];
	private VkSemaphore[] _renderFinished = [];
	private readonly VkSemaphore[] _acquire;
	private uint _imageIndex;
	private bool _acquired;

	public VulkanSurface(VulkanDevice device, SurfaceKHR surface)
	{
		_device = device;
		_surface = surface;
		_acquire = new VkSemaphore[device.FramesInFlight];
		for (var i = 0; i < _acquire.Length; i++) _acquire[i] = device.CreateSemaphore();
	}

	public TextureFormat Format { get; private set; } = TextureFormat.Bgra8Unorm;

	public uint Width { get; private set; }

	public uint Height { get; private set; }

	public PresentMode PresentMode { get; private set; }

	/// <summary>True when the last acquire or present reported the swapchain out of date or suboptimal.</summary>
	public bool NeedsReconfigure { get; private set; }

	/// <summary>The texture acquired this frame (valid between <see cref="GetCurrentTexture"/> and <see cref="Present"/>).</summary>
	public VulkanTexture? Current => _acquired ? _images[_imageIndex] : null;

	public void Configure(in SurfaceConfiguration configuration)
	{
		var vk = _device.Vk;
		var khr = _device.KhrSurface!;
		vk.DeviceWaitIdle(_device.Handle);

		VulkanDevice.Check(khr.GetPhysicalDeviceSurfaceCapabilities(_device.Physical, _surface, out var caps), "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");

		// currentExtent is the window's framebuffer size, or 0xFFFFFFFF when the swapchain decides (then use the request).
		var width = caps.CurrentExtent.Width != uint.MaxValue ? caps.CurrentExtent.Width : Math.Clamp(configuration.Width, caps.MinImageExtent.Width, caps.MaxImageExtent.Width);
		var height = caps.CurrentExtent.Height != uint.MaxValue ? caps.CurrentExtent.Height : Math.Clamp(configuration.Height, caps.MinImageExtent.Height, caps.MaxImageExtent.Height);

		_destroySwapchainImages();
		NeedsReconfigure = false;
		if (width == 0 || height == 0)
		{
			// Minimized: keep the old swapchain handle for the next recreation; nothing can be acquired meanwhile.
			Width = Height = 0;
			return;
		}

		var (format, colorSpace) = _chooseFormat(configuration.Format);
		var presentMode = _choosePresentMode(configuration.PresentMode);

		var imageCount = Math.Max(caps.MinImageCount, (uint)_device.FramesInFlight + 1);
		if (caps.MaxImageCount > 0) imageCount = Math.Min(imageCount, caps.MaxImageCount);

		var usage = ImageUsageFlags.ColorAttachmentBit;
		if ((caps.SupportedUsageFlags & ImageUsageFlags.TransferSrcBit) != 0) usage |= ImageUsageFlags.TransferSrcBit;
		if ((caps.SupportedUsageFlags & ImageUsageFlags.TransferDstBit) != 0) usage |= ImageUsageFlags.TransferDstBit;

		var old = _swapchain;
		var info = new SwapchainCreateInfoKHR
		{
			SType = StructureType.SwapchainCreateInfoKhr,
			Surface = _surface,
			MinImageCount = imageCount,
			ImageFormat = format,
			ImageColorSpace = colorSpace,
			ImageExtent = new Extent2D(width, height),
			ImageArrayLayers = 1,
			ImageUsage = usage,
			ImageSharingMode = SharingMode.Exclusive,
			PreTransform = caps.CurrentTransform,
			CompositeAlpha = _chooseCompositeAlpha(caps.SupportedCompositeAlpha),
			PresentMode = presentMode,
			Clipped = true,
			OldSwapchain = old,
		};
		var swapchainApi = _device.KhrSwapchain!;
		VulkanDevice.Check(swapchainApi.CreateSwapchain(_device.Handle, in info, null, out _swapchain), "vkCreateSwapchainKHR");
		if (old.Handle != 0) swapchainApi.DestroySwapchain(_device.Handle, old, null);

		uint count = 0;
		swapchainApi.GetSwapchainImages(_device.Handle, _swapchain, ref count, null);
		var images = new VkImage[count];
		fixed (VkImage* p = images) swapchainApi.GetSwapchainImages(_device.Handle, _swapchain, ref count, p);

		_images = new VulkanTexture[count];
		_renderFinished = new VkSemaphore[count];
		for (var i = 0; i < count; i++)
		{
			_images[i] = new VulkanTexture(_device, images[i], format, width, height);
			_renderFinished[i] = _device.CreateSemaphore();
		}

		Width = width;
		Height = height;
		Format = VulkanFormats.FromVk(format);
		PresentMode = presentMode switch
		{
			PresentModeKHR.MailboxKhr => PresentMode.Mailbox,
			PresentModeKHR.ImmediateKhr => PresentMode.Immediate,
			_ => PresentMode.Fifo,
		};
	}

	public SurfaceTexture GetCurrentTexture()
	{
		if (_acquired) return new SurfaceTexture(_images[_imageIndex], SurfaceTextureStatus.Success);
		if (_swapchain.Handle == 0 || Width == 0 || Height == 0) return new SurfaceTexture(null, SurfaceTextureStatus.Outdated);

		var semaphore = _acquire[_device.FrameIndex];
		var result = _device.KhrSwapchain!.AcquireNextImage(_device.Handle, _swapchain, ulong.MaxValue, semaphore, default, ref _imageIndex);
		switch (result)
		{
			case Result.Success:
			case Result.SuboptimalKhr:
				_acquired = true;
				_device.QueueImpl.WaitOn(semaphore);
				var texture = _images[_imageIndex];
				// The presentation engine hands the image back in PRESENT_SRC (or UNDEFINED the first time); either way its
				// previous contents are not needed.
				texture.Layout = ImageLayout.Undefined;
				if (result == Result.SuboptimalKhr) NeedsReconfigure = true;
				return new SurfaceTexture(texture, result == Result.Success ? SurfaceTextureStatus.Success : SurfaceTextureStatus.Suboptimal);
			case Result.ErrorOutOfDateKhr:
				NeedsReconfigure = true;
				return new SurfaceTexture(null, SurfaceTextureStatus.Outdated);
			default:
				VulkanDevice.Check(result, "vkAcquireNextImageKHR");
				return new SurfaceTexture(null, SurfaceTextureStatus.Outdated);
		}
	}

	public void Present()
	{
		if (!_acquired) return;
		_acquired = false;

		var signal = _renderFinished[_imageIndex];
		_device.QueueImpl.Flush(signal);

		var swapchain = _swapchain;
		var index = _imageIndex;
		var info = new PresentInfoKHR
		{
			SType = StructureType.PresentInfoKhr,
			WaitSemaphoreCount = 1,
			PWaitSemaphores = &signal,
			SwapchainCount = 1,
			PSwapchains = &swapchain,
			PImageIndices = &index,
		};
		var result = _device.KhrSwapchain!.QueuePresent(_device.VkQueue, in info);
		if (result is Result.ErrorOutOfDateKhr or Result.SuboptimalKhr) NeedsReconfigure = true;
		else VulkanDevice.Check(result, "vkQueuePresentKHR");
	}

	public void Destroy()
	{
		_destroySwapchainImages();
		if (_swapchain.Handle != 0) _device.KhrSwapchain!.DestroySwapchain(_device.Handle, _swapchain, null);
		_swapchain = default;
		foreach (var semaphore in _acquire) _device.Vk.DestroySemaphore(_device.Handle, semaphore, null);
	}

	public void DestroySurface() => _device.KhrSurface!.DestroySurface(_device.Instance, _surface, null);

	private void _destroySwapchainImages()
	{
		foreach (var image in _images) image.DestroyNow();
		foreach (var semaphore in _renderFinished) _device.Vk.DestroySemaphore(_device.Handle, semaphore, null);
		_images = [];
		_renderFinished = [];
		_acquired = false;
	}

	private (Format Format, ColorSpaceKHR ColorSpace) _chooseFormat(TextureFormat requested)
	{
		var khr = _device.KhrSurface!;
		uint count = 0;
		khr.GetPhysicalDeviceSurfaceFormats(_device.Physical, _surface, ref count, null);
		var formats = new SurfaceFormatKHR[count];
		fixed (SurfaceFormatKHR* p = formats) khr.GetPhysicalDeviceSurfaceFormats(_device.Physical, _surface, ref count, p);

		// Non-sRGB 8-bit formats first: colors are written as given (no implicit sRGB encoding).
		ReadOnlySpan<Format> preferred = requested != TextureFormat.Undefined
			? [requested.ToVk(), Silk.NET.Vulkan.Format.B8G8R8A8Unorm, Silk.NET.Vulkan.Format.R8G8B8A8Unorm]
			: [Silk.NET.Vulkan.Format.B8G8R8A8Unorm, Silk.NET.Vulkan.Format.R8G8B8A8Unorm];
		foreach (var want in preferred)
		{
			foreach (var f in formats)
			{
				if (f.Format == want && VulkanFormats.FromVk(f.Format) != TextureFormat.Undefined) return (f.Format, f.ColorSpace);
			}
		}

		foreach (var f in formats)
		{
			if (VulkanFormats.FromVk(f.Format) != TextureFormat.Undefined) return (f.Format, f.ColorSpace);
		}

		throw new InvalidOperationException("The window surface offers no supported color format.");
	}

	private PresentModeKHR _choosePresentMode(PresentMode requested)
	{
		var khr = _device.KhrSurface!;
		uint count = 0;
		khr.GetPhysicalDeviceSurfacePresentModes(_device.Physical, _surface, ref count, null);
		var modes = new PresentModeKHR[count];
		fixed (PresentModeKHR* p = modes) khr.GetPhysicalDeviceSurfacePresentModes(_device.Physical, _surface, ref count, p);

		var want = requested.ToVk();
		if (Array.IndexOf(modes, want) >= 0) return want;
		// No vsync requested but mailbox missing: immediate, if any; FIFO is always there.
		if (requested == PresentMode.Mailbox && Array.IndexOf(modes, PresentModeKHR.ImmediateKhr) >= 0) return PresentModeKHR.ImmediateKhr;
		return PresentModeKHR.FifoKhr;
	}

	private static CompositeAlphaFlagsKHR _chooseCompositeAlpha(CompositeAlphaFlagsKHR supported)
	{
		foreach (var flag in (ReadOnlySpan<CompositeAlphaFlagsKHR>)[CompositeAlphaFlagsKHR.OpaqueBitKhr, CompositeAlphaFlagsKHR.InheritBitKhr, CompositeAlphaFlagsKHR.PreMultipliedBitKhr, CompositeAlphaFlagsKHR.PostMultipliedBitKhr])
		{
			if ((supported & flag) != 0) return flag;
		}

		return CompositeAlphaFlagsKHR.OpaqueBitKhr;
	}
}
