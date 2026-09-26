namespace Ion.Extensions.Scenes;

/// <summary>
/// A loaded scene: its id and its bound <see cref="Ion.Schedule"/>.
/// </summary>
public class SceneInstance
{
	/// <summary>The scene id.</summary>
	public int Id { get; }

	/// <summary>The scene name (<c>Scene{Id}</c>).</summary>
	public string Name { get; }

	/// <summary>The scene's schedule.</summary>
	public Schedule Schedule { get; }

	/// <summary>Runs the scene's Init stage.</summary>
	public GameLoopDelegate Init => Schedule.Init;
	/// <summary>Runs the scene's First stage.</summary>
	public GameLoopDelegate First => Schedule.First;
	/// <summary>Runs the scene's Update stage.</summary>
	public GameLoopDelegate Update => Schedule.Update;
	/// <summary>Runs one of the scene's FixedUpdate steps.</summary>
	public GameLoopDelegate FixedUpdate => Schedule.FixedUpdate;
	/// <summary>Runs the scene's Render stage.</summary>
	public GameLoopDelegate Render => Schedule.Render;
	/// <summary>Runs the scene's Last stage.</summary>
	public GameLoopDelegate Last => Schedule.Last;
	/// <summary>Runs the scene's Destroy stage.</summary>
	public GameLoopDelegate Destroy => Schedule.Destroy;

	internal SceneInstance(int id, Schedule schedule)
	{
		Id = id;
		Name = $"Scene{id}";
		Schedule = schedule;
	}
}
