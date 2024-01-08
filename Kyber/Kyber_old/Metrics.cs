namespace Kyber;

public interface IMetrics
{
	/// <summary>
	/// The current Updates Per Second.
	/// </summary>
	float UPS { get; }
	float UpdateTime { get; }
	float FPS { get; }
	float FrameTime { get; }

	Dictionary<string, float> Timings { get; }

	IGraphicsMetrics Graphics { get; }
}

public interface IGraphicsMetrics
{
	public int DrawCount { get; }
	public int SpriteCount { get; }
	public int PrimitiveCount { get; }
}

public class Metrics : IMetrics
{
	private GraphicsMetrics _graphicsMetrics = new();

	public float UPS { get; set; } = 0;
	public float UpdateTime { get; set; } = 0;

	public float FPS { get; set; } = 0;
	public float FrameTime { get; set; } = 0;

	public Dictionary<string, float> Timings { get; } = new();

	public IGraphicsMetrics Graphics => _graphicsMetrics;

	public void Reset()
	{
		UPS = 0;
		FPS = 0;
		_graphicsMetrics.Reset();
		foreach(var key in Timings.Keys) Timings[key] = 0f;
	}
}

public class GraphicsMetrics : IGraphicsMetrics
{
	public int DrawCount { get; set; } = 0;

	public int SpriteCount { get; set; } = 0;

	public int PrimitiveCount { get; set; } = 0;

	public void Reset()
	{
		DrawCount = 0;
		SpriteCount = 0;
		PrimitiveCount = 0;
	}
}
