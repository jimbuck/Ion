namespace Kyber.Graphics;

public interface IGraphicsDevice
{

}

public class GraphicsDevice : IGraphicsDevice, IDisposable
{
	private readonly IGameConfig _config;
	private readonly Window _window;
	private readonly IEventListener _events;
	private readonly ILogger _logger;

	private Veldrid.GraphicsDevice? _gd;
	private Veldrid.CommandList? _cl;

	// TODO: Remove this once the graphics API is implemented.
#pragma warning disable CS8603 // Possible null reference return.
	public Veldrid.GraphicsDevice Internal => _gd;
	public Veldrid.CommandList CommandList => _cl;
	public Veldrid.ResourceFactory Factory => _gd?.ResourceFactory;
#pragma warning restore CS8603 // Possible null reference return.

	public GraphicsDevice(IGameConfig config, IWindow window, IEventListener events, ILogger<GraphicsDevice> logger)
	{
		_config = config;
		_window = (Window)window;
		_events = events;
		_logger = logger;
	}

	public void Initialize()
	{
		if (_config.Output == GraphicsOutput.None) return;

		_logger.LogInformation("Creating graphics device...");

		_gd = Veldrid.StartupUtilities.VeldridStartup.CreateGraphicsDevice(_window.Sdl2Window, new Veldrid.GraphicsDeviceOptions()
		{
#if DEBUG
			Debug = true,
#endif
			SwapchainDepthFormat = Veldrid.PixelFormat.D32_Float_S8_UInt,
			ResourceBindingModel = Veldrid.ResourceBindingModel.Improved,
			PreferStandardClipSpaceYDirection = true,
			PreferDepthRangeZeroToOne = true,
			SyncToVerticalBlank = _config.VSync,
		}, _config.PreferredBackend.ToInternal());

		_cl = _gd.ResourceFactory.CreateCommandList();

		_logger.LogInformation("Graphics device created!");
	}

	public Matrix4x4 CreateOrthographic(float left, float right, float bottom, float top, float near, float far)
	{
		Matrix4x4 ortho;
		if (_gd?.IsDepthRangeZeroToOne ?? false) ortho = Matrix4x4.CreateOrthographicOffCenter(left, right, bottom, top, far, near);
		else ortho = Matrix4x4.CreateOrthographicOffCenter(left, right, bottom, top, near, far);

		if (_gd?.IsClipSpaceYInverted ?? false)
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
		if (_gd?.IsDepthRangeZeroToOne ?? false)
		{
			persp = _createPerspective(fov, aspectRatio, far, near);
		}
		else
		{
			persp = _createPerspective(fov, aspectRatio, near, far);
		}

		if (_gd?.IsClipSpaceYInverted ?? false)
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
		_cl?.Dispose();
		_gd?.Dispose();
	}
}
