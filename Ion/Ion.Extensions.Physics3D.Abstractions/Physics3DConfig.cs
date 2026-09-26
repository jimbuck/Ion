using System.Numerics;

namespace Ion.Extensions.Physics3D;

/// <summary>
/// The 3D physics settings, bound from the <c>Ion:Physics3D</c> configuration section (for example
/// <c>--Ion:Physics3D:ThreadCount=4</c>) and adjustable in code with <c>AddPhysics3D(configure: ...)</c>.
/// </summary>
public sealed class Physics3DConfig
{
	/// <summary>The configuration section: <c>Ion:Physics3D</c>.</summary>
	public const string Section = "Ion:Physics3D";

	/// <summary>The gravity along x.</summary>
	public float GravityX { get; set; }

	/// <summary>The gravity along y (y is up: -9.81 by default).</summary>
	public float GravityY { get; set; } = -9.81f;

	/// <summary>The gravity along z.</summary>
	public float GravityZ { get; set; }

	/// <summary>The gravity as a vector.</summary>
	public Vector3 Gravity => new(GravityX, GravityY, GravityZ);

	/// <summary>The solver sub-steps per fixed step (BepuPhysics' substepping; more is stiffer and slower).</summary>
	public int SubSteps { get; set; } = 1;

	/// <summary>The velocity iterations per sub-step (8 by default).</summary>
	public int Iterations { get; set; } = 8;

	/// <summary>
	/// The number of threads that step the world. 0 or 1 (the default) steps it on the game thread, synchronously; more
	/// creates a BepuPhysics <c>ThreadDispatcher</c> with that many workers, which the step still waits for (the frame never
	/// goes async). Multithreaded steps are deterministic too (the simulation runs in its deterministic mode), but their results differ from single-threaded ones: a replay must use the same thread count.
	/// </summary>
	public int ThreadCount { get; set; }

	/// <summary>The speed under which a body counts as resting and may go to sleep (0 disables sleeping).</summary>
	public float SleepThreshold { get; set; } = 0.01f;

	/// <summary>Draws every collider as a translucent mesh through the 3D renderer.</summary>
	public bool DebugDraw { get; set; }
}
