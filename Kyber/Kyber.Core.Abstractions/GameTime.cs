namespace Kyber;

public class GameTime
{
	/// <summary>
	/// The frame index, starting at zero and incrementing for each rendered frame.
	/// </summary>
	public uint Frame { get; set; }

	/// <summary>
	/// The time in seconds since the last update.
	/// </summary>
	public float Delta { get; set; }

	/// <summary>
	/// The interpolation ratio of the remaining accumulated time over the fixed time. Ranges from 0 to 1 inclusive. Defaults to 1 for standard frames.
	/// </summary>
	public float Alpha { get; set; }

	/// <summary>
	/// The total elapsed time.
	/// </summary>
	public TimeSpan Elapsed { get; set; }

	/// <summary>
	/// Treats gametime as a float delta in seconds.
	/// </summary>
	/// <param name="time"></param>
	public static implicit operator float(GameTime time) => time.Delta;
}
