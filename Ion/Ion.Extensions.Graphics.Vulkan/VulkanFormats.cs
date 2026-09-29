using Silk.NET.Vulkan;

using Ion.Extensions.Graphics.Rhi;

using RhiBlendFactor = Ion.Extensions.Graphics.Rhi.BlendFactor;
using RhiCompare = Ion.Extensions.Graphics.Rhi.CompareFunction;
using RhiCullMode = Ion.Extensions.Graphics.Rhi.CullMode;
using RhiFrontFace = Ion.Extensions.Graphics.Rhi.FrontFace;
using RhiFilter = Ion.Extensions.Graphics.Rhi.FilterMode;
using RhiIndexFormat = Ion.Extensions.Graphics.Rhi.IndexFormat;
using RhiPresentMode = Ion.Extensions.Graphics.Rhi.PresentMode;
using VkBlendFactor = Silk.NET.Vulkan.BlendFactor;
using VkCompareOp = Silk.NET.Vulkan.CompareOp;
using VkCullMode = Silk.NET.Vulkan.CullModeFlags;
using VkFrontFace = Silk.NET.Vulkan.FrontFace;
using VkIndexType = Silk.NET.Vulkan.IndexType;

namespace Ion.Extensions.Graphics.Vulkan;

/// <summary>
/// Conversions from RHI enums to Vulkan.
/// </summary>
internal static class VulkanFormats
{
	public static Format ToVk(this TextureFormat format) => format switch
	{
		TextureFormat.R8Unorm => Format.R8Unorm,
		TextureFormat.Rg8Unorm => Format.R8G8Unorm,
		TextureFormat.Rgba8Unorm => Format.R8G8B8A8Unorm,
		TextureFormat.Rgba8UnormSrgb => Format.R8G8B8A8Srgb,
		TextureFormat.Bgra8Unorm => Format.B8G8R8A8Unorm,
		TextureFormat.Bgra8UnormSrgb => Format.B8G8R8A8Srgb,
		TextureFormat.R16Float => Format.R16Sfloat,
		TextureFormat.Rgba16Float => Format.R16G16B16A16Sfloat,
		TextureFormat.R32Float => Format.R32Sfloat,
		TextureFormat.Rgba32Float => Format.R32G32B32A32Sfloat,
		TextureFormat.Depth16Unorm => Format.D16Unorm,
		TextureFormat.Depth24PlusStencil8 => Format.D24UnormS8Uint,
		TextureFormat.Depth32Float => Format.D32Sfloat,
		_ => throw new NotSupportedException($"Texture format {format} is not supported by the Vulkan backend."),
	};

	public static TextureFormat FromVk(Format format) => format switch
	{
		Format.R8G8B8A8Unorm => TextureFormat.Rgba8Unorm,
		Format.R8G8B8A8Srgb => TextureFormat.Rgba8UnormSrgb,
		Format.B8G8R8A8Unorm => TextureFormat.Bgra8Unorm,
		Format.B8G8R8A8Srgb => TextureFormat.Bgra8UnormSrgb,
		Format.R16G16B16A16Sfloat => TextureFormat.Rgba16Float,
		_ => TextureFormat.Undefined,
	};

	public static ImageAspectFlags Aspect(this TextureFormat format) => format switch
	{
		TextureFormat.Depth24PlusStencil8 => ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit,
		TextureFormat.Depth16Unorm or TextureFormat.Depth32Float => ImageAspectFlags.DepthBit,
		_ => ImageAspectFlags.ColorBit,
	};

	public static BufferUsageFlags ToVk(this BufferUsage usage)
	{
		BufferUsageFlags flags = 0;
		// Every buffer can be the target of IQueue.WriteBuffer (a staging copy) and read back.
		flags |= BufferUsageFlags.TransferDstBit | BufferUsageFlags.TransferSrcBit;
		if ((usage & BufferUsage.Index) != 0) flags |= BufferUsageFlags.IndexBufferBit;
		if ((usage & BufferUsage.Vertex) != 0) flags |= BufferUsageFlags.VertexBufferBit;
		if ((usage & BufferUsage.Uniform) != 0) flags |= BufferUsageFlags.UniformBufferBit;
		if ((usage & BufferUsage.Storage) != 0) flags |= BufferUsageFlags.StorageBufferBit;
		return flags;
	}

	public static ImageUsageFlags ToVk(this TextureUsage usage, TextureFormat format)
	{
		ImageUsageFlags flags = 0;
		if ((usage & TextureUsage.CopySrc) != 0) flags |= ImageUsageFlags.TransferSrcBit;
		if ((usage & TextureUsage.CopyDst) != 0) flags |= ImageUsageFlags.TransferDstBit;
		if ((usage & TextureUsage.TextureBinding) != 0) flags |= ImageUsageFlags.SampledBit;
		if ((usage & TextureUsage.RenderAttachment) != 0)
			flags |= format.IsDepth() ? ImageUsageFlags.DepthStencilAttachmentBit : ImageUsageFlags.ColorAttachmentBit;
		return flags;
	}

	public static DescriptorType ToVk(this BindingType type) => type switch
	{
		BindingType.UniformBuffer => DescriptorType.UniformBuffer,
		BindingType.StorageBuffer => DescriptorType.StorageBuffer,
		BindingType.Sampler => DescriptorType.Sampler,
		BindingType.Texture => DescriptorType.SampledImage,
		_ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
	};

	public static ShaderStageFlags ToVk(this ShaderStage stage)
	{
		ShaderStageFlags flags = 0;
		if ((stage & ShaderStage.Vertex) != 0) flags |= ShaderStageFlags.VertexBit;
		if ((stage & ShaderStage.Fragment) != 0) flags |= ShaderStageFlags.FragmentBit;
		return flags;
	}

	public static SamplerAddressMode ToVk(this AddressMode mode) => mode switch
	{
		AddressMode.Repeat => SamplerAddressMode.Repeat,
		AddressMode.MirrorRepeat => SamplerAddressMode.MirroredRepeat,
		_ => SamplerAddressMode.ClampToEdge,
	};

	public static Filter ToVk(this RhiFilter filter) => filter == RhiFilter.Linear ? Filter.Linear : Filter.Nearest;

	public static SamplerMipmapMode ToVkMipmap(this RhiFilter filter) => filter == RhiFilter.Linear ? SamplerMipmapMode.Linear : SamplerMipmapMode.Nearest;

	public static VkCompareOp ToVk(this RhiCompare compare) => compare switch
	{
		RhiCompare.Never => VkCompareOp.Never,
		RhiCompare.Less => VkCompareOp.Less,
		RhiCompare.Equal => VkCompareOp.Equal,
		RhiCompare.LessEqual => VkCompareOp.LessOrEqual,
		RhiCompare.Greater => VkCompareOp.Greater,
		RhiCompare.NotEqual => VkCompareOp.NotEqual,
		RhiCompare.GreaterEqual => VkCompareOp.GreaterOrEqual,
		_ => VkCompareOp.Always,
	};

	public static Format ToVk(this VertexFormat format) => format switch
	{
		VertexFormat.Float32 => Format.R32Sfloat,
		VertexFormat.Float32x2 => Format.R32G32Sfloat,
		VertexFormat.Float32x3 => Format.R32G32B32Sfloat,
		VertexFormat.Float32x4 => Format.R32G32B32A32Sfloat,
		VertexFormat.Unorm8x4 => Format.R8G8B8A8Unorm,
		VertexFormat.Uint8x4 => Format.R8G8B8A8Uint,
		VertexFormat.Uint32 => Format.R32Uint,
		VertexFormat.Sint32 => Format.R32Sint,
		VertexFormat.Unorm16x2 => Format.R16G16Unorm,
		VertexFormat.Unorm16x4 => Format.R16G16B16A16Unorm,
		_ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
	};

	public static Silk.NET.Vulkan.PrimitiveTopology ToVk(this Rhi.PrimitiveTopology topology) => topology switch
	{
		Rhi.PrimitiveTopology.PointList => Silk.NET.Vulkan.PrimitiveTopology.PointList,
		Rhi.PrimitiveTopology.LineList => Silk.NET.Vulkan.PrimitiveTopology.LineList,
		Rhi.PrimitiveTopology.LineStrip => Silk.NET.Vulkan.PrimitiveTopology.LineStrip,
		Rhi.PrimitiveTopology.TriangleStrip => Silk.NET.Vulkan.PrimitiveTopology.TriangleStrip,
		_ => Silk.NET.Vulkan.PrimitiveTopology.TriangleList,
	};

	public static VkCullMode ToVk(this RhiCullMode mode) => mode switch
	{
		RhiCullMode.Front => VkCullMode.FrontBit,
		RhiCullMode.Back => VkCullMode.BackBit,
		_ => VkCullMode.None,
	};

	// The backend flips the viewport (negative height) so clip space is y-up as in WebGPU and GL; that also flips the
	// winding seen in framebuffer space, so the RHI's counter-clockwise maps to Vulkan's counter-clockwise unchanged.
	public static VkFrontFace ToVk(this RhiFrontFace face) => face == RhiFrontFace.Cw ? VkFrontFace.Clockwise : VkFrontFace.CounterClockwise;

	public static VkBlendFactor ToVk(this RhiBlendFactor factor) => factor switch
	{
		RhiBlendFactor.Zero => VkBlendFactor.Zero,
		RhiBlendFactor.One => VkBlendFactor.One,
		RhiBlendFactor.Src => VkBlendFactor.SrcColor,
		RhiBlendFactor.OneMinusSrc => VkBlendFactor.OneMinusSrcColor,
		RhiBlendFactor.SrcAlpha => VkBlendFactor.SrcAlpha,
		RhiBlendFactor.OneMinusSrcAlpha => VkBlendFactor.OneMinusSrcAlpha,
		RhiBlendFactor.Dst => VkBlendFactor.DstColor,
		RhiBlendFactor.OneMinusDst => VkBlendFactor.OneMinusDstColor,
		RhiBlendFactor.DstAlpha => VkBlendFactor.DstAlpha,
		RhiBlendFactor.OneMinusDstAlpha => VkBlendFactor.OneMinusDstAlpha,
		RhiBlendFactor.SrcAlphaSaturated => VkBlendFactor.SrcAlphaSaturate,
		_ => VkBlendFactor.One,
	};

	public static BlendOp ToVk(this BlendOperation op) => op switch
	{
		BlendOperation.Subtract => BlendOp.Subtract,
		BlendOperation.ReverseSubtract => BlendOp.ReverseSubtract,
		BlendOperation.Min => BlendOp.Min,
		BlendOperation.Max => BlendOp.Max,
		_ => BlendOp.Add,
	};

	public static ColorComponentFlags ToVk(this ColorWriteMask mask)
	{
		ColorComponentFlags flags = 0;
		if ((mask & ColorWriteMask.Red) != 0) flags |= ColorComponentFlags.RBit;
		if ((mask & ColorWriteMask.Green) != 0) flags |= ColorComponentFlags.GBit;
		if ((mask & ColorWriteMask.Blue) != 0) flags |= ColorComponentFlags.BBit;
		if ((mask & ColorWriteMask.Alpha) != 0) flags |= ColorComponentFlags.ABit;
		return flags;
	}

	public static VkIndexType ToVk(this RhiIndexFormat format) => format == RhiIndexFormat.Uint16 ? VkIndexType.Uint16 : VkIndexType.Uint32;

	public static PresentModeKHR ToVk(this RhiPresentMode mode) => mode switch
	{
		RhiPresentMode.Mailbox => PresentModeKHR.MailboxKhr,
		RhiPresentMode.Immediate => PresentModeKHR.ImmediateKhr,
		_ => PresentModeKHR.FifoKhr,
	};

	public static SampleCountFlags ToVkSamples(uint count) => count switch
	{
		2 => SampleCountFlags.Count2Bit,
		4 => SampleCountFlags.Count4Bit,
		8 => SampleCountFlags.Count8Bit,
		_ => SampleCountFlags.Count1Bit,
	};
}
