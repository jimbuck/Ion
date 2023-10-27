
namespace Kyber.Builder;


public interface IPipelineBuilder
{
	IPipelineBuilder Use(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	GameLoopDelegate Build();
}

internal class PipelineBuilder : IPipelineBuilder
{
	private readonly List<Func<GameLoopDelegate, GameLoopDelegate>> _systems = new();
	private readonly List<string> _descriptions = new();

	public IPipelineBuilder Use(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_systems.Add(middleware);
		_descriptions.Add(_createMiddlewareDescription(middleware));
		return this;
	}

	public GameLoopDelegate Build()
	{
		GameLoopDelegate app = (dt) => { };

		for (var s = _systems.Count - 1; s >= 0; s--)
		{
			app = _systems[s](app);
		}

		return app;
	}

	private string _createMiddlewareDescription(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		if (middleware.Target != null)
		{
			if (middleware.Method.Name == "CreateMiddleware")
			{
				return middleware.Target.ToString()!;
			}

			return middleware.Target.GetType().FullName + "." + middleware.Method.Name;
		}

		return middleware.Method.Name.ToString();
	}
}