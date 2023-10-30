namespace Kyber.Extensions.Debug;

public class MicroTimerSystem
{
	[Init]
	public GameLoopDelegate Init(GameLoopDelegate next) => dt =>
	{
		using var _ = MicroTimer.Start("Init");
		next(dt);
	};

	[First]
	public GameLoopDelegate First(GameLoopDelegate next) => dt =>
	{
		using var _ = MicroTimer.Start("First");
		next(dt);
	};

	[Update]
	public GameLoopDelegate Update(GameLoopDelegate next) => dt =>
	{
		using var _ = MicroTimer.Start("Update");
		next(dt);
	};

	[Render]
	public GameLoopDelegate Render(GameLoopDelegate next) => dt =>
	{
		using var _ = MicroTimer.Start("Render");
		next(dt);
	};

	[Last]
	public GameLoopDelegate Last(GameLoopDelegate next) => dt =>
	{
		using var _ = MicroTimer.Start("Last");
		next(dt);
	};

	[Destroy]
	public GameLoopDelegate Destroy(GameLoopDelegate next) => dt =>
	{
		var timer = MicroTimer.Start("Destroy");
		next(dt);

		timer.Dispose();
		MicroTimer.Export("./trace.json");
	};
}
