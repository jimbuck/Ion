using Arch.Core;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.Ecs;

/// <summary>
/// Registers component types with Arch ahead of time. Arch creates a component's chunk arrays with
/// <c>Array.CreateInstance</c> unless the array type was registered (<see cref="ArrayRegistry"/>), which NativeAOT cannot
/// do for a type it did not see, so every component a game stores must be registered before the first entity with it is
/// created. The Ion generator registers the components of every <c>[Query]</c> method (a module initializer); the ECS
/// module registers its built-in components; register the others with <see cref="Register{T}"/>.
/// </summary>
public static class EcsComponents
{
	private static readonly Lock Gate = new();
	private static readonly HashSet<Type> Registered = [];

	/// <summary>Registers <typeparamref name="T"/> (safe to call more than once, from any thread).</summary>
	public static void Register<T>()
	{
		lock (Gate)
		{
			if (!Registered.Add(typeof(T))) return;
			ArrayRegistry.Add<T>();
			_ = Component<T>.ComponentType;
		}
	}

	/// <summary>Whether <typeparamref name="T"/> was registered through <see cref="Register{T}"/>.</summary>
	public static bool IsRegistered<T>()
	{
		lock (Gate) return Registered.Contains(typeof(T));
	}

	/// <summary>Registers the built-in components of the ECS module.</summary>
	public static void RegisterBuiltIns()
	{
		if (_builtIns) return;
		Register<Transform2D>();
		Register<GlobalTransform2D>();
		Register<Transform>();
		Register<GlobalTransform>();
		Register<Parent>();
		Register<Children>();
		Register<PendingParent>();
		Register<Sprite>();
		Register<SpriteAnimation>();
		Register<Aabb2D>();
		Register<Hidden>();
		Register<Visible>();
		Register<MainCamera>();
		Register<Camera2D>();
		Register<EntityName>();
		Register<MeshRenderer>();
		Register<Camera>();
		Register<DirectionalLight>();
		Register<PointLight>();
		Register<SpotLight>();
		Register<SceneEnvironment>();
		Register<PendingModel>();
		_builtIns = true;
	}

	private static volatile bool _builtIns;
}
