using System.Text;
using System.Text.RegularExpressions;

using Silk.NET.OpenGLES;

using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Graphics.GLES;

internal sealed unsafe class GlesBuffer : IBuffer
{
	private readonly GlesDevice _device;
	private bool _disposed;

	public GlesBuffer(GlesDevice device, in BufferDescriptor descriptor)
	{
		ArgumentOutOfRangeException.ThrowIfZero(descriptor.Size, nameof(descriptor));
		if ((descriptor.Usage & BufferUsage.Storage) != 0 && device.FeatureLevel < GlesFeatureLevel.Es31)
		{
			throw new NotSupportedException("Storage buffers need OpenGL ES 3.1.");
		}

		_device = device;
		Size = descriptor.Size;
		Usage = descriptor.Usage;
		GlUsage = (Usage & BufferUsage.MapRead) != 0 ? GLEnum.StreamRead
			: (Usage & BufferUsage.CopyDst) != 0 && (Usage & (BufferUsage.Uniform | BufferUsage.Vertex | BufferUsage.Index | BufferUsage.Storage)) != 0 ? GLEnum.DynamicDraw
			: GLEnum.StaticDraw;

		var gl = device.Gl;
		Handle = gl.GenBuffer();
		gl.BindBuffer(GLEnum.CopyWriteBuffer, Handle);
		gl.BufferData(GLEnum.CopyWriteBuffer, (nuint)Size, null, GlUsage);
		gl.BindBuffer(GLEnum.CopyWriteBuffer, 0);
	}

	public readonly uint Handle;

	/// <summary>The GL usage hint, reused when a whole-buffer write orphans the storage.</summary>
	public readonly GLEnum GlUsage;

	public ulong Size { get; }

	public BufferUsage Usage { get; }

	public void Read(ulong offset, Span<byte> destination)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if ((Usage & BufferUsage.MapRead) == 0) throw new InvalidOperationException("Only buffers created with BufferUsage.MapRead can be read.");
		if (offset + (ulong)destination.Length > Size) throw new ArgumentOutOfRangeException(nameof(destination));
		if (destination.IsEmpty) return;

		// Mapping for reading waits for every command that writes the buffer (the copies that filled it).
		var gl = _device.Gl;
		_device.Context.MakeCurrent();
		gl.BindBuffer(GLEnum.CopyReadBuffer, Handle);
		var mapped = gl.MapBufferRange(GLEnum.CopyReadBuffer, (nint)offset, (nuint)destination.Length, (uint)MapBufferAccessMask.ReadBit);
		if (mapped is null)
		{
			gl.BindBuffer(GLEnum.CopyReadBuffer, 0);
			throw new InvalidOperationException($"glMapBufferRange failed: {gl.GetError()}.");
		}

		new ReadOnlySpan<byte>(mapped, destination.Length).CopyTo(destination);
		gl.UnmapBuffer(GLEnum.CopyReadBuffer);
		gl.BindBuffer(GLEnum.CopyReadBuffer, 0);
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		var device = _device;
		var handle = Handle;
		device.Defer(() => device.Gl.DeleteBuffer(handle));
	}
}

internal sealed class GlesTexture : ITexture
{
	private readonly GlesDevice _device;
	private GlesTextureView? _defaultView;
	private bool _disposed;

	public GlesTexture(GlesDevice device, in TextureDescriptor descriptor)
	{
		ArgumentOutOfRangeException.ThrowIfZero(descriptor.Width, nameof(descriptor));
		ArgumentOutOfRangeException.ThrowIfZero(descriptor.Height, nameof(descriptor));
		_device = device;
		Width = descriptor.Width;
		Height = descriptor.Height;
		Format = descriptor.Format;
		Usage = descriptor.Usage;
		MipLevelCount = Math.Max(1, descriptor.MipLevelCount);
		SampleCount = Math.Max(1, descriptor.SampleCount);
		Dimension = descriptor.Dimension;
		Target = Dimension == TextureDimension.Cube ? GLEnum.TextureCubeMap : GLEnum.Texture2D;
		Gles = Format.ToGles();
		if (Dimension == TextureDimension.Cube)
		{
			if (Width != Height) throw new ArgumentException("The faces of a cube map must be square.", nameof(descriptor));
			if ((Usage & (TextureUsage.RenderAttachment | TextureUsage.CopySrc)) != 0 || SampleCount > 1)
			{
				throw new NotSupportedException("Cube maps are sampled textures only (no render attachment, copy source or multisampling).");
			}
		}

		if ((Usage & TextureUsage.RenderAttachment) != 0 && Format.IsFloat())
		{
			var renderable = Format is TextureFormat.R16Float or TextureFormat.Rgba16Float ? device.ColorBufferHalfFloat : device.ColorBufferFloat;
			if (!renderable) throw new NotSupportedException($"{Format} is not renderable on this device (needs EXT_color_buffer_float or EXT_color_buffer_half_float).");
		}

		var gl = device.Gl;
		if (SampleCount > 1)
		{
			if ((Usage & (TextureUsage.TextureBinding | TextureUsage.CopySrc | TextureUsage.CopyDst)) != 0)
			{
				throw new NotSupportedException("Multisampled textures on the GLES backend are render attachments only (no sampling or copies).");
			}

			// A multisampled renderbuffer works from ES 3.0 on.
			IsRenderbuffer = true;
			Handle = gl.GenRenderbuffer();
			gl.BindRenderbuffer(GLEnum.Renderbuffer, Handle);
			gl.RenderbufferStorageMultisample(GLEnum.Renderbuffer, SampleCount, Gles.Internal, Width, Height);
			gl.BindRenderbuffer(GLEnum.Renderbuffer, 0);
			return;
		}

		Handle = gl.GenTexture();
		gl.BindTexture(Target, Handle);
		gl.TexStorage2D(Target, MipLevelCount, Gles.Internal, Width, Height);
		gl.TexParameter(Target, GLEnum.TextureBaseLevel, 0);
		gl.TexParameter(Target, GLEnum.TextureMaxLevel, (int)MipLevelCount - 1);
		gl.BindTexture(Target, 0);
		AppliedMips = (0, MipLevelCount);
	}

	public readonly uint Handle;

	/// <summary>The GL texture target: <c>GL_TEXTURE_2D</c>, or <c>GL_TEXTURE_CUBE_MAP</c> for a cube map.</summary>
	public readonly GLEnum Target;

	public TextureDimension Dimension { get; }

	public readonly bool IsRenderbuffer;

	public readonly GlesTextureFormat Gles;

	/// <summary>The mip range last applied through <c>GL_TEXTURE_BASE_LEVEL</c> and <c>GL_TEXTURE_MAX_LEVEL</c> (views of some levels).</summary>
	public (uint Base, uint Count) AppliedMips;

	public uint Width { get; }

	public uint Height { get; }

	public TextureFormat Format { get; }

	public TextureUsage Usage { get; }

	public uint MipLevelCount { get; }

	public uint SampleCount { get; }

	public bool IsDisposed => _disposed;

	public ITextureView DefaultView => _defaultView ??= new GlesTextureView(this, new TextureViewDescriptor());

	public ITextureView CreateView(in TextureViewDescriptor descriptor) => new GlesTextureView(this, descriptor);

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		var device = _device;
		var handle = Handle;
		var renderbuffer = IsRenderbuffer;
		device.Defer(() =>
		{
			device.ForgetAttachment(handle, renderbuffer);
			if (renderbuffer) device.Gl.DeleteRenderbuffer(handle);
			else device.Gl.DeleteTexture(handle);
		});
	}
}

internal sealed class GlesTextureView : ITextureView
{
	public GlesTextureView(GlesTexture texture, in TextureViewDescriptor descriptor)
	{
		TextureImpl = texture;
		if (descriptor.BaseMipLevel >= texture.MipLevelCount) throw new ArgumentOutOfRangeException(nameof(descriptor), "The view's base mip level is past the texture's last level.");
		BaseMip = descriptor.BaseMipLevel;
		MipCount = descriptor.MipLevelCount == 0 ? texture.MipLevelCount - BaseMip : Math.Min(descriptor.MipLevelCount, texture.MipLevelCount - BaseMip);
	}

	public GlesTexture TextureImpl { get; }

	public uint BaseMip { get; }

	public uint MipCount { get; }

	public ITexture Texture => TextureImpl;

	public TextureFormat Format => TextureImpl.Format;

	/// <summary>The attachment key for a render pass into this view (its base level).</summary>
	public FramebufferAttachmentKey Attachment => new(TextureImpl.Handle, BaseMip, TextureImpl.IsRenderbuffer);

	/// <summary>Nothing to release: GL has no view objects (views are mip ranges applied at bind time).</summary>
	public void Dispose() { }
}

internal sealed class GlesSampler : ISampler
{
	private readonly GlesDevice _device;
	private bool _disposed;

	public GlesSampler(GlesDevice device, in SamplerDescriptor descriptor)
	{
		_device = device;
		var gl = device.Gl;
		Handle = gl.GenSampler();
		gl.SamplerParameter(Handle, GLEnum.TextureMagFilter, (int)descriptor.MagFilter.ToGlesMag());
		gl.SamplerParameter(Handle, GLEnum.TextureMinFilter, (int)GlesFormats.ToGlesMin(descriptor.MinFilter, descriptor.MipmapFilter));
		gl.SamplerParameter(Handle, GLEnum.TextureWrapS, (int)descriptor.AddressModeU.ToGles());
		gl.SamplerParameter(Handle, GLEnum.TextureWrapT, (int)descriptor.AddressModeV.ToGles());
		gl.SamplerParameter(Handle, GLEnum.TextureWrapR, (int)descriptor.AddressModeW.ToGles());
		gl.SamplerParameter(Handle, GLEnum.TextureMinLod, descriptor.LodMinClamp);
		gl.SamplerParameter(Handle, GLEnum.TextureMaxLod, descriptor.LodMaxClamp);
		if (descriptor.Compare is { } compare)
		{
			gl.SamplerParameter(Handle, GLEnum.TextureCompareMode, (int)GLEnum.CompareRefToTexture);
			gl.SamplerParameter(Handle, GLEnum.TextureCompareFunc, (int)compare.ToGles());
		}
	}

	public readonly uint Handle;

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		var device = _device;
		var handle = Handle;
		device.Defer(() => device.Gl.DeleteSampler(handle));
	}
}

internal sealed partial class GlesShaderModule : IShaderModule
{
	private readonly GlesDevice _device;
	private bool _disposed;

	public GlesShaderModule(GlesDevice device, in ShaderModuleDescriptor descriptor)
	{
		if (descriptor.Language != ShaderLanguage.GlslEs) throw new NotSupportedException($"The GLES backend takes GLSL ES shader modules, not {descriptor.Language}.");
		if (descriptor.Stage is not (ShaderStage.Vertex or ShaderStage.Fragment)) throw new ArgumentException("A shader module is a vertex or a fragment stage.", nameof(descriptor));
		_device = device;
		Stage = descriptor.Stage;
		Label = descriptor.Label;

		var source = Encoding.UTF8.GetString(descriptor.Code.Span);
		if (!source.TrimStart().StartsWith("#version", StringComparison.Ordinal)) throw new ArgumentException("The code is not GLSL ES source (no #version line).", nameof(descriptor));
		Bindings = GlesBindings.ParseTable(source);
		Source = device.FeatureLevel == GlesFeatureLevel.Es30 ? ToGlslEs300(source, Stage) : source;
		Handle = Compile(device.Gl, Source, Stage, Label);
	}

	public readonly uint Handle;

	/// <summary>The source compiled (rewritten to GLSL ES 3.00 at the ES 3.0 feature level).</summary>
	public string Source { get; }

	/// <summary>The flattening table of the source (empty for sources not produced by <c>Ion.Shaders</c>).</summary>
	public IReadOnlyList<GlesBindingEntry> Bindings { get; }

	public string? Label { get; }

	public ShaderStage Stage { get; }

	internal static uint Compile(GL gl, string source, ShaderStage stage, string? label)
	{
		var shader = gl.CreateShader(stage == ShaderStage.Vertex ? GLEnum.VertexShader : GLEnum.FragmentShader);
		gl.ShaderSource(shader, source);
		gl.CompileShader(shader);
		if (gl.GetShader(shader, GLEnum.CompileStatus) == 0)
		{
			var log = gl.GetShaderInfoLog(shader);
			gl.DeleteShader(shader);
			throw new InvalidOperationException($"GLSL ES {stage.ToString().ToLowerInvariant()} shader {label ?? "(unnamed)"} failed to compile: {log}");
		}

		return shader;
	}

	/// <summary>
	/// Rewrites GLSL ES 3.10 from <c>Ion.Shaders</c> to GLSL ES 3.00 for the ES 3.0 fallback: the version, and no binding
	/// qualifiers (slots are assigned by name from the flattening table) nor location qualifiers on varyings (the
	/// translation names them by location, so the stages link by name). Sources that use ES 3.1 features fail to compile.
	/// </summary>
	public static string ToGlslEs300(string source, ShaderStage stage)
	{
		var result = VersionLine().Replace(source, "#version 300 es", 1);
		result = BindingOnly().Replace(result, string.Empty);
		result = BindingFirst().Replace(result, string.Empty);
		result = stage == ShaderStage.Vertex ? VaryingOut().Replace(result, "out ") : VaryingIn().Replace(result, "in ");
		return result;
	}

	[GeneratedRegex(@"^#version 310 es", RegexOptions.Multiline)]
	private static partial Regex VersionLine();

	[GeneratedRegex(@"layout\(binding = \d+\)\s*")]
	private static partial Regex BindingOnly();

	[GeneratedRegex(@"binding = \d+,\s*")]
	private static partial Regex BindingFirst();

	[GeneratedRegex(@"layout\(location = \d+\) out ")]
	private static partial Regex VaryingOut();

	[GeneratedRegex(@"layout\(location = \d+\) in ")]
	private static partial Regex VaryingIn();

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		var device = _device;
		var handle = Handle;
		device.Defer(() => device.Gl.DeleteShader(handle));
	}
}

internal sealed class GlesBindGroupLayout(in BindGroupLayoutDescriptor descriptor) : IBindGroupLayout
{
	public IReadOnlyList<BindGroupLayoutEntry> Entries { get; } = descriptor.Entries.ToArray();

	public BindingType TypeOf(uint binding)
	{
		foreach (var entry in Entries)
		{
			if (entry.Binding == binding) return entry.Type;
		}

		throw new ArgumentException($"The bind group layout has no binding {binding}.");
	}

	public void Dispose() { }
}

/// <summary>A bound buffer range.</summary>
internal readonly record struct GlesBufferBinding(uint Binding, GlesBuffer Buffer, ulong Offset, ulong Size);

/// <summary>A bound texture view.</summary>
internal readonly record struct GlesTextureBinding(uint Binding, GlesTextureView View);

internal sealed class GlesBindGroup : IBindGroup
{
	public GlesBindGroup(GlesDevice device, in BindGroupDescriptor descriptor)
	{
		var layout = (GlesBindGroupLayout)descriptor.Layout;
		Layout = layout;
		var uniforms = new List<GlesBufferBinding>();
		var storage = new List<GlesBufferBinding>();
		var textures = new List<GlesTextureBinding>();
		var samplers = new List<(uint Binding, GlesSampler Sampler)>();
		foreach (var entry in descriptor.Entries)
		{
			if (entry.Binding >= GlesBindings.BindingsPerGroup) throw new ArgumentOutOfRangeException(nameof(descriptor), entry.Binding, $"The GLES backend supports binding numbers 0 to {GlesBindings.BindingsPerGroup - 1}.");
			switch (layout.TypeOf(entry.Binding))
			{
				case BindingType.UniformBuffer:
				case BindingType.StorageBuffer:
					var buffer = (GlesBuffer)(entry.Buffer ?? throw new ArgumentException($"Binding {entry.Binding} needs a buffer."));
					var size = entry.Size == 0 ? buffer.Size - entry.Offset : entry.Size;
					if (entry.Offset % device.Limits.MinUniformBufferOffsetAlignment != 0 && layout.TypeOf(entry.Binding) == BindingType.UniformBuffer)
					{
						throw new ArgumentException($"Uniform buffer offset {entry.Offset} of binding {entry.Binding} is not a multiple of MinUniformBufferOffsetAlignment ({device.Limits.MinUniformBufferOffsetAlignment}).");
					}

					(layout.TypeOf(entry.Binding) == BindingType.UniformBuffer ? uniforms : storage).Add(new GlesBufferBinding(entry.Binding, buffer, entry.Offset, size));
					break;
				case BindingType.Sampler:
					samplers.Add((entry.Binding, (GlesSampler)(entry.Sampler ?? throw new ArgumentException($"Binding {entry.Binding} needs a sampler."))));
					break;
				case BindingType.Texture:
					textures.Add(new GlesTextureBinding(entry.Binding, (GlesTextureView)(entry.TextureView ?? throw new ArgumentException($"Binding {entry.Binding} needs a texture view."))));
					break;
			}
		}

		Uniforms = [.. uniforms];
		Storage = [.. storage];
		Textures = [.. textures];
		Samplers = [.. samplers];
	}

	public IBindGroupLayout Layout { get; }

	public GlesBufferBinding[] Uniforms { get; }

	public GlesBufferBinding[] Storage { get; }

	public GlesTextureBinding[] Textures { get; }

	public (uint Binding, GlesSampler Sampler)[] Samplers { get; }

	public GlesSampler? SamplerAt(uint binding)
	{
		foreach (var (b, sampler) in Samplers)
		{
			if (b == binding) return sampler;
		}

		return null;
	}

	public void Dispose() { }
}

internal sealed class GlesPipelineLayout(in PipelineLayoutDescriptor descriptor) : IPipelineLayout
{
	public IBindGroupLayout[] BindGroupLayouts { get; } = descriptor.BindGroupLayouts.ToArray();

	public void Dispose() { }
}

/// <summary>A texture unit and the sampler binding whose sampler object is bound to it.</summary>
internal readonly record struct GlesSamplerPair(int Unit, uint SamplerGroup, uint SamplerBinding);

/// <summary>One vertex attribute of a pipeline, resolved for GL.</summary>
internal readonly record struct GlesAttribute(uint Location, uint Slot, uint Offset, GlesVertexFormat Format);

internal sealed class GlesRenderPipeline : IRenderPipeline
{
	private const string DepthOnlyFragmentBody = "void main() { }\n";

	private readonly GlesDevice _device;
	private bool _disposed;

	public GlesRenderPipeline(GlesDevice device, RenderPipelineDescriptor descriptor)
	{
		ArgumentNullException.ThrowIfNull(descriptor);
		_device = device;
		var gl = device.Gl;
		Label = descriptor.Label;

		var vertex = (GlesShaderModule)descriptor.Vertex.Module;
		var fragment = descriptor.Fragment is { } f ? (GlesShaderModule)f.Module : null;
		if (descriptor.Vertex.EntryPoint != "main" || (descriptor.Fragment is { } fs && fs.EntryPoint != "main"))
		{
			throw new NotSupportedException("GLSL ES shaders have a single entry point, main.");
		}

		// Program.
		Program = gl.CreateProgram();
		gl.AttachShader(Program, vertex.Handle);
		uint depthOnly = 0;
		if (fragment is not null) gl.AttachShader(Program, fragment.Handle);
		else
		{
			// GLES programs need a fragment stage; a depth-only pipeline gets an empty one.
			// Stages must declare the same GLSL ES version to link: take the vertex stage's version line.
			var version = vertex.Source.TrimStart();
			version = version[..version.IndexOf('\n')].Trim();
			depthOnly = GlesShaderModule.Compile(gl, version + "\n" + DepthOnlyFragmentBody, ShaderStage.Fragment, "depth-only");
			gl.AttachShader(Program, depthOnly);
		}

		gl.LinkProgram(Program);
		var linked = gl.GetProgram(Program, GLEnum.LinkStatus) != 0;
		gl.DetachShader(Program, vertex.Handle);
		if (fragment is not null) gl.DetachShader(Program, fragment.Handle);
		if (depthOnly != 0)
		{
			gl.DetachShader(Program, depthOnly);
			gl.DeleteShader(depthOnly);
		}

		if (!linked)
		{
			var log = gl.GetProgramInfoLog(Program);
			gl.DeleteProgram(Program);
			throw new InvalidOperationException($"Pipeline {Label ?? "(unnamed)"}: the GLSL ES program failed to link: {log}");
		}

		// Bindings: validate the flattened slots and (ES 3.0, no binding qualifiers) assign them by name.
		var bindings = new List<GlesBindingEntry>(vertex.Bindings);
		if (fragment is not null) bindings.AddRange(fragment.Bindings);
		var pairs = new List<GlesSamplerPair>();
		if (device.FeatureLevel == GlesFeatureLevel.Es30) gl.UseProgram(Program);
		foreach (var entry in bindings)
		{
			switch (entry.Kind)
			{
				case GlesBindingKind.UniformBlock:
					if (entry.Slot >= device.MaxUniformBufferBindings) throw new NotSupportedException($"Pipeline {Label}: uniform block {entry.Name} (set {entry.Group}, binding {entry.Binding}) flattens to binding point {entry.Slot}, but the device has {device.MaxUniformBufferBindings}.");
					if (device.FeatureLevel == GlesFeatureLevel.Es30)
					{
						var index = gl.GetUniformBlockIndex(Program, entry.Name);
						if (index != uint.MaxValue) gl.UniformBlockBinding(Program, index, (uint)entry.Slot);
					}

					break;
				case GlesBindingKind.StorageBlock:
					if (entry.Slot >= device.MaxStorageBufferBindings) throw new NotSupportedException($"Pipeline {Label}: storage block {entry.Name} (set {entry.Group}, binding {entry.Binding}) flattens to binding point {entry.Slot}, but the device has {device.MaxStorageBufferBindings}.");
					break;
				case GlesBindingKind.Sampler:
					if (entry.Slot >= device.MaxTextureUnits) throw new NotSupportedException($"Pipeline {Label}: sampler {entry.Name} (set {entry.Group}, binding {entry.Binding}) flattens to texture unit {entry.Slot}, but the device has {device.MaxTextureUnits}.");
					if (device.FeatureLevel == GlesFeatureLevel.Es30)
					{
						var location = gl.GetUniformLocation(Program, entry.Name);
						if (location >= 0) gl.Uniform1(location, entry.Slot);
					}

					if (!pairs.Exists(p => p.Unit == entry.Slot)) pairs.Add(new GlesSamplerPair(entry.Slot, entry.SamplerGroup, entry.SamplerBinding));
					break;
			}
		}

		if (device.FeatureLevel == GlesFeatureLevel.Es30) gl.UseProgram(0);
		SamplerPairs = [.. pairs];
		HasBindingTable = bindings.Count > 0;
		BaseInstanceLocation = gl.GetUniformLocation(Program, "SPIRV_Cross_BaseInstance");

		// Vertex input: a vertex array object with the attribute formats (and, from ES 3.1, their buffer bindings).
		var buffers = descriptor.Vertex.Buffers ?? [];
		Strides = new uint[buffers.Length];
		StepModes = new VertexStepMode[buffers.Length];
		var attributes = new List<GlesAttribute>();
		VertexArray = gl.GenVertexArray();
		gl.BindVertexArray(VertexArray);
		for (var slot = 0; slot < buffers.Length; slot++)
		{
			var buffer = buffers[slot];
			Strides[slot] = buffer.ArrayStride;
			StepModes[slot] = buffer.StepMode;
			var divisor = buffer.StepMode == VertexStepMode.Instance ? 1u : 0u;
			if (device.VertexAttribBinding) gl.VertexBindingDivisor((uint)slot, divisor);
			foreach (var attribute in buffer.Attributes)
			{
				var format = attribute.Format.ToGles();
				gl.EnableVertexAttribArray(attribute.ShaderLocation);
				if (device.VertexAttribBinding)
				{
					if (format.Integer) gl.VertexAttribIFormat(attribute.ShaderLocation, format.Components, format.Type, attribute.Offset);
					else gl.VertexAttribFormat(attribute.ShaderLocation, format.Components, format.Type, format.Normalized, attribute.Offset);
					gl.VertexAttribBinding(attribute.ShaderLocation, (uint)slot);
				}
				else
				{
					gl.VertexAttribDivisor(attribute.ShaderLocation, divisor);
				}

				attributes.Add(new GlesAttribute(attribute.ShaderLocation, (uint)slot, attribute.Offset, format));
			}
		}

		gl.BindVertexArray(0);
		device.Executor.InvalidateVertexArray();
		Attributes = [.. attributes];

		// Fixed-function state.
		Topology = descriptor.Primitive.Topology.ToGles();
		CullMode = descriptor.Primitive.CullMode;
		// Clip-space y is negated by the shader translation, which mirrors the winding: WebGPU's counter-clockwise front
		// faces are clockwise in GL.
		FrontFace = descriptor.Primitive.FrontFace == Rhi.FrontFace.Ccw ? GLEnum.CW : GLEnum.Ccw;
		Depth = descriptor.DepthStencil;
		var targets = descriptor.Fragment?.Targets ?? [];
		if (targets.Length > FramebufferKey.MaxColorAttachments) throw new ArgumentException($"At most {FramebufferKey.MaxColorAttachments} color targets.", nameof(descriptor));
		if (targets.Length > 1)
		{
			for (var i = 1; i < targets.Length; i++)
			{
				if (targets[i].Blend != targets[0].Blend || targets[i].WriteMask != targets[0].WriteMask)
				{
					device.LogWarning($"Pipeline {Label}: OpenGL ES 3.x applies one blend state and write mask to every color target; target 0's is used for all.");
					break;
				}
			}
		}

		Blend = targets.Length > 0 ? targets[0].Blend : null;
		WriteMask = targets.Length > 0 ? targets[0].WriteMask : ColorWriteMask.None;
		if (descriptor.SampleCount > 1) device.LogWarning($"Pipeline {Label}: multisampling follows the attachment on GLES; SampleCount is ignored.");
	}

	public readonly uint Program;
	public readonly uint VertexArray;
	public readonly uint[] Strides;
	public readonly VertexStepMode[] StepModes;
	public readonly GlesAttribute[] Attributes;
	public readonly GlesSamplerPair[] SamplerPairs;
	public readonly bool HasBindingTable;
	public readonly int BaseInstanceLocation;
	public readonly GLEnum Topology;
	public readonly CullMode CullMode;
	public readonly GLEnum FrontFace;
	public readonly DepthStencilState? Depth;
	public readonly BlendState? Blend;
	public readonly ColorWriteMask WriteMask;

	public string? Label { get; }

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		var device = _device;
		var program = Program;
		var vao = VertexArray;
		device.Defer(() =>
		{
			device.Gl.DeleteProgram(program);
			device.Gl.DeleteVertexArray(vao);
			device.Executor.InvalidateVertexArray();
		});
	}
}
