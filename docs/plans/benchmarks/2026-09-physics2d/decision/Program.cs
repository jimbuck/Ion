// The Stage 5b 2D physics decision benchmark. One scene, three engines:
//
//   box2d-native   Box2D v3.1.0, the C library (Box2D.NET.Bindings.Release 3.1.0, prebuilt natives for every RID)
//   box2d-managed  Box2D.NET 3.1.654 (ikpil), a line-by-line C# port of Box2D v3.1
//   aether         Aether.Physics2D 2.2.0 (the Farseer lineage, Box2D 2.x algorithms)
//
// The scene: a static box (floor and two walls) and N dynamic bodies (half boxes 0.5 x 0.5, half circles of radius 0.25)
// in a grid above it, falling under gravity -10 and piling up. The world is stepped 600 times with dt = 1/60 on one
// thread (Box2D: 4 substeps, its default; Aether: 8 velocity and 3 position iterations, its defaults). Printed per engine:
// setup time, total step time, mean and percentiles per step, the mean of the first and last 60 steps, and a hash of every
// body's position and rotation bits after the last step (to compare architectures).
//
// Usage: Physics2DDecision [bodies=10000] [steps=600] [engines=box2d-native,box2d-managed,aether] [repeats=3]

extern alias Managed;

using System.Diagnostics;
using System.Globalization;

using Native = Box2D.NET.Bindings.B2;
using MB = Managed::Box2D.NET;

var bodies = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 10_000;
var steps = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 600;
var engines = (args.Length > 2 ? args[2] : "box2d-native,box2d-managed,aether").Split(',');
var repeats = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 3;

Console.WriteLine($"# {bodies} bodies, {steps} steps, {repeats} repeats, {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}, {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}, AOT={!System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported}, {Environment.ProcessorCount} cpus");
Console.WriteLine("engine | repeat | setup ms | total step ms | mean ms/step | p50 | p95 | max | first 60 mean | last 60 mean | awake/total | hash");

foreach (var engine in engines)
{
	for (var r = 0; r < repeats; r++)
	{
		IScene scene = engine switch
		{
			"box2d-native" => new NativeScene(),
			"box2d-managed" => new ManagedScene(),
			"aether" => new AetherScene(),
			_ => throw new ArgumentException($"Unknown engine {engine}"),
		};

		var setup = Stopwatch.StartNew();
		scene.Build(bodies);
		setup.Stop();

		var times = new double[steps];
		for (var i = 0; i < steps; i++)
		{
			var t0 = Stopwatch.GetTimestamp();
			scene.Step(1f / 60f);
			times[i] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
		}

		var total = times.Sum();
		var sorted = times.Order().ToArray();
		var hash = scene.Hash();
		var awake = scene.Awake();
		scene.Dispose();

		Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
			$"{engine} | {r} | {setup.Elapsed.TotalMilliseconds:F1} | {total:F0} | {total / steps:F3} | {sorted[steps / 2]:F3} | {sorted[(int)(steps * 0.95)]:F3} | {sorted[^1]:F3} | {times.Take(60).Average():F3} | {times.Skip(steps - 60).Average():F3} | {awake}/{bodies} | {hash:X16}"));
	}
}

static class Layout
{
	public const float Size = 0.5f;
	public const float Spacing = 0.6f;

	public static int Columns(int count) => Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(count)));

	public static float HalfWidth(int count) => Columns(count) * Spacing / 2f + 1f;

	// The position of body i: a grid centered on x = 0, starting 1 unit above the floor.
	public static (float X, float Y) Position(int i, int count)
	{
		var columns = Columns(count);
		var column = i % columns;
		var row = i / columns;
		return ((column - (columns - 1) / 2f) * Spacing, 1f + row * Spacing);
	}

	public static float WallHeight(int count) => (count / Columns(count) + 2) * Spacing + 10f;

	public static ulong Mix(ulong hash, float value) => (hash ^ (uint)BitConverter.SingleToInt32Bits(value)) * 1099511628211UL;
}

interface IScene : IDisposable
{
	void Build(int count);
	void Step(float dt);
	ulong Hash();
	int Awake();
}

sealed unsafe class NativeScene : IScene
{
	private Native.WorldId _world;
	private Native.BodyId[] _bodies = [];

	public void Build(int count)
	{
		var worldDef = Native.DefaultWorldDef();
		worldDef.gravity = new Native.Vec2 { x = 0, y = -10 };
		worldDef.workerCount = 1;
		_world = Native.CreateWorld(&worldDef);

		var groundDef = Native.DefaultBodyDef();
		var ground = Native.CreateBody(_world, &groundDef);
		var shapeDef = Native.DefaultShapeDef();
		var half = Layout.HalfWidth(count);
		var height = Layout.WallHeight(count);
		var floor = Native.MakeOffsetBox(half + 1, 0.5f, new Native.Vec2 { x = 0, y = -0.5f }, Native.MakeRot(0));
		Native.CreatePolygonShape(ground, &shapeDef, &floor);
		var left = Native.MakeOffsetBox(0.5f, height / 2, new Native.Vec2 { x = -half - 0.5f, y = height / 2 }, Native.MakeRot(0));
		Native.CreatePolygonShape(ground, &shapeDef, &left);
		var right = Native.MakeOffsetBox(0.5f, height / 2, new Native.Vec2 { x = half + 0.5f, y = height / 2 }, Native.MakeRot(0));
		Native.CreatePolygonShape(ground, &shapeDef, &right);

		_bodies = new Native.BodyId[count];
		var box = Native.MakeBox(Layout.Size / 2, Layout.Size / 2);
		var circle = new Native.Circle { center = default, radius = Layout.Size / 2 };
		for (var i = 0; i < count; i++)
		{
			var (x, y) = Layout.Position(i, count);
			var bodyDef = Native.DefaultBodyDef();
			bodyDef.type = Native.BodyType.dynamicBody;
			bodyDef.position = new Native.Vec2 { x = x, y = y };
			var body = Native.CreateBody(_world, &bodyDef);
			var def = Native.DefaultShapeDef();
			def.density = 1f;
			def.material.friction = 0.6f;
			if (i % 2 == 0) Native.CreatePolygonShape(body, &def, &box);
			else Native.CreateCircleShape(body, &def, &circle);
			_bodies[i] = body;
		}
	}

	public void Step(float dt) => Native.WorldStep(_world, dt, 4);

	public ulong Hash()
	{
		var hash = 14695981039346656037UL;
		foreach (var body in _bodies)
		{
			var p = Native.BodyGetPosition(body);
			var q = Native.BodyGetRotation(body);
			hash = Layout.Mix(Layout.Mix(Layout.Mix(Layout.Mix(hash, p.x), p.y), q.c), q.s);
		}

		return hash;
	}

	public int Awake() => _bodies.Count(Native.BodyIsAwake);

	public void Dispose() => Native.DestroyWorld(_world);
}

sealed class ManagedScene : IScene
{
	private MB.B2WorldId _world;
	private MB.B2BodyId[] _bodies = [];

	public void Build(int count)
	{
		var worldDef = MB.B2Types.b2DefaultWorldDef();
		worldDef.gravity = new MB.B2Vec2(0, -10);
		worldDef.workerCount = 1;
		_world = MB.B2Worlds.b2CreateWorld(in worldDef);

		var groundDef = MB.B2Types.b2DefaultBodyDef();
		var ground = MB.B2Bodies.b2CreateBody(_world, in groundDef);
		var shapeDef = MB.B2Types.b2DefaultShapeDef();
		var half = Layout.HalfWidth(count);
		var height = Layout.WallHeight(count);
		var floor = MB.B2Geometries.b2MakeOffsetBox(half + 1, 0.5f, new MB.B2Vec2(0, -0.5f), MB.B2MathFunction.b2MakeRot(0));
		MB.B2Shapes.b2CreatePolygonShape(ground, in shapeDef, in floor);
		var left = MB.B2Geometries.b2MakeOffsetBox(0.5f, height / 2, new MB.B2Vec2(-half - 0.5f, height / 2), MB.B2MathFunction.b2MakeRot(0));
		MB.B2Shapes.b2CreatePolygonShape(ground, in shapeDef, in left);
		var right = MB.B2Geometries.b2MakeOffsetBox(0.5f, height / 2, new MB.B2Vec2(half + 0.5f, height / 2), MB.B2MathFunction.b2MakeRot(0));
		MB.B2Shapes.b2CreatePolygonShape(ground, in shapeDef, in right);

		_bodies = new MB.B2BodyId[count];
		var box = MB.B2Geometries.b2MakeBox(Layout.Size / 2, Layout.Size / 2);
		var circle = new MB.B2Circle(new MB.B2Vec2(0, 0), Layout.Size / 2);
		for (var i = 0; i < count; i++)
		{
			var (x, y) = Layout.Position(i, count);
			var bodyDef = MB.B2Types.b2DefaultBodyDef();
			bodyDef.type = MB.B2BodyType.b2_dynamicBody;
			bodyDef.position = new MB.B2Vec2(x, y);
			var body = MB.B2Bodies.b2CreateBody(_world, in bodyDef);
			var def = MB.B2Types.b2DefaultShapeDef();
			def.density = 1f;
			def.material.friction = 0.6f;
			if (i % 2 == 0) MB.B2Shapes.b2CreatePolygonShape(body, in def, in box);
			else MB.B2Shapes.b2CreateCircleShape(body, in def, in circle);
			_bodies[i] = body;
		}
	}

	public void Step(float dt) => MB.B2Worlds.b2World_Step(_world, dt, 4);

	public ulong Hash()
	{
		var hash = 14695981039346656037UL;
		foreach (var body in _bodies)
		{
			var p = MB.B2Bodies.b2Body_GetPosition(body);
			var q = MB.B2Bodies.b2Body_GetRotation(body);
			hash = Layout.Mix(Layout.Mix(Layout.Mix(Layout.Mix(hash, p.X), p.Y), q.c), q.s);
		}

		return hash;
	}

	public int Awake() => _bodies.Count(MB.B2Bodies.b2Body_IsAwake);

	public void Dispose() => MB.B2Worlds.b2DestroyWorld(_world);
}

sealed class AetherScene : IScene
{
	private nkast.Aether.Physics2D.Dynamics.World _world = null!;
	private nkast.Aether.Physics2D.Dynamics.Body[] _bodies = [];

	public void Build(int count)
	{
		_world = new nkast.Aether.Physics2D.Dynamics.World(new nkast.Aether.Physics2D.Common.Vector2(0, -10));
		// Single-threaded, like the Box2D runs.
		_world.ContactManager.VelocityConstraintsMultithreadThreshold = int.MaxValue;
		_world.ContactManager.PositionConstraintsMultithreadThreshold = int.MaxValue;
		_world.ContactManager.CollideMultithreadThreshold = int.MaxValue;

		var half = Layout.HalfWidth(count);
		var height = Layout.WallHeight(count);
		var ground = _world.CreateBody();
		ground.CreateRectangle(2 * (half + 1), 1f, 1f, new nkast.Aether.Physics2D.Common.Vector2(0, -0.5f));
		ground.CreateRectangle(1f, height, 1f, new nkast.Aether.Physics2D.Common.Vector2(-half - 0.5f, height / 2));
		ground.CreateRectangle(1f, height, 1f, new nkast.Aether.Physics2D.Common.Vector2(half + 0.5f, height / 2));

		_bodies = new nkast.Aether.Physics2D.Dynamics.Body[count];
		for (var i = 0; i < count; i++)
		{
			var (x, y) = Layout.Position(i, count);
			var body = _world.CreateBody(new nkast.Aether.Physics2D.Common.Vector2(x, y), 0, nkast.Aether.Physics2D.Dynamics.BodyType.Dynamic);
			var fixture = i % 2 == 0
				? body.CreateRectangle(Layout.Size, Layout.Size, 1f, nkast.Aether.Physics2D.Common.Vector2.Zero)
				: body.CreateCircle(Layout.Size / 2, 1f);
			fixture.Friction = 0.6f;
			_bodies[i] = body;
		}
	}

	public void Step(float dt) => _world.Step(dt);

	public ulong Hash()
	{
		var hash = 14695981039346656037UL;
		foreach (var body in _bodies)
		{
			var t = body.GetTransform();
			hash = Layout.Mix(Layout.Mix(Layout.Mix(Layout.Mix(hash, t.p.X), t.p.Y), t.q.R), t.q.i);
		}

		return hash;
	}

	public int Awake() => _bodies.Count(b => b.Awake);

	public void Dispose() => _world.Clear();
}
