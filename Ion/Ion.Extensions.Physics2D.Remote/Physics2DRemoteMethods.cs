using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

using Arch.Core;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Ion.Extensions.Ecs;
using Ion.Extensions.Remote;

using static Ion.Extensions.Remote.RemoteSchema;

namespace Ion.Extensions.Physics2D;

/// <summary>Registration of the 2D physics module's remote methods.</summary>
public static class Physics2DRemoteExtensions
{
	/// <summary>
	/// Adds <c>physics2d.bodies</c> (watchable) and <c>physics2d.raycast</c>, both read methods, to the remote protocol, over
	/// the root 2D physics world (<c>AddPhysics2D</c>). Does nothing when the remote module is compiled out
	/// (<see cref="RemoteFeature.IsSupported"/>). Safe to call more than once.
	/// </summary>
	public static IServiceCollection AddPhysics2DRemote(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);
		if (RemoteFeature.IsSupported)
		{
			services.TryAddEnumerable(ServiceDescriptor.Singleton<IRemoteMethodProvider, Physics2DRemoteMethods>(static sp => new Physics2DRemoteMethods(sp)));
		}

		return services;
	}
}

/// <summary>
/// The 2D physics module's remote methods, on the root physics world (the one the root schedule steps; scene worlds are
/// not exposed). They run on the game thread at the end of a frame and see the state of the last fixed step.
/// </summary>
public sealed class Physics2DRemoteMethods(IServiceProvider services) : IRemoteMethodProvider
{
	private static readonly QueryDescription Colliders = new QueryDescription().WithAll<Collider2D, Transform2D>();

	private PhysicsWorld2D Physics => services.GetService(typeof(PhysicsWorld2D)) as PhysicsWorld2D
		?? throw new RemoteException(RemoteErrorCodes.Unsupported, "The game has no 2D physics (AddPhysics2D).");

	private static JsonObject Vector(string description) => new()
	{
		["type"] = "array",
		["items"] = new JsonObject { ["type"] = "number" },
		["minItems"] = 2,
		["maxItems"] = 2,
		["description"] = description,
	};

	/// <inheritdoc/>
	public void Register(RemoteMethodRegistry methods)
	{
		ArgumentNullException.ThrowIfNull(methods);
		methods
			.Read("physics2d.bodies", "The 2D physics bodies (entities with a Collider2D): entity, name, body type, position, rotation, velocities, shape, sensor and layer, plus the world's gravity and step count.", Bodies,
				Object(
					("name?", String("Only entities whose EntityName matches: exact, or a prefix ending in '*'.")),
					("type?", String("Only bodies of this type: static, kinematic or dynamic.")),
					("limit?", Integer("At most this many bodies (default 1000)."))), watchable: true)
			.Read("physics2d.raycast", "Casts a ray in the 2D physics world and returns the closest hit (entity, name, point, normal, fraction) on a collider in the mask.", RayCast,
				Object(
					("origin", Vector("The start point [x, y] in world units.")),
					("translation?", Vector("The ray [dx, dy]; give this or 'to'.")),
					("to?", Vector("The end point [x, y]; give this or 'translation'.")),
					("mask?", Integer("The layer mask (default: every layer)."))));
	}

	private JsonNode Bodies(RemoteRequest request)
	{
		var physics = Physics;
		var world = physics.Entities;
		var name = request.GetOptionalString("name");
		var type = request.GetOptionalString("type")?.ToLowerInvariant();
		if (type is not (null or "static" or "kinematic" or "dynamic")) throw RemoteException.InvalidParams("Parameter 'type' must be static, kinematic or dynamic.");
		var limit = request.GetInt32("limit", 1000, 1, 100_000);

		var bodies = new JsonArray();
		var total = 0;
		world.Query(in Colliders, (Entity entity, ref Collider2D collider, ref Transform2D transform) =>
		{
			var entityName = world.TryGet<EntityName>(entity, out var n) ? n.Value : null;
			if (name is not null && !NameMatches(entityName, name)) return;
			var hasBody = world.TryGet<RigidBody2D>(entity, out var body);
			var bodyType = hasBody ? TypeName(body.Type) : "static";
			if (type is not null && type != bodyType) return;
			total++;
			if (bodies.Count >= limit) return;
			var item = new JsonObject
			{
				["entity"] = entity.Id,
				["name"] = entityName,
				["type"] = bodyType,
				["position"] = Pair(transform.Position),
				["rotation"] = transform.Rotation,
				["velocity"] = Pair(hasBody ? body.LinearVelocity : Vector2.Zero),
				["angularVelocity"] = hasBody ? body.AngularVelocity : 0f,
				["shape"] = ShapeName(collider.Shape),
				["sensor"] = collider.IsSensor,
				["layer"] = collider.Layer,
				["simulated"] = collider.HasBody,
			};
			bodies.Add((JsonNode)item);
		});

		return new JsonObject
		{
			["bodies"] = bodies,
			["total"] = total,
			["truncated"] = total > bodies.Count,
			["bodyCount"] = physics.BodyCount,
			["stepCount"] = physics.StepCount,
			["gravity"] = Pair(physics.Gravity),
		};
	}

	private JsonNode RayCast(RemoteRequest request)
	{
		var physics = Physics;
		var origin = ReadVector(request, "origin") ?? throw RemoteException.InvalidParams("Missing required parameter 'origin' ([x, y]).");
		var translation = ReadVector(request, "translation") ?? (ReadVector(request, "to") is { } to ? to - origin
			: throw RemoteException.InvalidParams("Give 'translation' ([dx, dy]) or 'to' ([x, y])."));
		var mask = request.GetOptionalInt64("mask") is { } m ? unchecked((uint)m) : uint.MaxValue;

		if (!physics.RayCast(origin, translation, out var hit, mask)) return new JsonObject { ["hit"] = false };
		var world = physics.Entities;
		return new JsonObject
		{
			["hit"] = true,
			["entity"] = hit.Entity.Id,
			["name"] = world.IsAlive(hit.Entity) && world.TryGet<EntityName>(hit.Entity, out var n) ? n.Value : null,
			["point"] = Pair(hit.Point),
			["normal"] = Pair(hit.Normal),
			["fraction"] = hit.Fraction,
		};
	}

	private static Vector2? ReadVector(RemoteRequest request, string name)
	{
		var array = request.GetArray(name);
		if (array is null) return null;
		if (array.Count != 2 || array[0] is not JsonValue x || array[1] is not JsonValue y || x.GetValueKind() != JsonValueKind.Number || y.GetValueKind() != JsonValueKind.Number)
		{
			throw RemoteException.InvalidParams($"Parameter '{name}' must be [x, y].");
		}

		return new Vector2((float)x.GetValue<double>(), (float)y.GetValue<double>());
	}

	private static JsonArray Pair(Vector2 v) => new(v.X, v.Y);

	private static bool NameMatches(string? name, string pattern) =>
		name is not null && (pattern.EndsWith('*') ? name.StartsWith(pattern[..^1], StringComparison.Ordinal) : name == pattern);

	private static string TypeName(RigidBodyType2D type) => type switch
	{
		RigidBodyType2D.Static => "static",
		RigidBodyType2D.Kinematic => "kinematic",
		_ => "dynamic",
	};

	private static string ShapeName(ColliderShape2D shape) => shape switch
	{
		ColliderShape2D.Box => "box",
		ColliderShape2D.Circle => "circle",
		ColliderShape2D.Capsule => "capsule",
		_ => "polygon",
	};
}
