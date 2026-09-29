using Arch.Core;

using Microsoft.Extensions.DependencyInjection;

namespace Ion.Extensions.Ecs;

/// <summary>
/// The worlds of an application: the root <see cref="World"/> (resolved from the root service provider, used by the root
/// schedule's systems) and one per service scope (each scene gets its own, disposed with the scene's scope). Resolving
/// <see cref="World"/>, <see cref="Commands"/> or <see cref="NameRegistry"/> from a provider returns the ones of that
/// provider's scope.
/// </summary>
/// <remarks>
/// <see cref="World"/> is registered as a transient whose factory returns the scope's world, rather than as a scoped
/// service: a scoped service cannot be resolved from the root provider (DI's scope validation, and Ion's ION006), and the
/// root schedule needs a world too. Worlds are created on first use.
/// </remarks>
public sealed class EcsWorlds : IDisposable
{
	private readonly IServiceProvider _root;
	private readonly List<World> _live = [];
	private EcsScope? _rootScope;
	private bool _disposed;

	/// <summary>Creates the registry for the application whose root provider is <paramref name="root"/>.</summary>
	public EcsWorlds(IServiceProvider root)
	{
		ArgumentNullException.ThrowIfNull(root);
		EcsComponents.RegisterBuiltIns();
		_root = root;
	}

	/// <summary>The root world (created on first use).</summary>
	public World Root => RootScope.World;

	/// <summary>The live worlds: the root one (once used) and those of the live scopes, in creation order.</summary>
	public IReadOnlyList<World> Worlds => _live;

	/// <summary>The number of live entities across <see cref="Worlds"/>.</summary>
	public int EntityCount
	{
		get
		{
			var total = 0;
			for (var i = 0; i < _live.Count; i++) total += _live[i].Size;
			return total;
		}
	}

	private EcsScope RootScope
	{
		get
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			return _rootScope ??= new EcsScope(this);
		}
	}

	/// <summary>The world of <paramref name="services"/>' scope (the root world for the root provider).</summary>
	public World WorldFor(IServiceProvider services) => ScopeFor(services).World;

	/// <summary>The commands of <paramref name="services"/>' scope.</summary>
	public Commands CommandsFor(IServiceProvider services) => ScopeFor(services).Commands;

	/// <summary>The name registry of <paramref name="services"/>' scope.</summary>
	public NameRegistry NamesFor(IServiceProvider services) => ScopeFor(services).Names;

	private EcsScope ScopeFor(IServiceProvider services)
	{
		ArgumentNullException.ThrowIfNull(services);
		return ReferenceEquals(services, _root) ? RootScope : services.GetRequiredService<EcsScope>();
	}

	internal World CreateWorld()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		var world = World.Create();
		_live.Add(world);
		return world;
	}

	internal void Release(World world)
	{
		if (_live.Remove(world)) world.Dispose();
	}

	/// <summary>Disposes the root world.</summary>
	public void Dispose()
	{
		if (_disposed) return;
		_rootScope?.Dispose();
		_disposed = true;
	}
}

/// <summary>The world, commands and names of one service scope (a scene), disposed with it.</summary>
internal sealed class EcsScope(EcsWorlds worlds) : IDisposable
{
	private World? _world;
	private Commands? _commands;
	private NameRegistry? _names;

	public World World => _world ??= worlds.CreateWorld();

	public Commands Commands => _commands ??= new Commands(World);

	public NameRegistry Names => _names ??= new NameRegistry(World);

	public void Dispose()
	{
		if (_world is { } world) worlds.Release(world);
	}
}
