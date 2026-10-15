using System.Numerics;

using Ion.Extensions.Assets;

namespace Ion.Extensions.Graphics;

/// <summary>
/// The triangles of a model on the CPU, without materials or GPU resources: every triangle primitive of every node of the
/// model's default scene, placed by the node's model matrix, merged into one triangle list in model space (front faces
/// counterclockwise, as drawn). Load it from the same glTF file as the <see cref="IModel"/>
/// (<c>Load&lt;ModelGeometry&gt;("level.glb")</c>, with the 3D rendering module) for collision geometry, for example
/// <c>IPhysicsWorld3D.CreateMesh(geometry.Positions, geometry.Indices)</c>.
/// </summary>
public sealed class ModelGeometry : IAsset
{
	private static long _nextId;

	/// <summary>Creates model geometry from positions and triangle-list indices.</summary>
	public ModelGeometry(string name, Vector3[] positions, uint[] indices)
	{
		ArgumentNullException.ThrowIfNull(positions);
		ArgumentNullException.ThrowIfNull(indices);
		Name = name;
		Positions = positions;
		Indices = indices;
		Bounds = Aabb.FromPoints(positions);
	}

	/// <inheritdoc/>
	public nint Id { get; } = (nint)Interlocked.Increment(ref _nextId);

	/// <inheritdoc/>
	public string Name { get; }

	/// <summary>The positions, in model space.</summary>
	public Vector3[] Positions { get; }

	/// <summary>Triangle-list indices into <see cref="Positions"/>.</summary>
	public uint[] Indices { get; }

	/// <summary>The number of triangles.</summary>
	public int TriangleCount => Indices.Length / 3;

	/// <summary>The bounds of the positions.</summary>
	public Aabb Bounds { get; }

	/// <summary>Nothing to release (the arrays are managed).</summary>
	public void Dispose()
	{
	}
}
