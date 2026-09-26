using System.Numerics;

namespace Ion.Extensions.Graphics;

/// <summary>The vertex attributes a mesh's source data has.</summary>
[Flags]
public enum VertexAttributes
{
	/// <summary>None.</summary>
	None = 0,
	/// <summary>Positions (always present).</summary>
	Position = 1 << 0,
	/// <summary>Normals.</summary>
	Normal = 1 << 1,
	/// <summary>Tangents with the bitangent sign in w.</summary>
	Tangent = 1 << 2,
	/// <summary>First texture coordinates.</summary>
	Uv0 = 1 << 3,
	/// <summary>Second texture coordinates.</summary>
	Uv1 = 1 << 4,
	/// <summary>Vertex colors.</summary>
	Color = 1 << 5,
}

/// <summary>A range of a mesh's indices drawn as one piece (every sub-mesh is drawn with the renderer's material).</summary>
/// <param name="FirstIndex">The first index.</param>
/// <param name="IndexCount">The number of indices (three per triangle).</param>
/// <param name="BaseVertex">Added to every index of the range.</param>
public readonly record struct SubMesh(int FirstIndex, int IndexCount, int BaseVertex = 0);

/// <summary>
/// Mesh geometry on the CPU: vertex attributes as separate arrays, 32-bit triangle-list indices and sub-meshes. Hand it
/// to <see cref="IRenderer3D.CreateMesh"/>, which interleaves it into <see cref="MeshVertex"/>es on the GPU. Built by the
/// primitive builders and the glTF loader, or by hand.
/// </summary>
public sealed class MeshData
{
	/// <summary>Creates mesh data from positions and indices (one sub-mesh over every index when <paramref name="subMeshes"/> is null).</summary>
	public MeshData(string name, Vector3[] positions, uint[] indices, SubMesh[]? subMeshes = null)
	{
		ArgumentNullException.ThrowIfNull(positions);
		ArgumentNullException.ThrowIfNull(indices);
		Name = name;
		Positions = positions;
		Indices = indices;
		SubMeshes = subMeshes ?? [new SubMesh(0, indices.Length)];
	}

	/// <summary>A name for logs and labels.</summary>
	public string Name { get; set; }

	/// <summary>Positions.</summary>
	public Vector3[] Positions { get; set; }

	/// <summary>Normals (one per position), or null.</summary>
	public Vector3[]? Normals { get; set; }

	/// <summary>Tangents with the bitangent sign in w (one per position), or null.</summary>
	public Vector4[]? Tangents { get; set; }

	/// <summary>First texture coordinates (one per position), or null.</summary>
	public Vector2[]? Uv0 { get; set; }

	/// <summary>Second texture coordinates (one per position), or null.</summary>
	public Vector2[]? Uv1 { get; set; }

	/// <summary>Vertex colors (sRGB, one per position), or null.</summary>
	public Color[]? Colors { get; set; }

	/// <summary>Triangle-list indices.</summary>
	public uint[] Indices { get; set; }

	/// <summary>The sub-meshes.</summary>
	public SubMesh[] SubMeshes { get; set; }

	/// <summary>The number of vertices.</summary>
	public int VertexCount => Positions.Length;

	/// <summary>The number of triangles.</summary>
	public int TriangleCount => Indices.Length / 3;

	/// <summary>The attributes present.</summary>
	public VertexAttributes Attributes =>
		VertexAttributes.Position
		| (Normals is not null ? VertexAttributes.Normal : 0)
		| (Tangents is not null ? VertexAttributes.Tangent : 0)
		| (Uv0 is not null ? VertexAttributes.Uv0 : 0)
		| (Uv1 is not null ? VertexAttributes.Uv1 : 0)
		| (Colors is not null ? VertexAttributes.Color : 0);

	/// <summary>The bounding box of the positions.</summary>
	public Aabb ComputeBounds() => Aabb.FromPoints(Positions);

	/// <summary>The bounding sphere of the positions.</summary>
	public BoundingSphere ComputeSphere() => BoundingSphere.FromPoints(Positions);

	/// <summary>
	/// Checks that every attribute array has one entry per position, that the index count is a multiple of three and every
	/// index (plus its sub-mesh's base vertex) is in range.
	/// </summary>
	/// <exception cref="InvalidOperationException">The data is inconsistent; the message says how.</exception>
	public void Validate()
	{
		var n = Positions.Length;
		Check(Normals?.Length, nameof(Normals));
		Check(Tangents?.Length, nameof(Tangents));
		Check(Uv0?.Length, nameof(Uv0));
		Check(Uv1?.Length, nameof(Uv1));
		Check(Colors?.Length, nameof(Colors));
		if (Indices.Length % 3 != 0) throw new InvalidOperationException($"Mesh '{Name}': {Indices.Length} indices is not a whole number of triangles.");
		foreach (var sub in SubMeshes)
		{
			if (sub.FirstIndex < 0 || sub.IndexCount < 0 || sub.FirstIndex + sub.IndexCount > Indices.Length)
			{
				throw new InvalidOperationException($"Mesh '{Name}': sub-mesh {sub} is outside the {Indices.Length} indices.");
			}

			for (var i = sub.FirstIndex; i < sub.FirstIndex + sub.IndexCount; i++)
			{
				var index = (long)Indices[i] + sub.BaseVertex;
				if (index < 0 || index >= n) throw new InvalidOperationException($"Mesh '{Name}': index {Indices[i]} at {i} (base vertex {sub.BaseVertex}) is outside the {n} vertices.");
			}
		}

		void Check(int? length, string name)
		{
			if (length is { } l && l != n) throw new InvalidOperationException($"Mesh '{Name}': {name} has {l} entries for {n} positions.");
		}
	}

	/// <summary>Computes smooth normals: every vertex gets the area-weighted sum of its triangles' normals.</summary>
	public void ComputeNormals()
	{
		var normals = new Vector3[Positions.Length];
		ForEachTriangle((a, b, c) =>
		{
			var n = Vector3.Cross(Positions[b] - Positions[a], Positions[c] - Positions[a]);
			normals[a] += n;
			normals[b] += n;
			normals[c] += n;
		});
		for (var i = 0; i < normals.Length; i++)
		{
			normals[i] = normals[i].LengthSquared() > 0 ? Vector3.Normalize(normals[i]) : Vector3.UnitY;
		}

		Normals = normals;
	}

	/// <summary>
	/// Computes tangents from the positions, normals and first texture coordinates (per-triangle UV derivatives
	/// accumulated per vertex, Gram-Schmidt orthogonalized against the normal, handedness in w). Computes normals first
	/// when there are none; without texture coordinates every tangent is an axis orthogonal to the normal.
	/// </summary>
	public void ComputeTangents()
	{
		if (Normals is null) ComputeNormals();
		var normals = Normals!;
		var tangents = new Vector3[Positions.Length];
		var bitangents = new Vector3[Positions.Length];
		if (Uv0 is { } uv)
		{
			ForEachTriangle((a, b, c) =>
			{
				var e1 = Positions[b] - Positions[a];
				var e2 = Positions[c] - Positions[a];
				var d1 = uv[b] - uv[a];
				var d2 = uv[c] - uv[a];
				var det = d1.X * d2.Y - d2.X * d1.Y;
				if (MathF.Abs(det) < 1e-12f) return;
				var r = 1f / det;
				var t = (e1 * d2.Y - e2 * d1.Y) * r;
				var bt = (e2 * d1.X - e1 * d2.X) * r;
				tangents[a] += t; tangents[b] += t; tangents[c] += t;
				bitangents[a] += bt; bitangents[b] += bt; bitangents[c] += bt;
			});
		}

		var result = new Vector4[Positions.Length];
		for (var i = 0; i < result.Length; i++)
		{
			var n = normals[i];
			var t = tangents[i] - n * Vector3.Dot(n, tangents[i]);
			if (t.LengthSquared() < 1e-12f)
			{
				// No usable UV gradient: any axis orthogonal to the normal.
				t = Vector3.Cross(n, MathF.Abs(n.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX);
				t = Vector3.Cross(t, n);
			}

			t = Vector3.Normalize(t);
			var w = Vector3.Dot(Vector3.Cross(n, t), bitangents[i]) < 0 ? -1f : 1f;
			result[i] = new Vector4(t, w);
		}

		Tangents = result;
	}

	private void ForEachTriangle(Action<int, int, int> action)
	{
		foreach (var sub in SubMeshes)
		{
			for (var i = sub.FirstIndex; i + 2 < sub.FirstIndex + sub.IndexCount; i += 3)
			{
				action((int)Indices[i] + sub.BaseVertex, (int)Indices[i + 1] + sub.BaseVertex, (int)Indices[i + 2] + sub.BaseVertex);
			}
		}
	}
}
