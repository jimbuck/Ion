using System.Runtime.InteropServices;

using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

using Ion.Extensions.Graphics.Rhi;

using VkBuffer = Silk.NET.Vulkan.Buffer;
using VkImage = Silk.NET.Vulkan.Image;
using VkSampler = Silk.NET.Vulkan.Sampler;

namespace Ion.Extensions.Graphics.Vulkan;

internal sealed unsafe class VulkanBuffer : IBuffer
{
	private readonly VulkanDevice _device;
	private readonly DeviceMemory _memory;
	private readonly byte* _mapped;
	private bool _disposed;

	public VulkanBuffer(VulkanDevice device, in BufferDescriptor descriptor)
	{
		ArgumentOutOfRangeException.ThrowIfZero(descriptor.Size, nameof(descriptor));
		_device = device;
		Size = descriptor.Size;
		Usage = descriptor.Usage;
		var vk = device.Vk;

		var info = new BufferCreateInfo { SType = StructureType.BufferCreateInfo, Size = Size, Usage = Usage.ToVk(), SharingMode = SharingMode.Exclusive };
		VulkanDevice.Check(vk.CreateBuffer(device.Handle, in info, null, out Handle), "vkCreateBuffer");
		vk.GetBufferMemoryRequirements(device.Handle, Handle, out var requirements);

		var hostVisible = (Usage & (BufferUsage.MapRead | BufferUsage.MapWrite)) != 0;
		_memory = hostVisible
			? device.AllocateMemory(requirements, MemoryPropertyFlags.HostCachedBit, MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit)
			: device.AllocateMemory(requirements, MemoryPropertyFlags.DeviceLocalBit, 0);
		VulkanDevice.Check(vk.BindBufferMemory(device.Handle, Handle, _memory, 0), "vkBindBufferMemory");

		if (hostVisible)
		{
			void* mapped;
			VulkanDevice.Check(vk.MapMemory(device.Handle, _memory, 0, Size, 0, &mapped), "vkMapMemory");
			_mapped = (byte*)mapped;
		}
	}

	public readonly VkBuffer Handle;

	public ulong Size { get; }

	public BufferUsage Usage { get; }

	public void Read(ulong offset, Span<byte> destination)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_mapped is null) throw new InvalidOperationException("Only buffers created with BufferUsage.MapRead can be read.");
		if (offset + (ulong)destination.Length > Size) throw new ArgumentOutOfRangeException(nameof(destination));
		_device.QueueImpl.WaitIdle();
		new ReadOnlySpan<byte>(_mapped + offset, destination.Length).CopyTo(destination);
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		var device = _device;
		var handle = Handle;
		var memory = _memory;
		device.Defer(() =>
		{
			device.Vk.DestroyBuffer(device.Handle, handle, null);
			device.Vk.FreeMemory(device.Handle, memory, null);
		});
	}
}

internal sealed unsafe class VulkanTexture : ITexture
{
	private readonly VulkanDevice _device;
	private readonly DeviceMemory _memory;
	private readonly bool _ownsImage;
	private VulkanTextureView? _defaultView;
	private bool _disposed;

	public VulkanTexture(VulkanDevice device, in TextureDescriptor descriptor)
	{
		ArgumentOutOfRangeException.ThrowIfZero(descriptor.Width, nameof(descriptor));
		ArgumentOutOfRangeException.ThrowIfZero(descriptor.Height, nameof(descriptor));
		_device = device;
		_ownsImage = true;
		Width = descriptor.Width;
		Height = descriptor.Height;
		Format = descriptor.Format;
		Usage = descriptor.Usage;
		MipLevelCount = Math.Max(1, descriptor.MipLevelCount);
		SampleCount = Math.Max(1, descriptor.SampleCount);
		Dimension = descriptor.Dimension;
		ArrayLayerCount = Dimension.ArrayLayerCount();
		if (Dimension == TextureDimension.Cube)
		{
			if (Width != Height) throw new ArgumentException("The faces of a cube map must be square.", nameof(descriptor));
			if ((Usage & (TextureUsage.RenderAttachment | TextureUsage.CopySrc)) != 0 || SampleCount > 1)
			{
				throw new NotSupportedException("Cube maps are sampled textures only (no render attachment, copy source or multisampling).");
			}
		}

		VkFormat = Format.IsDepth() ? device.PickDepthFormat(Format) : Format.ToVk();

		var vk = device.Vk;
		var info = new ImageCreateInfo
		{
			SType = StructureType.ImageCreateInfo,
			Flags = Dimension == TextureDimension.Cube ? ImageCreateFlags.CreateCubeCompatibleBit : 0,
			ImageType = ImageType.Type2D,
			Format = VkFormat,
			Extent = new Extent3D(Width, Height, 1),
			MipLevels = MipLevelCount,
			ArrayLayers = ArrayLayerCount,
			Samples = VulkanFormats.ToVkSamples(SampleCount),
			Tiling = ImageTiling.Optimal,
			Usage = Usage.ToVk(Format),
			SharingMode = SharingMode.Exclusive,
			InitialLayout = ImageLayout.Undefined,
		};
		VulkanDevice.Check(vk.CreateImage(device.Handle, in info, null, out Image), "vkCreateImage");
		vk.GetImageMemoryRequirements(device.Handle, Image, out var requirements);
		_memory = device.AllocateMemory(requirements, MemoryPropertyFlags.DeviceLocalBit, 0);
		VulkanDevice.Check(vk.BindImageMemory(device.Handle, Image, _memory, 0), "vkBindImageMemory");
	}

	/// <summary>Wraps a swapchain image (not owned).</summary>
	public VulkanTexture(VulkanDevice device, VkImage image, Format format, uint width, uint height)
	{
		_device = device;
		Image = image;
		VkFormat = format;
		Format = VulkanFormats.FromVk(format);
		Width = width;
		Height = height;
		Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.CopyDst;
		MipLevelCount = 1;
		SampleCount = 1;
		ArrayLayerCount = 1;
		IsSwapchainImage = true;
	}

	public readonly VkImage Image;

	public readonly Format VkFormat;

	/// <summary>The layout after every command recorded so far (tracked on the CPU in recording order).</summary>
	public ImageLayout Layout { get; set; } = ImageLayout.Undefined;

	/// <summary>True for swapchain images, which end every pass in <c>PRESENT_SRC_KHR</c>.</summary>
	public bool IsSwapchainImage { get; }

	public TextureDimension Dimension { get; }

	/// <summary>The number of array layers (6 for a cube map).</summary>
	public uint ArrayLayerCount { get; }

	public uint Width { get; }

	public uint Height { get; }

	public TextureFormat Format { get; }

	public TextureUsage Usage { get; }

	public uint MipLevelCount { get; }

	public uint SampleCount { get; }

	public ITextureView DefaultView => _defaultView ??= new VulkanTextureView(_device, this, new TextureViewDescriptor(), owned: false);

	public ITextureView CreateView(in TextureViewDescriptor descriptor) => new VulkanTextureView(_device, this, descriptor, owned: true);

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_defaultView?.Release();
		if (!_ownsImage) return;
		var device = _device;
		var image = Image;
		var memory = _memory;
		device.Defer(() =>
		{
			device.Vk.DestroyImage(device.Handle, image, null);
			device.Vk.FreeMemory(device.Handle, memory, null);
		});
	}

	/// <summary>Destroys a swapchain image's views immediately (the device is idle during swapchain recreation).</summary>
	public void DestroyNow()
	{
		_disposed = true;
		_defaultView?.DestroyNow();
	}
}

internal sealed unsafe class VulkanTextureView : ITextureView
{
	private readonly VulkanDevice _device;
	private readonly bool _owned;
	private bool _disposed;

	public VulkanTextureView(VulkanDevice device, VulkanTexture texture, in TextureViewDescriptor descriptor, bool owned)
	{
		_device = device;
		_owned = owned;
		TextureImpl = texture;
		var levels = descriptor.MipLevelCount == 0 ? texture.MipLevelCount - descriptor.BaseMipLevel : descriptor.MipLevelCount;
		var info = new ImageViewCreateInfo
		{
			SType = StructureType.ImageViewCreateInfo,
			Image = texture.Image,
			ViewType = texture.Dimension == TextureDimension.Cube ? ImageViewType.TypeCube : ImageViewType.Type2D,
			Format = texture.VkFormat,
			Components = new ComponentMapping(ComponentSwizzle.Identity, ComponentSwizzle.Identity, ComponentSwizzle.Identity, ComponentSwizzle.Identity),
			SubresourceRange = new ImageSubresourceRange(texture.Format.Aspect(), descriptor.BaseMipLevel, levels, 0, texture.ArrayLayerCount),
		};
		VulkanDevice.Check(device.Vk.CreateImageView(device.Handle, in info, null, out Handle), "vkCreateImageView");
	}

	public readonly ImageView Handle;

	public VulkanTexture TextureImpl { get; }

	public ITexture Texture => TextureImpl;

	public TextureFormat Format => TextureImpl.Format;

	public void Dispose()
	{
		// The default view belongs to its texture and is released with it.
		if (_owned) Release();
	}

	internal void Release()
	{
		if (_disposed) return;
		_disposed = true;
		var device = _device;
		var handle = Handle;
		device.Defer(() => device.Vk.DestroyImageView(device.Handle, handle, null));
	}

	internal void DestroyNow()
	{
		if (_disposed) return;
		_disposed = true;
		_device.Vk.DestroyImageView(_device.Handle, Handle, null);
	}
}

internal sealed unsafe class VulkanSampler : ISampler
{
	private readonly VulkanDevice _device;
	private bool _disposed;

	public VulkanSampler(VulkanDevice device, in SamplerDescriptor descriptor)
	{
		_device = device;
		var info = new SamplerCreateInfo
		{
			SType = StructureType.SamplerCreateInfo,
			MagFilter = descriptor.MagFilter.ToVk(),
			MinFilter = descriptor.MinFilter.ToVk(),
			MipmapMode = descriptor.MipmapFilter.ToVkMipmap(),
			AddressModeU = descriptor.AddressModeU.ToVk(),
			AddressModeV = descriptor.AddressModeV.ToVk(),
			AddressModeW = descriptor.AddressModeW.ToVk(),
			MinLod = descriptor.LodMinClamp,
			MaxLod = descriptor.LodMaxClamp,
			CompareEnable = descriptor.Compare.HasValue,
			CompareOp = descriptor.Compare?.ToVk() ?? CompareOp.Always,
			BorderColor = BorderColor.FloatTransparentBlack,
		};
		VulkanDevice.Check(device.Vk.CreateSampler(device.Handle, in info, null, out Handle), "vkCreateSampler");
	}

	public readonly VkSampler Handle;

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		var device = _device;
		var handle = Handle;
		device.Defer(() => device.Vk.DestroySampler(device.Handle, handle, null));
	}
}

internal sealed unsafe class VulkanShaderModule : IShaderModule
{
	private readonly VulkanDevice _device;
	private bool _disposed;

	public VulkanShaderModule(VulkanDevice device, in ShaderModuleDescriptor descriptor)
	{
		if (descriptor.Language != ShaderLanguage.SpirV) throw new NotSupportedException($"The Vulkan backend takes SPIR-V shader modules, not {descriptor.Language}.");
		var code = descriptor.Code.Span;
		if (code.Length == 0 || code.Length % 4 != 0) throw new ArgumentException("SPIR-V code must be a non-empty multiple of 4 bytes.", nameof(descriptor));
		if (MemoryMarshal.Read<uint>(code) != 0x07230203) throw new ArgumentException("The code is not SPIR-V (bad magic number).", nameof(descriptor));

		_device = device;
		Stage = descriptor.Stage;

		// Copy into a uint array: vkCreateShaderModule requires 4-byte aligned code.
		var words = new uint[code.Length / 4];
		code.CopyTo(MemoryMarshal.AsBytes(words.AsSpan()));
		fixed (uint* p = words)
		{
			var info = new ShaderModuleCreateInfo { SType = StructureType.ShaderModuleCreateInfo, CodeSize = (nuint)code.Length, PCode = p };
			VulkanDevice.Check(device.Vk.CreateShaderModule(device.Handle, in info, null, out Handle), "vkCreateShaderModule");
		}
	}

	public readonly ShaderModule Handle;

	public ShaderStage Stage { get; }

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		var device = _device;
		var handle = Handle;
		device.Defer(() => device.Vk.DestroyShaderModule(device.Handle, handle, null));
	}
}

internal sealed unsafe class VulkanBindGroupLayout : IBindGroupLayout
{
	private readonly VulkanDevice _device;
	private bool _disposed;

	public VulkanBindGroupLayout(VulkanDevice device, in BindGroupLayoutDescriptor descriptor)
	{
		_device = device;
		Entries = descriptor.Entries.ToArray();
		var bindings = stackalloc DescriptorSetLayoutBinding[Math.Max(1, Entries.Count)];
		for (var i = 0; i < Entries.Count; i++)
		{
			var entry = Entries[i];
			bindings[i] = new DescriptorSetLayoutBinding
			{
				Binding = entry.Binding,
				DescriptorType = entry.Type.ToVk(),
				DescriptorCount = 1,
				StageFlags = entry.Visibility.ToVk(),
			};
		}

		var info = new DescriptorSetLayoutCreateInfo { SType = StructureType.DescriptorSetLayoutCreateInfo, BindingCount = (uint)Entries.Count, PBindings = bindings };
		VulkanDevice.Check(device.Vk.CreateDescriptorSetLayout(device.Handle, in info, null, out Handle), "vkCreateDescriptorSetLayout");
	}

	public readonly DescriptorSetLayout Handle;

	public IReadOnlyList<BindGroupLayoutEntry> Entries { get; }

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		var device = _device;
		var handle = Handle;
		device.Defer(() => device.Vk.DestroyDescriptorSetLayout(device.Handle, handle, null));
	}
}

internal sealed unsafe class VulkanBindGroup : IBindGroup
{
	private readonly VulkanDevice _device;
	private readonly DescriptorPool _pool;
	private bool _disposed;

	public VulkanBindGroup(VulkanDevice device, in BindGroupDescriptor descriptor)
	{
		_device = device;
		var layout = (VulkanBindGroupLayout)descriptor.Layout;
		Layout = layout;
		Handle = device.AllocateDescriptorSet(layout.Handle, out _pool);

		var entries = descriptor.Entries;
		var writes = stackalloc WriteDescriptorSet[Math.Max(1, entries.Length)];
		var buffers = stackalloc DescriptorBufferInfo[Math.Max(1, entries.Length)];
		var images = stackalloc DescriptorImageInfo[Math.Max(1, entries.Length)];
		for (var i = 0; i < entries.Length; i++)
		{
			var entry = entries[i];
			var type = _typeOf(layout, entry.Binding);
			writes[i] = new WriteDescriptorSet
			{
				SType = StructureType.WriteDescriptorSet,
				DstSet = Handle,
				DstBinding = entry.Binding,
				DescriptorCount = 1,
				DescriptorType = type.ToVk(),
			};

			switch (type)
			{
				case BindingType.UniformBuffer or BindingType.StorageBuffer:
					var buffer = (VulkanBuffer)(entry.Buffer ?? throw new ArgumentException($"Binding {entry.Binding} needs a buffer."));
					buffers[i] = new DescriptorBufferInfo(buffer.Handle, entry.Offset, entry.Size == 0 ? Vk.WholeSize : entry.Size);
					writes[i].PBufferInfo = &buffers[i];
					break;
				case BindingType.Sampler:
					var sampler = (VulkanSampler)(entry.Sampler ?? throw new ArgumentException($"Binding {entry.Binding} needs a sampler."));
					images[i] = new DescriptorImageInfo { Sampler = sampler.Handle };
					writes[i].PImageInfo = &images[i];
					break;
				case BindingType.Texture:
					var view = (VulkanTextureView)(entry.TextureView ?? throw new ArgumentException($"Binding {entry.Binding} needs a texture view."));
					images[i] = new DescriptorImageInfo { ImageView = view.Handle, ImageLayout = ImageLayout.ShaderReadOnlyOptimal };
					writes[i].PImageInfo = &images[i];
					break;
			}
		}

		device.Vk.UpdateDescriptorSets(device.Handle, (uint)entries.Length, writes, 0, null);
	}

	public readonly DescriptorSet Handle;

	public IBindGroupLayout Layout { get; }

	private static BindingType _typeOf(VulkanBindGroupLayout layout, uint binding)
	{
		foreach (var entry in layout.Entries)
		{
			if (entry.Binding == binding) return entry.Type;
		}

		throw new ArgumentException($"The bind group layout has no binding {binding}.");
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		var device = _device;
		var handle = Handle;
		var pool = _pool;
		device.Defer(() => device.Vk.FreeDescriptorSets(device.Handle, pool, 1, in handle));
	}
}

internal sealed unsafe class VulkanPipelineLayout : IPipelineLayout
{
	private readonly VulkanDevice _device;
	private bool _disposed;

	public VulkanPipelineLayout(VulkanDevice device, in PipelineLayoutDescriptor descriptor)
	{
		_device = device;
		var layouts = descriptor.BindGroupLayouts;
		var handles = stackalloc DescriptorSetLayout[Math.Max(1, layouts.Length)];
		for (var i = 0; i < layouts.Length; i++) handles[i] = ((VulkanBindGroupLayout)layouts[i]).Handle;
		var info = new PipelineLayoutCreateInfo { SType = StructureType.PipelineLayoutCreateInfo, SetLayoutCount = (uint)layouts.Length, PSetLayouts = handles };
		VulkanDevice.Check(device.Vk.CreatePipelineLayout(device.Handle, in info, null, out Handle), "vkCreatePipelineLayout");
	}

	public readonly PipelineLayout Handle;

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		var device = _device;
		var handle = Handle;
		device.Defer(() => device.Vk.DestroyPipelineLayout(device.Handle, handle, null));
	}
}

internal sealed unsafe class VulkanRenderPipeline : IRenderPipeline
{
	private readonly VulkanDevice _device;
	private bool _disposed;

	public VulkanRenderPipeline(VulkanDevice device, RenderPipelineDescriptor descriptor)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		_device = device;
		Layout = (VulkanPipelineLayout)descriptor.Layout;
		var vk = device.Vk;

		// Shader stages.
		var vertexEntry = (byte*)SilkMarshal.StringToPtr(descriptor.Vertex.EntryPoint);
		var fragmentEntry = descriptor.Fragment is { } f ? (byte*)SilkMarshal.StringToPtr(f.EntryPoint) : null;
		try
		{
			var stages = stackalloc PipelineShaderStageCreateInfo[2];
			stages[0] = new PipelineShaderStageCreateInfo
			{
				SType = StructureType.PipelineShaderStageCreateInfo,
				Stage = ShaderStageFlags.VertexBit,
				Module = ((VulkanShaderModule)descriptor.Vertex.Module).Handle,
				PName = vertexEntry,
			};
			var stageCount = 1u;
			if (descriptor.Fragment is { } fragment)
			{
				stages[1] = new PipelineShaderStageCreateInfo
				{
					SType = StructureType.PipelineShaderStageCreateInfo,
					Stage = ShaderStageFlags.FragmentBit,
					Module = ((VulkanShaderModule)fragment.Module).Handle,
					PName = fragmentEntry,
				};
				stageCount = 2;
			}

			// Vertex input.
			var buffers = descriptor.Vertex.Buffers ?? [];
			var attributeCount = 0;
			foreach (var buffer in buffers) attributeCount += buffer.Attributes.Length;
			var bindings = stackalloc VertexInputBindingDescription[Math.Max(1, buffers.Length)];
			var attributes = stackalloc VertexInputAttributeDescription[Math.Max(1, attributeCount)];
			var a = 0;
			for (var i = 0; i < buffers.Length; i++)
			{
				var buffer = buffers[i];
				bindings[i] = new VertexInputBindingDescription((uint)i, buffer.ArrayStride, buffer.StepMode == VertexStepMode.Instance ? VertexInputRate.Instance : VertexInputRate.Vertex);
				foreach (var attribute in buffer.Attributes)
				{
					attributes[a++] = new VertexInputAttributeDescription(attribute.ShaderLocation, (uint)i, attribute.Format.ToVk(), attribute.Offset);
				}
			}

			var vertexInput = new PipelineVertexInputStateCreateInfo
			{
				SType = StructureType.PipelineVertexInputStateCreateInfo,
				VertexBindingDescriptionCount = (uint)buffers.Length,
				PVertexBindingDescriptions = bindings,
				VertexAttributeDescriptionCount = (uint)attributeCount,
				PVertexAttributeDescriptions = attributes,
			};

			var inputAssembly = new PipelineInputAssemblyStateCreateInfo
			{
				SType = StructureType.PipelineInputAssemblyStateCreateInfo,
				Topology = descriptor.Primitive.Topology.ToVk(),
			};

			var viewport = new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo, ViewportCount = 1, ScissorCount = 1 };

			var rasterization = new PipelineRasterizationStateCreateInfo
			{
				SType = StructureType.PipelineRasterizationStateCreateInfo,
				PolygonMode = PolygonMode.Fill,
				CullMode = descriptor.Primitive.CullMode.ToVk(),
				FrontFace = descriptor.Primitive.FrontFace.ToVk(),
				LineWidth = 1f,
			};

			var multisample = new PipelineMultisampleStateCreateInfo
			{
				SType = StructureType.PipelineMultisampleStateCreateInfo,
				RasterizationSamples = VulkanFormats.ToVkSamples(descriptor.SampleCount),
			};

			var depthStencil = new PipelineDepthStencilStateCreateInfo { SType = StructureType.PipelineDepthStencilStateCreateInfo };
			if (descriptor.DepthStencil is { } depth)
			{
				depthStencil.DepthTestEnable = true;
				depthStencil.DepthWriteEnable = depth.DepthWriteEnabled;
				depthStencil.DepthCompareOp = depth.DepthCompare.ToVk();
			}

			var targets = descriptor.Fragment?.Targets ?? [];
			var blendAttachments = stackalloc PipelineColorBlendAttachmentState[Math.Max(1, targets.Length)];
			for (var i = 0; i < targets.Length; i++)
			{
				var target = targets[i];
				blendAttachments[i] = new PipelineColorBlendAttachmentState { ColorWriteMask = target.WriteMask.ToVk() };
				if (target.Blend is { } blend)
				{
					blendAttachments[i].BlendEnable = true;
					blendAttachments[i].SrcColorBlendFactor = blend.Color.SrcFactor.ToVk();
					blendAttachments[i].DstColorBlendFactor = blend.Color.DstFactor.ToVk();
					blendAttachments[i].ColorBlendOp = blend.Color.Operation.ToVk();
					blendAttachments[i].SrcAlphaBlendFactor = blend.Alpha.SrcFactor.ToVk();
					blendAttachments[i].DstAlphaBlendFactor = blend.Alpha.DstFactor.ToVk();
					blendAttachments[i].AlphaBlendOp = blend.Alpha.Operation.ToVk();
				}
			}

			var colorBlend = new PipelineColorBlendStateCreateInfo
			{
				SType = StructureType.PipelineColorBlendStateCreateInfo,
				AttachmentCount = (uint)targets.Length,
				PAttachments = blendAttachments,
			};

			var dynamicStates = stackalloc DynamicState[] { DynamicState.Viewport, DynamicState.Scissor };
			var dynamic = new PipelineDynamicStateCreateInfo { SType = StructureType.PipelineDynamicStateCreateInfo, DynamicStateCount = 2, PDynamicStates = dynamicStates };

			// A render pass compatible with every pass into attachments of these formats (compatibility ignores load/store
			// operations and layouts).
			var key = RenderPassKey.Compatible(device, targets, descriptor.DepthStencil?.Format ?? TextureFormat.Undefined, descriptor.SampleCount);
			var renderPass = device.GetRenderPass(key);

			var info = new GraphicsPipelineCreateInfo
			{
				SType = StructureType.GraphicsPipelineCreateInfo,
				StageCount = stageCount,
				PStages = stages,
				PVertexInputState = &vertexInput,
				PInputAssemblyState = &inputAssembly,
				PViewportState = &viewport,
				PRasterizationState = &rasterization,
				PMultisampleState = &multisample,
				PDepthStencilState = &depthStencil,
				PColorBlendState = &colorBlend,
				PDynamicState = &dynamic,
				Layout = Layout.Handle,
				RenderPass = renderPass,
				Subpass = 0,
			};
			VulkanDevice.Check(vk.CreateGraphicsPipelines(device.Handle, default, 1, in info, null, out Handle), "vkCreateGraphicsPipelines");
		}
		finally
		{
			SilkMarshal.Free((nint)vertexEntry);
			if (fragmentEntry is not null) SilkMarshal.Free((nint)fragmentEntry);
		}
	}

	public readonly Pipeline Handle;

	public VulkanPipelineLayout Layout { get; }

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		var device = _device;
		var handle = Handle;
		device.Defer(() => device.Vk.DestroyPipeline(device.Handle, handle, null));
	}
}
