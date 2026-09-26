using System.Numerics;

namespace Ion.Extensions.Physics2D;

/// <summary>
/// The 2D physics settings, bound from the <c>Ion:Physics2D</c> configuration section (for example
/// <c>--Ion:Physics2D:DebugDraw=true</c>) and adjustable in code with <c>AddPhysics2D(configure: ...)</c>.
/// </summary>
/// <remarks>
/// Every length and speed is in world units (the <c>Transform2D</c>'s). Box2D v3 has no velocity or position iteration
/// counts: it solves with sub-steps (its "soft step" solver), so <see cref="SubSteps"/> is the accuracy knob.
/// </remarks>
public sealed class Physics2DConfig
{
	/// <summary>The configuration section: <c>Ion:Physics2D</c>.</summary>
	public const string Section = "Ion:Physics2D";

	/// <summary>The horizontal gravity in world units per second squared.</summary>
	public float GravityX { get; set; }

	/// <summary>The vertical gravity in world units per second squared (positive is down on screen). Default 9.81.</summary>
	public float GravityY { get; set; } = 9.81f;

	/// <summary>The gravity as a vector.</summary>
	public Vector2 Gravity => new(GravityX, GravityY);

	/// <summary>
	/// How many world units make a meter (1 when the game works in meters; for a pixel game, the size in pixels of a one
	/// meter object, for example 100). Box2D's tolerances, speed limits and default thresholds are tuned for objects of 0.1
	/// to 10 meters; the physics world divides every length by this before handing it to Box2D.
	/// </summary>
	public float UnitsPerMeter { get; set; } = 1f;

	/// <summary>The solver sub-steps per fixed step (Box2D's recommended 4; more is stiffer and slower).</summary>
	public int SubSteps { get; set; } = 4;

	/// <summary>Whether resting bodies go to sleep (and cost nothing until something wakes them).</summary>
	public bool EnableSleep { get; set; } = true;

	/// <summary>Whether dynamic bodies use continuous collision against static bodies (no tunneling through walls).</summary>
	public bool EnableContinuous { get; set; } = true;

	/// <summary>The contact stiffness in hertz (Box2D's default 30).</summary>
	public float ContactHertz { get; set; } = 30f;

	/// <summary>The contact damping ratio (Box2D's default 10).</summary>
	public float ContactDampingRatio { get; set; } = 10f;

	/// <summary>The relative speed in world units per second below which contacts do not bounce (0: Box2D's default of 1 meter per second).</summary>
	public float RestitutionThreshold { get; set; }

	/// <summary>The maximum speed of a body in world units per second (0: Box2D's default of 400 meters per second).</summary>
	public float MaxLinearSpeed { get; set; }

	/// <summary>Draws every collider and joint over the frame (the debug drawing step in Render).</summary>
	public bool DebugDraw { get; set; }
}
