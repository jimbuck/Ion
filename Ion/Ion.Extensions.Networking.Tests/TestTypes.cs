using Arch.Core;

using Ion.Extensions.Networking;

namespace Ion.Extensions.Networking.Tests;

// The replicated components and messages of the tests. The networking generator writes their serializers and registers
// them from a module initializer of this assembly.

public enum Team : byte
{
	Red = 1,
	Blue = 2,
}

public record struct Stats(int Kills, float Accuracy);

[Replicated]
public record struct Health(int Current, int Max);

[Replicated, Interpolated]
public record struct Position(Vector2 Value);

[Replicated(Authority = Authority.Owner), Predicted]
public record struct Avatar(Vector2 Position);

[Replicated(Authority = Authority.Owner)]
public record struct Aim(float Angle);

[Replicated]
public record struct Profile(FixedString32 Name, Team Team, Stats Stats, Quaternion Facing, double Score, [property: NetworkIgnore] int LocalOnly);

[NetworkMessage(Direction = MessageDirection.ClientToServer, Delivery = Delivery.Unreliable)]
public record struct MoveInput(Vector2 Move);

[NetworkMessage(Direction = MessageDirection.ClientToServer)]
public record struct ChatRequest(FixedString64 Text);

[NetworkMessage]
public record struct Chat(byte From, FixedString64 Text);

[NetworkMessage(Direction = MessageDirection.Both, Delivery = Delivery.ReliableUnordered)]
public record struct Note(int Value);

internal static class TestWorlds
{
	// Every component type the tests store (NativeAOT would need them registered; the JIT does not, but ION204 wants to
	// see them used as components).
	public static Entity CreateAll(World world, int i) => world.Create(
		new Health(i, 100),
		new Position(new Vector2(i, -i)),
		new Profile("p" + i, Team.Red, new Stats(i, 0.5f), Quaternion.Identity, i * 1.5, 7));

	public static Entity CreateAvatar(World world, NetworkId id) => world.Create(id, new Avatar(Vector2.Zero), new Aim(0));
}
