// Stage 4 spike: Silk.NET.Windowing driven by hand (never IWindow.Run), a Vulkan instance, surface, device and swapchain
// on Silk.NET.Vulkan, a clear to a known color on the swapchain image read back to the CPU, and Shaderc/SPIRV-Cross.
// Usage: dotnet run -- [glfw|sdl|headless]   (under xvfb-run for the windowed modes)
using System.Diagnostics;
using System.Runtime.InteropServices;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.Maths;
using Silk.NET.Shaderc;
using Result = Silk.NET.Vulkan.Result;
using SCompiler = Silk.NET.Shaderc.Compiler;
using Cross = Silk.NET.SPIRV.Cross.Cross;
using XContext = Silk.NET.SPIRV.Cross.Context;
using XParsedIr = Silk.NET.SPIRV.Cross.ParsedIr;
using XCompiler = Silk.NET.SPIRV.Cross.Compiler;
using XCompilerOptions = Silk.NET.SPIRV.Cross.CompilerOptions;
using XBackend = Silk.NET.SPIRV.Cross.Backend;
using XCaptureMode = Silk.NET.SPIRV.Cross.CaptureMode;
using XCompilerOption = Silk.NET.SPIRV.Cross.CompilerOption;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;
using Silk.NET.Windowing.Sdl;
using Silk.NET.Input;
using Silk.NET.Input.Glfw;
using Silk.NET.Input.Sdl;
using VkImage = Silk.NET.Vulkan.Image;
using VkBuffer = Silk.NET.Vulkan.Buffer;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

var start = Stopwatch.StartNew();
var mode = args.Length > 0 ? args[0] : "glfw";
Console.WriteLine($"mode={mode}");

unsafe
{
	// 1. Shaders: GLSL 4.5 to SPIR-V with Shaderc, SPIR-V to GLSL ES 3.10 with SPIRV-Cross.
	var spirv = CompileGlsl("""
		#version 450
		layout(location = 0) in vec2 inPos;
		layout(location = 1) in vec2 inUv;
		layout(location = 0) out vec2 vUv;
		void main() { vUv = inUv; gl_Position = vec4(inPos, 0.0, 1.0); }
		""", ShaderKind.VertexShader);
	Console.WriteLine($"shaderc: {spirv.Length} bytes of SPIR-V ({start.ElapsedMilliseconds} ms)");
	var gles = CrossToGles(spirv);
	Console.WriteLine($"spirv-cross GLSL ES:\n{gles}");

	// 2. Window, created and pumped by hand.
	IWindow? window = null;
	IInputContext? input = null;
	if (mode != "headless")
	{
		// Explicit registration, no reflection-based discovery.
		Window.ShouldLoadFirstPartyPlatforms(false);
		InputWindowExtensions.ShouldLoadFirstPartyPlatforms(false);
		if (mode == "sdl")
		{
			SdlWindowing.RegisterPlatform();
			SdlInput.RegisterPlatform();
		}
		else
		{
			GlfwWindowing.RegisterPlatform();
			GlfwInput.RegisterPlatform();
		}

		var options = WindowOptions.DefaultVulkan with { Size = new Vector2D<int>(320, 240), Title = "Ion spike", IsVisible = true };
		window = Window.Create(options);
		window.Initialize();
		input = window.CreateInput();
		Console.WriteLine($"window: size={window.Size} framebuffer={window.FramebufferSize} native={window.Native?.Kind} vk={(window.VkSurface is not null)} ({start.ElapsedMilliseconds} ms)");
		Console.WriteLine($"input: keyboards={input.Keyboards.Count} mice={input.Mice.Count} gamepads={input.Gamepads.Count}");
		for (var i = 0; i < 3; i++) window.DoEvents();
		if (args.Contains("--reflect"))
		{
			for (var t = window.GetType(); t is not null; t = t.BaseType)
			{
				foreach (var f in t.GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
				{
					if (f.GetValue(window) is Delegate d) Console.WriteLine($"  {t.Name}.{f.Name}: {string.Join(", ", d.GetInvocationList().Select(x => x.Method.DeclaringType?.Name + "." + x.Method.Name))}");
				}
			}
		}
	}

	// 3. Vulkan instance.
	var vk = Vk.GetApi();
	var extensions = new List<string>();
	if (window?.VkSurface is { } vkSurface)
	{
		var required = vkSurface.GetRequiredExtensions(out var count);
		for (var i = 0; i < count; i++) extensions.Add(SilkMarshal.PtrToString((nint)required[i])!);
	}
	Console.WriteLine($"instance extensions: {string.Join(", ", extensions)}");

	var appName = (byte*)SilkMarshal.StringToPtr("Ion spike");
	var appInfo = new ApplicationInfo { SType = StructureType.ApplicationInfo, PApplicationName = appName, ApiVersion = Vk.Version12 };
	var extPtrs = (byte**)SilkMarshal.StringArrayToPtr(extensions);
	var instanceInfo = new InstanceCreateInfo
	{
		SType = StructureType.InstanceCreateInfo,
		PApplicationInfo = &appInfo,
		EnabledExtensionCount = (uint)extensions.Count,
		PpEnabledExtensionNames = extPtrs,
	};
	Check(vk.CreateInstance(in instanceInfo, null, out var instance), "vkCreateInstance");

	SurfaceKHR surface = default;
	KhrSurface? khrSurface = null;
	if (window?.VkSurface is { } source)
	{
		surface = source.Create<AllocationCallbacks>(instance.ToHandle(), null).ToSurface();
		vk.TryGetInstanceExtension(instance, out khrSurface);
	}

	// 4. Physical device, queue family, device.
	uint deviceCount = 0;
	vk.EnumeratePhysicalDevices(instance, ref deviceCount, null);
	var devices = new PhysicalDevice[deviceCount];
	fixed (PhysicalDevice* p = devices) vk.EnumeratePhysicalDevices(instance, ref deviceCount, p);
	var physical = devices[0];
	vk.GetPhysicalDeviceProperties(physical, out var props);
	Console.WriteLine($"device: {SilkMarshal.PtrToString((nint)props.DeviceName)} api {props.ApiVersion >> 22}.{(props.ApiVersion >> 12) & 0x3ff}");

	uint familyCount = 0;
	vk.GetPhysicalDeviceQueueFamilyProperties(physical, ref familyCount, null);
	var families = new QueueFamilyProperties[familyCount];
	fixed (QueueFamilyProperties* p = families) vk.GetPhysicalDeviceQueueFamilyProperties(physical, ref familyCount, p);
	uint family = 0;
	for (uint i = 0; i < familyCount; i++)
	{
		if ((families[i].QueueFlags & QueueFlags.GraphicsBit) == 0) continue;
		if (khrSurface is not null)
		{
			khrSurface.GetPhysicalDeviceSurfaceSupport(physical, i, surface, out var supported);
			if (!supported) continue;
		}
		family = i;
		break;
	}

	var priority = 1f;
	var queueInfo = new DeviceQueueCreateInfo { SType = StructureType.DeviceQueueCreateInfo, QueueFamilyIndex = family, QueueCount = 1, PQueuePriorities = &priority };
	var deviceExtensions = khrSurface is not null ? new[] { KhrSwapchain.ExtensionName } : Array.Empty<string>();
	var devExtPtrs = (byte**)SilkMarshal.StringArrayToPtr(deviceExtensions);
	var deviceInfo = new DeviceCreateInfo
	{
		SType = StructureType.DeviceCreateInfo,
		QueueCreateInfoCount = 1,
		PQueueCreateInfos = &queueInfo,
		EnabledExtensionCount = (uint)deviceExtensions.Length,
		PpEnabledExtensionNames = devExtPtrs,
	};
	Check(vk.CreateDevice(physical, in deviceInfo, null, out var device), "vkCreateDevice");
	vk.GetDeviceQueue(device, family, 0, out var queue);

	// 5. The target image: a swapchain image when windowed, an offscreen image otherwise.
	uint width = 320, height = 240;
	var format = Format.B8G8R8A8Unorm;
	VkImage target;
	SwapchainKHR swapchain = default;
	KhrSwapchain? khrSwapchain = null;
	uint imageIndex = 0;
	VkSemaphore acquired = default;
	if (khrSurface is not null)
	{
		khrSurface.GetPhysicalDeviceSurfaceCapabilities(physical, surface, out var caps);
		width = caps.CurrentExtent.Width == uint.MaxValue ? (uint)window!.FramebufferSize.X : caps.CurrentExtent.Width;
		height = caps.CurrentExtent.Height == uint.MaxValue ? (uint)window!.FramebufferSize.Y : caps.CurrentExtent.Height;
		uint formatCount = 0;
		khrSurface.GetPhysicalDeviceSurfaceFormats(physical, surface, ref formatCount, null);
		var formats = new SurfaceFormatKHR[formatCount];
		fixed (SurfaceFormatKHR* p = formats) khrSurface.GetPhysicalDeviceSurfaceFormats(physical, surface, ref formatCount, p);
		format = formats[0].Format;
		Console.WriteLine($"surface: extent {width}x{height}, formats {string.Join(",", formats.Select(f => f.Format))}, usage {caps.SupportedUsageFlags}");

		vk.TryGetDeviceExtension(instance, device, out khrSwapchain);
		var swapInfo = new SwapchainCreateInfoKHR
		{
			SType = StructureType.SwapchainCreateInfoKhr,
			Surface = surface,
			MinImageCount = Math.Max(caps.MinImageCount, 2),
			ImageFormat = format,
			ImageColorSpace = formats[0].ColorSpace,
			ImageExtent = new Extent2D(width, height),
			ImageArrayLayers = 1,
			ImageUsage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit,
			ImageSharingMode = SharingMode.Exclusive,
			PreTransform = caps.CurrentTransform,
			CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
			PresentMode = PresentModeKHR.FifoKhr,
			Clipped = true,
		};
		Check(khrSwapchain!.CreateSwapchain(device, in swapInfo, null, out swapchain), "vkCreateSwapchainKHR");
		uint imageCount = 0;
		khrSwapchain.GetSwapchainImages(device, swapchain, ref imageCount, null);
		var images = new VkImage[imageCount];
		fixed (VkImage* p = images) khrSwapchain.GetSwapchainImages(device, swapchain, ref imageCount, p);
		var semInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
		vk.CreateSemaphore(device, in semInfo, null, out acquired);
		Check(khrSwapchain.AcquireNextImage(device, swapchain, ulong.MaxValue, acquired, default, ref imageIndex), "vkAcquireNextImageKHR");
		target = images[imageIndex];
		Console.WriteLine($"swapchain: {imageCount} images, acquired {imageIndex}");
	}
	else
	{
		var imageInfo = new ImageCreateInfo
		{
			SType = StructureType.ImageCreateInfo,
			ImageType = ImageType.Type2D,
			Format = format,
			Extent = new Extent3D(width, height, 1),
			MipLevels = 1,
			ArrayLayers = 1,
			Samples = SampleCountFlags.Count1Bit,
			Tiling = ImageTiling.Optimal,
			Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit | ImageUsageFlags.TransferDstBit,
		};
		Check(vk.CreateImage(device, in imageInfo, null, out target), "vkCreateImage");
		vk.GetImageMemoryRequirements(device, target, out var req);
		var mem = Allocate(vk, physical, device, req, MemoryPropertyFlags.DeviceLocalBit);
		vk.BindImageMemory(device, target, mem, 0);
	}

	// 6. Readback buffer.
	var size = (ulong)(width * height * 4);
	var bufferInfo = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = size, Usage = BufferUsageFlags.TransferDstBit };
	vk.CreateBuffer(device, in bufferInfo, null, out var readback);
	vk.GetBufferMemoryRequirements(device, readback, out var breq);
	var bmem = Allocate(vk, physical, device, breq, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
	vk.BindBufferMemory(device, readback, bmem, 0);

	// 7. Record: clear to (255, 128, 0), copy to the buffer, transition to present.
	var poolInfo = new CommandPoolCreateInfo { SType = StructureType.CommandPoolCreateInfo, QueueFamilyIndex = family };
	vk.CreateCommandPool(device, in poolInfo, null, out var pool);
	var allocInfo = new CommandBufferAllocateInfo { SType = StructureType.CommandBufferAllocateInfo, CommandPool = pool, Level = CommandBufferLevel.Primary, CommandBufferCount = 1 };
	vk.AllocateCommandBuffers(device, in allocInfo, out var cmd);
	var beginInfo = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
	vk.BeginCommandBuffer(cmd, in beginInfo);
	var range = new ImageSubresourceRange(ImageAspectFlags.ColorBit, 0, 1, 0, 1);
	Barrier(vk, cmd, target, ImageLayout.Undefined, ImageLayout.TransferDstOptimal, range);
	var clear = new ClearColorValue(1f, 128 / 255f, 0f, 1f);
	vk.CmdClearColorImage(cmd, target, ImageLayout.TransferDstOptimal, in clear, 1, in range);
	Barrier(vk, cmd, target, ImageLayout.TransferDstOptimal, ImageLayout.TransferSrcOptimal, range);
	var region = new BufferImageCopy { ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1), ImageExtent = new Extent3D(width, height, 1) };
	vk.CmdCopyImageToBuffer(cmd, target, ImageLayout.TransferSrcOptimal, readback, 1, in region);
	if (khrSwapchain is not null) Barrier(vk, cmd, target, ImageLayout.TransferSrcOptimal, ImageLayout.PresentSrcKhr, range);
	vk.EndCommandBuffer(cmd);

	var waitStage = PipelineStageFlags.TransferBit;
	var submit = new SubmitInfo { SType = StructureType.SubmitInfo, CommandBufferCount = 1, PCommandBuffers = &cmd };
	if (khrSwapchain is not null)
	{
		submit.WaitSemaphoreCount = 1;
		submit.PWaitSemaphores = &acquired;
		submit.PWaitDstStageMask = &waitStage;
	}
	Check(vk.QueueSubmit(queue, 1, in submit, default), "vkQueueSubmit");
	vk.QueueWaitIdle(queue);

	if (khrSwapchain is not null)
	{
		var present = new PresentInfoKHR { SType = StructureType.PresentInfoKhr, SwapchainCount = 1, PSwapchains = &swapchain, PImageIndices = &imageIndex };
		Console.WriteLine($"present: {khrSwapchain.QueuePresent(queue, in present)}");
		vk.QueueWaitIdle(queue);
	}

	void* mapped;
	vk.MapMemory(device, bmem, 0, size, 0, &mapped);
	var pixel = new ReadOnlySpan<byte>(mapped, 4);
	Console.WriteLine($"readback pixel (format {format}): {pixel[0]},{pixel[1]},{pixel[2]},{pixel[3]}");
	vk.UnmapMemory(device, bmem);

	Console.WriteLine($"total: {start.ElapsedMilliseconds} ms");

	if (window is not null)
	{
		for (var i = 0; i < 5; i++) window.DoEvents();
		window.Reset();
		window.Dispose();
		Console.WriteLine("window reset and disposed");
	}
}

static void Check(Result result, string what)
{
	if (result != Result.Success) throw new InvalidOperationException($"{what}: {result}");
}

static unsafe DeviceMemory Allocate(Vk vk, PhysicalDevice physical, Device device, MemoryRequirements req, MemoryPropertyFlags flags)
{
	vk.GetPhysicalDeviceMemoryProperties(physical, out var memProps);
	for (var i = 0; i < memProps.MemoryTypeCount; i++)
	{
		if ((req.MemoryTypeBits & (1u << i)) == 0 || (memProps.MemoryTypes[i].PropertyFlags & flags) != flags) continue;
		var info = new MemoryAllocateInfo { SType = StructureType.MemoryAllocateInfo, AllocationSize = req.Size, MemoryTypeIndex = (uint)i };
		Check(vk.AllocateMemory(device, in info, null, out var memory), "vkAllocateMemory");
		return memory;
	}
	throw new InvalidOperationException("no memory type");
}

static unsafe void Barrier(Vk vk, CommandBuffer cmd, VkImage image, ImageLayout from, ImageLayout to, ImageSubresourceRange range)
{
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

static unsafe byte[] CompileGlsl(string source, ShaderKind kind)
{
	var shaderc = Shaderc.GetApi();
	var compiler = shaderc.CompilerInitialize();
	var options = shaderc.CompileOptionsInitialize();
	shaderc.CompileOptionsSetTargetEnv(options, TargetEnv.Vulkan, (uint)EnvVersion.Vulkan10);
	var result = shaderc.CompileIntoSpv(compiler, source, (nuint)System.Text.Encoding.UTF8.GetByteCount(source), kind, "spike.vert", "main", options);
	if (shaderc.ResultGetCompilationStatus(result) != CompilationStatus.Success)
		throw new InvalidOperationException(shaderc.ResultGetErrorMessageS(result));
	var bytes = new ReadOnlySpan<byte>(shaderc.ResultGetBytes(result), (int)shaderc.ResultGetLength(result)).ToArray();
	shaderc.ResultRelease(result);
	shaderc.CompileOptionsRelease(options);
	shaderc.CompilerRelease(compiler);
	return bytes;
}

static unsafe string CrossToGles(byte[] spirv)
{
	var cross = Cross.GetApi();
	XContext* context;
	cross.ContextCreate(&context);
	XParsedIr* ir;
	fixed (byte* p = spirv) cross.ContextParseSpirv(context, (uint*)p, (nuint)(spirv.Length / 4), &ir);
	XCompiler* compiler;
	cross.ContextCreateCompiler(context, XBackend.Glsl, ir, XCaptureMode.TakeOwnership, &compiler);
	XCompilerOptions* options;
	cross.CompilerCreateCompilerOptions(compiler, &options);
	cross.CompilerOptionsSetUint(options, XCompilerOption.GlslVersion, 310);
	cross.CompilerOptionsSetBool(options, XCompilerOption.GlslES, 1);
	cross.CompilerInstallCompilerOptions(compiler, options);
	byte* source;
	var result = cross.CompilerCompile(compiler, &source);
	var text = result == Silk.NET.SPIRV.Cross.Result.Success ? SilkMarshal.PtrToString((nint)source)! : $"error {result}";
	cross.ContextDestroy(context);
	return text;
}
