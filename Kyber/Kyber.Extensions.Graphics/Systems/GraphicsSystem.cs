
namespace Kyber.Builder;

public class GraphicsSystem
{
	private readonly GraphicsContext _graphics;

	public GraphicsSystem(IGraphicsContext graphics)
	{
		_graphics = (GraphicsContext)graphics;
	}

	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		_graphics.Initialize();
		// ASSET MANAGER INIT

		next(dt);
	}

	[First]
	public void First(GameTime dt, GameLoopDelegate next)
	{
		_graphics.First();
		next(dt);
	}

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		_graphics.BeginFrame(dt);
		next(dt);
		_graphics.EndFrame(dt);
	}
}
