namespace Kyber;

public class GameTime
{
	/// <summary>
	/// The frame index, starting at zero and incrementing for each rendered frame.
	/// </summary>
	public uint Frame { get; internal set; }

	/// <summary>
	/// The time in seconds since the last update. Typically a fixed timestep.
	/// </summary>
	public float Delta { get; internal set; }

	/// <summary>
	/// The interpolation ratio of the remaining accumulated time over the fixed time. Ranges from 0 to 1 inclusive. Defaults to 1 for standard frames.
	/// </summary>
	public float Alpha { get; internal set; }

	/// <summary>
	/// The total elapsed time.
	/// </summary>
	public TimeSpan Elapsed { get; internal set; }

	/// <summary>
	/// Treats gametime as a float delta in seconds.
	/// </summary>
	/// <param name="time"></param>
	public static implicit operator float(GameTime time) => time.Delta;
}
