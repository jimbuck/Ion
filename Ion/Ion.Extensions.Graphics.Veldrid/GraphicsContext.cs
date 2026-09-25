using System.Numerics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Veldrid;
using Ion.Extensions.Debug;

namespace Ion.Extensions.Graphics;

public interface IGraphicsContext
{
	Matrix4x4 ProjectionMatrix { get; }
	bool NoRender { get; }
	public GraphicsDevice? GraphicsDevice { get; }
	public ResourceFactory Factory { get; }

	void SubmitCommands(CommandList cl);

	Matrix4x4 CreateOrthographic(float left, float right, float bottom, float top, float near, float far);
	Matrix4x4 CreatePerspective(float fov, float aspectRatio, float near, float far);
}

/// <summary>
/// Maps Ion's <see cref="GraphicsBackend"/> onto <see cref="Veldrid.GraphicsBackend"/>.
/// The two enums do not share ordinals, so they must never be cast onto each other.
/// </summary>
internal static class VeldridBackendMapper
{
	/// <exception cref="NotSupportedException">The backend has no Veldrid implementation (Direct3D12, WebGPU).</exception>
	public static Veldrid.GraphicsBackend ToVeldrid(GraphicsBackend backend) => backend switch
	{
		GraphicsBackend.Direct3D11 => Veldrid.GraphicsBackend.Direct3D11,
		GraphicsBackend.Vulkan => Veldrid.GraphicsBackend.Vulkan,
		GraphicsBackend.OpenGL => Veldrid.GraphicsBackend.OpenGL,
		GraphicsBackend.Metal => Veldrid.GraphicsBackend.Metal,
		GraphicsBackend.OpenGLES => Veldrid.GraphicsBackend.OpenGLES,
		GraphicsBackend.Auto => Veldrid.StartupUtilities.VeldridStartup.GetPlatformDefaultBackend(),
		_ => throw new NotSupportedException($"Graphics backend {backend} is not supported by the Veldrid graphics extension."),
	};
}

internal class GraphicsContext : IGraphicsContext, IDisposable
{
	private readonly IOptionsMonitor<GraphicsConfig> _config;
	private EventReader<WindowResizeEvent> _resizes;
	private readonly ILogger _logger;
	private readonly Window _window;
	private readonly ITraceTimer<GraphicsContext> _trace;

#pragma warning disable CS8603 // Possible null reference return.
	private CommandList? _commandList;
	public GraphicsDevice? GraphicsDevice { get; private set; }
	public ResourceFactory Factory => GraphicsDevice?.ResourceFactory;
#pragma warning restore CS8603 // Possible null reference return.

	public Matrix4x4 ProjectionMatrix { get; private set; } = Matrix4x4.Identity;

	public bool NoRender { get; }

	public GraphicsContext(IOptionsMonitor<GraphicsConfig> config, IEvents events, ILogger<GraphicsContext> logger, Window window, ITraceTimer<GraphicsContext> trace)
	{
		_config = config;
		_resizes = events.Reader<WindowResizeEvent>();
		_logger = logger;
		_window = window;
		_trace = trace;

		NoRender = _config.CurrentValue.Output == GraphicsOutput.None;
	}

	public void Initialize()
	{
		if (NoRender) return;

		_logger.LogInformation("Creating graphics device...");

		var config = _config.CurrentValue;

		var veldridBackend = VeldridBackendMapper.ToVeldrid(config.PreferredBackend);

		if (!GraphicsDevice.IsBackendSupported(veldridBackend))
		{
			var fallback = Veldrid.StartupUtilities.VeldridStartup.GetPlatformDefaultBackend();
			_logger.LogWarning("Preferred graphics backend {preferredBackend} is not supported on this platform, falling back to {fallbackBackend}", veldridBackend.ToString("G"), fallback.ToString("G"));
			veldridBackend = fallback;
		}

		_logger.LogInformation("Initializing {graphicsBackend}", veldridBackend.ToString("G"));

		var timer = _trace.Start("Initialize");

		GraphicsDevice = Veldrid.StartupUtilities.VeldridStartup.CreateGraphicsDevice(_window.Sdl2Window, new GraphicsDeviceOptions()
		{
#if DEBUG
			//Debug = true,
#endif
			SwapchainDepthFormat = PixelFormat.D32_Float_S8_UInt,
			ResourceBindingModel = ResourceBindingModel.Default,
			PreferStandardClipSpaceYDirection = true,
			PreferDepthRangeZeroToOne = true,
			SyncToVerticalBlank = config.VSync,
		}, veldridBackend);

		_commandList = GraphicsDevice.ResourceFactory.CreateCommandList();

		_logger.LogInformation("Graphics device created ({graphicsBackend})!", GraphicsDevice.BackendType.ToString("G"));

		UpdateProjection((uint)_window.Width, (uint)_window.Height);

		timer.Stop();
	}

	public void UpdateProjection(uint width, uint height)
	{
		ProjectionMatrix = CreateOrthographic(0, width, 0, height, 1000f, -100f);
	}

	public void BeginFrame(GameTime dt)
	{
		if (NoRender || GraphicsDevice is null || _commandList is null) return;

		var timer = _trace.Start("BeginFrame");

		_commandList.Begin();
		_commandList.SetFramebuffer(GraphicsDevice.MainSwapchain.Framebuffer);
		_commandList.SetFullViewports();
		_commandList.ClearColorTarget(0, new(_config.CurrentValue.ClearColor.ToVector4()));
		_commandList.ClearDepthStencil(GraphicsDevice.IsDepthRangeZeroToOne ? 0f : 1f);
		_commandList.End();
		GraphicsDevice.SubmitCommands(_commandList);
		timer.Stop();
	}

	public void EndFrame(GameTime dt)
	{
		if (NoRender || GraphicsDevice is null || _commandList is null || _window.Sdl2Window is null) return;

		var timer = _trace.Start("EndFrame::WaitForIdle");

		GraphicsDevice.WaitForIdle();

		timer.Then("EndFrame::SwapBuffers");

		if (_window.IsClosing) return;

		if (_window.Sdl2Window.Exists) GraphicsDevice.SwapBuffers();

		timer.Then("EndFrame::HandleResize");

		if (_resizes.TryReadLatest(out var e))
		{
			_logger.LogInformation($"Updating projection {e.Width}x{e.Height}!");
			GraphicsDevice.ResizeMainWindow(e.Width, e.Height);
			UpdateProjection(e.Width, e.Height);
		}
	}

	public void SubmitCommands(CommandList commandList)
	{
		var timer = _trace.Start("SubmitCommands");
		GraphicsDevice?.SubmitCommands(commandList);
		timer.Stop();
	}

	public Matrix4x4 CreateOrthographic(float left, float right, float bottom, float top, float near, float far)
	{
		Matrix4x4 ortho;
		if (GraphicsDevice?.IsDepthRangeZeroToOne ?? false) ortho = Matrix4x4.CreateOrthographicOffCenter(left, right, bottom, top, far, near);
		else ortho = Matrix4x4.CreateOrthographicOffCenter(left, right, bottom, top, near, far);

		if (GraphicsDevice?.IsClipSpaceYInverted ?? false)
		{
			ortho *= new Matrix4x4(
				1, 0, 0, 0,
				0, -1, 0, 0,
				0, 0, 1, 0,
				0, 0, 0, 1);
		}

		return ortho;
	}

	public Matrix4x4 CreatePerspective(float fov, float aspectRatio, float near, float far)
	{
		Matrix4x4 persp;
		if (GraphicsDevice?.IsDepthRangeZeroToOne ?? false)
		{
			persp = _createPerspective(fov, aspectRatio, far, near);
		}
		else
		{
			persp = _createPerspective(fov, aspectRatio, near, far);
		}

		if (GraphicsDevice?.IsClipSpaceYInverted ?? false)
		{
			persp *= new Matrix4x4(
				1, 0, 0, 0,
				0, -1, 0, 0,
				0, 0, 1, 0,
				0, 0, 0, 1);
		}

		return persp;
	}

	private static Matrix4x4 _createPerspective(float fov, float aspectRatio, float near, float far)
	{
		if (fov <= 0.0f || fov >= MathF.PI) throw new ArgumentOutOfRangeException(nameof(fov));
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

	private bool _disposed;

	public void Dispose()
	{
		// Registered both as itself and as IGraphicsContext, so the container may call this twice.
		if (_disposed) return;
		_disposed = true;

		_commandList?.Dispose();
		GraphicsDevice?.Dispose();
	}
}
