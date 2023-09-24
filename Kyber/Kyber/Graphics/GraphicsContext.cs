using Kyber.Utils;

using Veldrid;

namespace Kyber.Graphics;

internal class GraphicsContext : IGraphicsContext, IDisposable
{
	private readonly IGameConfig _config;
	private readonly IEventListener _events;
	private readonly ILogger _logger;
	private readonly Window _window;

#pragma warning disable CS8603 // Possible null reference return.
	private CommandList? _commandList;
	public GraphicsDevice? GraphicsDevice { get; private set; }
	public ResourceFactory Factory => GraphicsDevice?.ResourceFactory;
#pragma warning restore CS8603 // Possible null reference return.

	public Matrix4x4 ProjectionMatrix { get; private set; } = Matrix4x4.Identity;

	public bool NoRender { get; }

	public GraphicsContext(IGameConfig config, IEventListener events, ILogger<GraphicsContext> logger, IWindow window)
	{
		_config = config;
		_events = events;
		_logger = logger;
		_window = (Window)window;

		NoRender = _config.Output == GraphicsOutput.None;
	}

	public void Initialize()
	{
		if (NoRender) return;

		using var _ = MicroTimer.Start("GraphicsContext::Initialize");
		_logger.LogInformation("Creating graphics device...");

		if (!GraphicsDevice.IsBackendSupported(_config.PreferredBackend))
		{
			throw new KyberException($"Unsupported backend! ({_config.PreferredBackend}");
		}

		GraphicsDevice = Veldrid.StartupUtilities.VeldridStartup.CreateGraphicsDevice(_window.Sdl2Window, new GraphicsDeviceOptions()
		{
#if DEBUG
			//Debug = true,
#endif
			SwapchainDepthFormat = PixelFormat.D32_Float_S8_UInt,
			ResourceBindingModel = ResourceBindingModel.Default,
			PreferStandardClipSpaceYDirection = true,
			PreferDepthRangeZeroToOne = true,
			SyncToVerticalBlank = _config.VSync,
		}, _config.PreferredBackend);

		_commandList = GraphicsDevice.ResourceFactory.CreateCommandList();

		_logger.LogInformation($"Graphics device created ({GraphicsDevice.BackendType})!");

		UpdateProjection((uint)_window.Width, (uint)_window.Height);
	}

	public void First()
	{
		if (_events.OnLatest<WindowResizeEvent>(out var e))
		{
			_logger.LogInformation($"Updating projection {e.Data.Width}x{e.Data.Height}!");
			UpdateProjection(e.Data.Width, e.Data.Height);
		}
	}

	public void UpdateProjection(uint width, uint height)
	{
		ProjectionMatrix = CreateOrthographic(0, width, 0, height, 1000f, -100f);
	}

	public void BeginFrame(GameTime dt)
	{
		if (NoRender || GraphicsDevice is null || _commandList is null) return;

		using var _ = MicroTimer.Start("GraphicsContext::BeginFrame");

		_commandList.Begin();
		_commandList.SetFramebuffer(GraphicsDevice.MainSwapchain.Framebuffer);
		_commandList.SetFullViewports();
		_commandList.ClearColorTarget(0, _config.ClearColor);
		_commandList.ClearDepthStencil(GraphicsDevice.IsDepthRangeZeroToOne ? 0f : 1f);
		_commandList.End();
		GraphicsDevice.SubmitCommands(_commandList);
	}

	public void EndFrame(GameTime dt)
	{
		if (NoRender || GraphicsDevice is null || _commandList is null) return;

		using var timer = MicroTimer.Start("GraphicsContext::EndFrame::WaitForIdle");

		GraphicsDevice.WaitForIdle();

		timer.Then("GraphicsContext::EndFrame::SwapBuffers");

		if (_window.HasClosed) return;

		GraphicsDevice.SwapBuffers();

		timer.Then("GraphicsContext::EndFrame::HandleResize");

		if (_events.OnLatest<WindowResizeEvent>(out var e))
		{
			GraphicsDevice.ResizeMainWindow(e.Data.Width, e.Data.Height);
		}
	}

	public void SubmitCommands(CommandList commandList)
	{
		using var _ = MicroTimer.Start("GraphicsContext::SubmitCommands");
		GraphicsDevice?.SubmitCommands(commandList);
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
