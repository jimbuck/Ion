
namespace Ion;


public interface IMiddlewarePipelineBuilder
{
	IMiddlewarePipelineBuilder Use(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	GameLoopDelegate Build();
}

public class MiddlewarePipelineBuilder : IMiddlewarePipelineBuilder
{
	private readonly List<Func<GameLoopDelegate, GameLoopDelegate>> _systems = [];
	private readonly List<string> _descriptions = [];

	public IMiddlewarePipelineBuilder Use(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		_systems.Add(middleware);
		_descriptions.Add(_createMiddlewareDescription(middleware));
		return this;
	}

	public GameLoopDelegate Build()
	{
		GameLoopDelegate gameLoop = (dt) => { };

		for (var s = _systems.Count - 1; s >= 0; s--) gameLoop = _systems[s](gameLoop);

		return gameLoop;
	}

	private static string _createMiddlewareDescription(Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		if (middleware.Target is not null)
		{
			if (middleware.Method.Name == "CreateMiddleware")
			{
				return middleware.Target?.ToString() ?? middleware.Method.Name.ToString(); ;
			}
			else
			{
				return middleware.Target.GetType().FullName + "." + middleware.Method.Name;
			}
		}

		return middleware.Method.Name.ToString();
	}
}