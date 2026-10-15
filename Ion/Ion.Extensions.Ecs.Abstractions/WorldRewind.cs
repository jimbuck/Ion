using Arch.Core;

namespace Ion.Extensions.Ecs;

/// <summary>
/// A service that keeps its own copy of entity state (a physics engine's bodies) and follows a temporary rewind of a
/// world's components: lag compensation (<c>INetworkWorld.WithWorldAtTick</c>) sets the components back to an earlier
/// tick, tells every registered listener, runs its check (a raycast where the shooter saw the target), restores the
/// components, and tells them again. Register implementations as <see cref="IWorldRewindListener"/> singletons.
/// </summary>
public interface IWorldRewindListener
{
	/// <summary><paramref name="world"/>'s components now hold an earlier state: mirror it, so queries see it.</summary>
	void Rewound(World world);

	/// <summary><paramref name="world"/>'s components are back to the present: undo what <see cref="Rewound"/> did.</summary>
	void Restored(World world);
}
