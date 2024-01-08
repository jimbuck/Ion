namespace Ion.Extensions.Scenes;

public class Scene
{
	public int Id { get; }
    public string Name { get; }

	public GameLoopDelegate Init { get; set; } = (dt) => { };
	public GameLoopDelegate First { get; set; } = (dt) => { };
	public GameLoopDelegate Update { get; set; } = (dt) => { };
	public GameLoopDelegate FixedUpdate { get; set; } = (dt) => { };
	public GameLoopDelegate Render { get; set; } = (dt) => { };
	public GameLoopDelegate Last { get; set; } = (dt) => { };
	public GameLoopDelegate Destroy { get; set; } = (dt) => { };

	internal Scene(int id)
    {
		Id = id;
		Name = $"Scene{id}";
    }
}
