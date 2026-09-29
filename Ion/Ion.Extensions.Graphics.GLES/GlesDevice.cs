using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Silk.NET.OpenGLES;

using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Graphics.GLES;

/// <summary>
/// The OpenGL ES feature level the backend uses: the lower of what the context offers and
/// <see cref="GlesDeviceOptions.MaxFeatureLevel"/> (which tests use to exercise the fallbacks on a newer driver).
/// </summary>
public enum GlesFeatureLevel
{
	/// <summary>
	/// OpenGL ES 3.0: GLSL ES 3.00 (the 3.10 sources are rewritten at load: no binding or varying location qualifiers, so
	/// slots are assigned by name from the flattening table), vertex attribute pointers re-specified per draw, no storage
	/// buffers, base vertex emulated through vertex buffer offsets.
	/// </summary>
	Es30,
	/// <summary>OpenGL ES 3.1: binding qualifiers in shaders, separate vertex attribute formats and bindings, storage buffers. Base vertex emulated.</summary>
	Es31,
	/// <summary>OpenGL ES 3.2: adds native base vertex draws.</summary>
	Es32,
}

/// <summary>
/// Options for <see cref="GlesDevice.Create"/>.
/// </summary>
public sealed class GlesDeviceOptions
{
	/// <summary>The context to render with: a window's (<see cref="SilkGlesContext"/>) or null for a headless EGL context the device creates and owns.</summary>
	public IGlesContext? Context { get; init; }

	/// <summary>Frames the CPU may record ahead of the GPU; clamped to [1, 3].</summary>
	public int FramesInFlight { get; init; } = 2;

	/// <summary>Check <c>glGetError</c> after every submission, upload and resource creation, and log errors.</summary>
	public bool Validation { get; init; }

	/// <summary>The highest feature level to use (lower it to exercise the ES 3.0 or 3.1 paths on a newer driver).</summary>
	public GlesFeatureLevel MaxFeatureLevel { get; init; } = GlesFeatureLevel.Es32;
}

/// <summary>
/// The OpenGL ES 3.1 implementation of <see cref="IGraphicsDevice"/> on <c>Silk.NET.OpenGLES</c>, with ES 3.0 fallbacks.
/// </summary>
/// <remarks>
/// <para>
/// Recording: command encoders record into a command list that <see cref="IQueue.Submit(ICommandBuffer)"/> replays on the
/// context, so a <see cref="IQueue.WriteBuffer"/> made before a submission is seen by it and one made after is not, exactly
/// as on Vulkan (uploads execute at the call, in submission order).
/// </para>
/// <para>
/// Frames in flight: <see cref="EndFrame"/> inserts a fence sync for the frame slot, advances <see cref="FrameIndex"/> and
/// waits (<c>glClientWaitSync</c>) for the fence of the slot it moves to, so the CPU runs at most
/// <see cref="FramesInFlight"/> frames ahead and per-frame ring buffers indexed by <see cref="FrameIndex"/> are never
/// overwritten while the GPU reads them. Disposed objects are deleted when their frame slot comes round again.
/// </para>
/// <para>
/// Clip space is WebGPU's: the shader translation negates y and remaps depth (see <c>Ion.Shaders</c>), so everything renders
/// upside down in GL terms, texture row 0 is the top row as in WebGPU and Vulkan, viewports and scissors take top-left
/// origins as given, front faces are inverted, and the window surface is an offscreen target blitted to the default
/// framebuffer with a vertical flip at present.
/// </para>
/// <para>
/// Bind groups: <c>(group, binding)</c> pairs are flattened by <see cref="GlesBindings"/> to uniform block binding points and
/// texture units; the sampler object bound to a unit is the one the shader combined with that texture (from the flattening
/// table), or the group's only sampler.
/// </para>
/// </remarks>
public sealed unsafe class GlesDevice : IGraphicsDevice
{
	private readonly ILogger _logger;
	private readonly FrameSlot[] _frames;
	private readonly Dictionary<FramebufferKey, uint> _framebuffers = [];
	private readonly bool _ownsContext;
	private bool _disposed;

	internal readonly GL Gl;
	internal readonly GlesQueue QueueImpl;
	internal readonly GlesExecutor Executor;
	internal readonly GlesSurface? SurfaceImpl;

	private GlesDevice(IGlesContext context, bool ownsContext, GlesDeviceOptions options, ILogger logger)
	{
		_logger = logger;
		Context = context;
		_ownsContext = ownsContext;
		context.MakeCurrent();
		Gl = GL.GetApi(context.GetProcAddress);
		Validation = options.Validation;

		var major = Gl.GetInteger(GLEnum.MajorVersion);
		var minor = Gl.GetInteger(GLEnum.MinorVersion);
		Version = new Version(major, minor);
		if (major < 3) throw new NotSupportedException($"The GLES backend needs OpenGL ES 3.0 or later; the context is {major}.{minor}.");
		var actual = major > 3 || minor >= 2 ? GlesFeatureLevel.Es32 : minor == 1 ? GlesFeatureLevel.Es31 : GlesFeatureLevel.Es30;
		FeatureLevel = (GlesFeatureLevel)Math.Min((int)actual, (int)options.MaxFeatureLevel);

		var extensionCount = Gl.GetInteger(GLEnum.NumExtensions);
		for (var i = 0; i < extensionCount; i++)
		{
			var name = Marshal.PtrToStringUTF8((nint)Gl.GetString(GLEnum.Extensions, (uint)i));
			if (name is not null) Extensions.Add(name);
		}

		AdapterName = Marshal.PtrToStringUTF8((nint)Gl.GetString(GLEnum.Renderer)) ?? "OpenGL ES device";
		VersionString = Marshal.PtrToStringUTF8((nint)Gl.GetString(GLEnum.Version)) ?? $"OpenGL ES {major}.{minor}";

		MaxUniformBufferBindings = Gl.GetInteger(GLEnum.MaxUniformBufferBindings);
		MaxTextureUnits = Gl.GetInteger(GLEnum.MaxCombinedTextureImageUnits);
		MaxStorageBufferBindings = FeatureLevel >= GlesFeatureLevel.Es31 ? Gl.GetInteger(GLEnum.MaxShaderStorageBufferBindings) : 0;
		var vertexStorageBlocks = FeatureLevel >= GlesFeatureLevel.Es31 ? Gl.GetInteger(GLEnum.MaxVertexShaderStorageBlocks) : 0;
		Limits = new DeviceLimits
		{
			MaxTextureDimension2D = (uint)Gl.GetInteger(GLEnum.MaxTextureSize),
			MinUniformBufferOffsetAlignment = (ulong)Math.Max(1, Gl.GetInteger(GLEnum.UniformBufferOffsetAlignment)),
			MaxUniformBufferBindingSize = (ulong)Gl.GetInteger(GLEnum.MaxUniformBlockSize),
			MaxBindGroups = (uint)Math.Clamp(MaxUniformBufferBindings / GlesBindings.BindingsPerGroup, 1, GlesBindings.MaxGroups),
			VertexStorageBuffers = vertexStorageBlocks > 0,
		};

		ColorBufferFloat = FeatureLevel >= GlesFeatureLevel.Es32 || Extensions.Contains("GL_EXT_color_buffer_float");
		ColorBufferHalfFloat = ColorBufferFloat || Extensions.Contains("GL_EXT_color_buffer_half_float");

		// WebGPU does not dither; GL enables it by default.
		Gl.Disable(GLEnum.Dither);

		FramesInFlight = Math.Clamp(options.FramesInFlight, 1, 3);
		_frames = new FrameSlot[FramesInFlight];
		for (var i = 0; i < FramesInFlight; i++) _frames[i] = new FrameSlot(this);

		QueueImpl = new GlesQueue(this);
		Executor = new GlesExecutor(this);
		if (context.IsPresentable) SurfaceImpl = new GlesSurface(this);
		CheckErrors("device creation");
	}

	/// <summary>The context the device renders with.</summary>
	public IGlesContext Context { get; }

	/// <summary>The context's OpenGL ES version.</summary>
	public Version Version { get; }

	/// <summary>The context's <c>GL_VERSION</c> string.</summary>
	public string VersionString { get; }

	/// <summary>The feature level in use (see <see cref="GlesFeatureLevel"/>).</summary>
	public GlesFeatureLevel FeatureLevel { get; }

	/// <summary>The context's extensions.</summary>
	public HashSet<string> Extensions { get; } = new(StringComparer.Ordinal);

	/// <summary><c>GL_MAX_UNIFORM_BUFFER_BINDINGS</c>: flattened uniform block slots must stay below it.</summary>
	public int MaxUniformBufferBindings { get; }

	/// <summary><c>GL_MAX_COMBINED_TEXTURE_IMAGE_UNITS</c>: flattened texture units must stay below it.</summary>
	public int MaxTextureUnits { get; }

	/// <summary><c>GL_MAX_SHADER_STORAGE_BUFFER_BINDINGS</c> (0 below ES 3.1).</summary>
	public int MaxStorageBufferBindings { get; }

	/// <summary>Whether 32-bit float color formats are renderable.</summary>
	public bool ColorBufferFloat { get; }

	/// <summary>Whether 16-bit float color formats are renderable.</summary>
	public bool ColorBufferHalfFloat { get; }

	/// <summary>Whether <c>glGetError</c> is checked and logged.</summary>
	public bool Validation { get; }

	/// <summary>Whether draws with a base vertex use <c>glDrawElementsBaseVertex</c> (ES 3.2) rather than vertex buffer offsets.</summary>
	public bool NativeBaseVertex => FeatureLevel >= GlesFeatureLevel.Es32;

	/// <summary>Whether separate vertex attribute formats and bindings (<c>glBindVertexBuffer</c>, ES 3.1) are used.</summary>
	public bool VertexAttribBinding => FeatureLevel >= GlesFeatureLevel.Es31;

	/// <inheritdoc/>
	public GraphicsBackend Backend => GraphicsBackend.OpenGLES;

	/// <inheritdoc/>
	public string AdapterName { get; }

	/// <inheritdoc/>
	public ShaderLanguage ShaderLanguage => ShaderLanguage.GlslEs;

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

	internal FrameSlot CurrentFrame => _frames[FrameIndex];

	/// <summary>Whether a headless OpenGL ES 3 context can be created through EGL on this machine.</summary>
	public static bool IsHeadlessAvailable() => EglContext.IsAvailable();

	/// <summary>
	/// Creates the device on <see cref="GlesDeviceOptions.Context"/> (a window's context, which gets a surface) or, when it
	/// is null, on a new headless EGL context.
	/// </summary>
	public static GlesDevice Create(GlesDeviceOptions options, ILogger? logger = null)
	{
		ArgumentNullException.ThrowIfNull(options);
		logger ??= NullLogger.Instance;
		var owns = options.Context is null;
		var context = options.Context ?? EglContext.Create(debug: options.Validation);
		try
		{
			var device = new GlesDevice(context, owns, options, logger);
			logger.LogInformation("OpenGL ES device: {Adapter} ({Version}), feature level {Level}, {Frames} frames in flight, validation {Validation}, {Context}.",
				device.AdapterName, device.VersionString, device.FeatureLevel, device.FramesInFlight, options.Validation ? "on" : "off", context.Description);
			return device;
		}
		catch
		{
			if (owns) context.Dispose();
			throw;
		}
	}

	/// <inheritdoc/>
	public IBuffer CreateBuffer(in BufferDescriptor descriptor) => _checked(new GlesBuffer(_current(), descriptor), "CreateBuffer");

	/// <inheritdoc/>
	public ITexture CreateTexture(in TextureDescriptor descriptor) => _checked(new GlesTexture(_current(), descriptor), "CreateTexture");

	/// <inheritdoc/>
	public ISampler CreateSampler(in SamplerDescriptor descriptor) => _checked(new GlesSampler(_current(), descriptor), "CreateSampler");

	/// <inheritdoc/>
	public IShaderModule CreateShaderModule(in ShaderModuleDescriptor descriptor) => _checked(new GlesShaderModule(_current(), descriptor), "CreateShaderModule");

	/// <inheritdoc/>
	public IBindGroupLayout CreateBindGroupLayout(in BindGroupLayoutDescriptor descriptor) => new GlesBindGroupLayout(descriptor);

	/// <inheritdoc/>
	public IBindGroup CreateBindGroup(in BindGroupDescriptor descriptor) => new GlesBindGroup(this, descriptor);

	/// <inheritdoc/>
	public IPipelineLayout CreatePipelineLayout(in PipelineLayoutDescriptor descriptor) => new GlesPipelineLayout(descriptor);

	/// <inheritdoc/>
	public IRenderPipeline CreateRenderPipeline(RenderPipelineDescriptor descriptor) => _checked(new GlesRenderPipeline(_current(), descriptor), "CreateRenderPipeline");

	/// <inheritdoc/>
	public ICommandEncoder CreateCommandEncoder(string? label = null)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		return CurrentFrame.RentEncoder();
	}

	/// <inheritdoc/>
	public void BeginFrame()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		Context.MakeCurrent();
	}

	/// <inheritdoc/>
	public void EndFrame()
	{
		_current();
		var frame = CurrentFrame;
		if (frame.Fence != 0) Gl.DeleteSync(frame.Fence);
		frame.Fence = Gl.FenceSync(GLEnum.SyncGpuCommandsComplete, (uint)0);
		Gl.Flush();
		FrameIndex = (FrameIndex + 1) % FramesInFlight;
		_frames[FrameIndex].WaitAndReset();
	}

	/// <inheritdoc/>
	public void Poll(bool wait = false)
	{
		_current();
		if (wait) Gl.Finish();
		else Gl.Flush();
	}

	/// <summary>Waits for the GPU, deletes every object and, for a headless device, the EGL context.</summary>
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		Context.MakeCurrent();
		Gl.Finish();
		SurfaceImpl?.Destroy();
		foreach (var frame in _frames) frame.Destroy();
		foreach (var framebuffer in _framebuffers.Values) Gl.DeleteFramebuffer(framebuffer);
		_framebuffers.Clear();
		Executor.Destroy();
		if (_ownsContext) Context.Dispose();
	}

	/// <summary>Deletes an object once the frames that may use it have completed.</summary>
	internal void Defer(Action destroy)
	{
		if (_disposed) return;
		CurrentFrame.Deferred.Add(destroy);
	}

	/// <summary>Logs every pending GL error (validation only).</summary>
	internal void CheckErrors(string where)
	{
		if (!Validation) return;
		for (var i = 0; i < 16; i++)
		{
			var error = Gl.GetError();
			if (error == GLEnum.NoError) return;
			_logger.LogError("OpenGL ES error {Error} (0x{Code:X}) after {Where}.", error, (int)error, where);
		}
	}

	internal void LogWarning(string message) => _logger.LogWarning("{Message}", message);

	/// <summary>
	/// Makes the device's context current on this thread (a no-op when it already is): another context, such as an
	/// availability probe or a second device, may have been made current since the last call.
	/// </summary>
	private GlesDevice _current()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		Context.MakeCurrent();
		return this;
	}

	private T _checked<T>(T value, string where)
	{
		CheckErrors(where);
		return value;
	}

	/// <summary>The framebuffer object for a set of attachments, created on first use and cached until one of them is deleted.</summary>
	internal uint GetFramebuffer(in FramebufferKey key)
	{
		if (_framebuffers.TryGetValue(key, out var framebuffer)) return framebuffer;
		framebuffer = Gl.GenFramebuffer();
		Gl.BindFramebuffer(GLEnum.Framebuffer, framebuffer);
		Span<GLEnum> drawBuffers = stackalloc GLEnum[FramebufferKey.MaxColorAttachments];
		for (var i = 0; i < key.ColorCount; i++)
		{
			var attachment = GLEnum.ColorAttachment0 + i;
			_attach(key[i], attachment);
			drawBuffers[i] = attachment;
		}

		if (key.Depth.Handle != 0) _attach(key.Depth, key.DepthHasStencil ? GLEnum.DepthStencilAttachment : GLEnum.DepthAttachment);
		Gl.DrawBuffers((uint)key.ColorCount, drawBuffers);
		if (key.ColorCount > 0) Gl.ReadBuffer(GLEnum.ColorAttachment0);
		else Gl.ReadBuffer(GLEnum.None);

		var status = Gl.CheckFramebufferStatus(GLEnum.Framebuffer);
		if (status != GLEnum.FramebufferComplete)
		{
			Gl.BindFramebuffer(GLEnum.Framebuffer, 0);
			Gl.DeleteFramebuffer(framebuffer);
			throw new InvalidOperationException($"The render pass attachments do not form a complete framebuffer ({status}); check that the formats are renderable on this device.");
		}

		_framebuffers.Add(key, framebuffer);
		return framebuffer;
	}

	private void _attach(in FramebufferAttachmentKey attachment, GLEnum point)
	{
		if (attachment.IsRenderbuffer) Gl.FramebufferRenderbuffer(GLEnum.Framebuffer, point, GLEnum.Renderbuffer, attachment.Handle);
		else Gl.FramebufferTexture2D(GLEnum.Framebuffer, point, GLEnum.Texture2D, attachment.Handle, (int)attachment.Level);
	}

	/// <summary>Deletes the cached framebuffers that attach <paramref name="handle"/> (the texture is being deleted).</summary>
	internal void ForgetAttachment(uint handle, bool isRenderbuffer)
	{
		List<FramebufferKey>? stale = null;
		foreach (var key in _framebuffers.Keys)
		{
			if (key.Uses(handle, isRenderbuffer)) (stale ??= []).Add(key);
		}

		if (!isRenderbuffer) Executor.ForgetTexture(handle);
		if (stale is null) return;
		foreach (var key in stale)
		{
			Gl.DeleteFramebuffer(_framebuffers[key]);
			_framebuffers.Remove(key);
		}

		Executor.InvalidateFramebuffer();
	}

	/// <summary>The per-frame state of one frame slot.</summary>
	internal sealed class FrameSlot(GlesDevice device)
	{
		private readonly List<GlesCommandEncoder> _encoders = [];
		private int _nextEncoder;

		public nint Fence;
		public readonly List<Action> Deferred = [];

		public GlesCommandEncoder RentEncoder()
		{
			GlesCommandEncoder encoder;
			if (_nextEncoder < _encoders.Count)
			{
				encoder = _encoders[_nextEncoder];
			}
			else
			{
				encoder = new GlesCommandEncoder(device);
				_encoders.Add(encoder);
			}

			_nextEncoder++;
			encoder.Begin();
			return encoder;
		}

		public void WaitAndReset()
		{
			if (Fence != 0)
			{
				// Flush on the first wait so the fence is guaranteed to signal; loop on timeouts (a very slow frame).
				var flags = (uint)GLEnum.SyncFlushCommandsBit;
				while (true)
				{
					var result = device.Gl.ClientWaitSync(Fence, flags, 1_000_000_000);
					if (result is GLEnum.AlreadySignaled or GLEnum.ConditionSatisfied) break;
					if (result == GLEnum.WaitFailed)
					{
						device.Gl.Finish();
						break;
					}

					flags = 0;
				}

				device.Gl.DeleteSync(Fence);
				Fence = 0;
			}

			foreach (var destroy in Deferred) destroy();
			Deferred.Clear();
			_nextEncoder = 0;
		}

		public void Destroy()
		{
			foreach (var destroy in Deferred) destroy();
			Deferred.Clear();
			if (Fence != 0) device.Gl.DeleteSync(Fence);
			Fence = 0;
		}
	}
}

/// <summary>One framebuffer attachment: a texture level or a renderbuffer.</summary>
internal readonly record struct FramebufferAttachmentKey(uint Handle, uint Level, bool IsRenderbuffer);

/// <summary>The attachments of a framebuffer object (the cache key).</summary>
internal struct FramebufferKey : IEquatable<FramebufferKey>
{
	public const int MaxColorAttachments = 4;

	public int ColorCount;
	public FramebufferAttachmentKey Color0;
	public FramebufferAttachmentKey Color1;
	public FramebufferAttachmentKey Color2;
	public FramebufferAttachmentKey Color3;
	public FramebufferAttachmentKey Depth;
	public bool DepthHasStencil;

	public FramebufferAttachmentKey this[int index]
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

	public readonly bool Uses(uint handle, bool isRenderbuffer)
	{
		for (var i = 0; i < ColorCount; i++)
		{
			if (this[i].Handle == handle && this[i].IsRenderbuffer == isRenderbuffer) return true;
		}

		return Depth.Handle == handle && Depth.IsRenderbuffer == isRenderbuffer;
	}

	public readonly bool Equals(FramebufferKey other) =>
		ColorCount == other.ColorCount && Color0 == other.Color0 && Color1 == other.Color1 && Color2 == other.Color2 && Color3 == other.Color3
		&& Depth == other.Depth && DepthHasStencil == other.DepthHasStencil;

	public override readonly bool Equals(object? obj) => obj is FramebufferKey other && Equals(other);

	public override readonly int GetHashCode() => HashCode.Combine(ColorCount, Color0, Color1, Color2, Color3, Depth, DepthHasStencil);
}
