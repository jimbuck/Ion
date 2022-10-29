using Veldrid;

namespace Kyber.Graphics;

public class GraphicsDeviceInitializerSystem : IInitializeSystem, IFirstSystem
{
	private readonly GraphicsDevice _graphicsDevice;

	public bool IsEnabled { get; set; } = true;
	public GraphicsDeviceInitializerSystem(IGraphicsDevice graphicsDevice)
	{
		_graphicsDevice = (GraphicsDevice)graphicsDevice;
	}

	public void Initialize()
	{
		_graphicsDevice.Initialize();
	}

	public void First(float dt)
	{
		_graphicsDevice.CommandList.Begin();
		_graphicsDevice.CommandList.SetFramebuffer(_graphicsDevice.Internal.SwapchainFramebuffer);
		_graphicsDevice.CommandList.SetFullViewports();
		_graphicsDevice.CommandList.ClearDepthStencil(_graphicsDevice.Internal.IsDepthRangeZeroToOne ? 0f : 1f);
		_graphicsDevice.CommandList.ClearColorTarget(0, Color.Black);
	}
}

public class GraphicsDeviceSwapBuffers : ILastSystem
{
	private readonly GraphicsDevice _graphicsDevice;
	private readonly IEventListener _events;
	private readonly IWindow _window;

	public bool IsEnabled { get; set; } = true;
	public GraphicsDeviceSwapBuffers(IGraphicsDevice graphicsDevice, IEventListener events, IWindow window)
	{
		_graphicsDevice = (GraphicsDevice)graphicsDevice;
		_events = events;
		_window = window;
	}

	public void Last(float dt)
	{
		_graphicsDevice.CommandList.End();
		_graphicsDevice.Internal.SubmitCommands(_graphicsDevice.CommandList);

		if (_window.HasClosed) return;

		_graphicsDevice.Internal.SwapBuffers();
		//_graphicsDevice.Internal.WaitForIdle();

		if (_events.OnLatest<WindowResizeEvent>(out var e))
		{
			_graphicsDevice.Internal.ResizeMainWindow(e.Data.Width, e.Data.Height);
		}
	}
}