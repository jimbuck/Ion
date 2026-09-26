using Arch.Core;

namespace Ion.Extensions.Ecs;

/// <summary>
/// Saves the entities of a <see cref="World"/> with their registered components, and loads them into a world. The ECS
/// module ships a JSON and a binary implementation (<c>JsonWorldSerializer</c>, <c>BinaryWorldSerializer</c>).
/// </summary>
/// <remarks>
/// Entity references in components (<see cref="Parent"/>, <see cref="Children"/>, and components registered with a codec
/// that maps entities) are saved as positions in the saved entity list and resolved to the new entities on load, so a
/// hierarchy survives a round trip into any world. Components without a registration are skipped.
/// </remarks>
public interface IWorldSerializer
{
	/// <summary>Writes every entity of <paramref name="world"/> and its registered components to <paramref name="stream"/>.</summary>
	void Serialize(World world, Stream stream);

	/// <summary>Creates the entities saved in <paramref name="stream"/> in <paramref name="world"/>; returns them in saved order.</summary>
	IReadOnlyList<Entity> Deserialize(Stream stream, World world);
}
