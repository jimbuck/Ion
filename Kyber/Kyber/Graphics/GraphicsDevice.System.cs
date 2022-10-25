namespace Kyber.Graphics;

public class GraphicsDeviceInitializerSystem : IInitializeSystem
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
}