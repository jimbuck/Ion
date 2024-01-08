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

internal class GraphicsContext : IGraphicsContext, IDisposable
{
	private readonly IOptionsMonitor<GraphicsConfig> _config;
	private readonly IEventListener _events;
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
		if (NoRender) return;

		_logger.LogInformation("Creating graphics device...");

		var config = _config.CurrentValue;

		var veldridBackend = (Veldrid.GraphicsBackend)config.PreferredBackend;

		if (!GraphicsDevice.IsBackendSupported(veldridBackend))
		{
			throw new Exception($"Unsupported backend! ({config.PreferredBackend}");
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

		_logger.LogInformation($"Graphics device created ({GraphicsDevice.BackendType})!");

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

		if (_window.HasClosed) return;

		if (_window.Sdl2Window.Exists) GraphicsDevice.SwapBuffers();

		timer.Then("EndFrame::HandleResize");

		if (_events.OnLatest<WindowResizeEvent>(out var e))
		{
			_logger.LogInformation($"Updating projection {e.Data.Width}x{e.Data.Height}!");
			GraphicsDevice.ResizeMainWindow(e.Data.Width, e.Data.Height);
			UpdateProjection(e.Data.Width, e.Data.Height);
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
		if (fov <= 0.0f || fov >= MathF.PI)
			throw new ArgumentOutOfRangeException(nameof(fov));

		if (near <= 0.0f)
			throw new ArgumentOutOfRangeException(nameof(near));

		if (far <= 0.0f)
			throw new ArgumentOutOfRangeException(nameof(far));

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

	public void Dispose()
	{
		_commandList?.Dispose();
		GraphicsDevice?.Dispose();
	}
}
