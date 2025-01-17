﻿using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using WebGPU;
using static WebGPU.WebGPU;

using Ion.Extensions.Debug;


namespace Ion.Extensions.Graphics;

public interface IGraphicsContext
{
	Matrix4x4 ProjectionMatrix { get; }
	bool NoRender { get; }
	public WGPUDevice Device { get; }
	public WGPUQueue Queue { get; }
	public Texture2D RenderTarget { get; }
	public WGPUTextureFormat SwapChainFormat { get; }

	Matrix4x4 CreateOrthographic(float left, float right, float bottom, float top, float near, float far);
	Matrix4x4 CreatePerspective(float fov, float aspectRatio, float near, float far);
}

internal unsafe class GraphicsContext : IGraphicsContext, IDisposable
{
	private readonly IOptionsMonitor<GraphicsConfig> _config;
	private readonly IEventListener _events;
	private readonly ILogger _logger;
	private readonly Window _window;
	private readonly ITraceTimer<GraphicsContext> _trace;

	private WGPUInstance _instance = default!;
	private WGPUDevice _device = default!;
	private WGPUAdapter _adapter = default!;
	private WGPUAdapterProperties _adapterProperties = default!;
	private WGPUSupportedLimits _adapterLimits = default!;

	private WGPUSurface _surface = default!;
	private WGPUQueue _queue = default!;
	private WGPUTextureFormat _swapchainFormat = default!;

	private Texture2D _renderTarget = default!;

	public WGPUDevice Device => _device;
	public WGPUQueue Queue => _queue;
	public Texture2D RenderTarget => _renderTarget;
	public WGPUTextureFormat SwapChainFormat => _swapchainFormat;

	public uint RenderTargetWidth => _window.Width;
	public uint RenderTargetHeight => _window.Height;

	public Matrix4x4 ProjectionMatrix { get; private set; } = Matrix4x4.Identity;

	public bool NoRender { get; }

	public GraphicsContext(IOptionsMonitor<GraphicsConfig> config, IEventListener events, ILogger<GraphicsContext> logger, IWindow window, ITraceTimer<GraphicsContext> trace)
	{
		_config = config;
		_events = events;
		_logger = logger;
		_window = (Window)window;
		_trace = trace;

		NoRender = _config.CurrentValue.Output == GraphicsOutput.None;
	}

	public void Initialize()
	{
		_logger.LogInformation("Creating graphics device...");

		var config = _config.CurrentValue;

		var backend = _getPreferredBackend(config.PreferredBackend);

		if (!_isBackendSupported(backend))
		{
			throw new NotSupportedException($"Unsupported backend! ({config.PreferredBackend}");
		}

		_logger.LogInformation("Initializing {graphicsBackend}", backend.ToString("G"));

		var timer = _trace.Start("Initialize");

		_createGraphicsDevice(backend);

		_logger.LogInformation("Graphics device created ({Backend})!", backend.ToString("G"));

		_updateRenderTarget();
		_clearRenderTarget();
		UpdateProjection(_window.Width, _window.Height);

		timer.Stop();
	}

	private void _createGraphicsDevice(WGPUBackendType backend)
	{
		WGPUInstanceExtras extras = new()
		{
#if DEBUG
			flags = WGPUInstanceFlags.Validation
#endif
		};

		WGPUInstanceDescriptor instanceDescriptor = new()
		{
			nextInChain = (WGPUChainedStruct*)&extras
		};
		_instance = wgpuCreateInstance(&instanceDescriptor);

		_surface = _window.CreateSurface(_instance);

		WGPURequestAdapterOptions options = new()
		{
			nextInChain = null,
			compatibleSurface = _surface,
			backendType = backend,
			powerPreference = WGPUPowerPreference.HighPerformance
		};

		// Call to the WebGPU request adapter procedure
		WGPUAdapter result = WGPUAdapter.Null;
		wgpuInstanceRequestAdapter(_instance, &options, &_onAdapterRequestEnded, new nint(&result));
		_adapter = result;
		wgpuAdapterGetProperties(_adapter, out WGPUAdapterProperties properties);

		WGPUSupportedLimits limits;
		wgpuAdapterGetLimits(_adapter, &limits);

		_adapterProperties = properties;
		_adapterLimits = limits;

		fixed (sbyte* pDeviceName = "Ion Device".GetUtf8Span())
		{
			WGPUDeviceDescriptor deviceDesc = new()
			{
				nextInChain = null,
				label = pDeviceName,
				requiredFeatureCount = 0,
				requiredLimits = null
			};
			deviceDesc.defaultQueue.nextInChain = null;
			//deviceDesc.defaultQueue.label = "The default queue";

			WGPUDevice device = WGPUDevice.Null;
			wgpuAdapterRequestDevice(_adapter, &deviceDesc, &_onDeviceRequestEnded, new nint(&device));
			_device = device;
		}

		wgpuDeviceSetUncapturedErrorCallback(Device, _errorCallback);

		_queue = wgpuDeviceGetQueue(Device);

		// WGPUTextureFormat_BGRA8UnormSrgb on desktop, WGPUTextureFormat_BGRA8Unorm on mobile
		_swapchainFormat = wgpuSurfaceGetPreferredFormat(_surface, _adapter);
		System.Diagnostics.Debug.Assert(_swapchainFormat != WGPUTextureFormat.Undefined);

		_configureSurface(_window.Width, _window.Height);
	}

	public void UpdateProjection(uint width, uint height)
	{
		ProjectionMatrix = CreateOrthographic(0, width, 0, height, 1000f, -100f);
	}

	public void BeginFrame(GameTime dt)
	{
		var timer = _trace.Start("BeginFrame");

		_updateRenderTarget();
		_clearRenderTarget();

		timer.Stop();
	}

	public void EndFrame(GameTime dt)
	{
		var timer = _trace.Start("EndFrame::Present");

		// We can tell the surface to present the next texture.
		wgpuSurfacePresent(_surface);

		timer.Then("EndFrame::HandleResize");

		if (_events.OnLatest<WindowResizeEvent>(out var e))
		{
			_logger.LogInformation($"Updating projection {e.Data.Width}x{e.Data.Height}!");
			_configureSurface(e.Data.Width, e.Data.Height);
			UpdateProjection(e.Data.Width, e.Data.Height);
		}

		timer.Stop();
	}

	private void _configureSurface(uint width, uint height)
	{
		if (width == 0 || height == 0) return;

		var config = _config.CurrentValue;

		WGPUTextureFormat viewFormat = _swapchainFormat;
		WGPUSurfaceConfiguration surfaceConfiguration = new()
		{
			nextInChain = null,
			device = Device,
			format = _swapchainFormat,
			usage = WGPUTextureUsage.RenderAttachment,
			viewFormatCount = 1,
			viewFormats = &viewFormat,
			alphaMode = WGPUCompositeAlphaMode.Auto,
			width = width,
			height = height,
			presentMode = config.VSync ? WGPUPresentMode.Fifo : WGPUPresentMode.Immediate,
		};
		wgpuSurfaceConfigure(_surface, &surfaceConfiguration);
		_logger.LogInformation("SwapChain created");
	}

	private void _updateRenderTarget()
	{
		WGPUSurfaceTexture surfaceTexture = default;
		wgpuSurfaceGetCurrentTexture(_surface, &surfaceTexture);

		// Getting the texture may fail, in particular if the window has been resized
		// and thus the target surface changed.
		if (surfaceTexture.status == WGPUSurfaceGetCurrentTextureStatus.Timeout)
		{
			_logger.LogError("Cannot acquire next swap chain texture");
			return;
		}

		if (surfaceTexture.status == WGPUSurfaceGetCurrentTextureStatus.Outdated)
		{
			_logger.LogWarning("Surface texture is outdated, reconfigure the surface!");
			return;
		}

		_renderTarget = new Texture2D("SwapChain", surfaceTexture.texture, new WGPUTextureDescriptor
		{
			dimension = WGPUTextureDimension._2D,
			format = _swapchainFormat,
			size = new WGPUExtent3D(_window.Width, _window.Height, 1),
			mipLevelCount = 1,
			sampleCount = 1,
		});
	}

	private void _clearRenderTarget()
	{
		var timer = _trace.Start("ClearRenderTarget");

		var config = _config.CurrentValue;

		var encoder = _device.CreateCommandEncoder("Main Command Encoder");

		WGPUTextureView targetView = _renderTarget.CreateView();

		WGPURenderPassColorAttachment renderPassColorAttachment = new()
		{
			view = targetView,
			// Not relevant here because we do not use multi-sampling
			resolveTarget = WGPUTextureView.Null,
			loadOp = WGPULoadOp.Clear,
			storeOp = WGPUStoreOp.Store,
			clearValue = new WGPUColor(config.ClearColor.R, config.ClearColor.G, config.ClearColor.B, config.ClearColor.A)
		};

		// Describe a render pass, which targets the texture view
		WGPURenderPassDescriptor renderPassDesc = new()
		{
			nextInChain = null,
			colorAttachmentCount = 1,
			colorAttachments = &renderPassColorAttachment,
			// No depth buffer for now
			depthStencilAttachment = null,

			// We do not use timers for now neither
			timestampWrites = null
		};

		// Create a render pass. We end it immediately because we use its built-in
		// mechanism for clearing the screen when it begins (see descriptor).
		var renderPass = encoder.BeginRenderPass(renderPassDesc);
		renderPass.End();

		var clearCommand = encoder.Finish("ClearEncoder");

		_queue.Submit(clearCommand);

		timer.Stop();
	}

	public Matrix4x4 CreateOrthographic(float left, float right, float bottom, float top, float near, float far)
	{
		return Matrix4x4.CreateOrthographicOffCenter(left, right, bottom, top, near, far);
	}

	public Matrix4x4 CreatePerspective(float fov, float aspectRatio, float near, float far)
	{
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(fov, 0f);
		ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(fov, MathF.PI);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(near, 0.0f);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(far, 0.0f);

		float yScale = 1.0f / MathF.Tan(fov * 0.5f);
		float xScale = yScale / aspectRatio;

		Matrix4x4 result;

		result.M11 = xScale;
		result.M12 = result.M13 = result.M14 = 0.0f;

		result.M22 = yScale;
		result.M21 = result.M23 = result.M24 = 0.0f;

		result.M31 = result.M32 = 0.0f;
		var negFarRange = float.IsPositiveInfinity(far) ? -1.0f : far / (near - far);
		result.M33 = negFarRange;
		result.M34 = -1.0f;

		result.M41 = result.M42 = result.M44 = 0.0f;
		result.M43 = near * negFarRange;

		return result;
	}	

	private void _errorCallback(WGPUErrorType type, string message)
	{
		_logger.LogError("{_message} ({errorType})", message.Replace("\\r\\n", "\n"), type.ToString("G"));
	}

	public void Dispose()
	{
		wgpuDeviceRelease(_device);
		wgpuSurfaceRelease(_surface);
		wgpuAdapterRelease(_adapter);
		wgpuInstanceRelease(_instance);
	}

	private static bool _isBackendSupported(WGPUBackendType backend)
	{
		return backend switch
		{
			WGPUBackendType.Null => false,
			WGPUBackendType.D3D11 or WGPUBackendType.D3D12 => RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
			WGPUBackendType.Vulkan => true,
			WGPUBackendType.OpenGL => true,
			WGPUBackendType.Metal => RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
			WGPUBackendType.OpenGLES => !RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
			WGPUBackendType.WebGPU => OperatingSystem.IsBrowser(),
			_ => throw new Exception("Illegal backend!"),
		};
	}

	private static WGPUBackendType _getPreferredBackend(GraphicsBackend backend)
	{
		var wgpuBackend = backend switch
		{
			GraphicsBackend.Vulkan => WGPUBackendType.Vulkan,
			GraphicsBackend.OpenGL => WGPUBackendType.OpenGL,
			GraphicsBackend.OpenGLES => WGPUBackendType.OpenGLES,
			GraphicsBackend.Metal => WGPUBackendType.Metal,
			GraphicsBackend.Direct3D11 => WGPUBackendType.D3D11,
			GraphicsBackend.Direct3D12 => WGPUBackendType.D3D12,
			GraphicsBackend.WebGPU => WGPUBackendType.WebGPU,
			_ => throw new Exception("Illegal backend!"),
		};

		if (_isBackendSupported(wgpuBackend)) return wgpuBackend;

		if (_isBackendSupported(WGPUBackendType.Vulkan)) return WGPUBackendType.Vulkan;
		if (_isBackendSupported(WGPUBackendType.Metal)) return WGPUBackendType.Metal;

		return WGPUBackendType.OpenGL;
	}

	[UnmanagedCallersOnly]
	private static void _onAdapterRequestEnded(WGPURequestAdapterStatus status, WGPUAdapter candidateAdapter, sbyte* message, nint pUserData)
	{
		if (status == WGPURequestAdapterStatus.Success)
		{
			*(WGPUAdapter*)pUserData = candidateAdapter;
		}
		else
		{
			Console.WriteLine("Could not get WebGPU adapter: " + Interop.GetString(message));
		}
	}

	[UnmanagedCallersOnly]
	private static void _onDeviceRequestEnded(WGPURequestDeviceStatus status, WGPUDevice device, sbyte* message, nint pUserData)
	{
		if (status == WGPURequestDeviceStatus.Success)
		{
			*(WGPUDevice*)pUserData = device;
		}
		else
		{
			Console.WriteLine("Could not get WebGPU device: " + Interop.GetString(message));
		}
	}
}
