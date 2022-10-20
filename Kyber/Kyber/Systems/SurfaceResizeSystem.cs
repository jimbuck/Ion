using Kyber.Events;

namespace Kyber.Systems;

internal class SurfaceResizeSystem : IPostUpdateSystem
{
	private readonly GraphicsDevice _graphicsDevice;
	private readonly IEventListener _events;

	public bool IsEnabled { get; set; } = true;

	public SurfaceResizeSystem(GraphicsDevice graphicsDevice, IEventListener events)
	{
		_graphicsDevice = graphicsDevice;
		_events = events;
	}

	public void PostUpdate(float dt)
	{
		if (_events.OnLatest<SurfaceResizeEvent>(out var e)) _graphicsDevice.Internal?.ResizeMainWindow(e.Data.Width, e.Data.Height);
	}
}
