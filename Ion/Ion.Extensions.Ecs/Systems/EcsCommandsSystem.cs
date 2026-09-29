namespace Ion.Extensions.Ecs;

/// <summary>
/// Plays back the schedule's <see cref="Commands"/> (the root world's, or the scene's) at the end of every stage, at
/// <see cref="StageOrder.Ecs"/>: structural changes recorded by a stage's steps are applied once they all ran, before the
/// next stage (and before the next fixed step).
/// </summary>
public sealed class EcsCommandsSystem(Commands commands)
{
	/// <summary>The commands this system plays back.</summary>
	public Commands Commands => commands;

	/// <summary>Plays back the commands recorded during Init.</summary>
	[Init(Order = StageOrder.Ecs)]
	public void FlushInit(GameTime dt) => commands.Flush();

	/// <summary>Plays back the commands recorded during First.</summary>
	[First(Order = StageOrder.Ecs)]
	public void FlushFirst(GameTime dt) => commands.Flush();

	/// <summary>Plays back the commands recorded during a fixed step.</summary>
	[FixedUpdate(Order = StageOrder.Ecs)]
	public void FlushFixedUpdate(GameTime dt) => commands.Flush();

	/// <summary>Plays back the commands recorded during Update.</summary>
	[Update(Order = StageOrder.Ecs)]
	public void FlushUpdate(GameTime dt) => commands.Flush();

	/// <summary>Plays back the commands recorded during Render.</summary>
	[Render(Order = StageOrder.Ecs)]
	public void FlushRender(GameTime dt) => commands.Flush();

	/// <summary>Plays back the commands recorded during Last.</summary>
	[Last(Order = StageOrder.Ecs)]
	public void FlushLast(GameTime dt) => commands.Flush();

	/// <summary>Plays back the commands recorded during Destroy.</summary>
	[Destroy(Order = StageOrder.Ecs)]
	public void FlushDestroy(GameTime dt) => commands.Flush();
}

/// <summary>Writes the number of live entities of every world into <see cref="FrameStats.Entities"/>.</summary>
internal sealed class EcsStatsSource(EcsWorlds worlds) : IFrameStatsSource
{
	public void Collect(ref FrameStats stats) => stats.Entities = worlds.EntityCount;
}
