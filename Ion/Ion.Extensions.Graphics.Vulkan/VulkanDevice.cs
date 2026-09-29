using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Silk.NET.Core;
using Silk.NET.Core.Contexts;
using Silk.NET.Core.Native;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using Silk.NET.Vulkan.Extensions.KHR;

using Ion.Extensions.Graphics.Rhi;

using VkBuffer = Silk.NET.Vulkan.Buffer;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;

namespace Ion.Extensions.Graphics.Vulkan;

/// <summary>
/// Options for <see cref="VulkanDevice.Create"/>.
/// </summary>
public sealed class VulkanDeviceOptions
{
	/// <summary>The window's Vulkan surface source (Silk.NET's <c>IView.VkSurface</c>), or null for a headless device.</summary>
	public IVkSurface? Surface { get; init; }

	/// <summary>Frames the CPU may record ahead of the GPU; clamped to [1, 3].</summary>
	public int FramesInFlight { get; init; } = 2;

	/// <summary>Enable <c>VK_LAYER_KHRONOS_validation</c> and a debug messenger when available.</summary>
	public bool Validation { get; init; }

	/// <summary>Pick the first adapter whose name contains this (case-insensitive); null for the best by type.</summary>
	public string? Adapter { get; init; }

	/// <summary>The application name reported to the driver.</summary>
	public string ApplicationName { get; init; } = "Ion";
}

/// <summary>
/// The Vulkan implementation of <see cref="IGraphicsDevice"/> on <c>Silk.NET.Vulkan</c>.
/// </summary>
/// <remarks>
/// <para>
/// One graphics queue (which also presents). Frames in flight each own a command pool, the fences of their submissions,
/// a linear staging ring for uploads and a deferred-destruction list; <see cref="EndFrame"/> submits, advances to the next
/// slot and waits for that slot's previous use to complete, so everything recorded until the next <see cref="EndFrame"/>
/// may reuse the slot's memory.
/// </para>
/// <para>
/// Memory: one <c>VkDeviceMemory</c> per buffer and texture (Silk.NET 2.x has no memory allocator); uploads go through the
/// per-frame staging ring. Synchronization is deliberately conservative (full pipeline barriers around copies and render
/// passes); correctness first, the 2D renderer will tell where it matters.
/// </para>
/// <para>
/// Clip space is WebGPU's (y up, depth in [0, 1]): the viewport is flipped with a negative height (core in Vulkan 1.1).
/// </para>
/// <para>
/// Texture layouts are tracked on the CPU in recording order, which assumes command encoders are submitted in the order
/// they are created (the normal case) and that textures are written before the passes that sample them are recorded.
/// </para>
/// <para>
/// macOS and iOS: Vulkan runs over MoltenVK. The device enables <c>VK_KHR_portability_enumeration</c> (instance) and
/// <c>VK_KHR_portability_subset</c> (device) whenever the loader and driver report them, which MoltenVK requires; ship
/// <c>libMoltenVK.dylib</c> with the game (for example through the <c>Silk.NET.MoltenVK.Native</c> package).
/// </para>
/// </remarks>
public sealed unsafe class VulkanDevice : IGraphicsDevice
{
	private const string ValidationLayer = "VK_LAYER_KHRONOS_validation";
	private const string PortabilityEnumeration = "VK_KHR_portability_enumeration";
	private const string PortabilitySubset = "VK_KHR_portability_subset";
	private const ulong StagingChunkSize = 4 * 1024 * 1024;

	private GCHandle _loggerHandle;

	private readonly ILogger _logger;
	private readonly FrameSlot[] _frames;
	private readonly Stack<Fence> _freeFences = new();
	private readonly List<DescriptorPool> _descriptorPools = [];
	private readonly Dictionary<RenderPassKey, RenderPass> _renderPasses = [];
	private readonly PhysicalDeviceMemoryProperties _memory;
	private ExtDebugUtils? _debugUtils;
	private DebugUtilsMessengerEXT _messenger;
	private bool _disposed;

	internal readonly Vk Vk;
	internal readonly Instance Instance;
	internal readonly PhysicalDevice Physical;
	internal readonly Device Handle;
	internal readonly Silk.NET.Vulkan.Queue VkQueue;
	internal readonly uint QueueFamily;
	internal readonly KhrSurface? KhrSurface;
	internal readonly KhrSwapchain? KhrSwapchain;
	internal readonly VulkanQueue QueueImpl;
	internal readonly VulkanSurface? SurfaceImpl;

	private VulkanDevice(ILogger logger, Vk vk, Instance instance, PhysicalDevice physical, Device device, uint family, KhrSurface? khrSurface, SurfaceKHR surface, int framesInFlight)
	{
		_logger = logger;
		Vk = vk;
		Instance = instance;
		Physical = physical;
		Handle = device;
		QueueFamily = family;
		vk.GetDeviceQueue(device, family, 0, out VkQueue);
		vk.GetPhysicalDeviceMemoryProperties(physical, out _memory);

		vk.GetPhysicalDeviceProperties(physical, out var props);
		AdapterName = SilkMarshal.PtrToString((nint)props.DeviceName) ?? "Vulkan device";
		Limits = new DeviceLimits
		{
			MaxTextureDimension2D = props.Limits.MaxImageDimension2D,
			MinUniformBufferOffsetAlignment = props.Limits.MinUniformBufferOffsetAlignment,
			MaxUniformBufferBindingSize = props.Limits.MaxUniformBufferRange,
			MaxBindGroups = Math.Min(props.Limits.MaxBoundDescriptorSets, 4u),
			VertexStorageBuffers = true,
		};
		ApiVersion = props.ApiVersion;

		FramesInFlight = framesInFlight;
		_frames = new FrameSlot[framesInFlight];
		for (var i = 0; i < framesInFlight; i++) _frames[i] = new FrameSlot(this);

		QueueImpl = new VulkanQueue(this);

		if (khrSurface is not null)
		{
			KhrSurface = khrSurface;
			vk.TryGetDeviceExtension(instance, device, out KhrSwapchain? swapchain);
			KhrSwapchain = swapchain ?? throw new InvalidOperationException("VK_KHR_swapchain is not available.");
			SurfaceImpl = new VulkanSurface(this, surface);
		}
	}

	/// <summary>The Vulkan API version of the physical device.</summary>
	public uint ApiVersion { get; }

	/// <inheritdoc/>
	public GraphicsBackend Backend => GraphicsBackend.Vulkan;

	/// <inheritdoc/>
	public string AdapterName { get; }

	/// <inheritdoc/>
	public ShaderLanguage ShaderLanguage => ShaderLanguage.SpirV;

	/// <inheritdoc/>
	public DeviceLimits Limits { get; }

	/// <inheritdoc/>
	public int FramesInFlight { get; }

	/// <inheritdoc/>
	public int FrameIndex { get; private set; }

	/// <inheritdoc/>
	public IQueue Queue => QueueImpl;

	/// <inheritdoc/>
	public ISurface? Surface => SurfaceImpl;

	/// <summary>Whether validation layers are active.</summary>
	public bool ValidationEnabled { get; private set; }

	internal FrameSlot CurrentFrame => _frames[FrameIndex];

	/// <summary>
	/// Whether a Vulkan driver with at least one device is available (headless, no surface). Used by tests to skip on
	/// machines without Vulkan.
	/// </summary>
	public static bool IsAvailable()
	{
		try
		{
			var vk = Vk.GetApi();
			var appInfo = new ApplicationInfo { SType = StructureType.ApplicationInfo, ApiVersion = Vk.Version11 };
			var info = new InstanceCreateInfo { SType = StructureType.InstanceCreateInfo, PApplicationInfo = &appInfo };
			if (vk.CreateInstance(in info, null, out var instance) != Result.Success) return false;
			uint count = 0;
			vk.EnumeratePhysicalDevices(instance, ref count, null);
			vk.DestroyInstance(instance, null);
			return count > 0;
		}
		catch (Exception)
		{
			return false;
		}
	}

	/// <summary>
	/// Creates the instance, picks a physical device, creates the device and, when <see cref="VulkanDeviceOptions.Surface"/>
	/// is set, the window surface (configure it with <see cref="ISurface.Configure"/> before the first frame).
	/// </summary>
	public static VulkanDevice Create(VulkanDeviceOptions options, ILogger? logger = null)
	{
		logger ??= NullLogger.Instance;
		var vk = Vk.GetApi();

		// Instance extensions and layers.
		var available = EnumerateInstanceExtensions(vk);
		var extensions = new List<string>();
		if (options.Surface is { } surfaceSource)
		{
			var required = surfaceSource.GetRequiredExtensions(out var count);
			for (var i = 0; i < count; i++) extensions.Add(SilkMarshal.PtrToString((nint)required[i])!);
		}

		var flags = (InstanceCreateFlags)0;
		if (available.Contains(PortabilityEnumeration))
		{
			extensions.Add(PortabilityEnumeration);
			flags |= InstanceCreateFlags.EnumeratePortabilityBitKhr;
		}

		var layers = new List<string>();
		var validation = false;
		if (options.Validation)
		{
			if (EnumerateLayers(vk).Contains(ValidationLayer))
			{
				layers.Add(ValidationLayer);
				validation = true;
			}
			else
			{
				logger.LogWarning("Vulkan validation was requested but {Layer} is not installed.", ValidationLayer);
			}

			if (available.Contains(ExtDebugUtils.ExtensionName)) extensions.Add(ExtDebugUtils.ExtensionName);
		}

		var appName = (byte*)SilkMarshal.StringToPtr(options.ApplicationName);
		var engineName = (byte*)SilkMarshal.StringToPtr("Ion");
		var extPtrs = (byte**)SilkMarshal.StringArrayToPtr(extensions);
		var layerPtrs = (byte**)SilkMarshal.StringArrayToPtr(layers);
		Instance instance;
		try
		{
			var appInfo = new ApplicationInfo
			{
				SType = StructureType.ApplicationInfo,
				PApplicationName = appName,
				ApplicationVersion = new Version32(1, 0, 0),
				PEngineName = engineName,
				EngineVersion = new Version32(0, 3, 0),
				ApiVersion = Vk.Version11,
			};
			var info = new InstanceCreateInfo
			{
				SType = StructureType.InstanceCreateInfo,
				Flags = flags,
				PApplicationInfo = &appInfo,
				EnabledExtensionCount = (uint)extensions.Count,
				PpEnabledExtensionNames = extPtrs,
				EnabledLayerCount = (uint)layers.Count,
				PpEnabledLayerNames = layerPtrs,
			};
			Check(vk.CreateInstance(in info, null, out instance), "vkCreateInstance");
		}
		finally
		{
			SilkMarshal.Free((nint)appName);
			SilkMarshal.Free((nint)engineName);
			SilkMarshal.Free((nint)extPtrs);
			SilkMarshal.Free((nint)layerPtrs);
		}

		KhrSurface? khrSurface = null;
		SurfaceKHR surface = default;
		if (options.Surface is { } source)
		{
			surface = source.Create<AllocationCallbacks>(instance.ToHandle(), null).ToSurface();
			if (!vk.TryGetInstanceExtension(instance, out khrSurface)) throw new InvalidOperationException("VK_KHR_surface is not available.");
		}

		var (physical, family, name) = PickPhysicalDevice(vk, instance, khrSurface, surface, options.Adapter);

		// Device.
		var deviceExtensions = new List<string>();
		var availableDevice = EnumerateDeviceExtensions(vk, physical);
		if (khrSurface is not null) deviceExtensions.Add(KhrSwapchain.ExtensionName);
		if (availableDevice.Contains(PortabilitySubset)) deviceExtensions.Add(PortabilitySubset);

		var priority = 1f;
		var queueInfo = new DeviceQueueCreateInfo { SType = StructureType.DeviceQueueCreateInfo, QueueFamilyIndex = family, QueueCount = 1, PQueuePriorities = &priority };
		var devExtPtrs = (byte**)SilkMarshal.StringArrayToPtr(deviceExtensions);
		Device device;
		try
		{
			var features = new PhysicalDeviceFeatures();
			var deviceInfo = new DeviceCreateInfo
			{
				SType = StructureType.DeviceCreateInfo,
				QueueCreateInfoCount = 1,
				PQueueCreateInfos = &queueInfo,
				EnabledExtensionCount = (uint)deviceExtensions.Count,
				PpEnabledExtensionNames = devExtPtrs,
				PEnabledFeatures = &features,
			};
			Check(vk.CreateDevice(physical, in deviceInfo, null, out device), "vkCreateDevice");
		}
		finally
		{
			SilkMarshal.Free((nint)devExtPtrs);
		}

		var frames = Math.Clamp(options.FramesInFlight, 1, 3);
		var result = new VulkanDevice(logger, vk, instance, physical, device, family, khrSurface, surface, frames)
		{
			ValidationEnabled = validation,
		};

		if (options.Validation && extensions.Contains(ExtDebugUtils.ExtensionName)) result._createMessenger();

		logger.LogInformation("Vulkan device: {Adapter} (API {Major}.{Minor}), {Frames} frames in flight, validation {Validation}, {Mode}.",
			name, result.ApiVersion >> 22, (result.ApiVersion >> 12) & 0x3FF, frames, validation ? "on" : "off", khrSurface is null ? "headless" : "windowed");
		return result;
	}

	/// <inheritdoc/>
	public IBuffer CreateBuffer(in BufferDescriptor descriptor) => new VulkanBuffer(this, descriptor);

	/// <inheritdoc/>
	public ITexture CreateTexture(in TextureDescriptor descriptor) => new VulkanTexture(this, descriptor);

	/// <inheritdoc/>
	public ISampler CreateSampler(in SamplerDescriptor descriptor) => new VulkanSampler(this, descriptor);

	/// <inheritdoc/>
	public IShaderModule CreateShaderModule(in ShaderModuleDescriptor descriptor) => new VulkanShaderModule(this, descriptor);

	/// <inheritdoc/>
	public IBindGroupLayout CreateBindGroupLayout(in BindGroupLayoutDescriptor descriptor) => new VulkanBindGroupLayout(this, descriptor);

	/// <inheritdoc/>
	public IBindGroup CreateBindGroup(in BindGroupDescriptor descriptor) => new VulkanBindGroup(this, descriptor);

	/// <inheritdoc/>
	public IPipelineLayout CreatePipelineLayout(in PipelineLayoutDescriptor descriptor) => new VulkanPipelineLayout(this, descriptor);

	/// <inheritdoc/>
	public IRenderPipeline CreateRenderPipeline(RenderPipelineDescriptor descriptor) => new VulkanRenderPipeline(this, descriptor);

	/// <inheritdoc/>
	public ICommandEncoder CreateCommandEncoder(string? label = null) => CurrentFrame.RentEncoder();

	/// <inheritdoc/>
	public void BeginFrame()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}

	/// <inheritdoc/>
	public void EndFrame()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		QueueImpl.Flush();
		FrameIndex = (FrameIndex + 1) % FramesInFlight;
		_recycle(_frames[FrameIndex]);
	}

	/// <inheritdoc/>
	public void Poll(bool wait = false)
	{
		QueueImpl.Flush();
		if (wait) Check(Vk.QueueWaitIdle(VkQueue), "vkQueueWaitIdle");
	}

	/// <summary>Waits for the GPU, destroys everything and the instance.</summary>
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		Vk.DeviceWaitIdle(Handle);
		SurfaceImpl?.Destroy();
		foreach (var frame in _frames) frame.Destroy();
		while (_freeFences.TryPop(out var fence)) Vk.DestroyFence(Handle, fence, null);
		foreach (var pool in _descriptorPools) Vk.DestroyDescriptorPool(Handle, pool, null);
		foreach (var pass in _renderPasses.Values) Vk.DestroyRenderPass(Handle, pass, null);
		Vk.DestroyDevice(Handle, null);
		if (_messenger.Handle != 0) _debugUtils!.DestroyDebugUtilsMessenger(Instance, _messenger, null);
		if (_loggerHandle.IsAllocated) _loggerHandle.Free();
		SurfaceImpl?.DestroySurface();
		Vk.DestroyInstance(Instance, null);
	}

	internal static void Check(Result result, string what)
	{
		if (result != Result.Success) throw new VulkanException(what, result);
	}

	/// <summary>Destroys <paramref name="destroy"/>'s object once the frames that may use it have completed.</summary>
	internal void Defer(Action destroy)
	{
		if (_disposed) return;
		CurrentFrame.Deferred.Add(destroy);
	}

	internal Fence RentFence()
	{
		if (_freeFences.TryPop(out var fence)) return fence;
		var info = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };
		Check(Vk.CreateFence(Handle, in info, null, out fence), "vkCreateFence");
		return fence;
	}

	internal VkSemaphore CreateSemaphore()
	{
		var info = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };
		Check(Vk.CreateSemaphore(Handle, in info, null, out var semaphore), "vkCreateSemaphore");
		return semaphore;
	}

	internal DeviceMemory AllocateMemory(in MemoryRequirements requirements, MemoryPropertyFlags preferred, MemoryPropertyFlags required)
	{
		var index = FindMemoryType(requirements.MemoryTypeBits, preferred | required);
		if (index < 0) index = FindMemoryType(requirements.MemoryTypeBits, required);
		if (index < 0) throw new InvalidOperationException($"No Vulkan memory type with {required}.");

		var info = new MemoryAllocateInfo { SType = StructureType.MemoryAllocateInfo, AllocationSize = requirements.Size, MemoryTypeIndex = (uint)index };
		Check(Vk.AllocateMemory(Handle, in info, null, out var memory), "vkAllocateMemory");
		return memory;
	}

	private int FindMemoryType(uint typeBits, MemoryPropertyFlags flags)
	{
		for (var i = 0; i < _memory.MemoryTypeCount; i++)
		{
			if ((typeBits & (1u << i)) != 0 && (_memory.MemoryTypes[i].PropertyFlags & flags) == flags) return i;
		}

		return -1;
	}

	internal DescriptorSet AllocateDescriptorSet(DescriptorSetLayout layout, out DescriptorPool pool)
	{
		for (var attempt = 0; attempt < 2; attempt++)
		{
			if (_descriptorPools.Count > 0)
			{
				pool = _descriptorPools[^1];
				var info = new DescriptorSetAllocateInfo { SType = StructureType.DescriptorSetAllocateInfo, DescriptorPool = pool, DescriptorSetCount = 1, PSetLayouts = &layout };
				var result = Vk.AllocateDescriptorSets(Handle, in info, out var set);
				if (result == Result.Success) return set;
				if (result is not (Result.ErrorOutOfPoolMemory or Result.ErrorFragmentedPool)) Check(result, "vkAllocateDescriptorSets");
			}

			_descriptorPools.Add(_createDescriptorPool());
		}

		throw new InvalidOperationException("Could not allocate a descriptor set.");
	}

	private DescriptorPool _createDescriptorPool()
	{
		const uint sets = 256;
		var sizes = stackalloc DescriptorPoolSize[]
		{
			new(DescriptorType.UniformBuffer, sets * 2),
			new(DescriptorType.StorageBuffer, sets),
			new(DescriptorType.Sampler, sets * 2),
			new(DescriptorType.SampledImage, sets * 2),
		};
		var info = new DescriptorPoolCreateInfo
		{
			SType = StructureType.DescriptorPoolCreateInfo,
			Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit,
			MaxSets = sets,
			PoolSizeCount = 4,
			PPoolSizes = sizes,
		};
		Check(Vk.CreateDescriptorPool(Handle, in info, null, out var pool), "vkCreateDescriptorPool");
		return pool;
	}

	internal RenderPass GetRenderPass(in RenderPassKey key)
	{
		if (_renderPasses.TryGetValue(key, out var pass)) return pass;
		pass = key.Create(this);
		_renderPasses.Add(key, pass);
		return pass;
	}

	internal Format PickDepthFormat(TextureFormat format)
	{
		if (format != TextureFormat.Depth24PlusStencil8) return format.ToVk();
		foreach (var candidate in (ReadOnlySpan<Format>)[Format.D24UnormS8Uint, Format.D32SfloatS8Uint])
		{
			Vk.GetPhysicalDeviceFormatProperties(Physical, candidate, out var props);
			if ((props.OptimalTilingFeatures & FormatFeatureFlags.DepthStencilAttachmentBit) != 0) return candidate;
		}

		return Format.D32SfloatS8Uint;
	}

	/// <summary>Whether <paramref name="format"/> supports <paramref name="features"/> with optimal tiling.</summary>
	internal bool Supports(Format format, FormatFeatureFlags features)
	{
		Vk.GetPhysicalDeviceFormatProperties(Physical, format, out var props);
		return (props.OptimalTilingFeatures & features) == features;
	}

	private void _recycle(FrameSlot frame)
	{
		frame.WaitAndReset();
		foreach (var fence in frame.Fences) _freeFences.Push(fence);
		frame.Fences.Clear();
	}

	private void _createMessenger()
	{
		if (!Vk.TryGetInstanceExtension(Instance, out _debugUtils)) return;

		// The callback finds this device's logger through the user data pointer (a GC handle), so several devices (tests)
		// log to their own loggers.
		_loggerHandle = GCHandle.Alloc(_logger);
		var info = new DebugUtilsMessengerCreateInfoEXT
		{
			SType = StructureType.DebugUtilsMessengerCreateInfoExt,
			MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.WarningBitExt | DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt,
			MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt,
			PfnUserCallback = new PfnDebugUtilsMessengerCallbackEXT(&_debugCallback),
			PUserData = (void*)GCHandle.ToIntPtr(_loggerHandle),
		};
		if (_debugUtils!.CreateDebugUtilsMessenger(Instance, in info, null, out _messenger) != Result.Success)
		{
			_logger.LogWarning("Could not create the Vulkan debug messenger.");
		}
	}

	[UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
	private static Bool32 _debugCallback(DebugUtilsMessageSeverityFlagsEXT severity, DebugUtilsMessageTypeFlagsEXT types, DebugUtilsMessengerCallbackDataEXT* data, void* user)
	{
		var message = SilkMarshal.PtrToString((nint)data->PMessage) ?? string.Empty;
		var level = (severity & DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt) != 0 ? LogLevel.Error : LogLevel.Warning;
		var logger = user is null ? NullLogger.Instance : GCHandle.FromIntPtr((nint)user).Target as ILogger ?? NullLogger.Instance;
		logger.Log(level, "Vulkan {Types}: {Message}", types, message);
		return new Bool32(false);
	}

	private static HashSet<string> EnumerateInstanceExtensions(Vk vk)
	{
		uint count = 0;
		vk.EnumerateInstanceExtensionProperties((byte*)null, ref count, null);
		var props = new ExtensionProperties[count];
		fixed (ExtensionProperties* p = props) vk.EnumerateInstanceExtensionProperties((byte*)null, ref count, p);
		var set = new HashSet<string>();
		foreach (var prop in props) set.Add(SilkMarshal.PtrToString((nint)prop.ExtensionName)!);
		return set;
	}

	private static HashSet<string> EnumerateDeviceExtensions(Vk vk, PhysicalDevice device)
	{
		uint count = 0;
		vk.EnumerateDeviceExtensionProperties(device, (byte*)null, ref count, null);
		var props = new ExtensionProperties[count];
		fixed (ExtensionProperties* p = props) vk.EnumerateDeviceExtensionProperties(device, (byte*)null, ref count, p);
		var set = new HashSet<string>();
		foreach (var prop in props) set.Add(SilkMarshal.PtrToString((nint)prop.ExtensionName)!);
		return set;
	}

	private static HashSet<string> EnumerateLayers(Vk vk)
	{
		uint count = 0;
		vk.EnumerateInstanceLayerProperties(ref count, null);
		var props = new LayerProperties[count];
		fixed (LayerProperties* p = props) vk.EnumerateInstanceLayerProperties(ref count, p);
		var set = new HashSet<string>();
		foreach (var prop in props) set.Add(SilkMarshal.PtrToString((nint)prop.LayerName)!);
		return set;
	}

	private static (PhysicalDevice Device, uint Family, string Name) PickPhysicalDevice(Vk vk, Instance instance, KhrSurface? khrSurface, SurfaceKHR surface, string? adapter)
	{
		uint count = 0;
		vk.EnumeratePhysicalDevices(instance, ref count, null);
		if (count == 0) throw new InvalidOperationException("No Vulkan device found. Install a Vulkan driver (on Linux CI: Mesa lavapipe, package mesa-vulkan-drivers).");
		var devices = new PhysicalDevice[count];
		fixed (PhysicalDevice* p = devices) vk.EnumeratePhysicalDevices(instance, ref count, p);

		(PhysicalDevice Device, uint Family, string Name, int Score) best = default;
		best.Score = int.MinValue;
		foreach (var device in devices)
		{
			vk.GetPhysicalDeviceProperties(device, out var props);
			var name = SilkMarshal.PtrToString((nint)props.DeviceName) ?? "?";
			if (khrSurface is not null && !EnumerateDeviceExtensions(vk, device).Contains(KhrSwapchain.ExtensionName)) continue;

			var family = FindQueueFamily(vk, device, khrSurface, surface);
			if (family < 0) continue;

			var score = props.DeviceType switch
			{
				PhysicalDeviceType.DiscreteGpu => 400,
				PhysicalDeviceType.IntegratedGpu => 300,
				PhysicalDeviceType.VirtualGpu => 200,
				PhysicalDeviceType.Cpu => 100,
				_ => 0,
			};
			if (adapter is not null && name.Contains(adapter, StringComparison.OrdinalIgnoreCase)) score += 10_000;
			if (score > best.Score) best = (device, (uint)family, name, score);
		}

		if (best.Score == int.MinValue) throw new InvalidOperationException("No Vulkan device supports graphics" + (khrSurface is null ? "." : " and presentation to this window."));
		return (best.Device, best.Family, best.Name ?? "?");
	}

	private static int FindQueueFamily(Vk vk, PhysicalDevice device, KhrSurface? khrSurface, SurfaceKHR surface)
	{
		uint count = 0;
		vk.GetPhysicalDeviceQueueFamilyProperties(device, ref count, null);
		var families = new QueueFamilyProperties[count];
		fixed (QueueFamilyProperties* p = families) vk.GetPhysicalDeviceQueueFamilyProperties(device, ref count, p);
		for (var i = 0; i < count; i++)
		{
			if ((families[i].QueueFlags & QueueFlags.GraphicsBit) == 0) continue;
			if (khrSurface is not null)
			{
				khrSurface.GetPhysicalDeviceSurfaceSupport(device, (uint)i, surface, out var supported);
				if (!supported) continue;
			}

			return i;
		}

		return -1;
	}

	/// <summary>
	/// The per-frame state of one frame slot.
	/// </summary>
	internal sealed class FrameSlot
	{
		private readonly VulkanDevice _device;
		private readonly CommandPool _pool;
		private readonly List<CommandBuffer> _buffers = [];
		private readonly List<VulkanCommandEncoder> _encoders = [];
		private readonly List<StagingChunk> _staging = [];
		private int _nextBuffer;
		private int _nextEncoder;
		private int _stagingChunk;

		public readonly List<Fence> Fences = [];
		public readonly List<Action> Deferred = [];

		public FrameSlot(VulkanDevice device)
		{
			_device = device;
			var info = new CommandPoolCreateInfo { SType = StructureType.CommandPoolCreateInfo, Flags = CommandPoolCreateFlags.TransientBit, QueueFamilyIndex = device.QueueFamily };
			Check(device.Vk.CreateCommandPool(device.Handle, in info, null, out _pool), "vkCreateCommandPool");
		}

		public CommandBuffer BeginCommandBuffer()
		{
			CommandBuffer cmd;
			if (_nextBuffer < _buffers.Count)
			{
				cmd = _buffers[_nextBuffer];
			}
			else
			{
				var info = new CommandBufferAllocateInfo { SType = StructureType.CommandBufferAllocateInfo, CommandPool = _pool, Level = CommandBufferLevel.Primary, CommandBufferCount = 1 };
				Check(_device.Vk.AllocateCommandBuffers(_device.Handle, in info, out cmd), "vkAllocateCommandBuffers");
				_buffers.Add(cmd);
			}

			_nextBuffer++;
			var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit };
			Check(_device.Vk.BeginCommandBuffer(cmd, in begin), "vkBeginCommandBuffer");
			return cmd;
		}

		public VulkanCommandEncoder RentEncoder()
		{
			VulkanCommandEncoder encoder;
			if (_nextEncoder < _encoders.Count)
			{
				encoder = _encoders[_nextEncoder];
			}
			else
			{
				encoder = new VulkanCommandEncoder(_device);
				_encoders.Add(encoder);
			}

			_nextEncoder++;
			encoder.Begin(BeginCommandBuffer());
			return encoder;
		}

		public StagingAllocation AllocateStaging(ulong size, ulong alignment)
		{
			while (true)
			{
				if (_stagingChunk < _staging.Count)
				{
					var chunk = _staging[_stagingChunk];
					var offset = (chunk.Used + alignment - 1) / alignment * alignment;
					if (offset + size <= chunk.Size)
					{
						chunk.Used = offset + size;
						return new StagingAllocation(chunk.Buffer, offset, chunk.Mapped + offset);
					}

					_stagingChunk++;
					continue;
				}

				_staging.Add(new StagingChunk(_device, Math.Max(StagingChunkSize, size)));
			}
		}

		public void WaitAndReset()
		{
			var vk = _device.Vk;
			if (Fences.Count > 0)
			{
				var fences = CollectionsMarshal.AsSpan(Fences);
				fixed (Fence* p = fences)
				{
					Check(vk.WaitForFences(_device.Handle, (uint)fences.Length, p, true, ulong.MaxValue), "vkWaitForFences");
					Check(vk.ResetFences(_device.Handle, (uint)fences.Length, p), "vkResetFences");
				}
			}

			foreach (var destroy in Deferred) destroy();
			Deferred.Clear();

			Check(vk.ResetCommandPool(_device.Handle, _pool, 0), "vkResetCommandPool");
			_nextBuffer = 0;
			_nextEncoder = 0;
			_stagingChunk = 0;
			foreach (var chunk in _staging) chunk.Used = 0;
		}

		public void Destroy()
		{
			foreach (var destroy in Deferred) destroy();
			Deferred.Clear();
			foreach (var chunk in _staging) chunk.Destroy();
			foreach (var fence in Fences) _device.Vk.DestroyFence(_device.Handle, fence, null);
			Fences.Clear();
			_device.Vk.DestroyCommandPool(_device.Handle, _pool, null);
		}
	}

	internal readonly struct StagingAllocation(VkBuffer buffer, ulong offset, byte* pointer)
	{
		public readonly VkBuffer Buffer = buffer;
		public readonly ulong Offset = offset;
		public readonly byte* Pointer = pointer;
	}

	private sealed class StagingChunk
	{
		private readonly VulkanDevice _device;
		private readonly DeviceMemory _memory;

		public StagingChunk(VulkanDevice device, ulong size)
		{
			_device = device;
			Size = size;
			var vk = device.Vk;
			var info = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = size, Usage = BufferUsageFlags.TransferSrcBit, SharingMode = SharingMode.Exclusive };
			Check(vk.CreateBuffer(device.Handle, in info, null, out Buffer), "vkCreateBuffer(staging)");
			vk.GetBufferMemoryRequirements(device.Handle, Buffer, out var requirements);
			_memory = device.AllocateMemory(requirements, 0, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit);
			Check(vk.BindBufferMemory(device.Handle, Buffer, _memory, 0), "vkBindBufferMemory(staging)");
			void* mapped;
			Check(vk.MapMemory(device.Handle, _memory, 0, size, 0, &mapped), "vkMapMemory(staging)");
			Mapped = (byte*)mapped;
		}

		public readonly VkBuffer Buffer;
		public readonly byte* Mapped;
		public readonly ulong Size;
		public ulong Used;

		public void Destroy()
		{
			_device.Vk.UnmapMemory(_device.Handle, _memory);
			_device.Vk.DestroyBuffer(_device.Handle, Buffer, null);
			_device.Vk.FreeMemory(_device.Handle, _memory, null);
		}
	}
}

/// <summary>
/// A Vulkan call failed.
/// </summary>
public sealed class VulkanException(string call, Result result) : Exception($"{call} failed: {result}.")
{
	/// <summary>The Vulkan result code.</summary>
	public Result Result { get; } = result;
}
