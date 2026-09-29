using System.Numerics;

namespace Ion.Extensions.Graphics;

/// <summary>
/// Builders of primitive meshes (cube, sphere, plane, cylinder) with normals, tangents and texture coordinates, centered
/// on the origin, front faces counter-clockwise seen from outside (the RHI's default front face).
/// </summary>
public static class MeshPrimitives
{
	/// <summary>An axis-aligned cube of edge <paramref name="size"/>: 24 vertices (flat faces), each face's UVs covering the whole texture.</summary>
	public static MeshData Cube(float size = 1f)
	{
		var h = size * 0.5f;
		// (normal, u, v) per face, with u x v = normal so (-1,-1), (1,-1), (1,1), (-1,1) wind counter-clockwise.
		ReadOnlySpan<(Vector3 N, Vector3 U, Vector3 V)> faces =
		[
			(Vector3.UnitX, -Vector3.UnitZ, Vector3.UnitY),
			(-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
			(Vector3.UnitY, Vector3.UnitX, -Vector3.UnitZ),
			(-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
			(Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY),
			(-Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY),
		];

		var positions = new Vector3[24];
		var normals = new Vector3[24];
		var uvs = new Vector2[24];
		var indices = new uint[36];
		ReadOnlySpan<Vector2> corners = [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)];
		for (var f = 0; f < 6; f++)
		{
			var (n, u, v) = faces[f];
			for (var c = 0; c < 4; c++)
			{
				var i = f * 4 + c;
				positions[i] = (n + u * corners[c].X + v * corners[c].Y) * h;
				normals[i] = n;
				uvs[i] = new Vector2((corners[c].X + 1) * 0.5f, (1 - corners[c].Y) * 0.5f);
			}

			var b = (uint)(f * 4);
			indices[f * 6 + 0] = b;
			indices[f * 6 + 1] = b + 1;
			indices[f * 6 + 2] = b + 2;
			indices[f * 6 + 3] = b;
			indices[f * 6 + 4] = b + 2;
			indices[f * 6 + 5] = b + 3;
		}

		var mesh = new MeshData("Cube", positions, indices) { Normals = normals, Uv0 = uvs };
		mesh.ComputeTangents();
		return mesh;
	}

	/// <summary>
	/// A UV sphere of <paramref name="radius"/> with <paramref name="segments"/> around the equator and
	/// <paramref name="rings"/> from pole to pole (u around, v from the north pole down).
	/// </summary>
	public static MeshData Sphere(float radius = 0.5f, int segments = 32, int rings = 16)
	{
		segments = Math.Max(3, segments);
		rings = Math.Max(2, rings);
		var count = (segments + 1) * (rings + 1);
		var positions = new Vector3[count];
		var normals = new Vector3[count];
		var uvs = new Vector2[count];
		for (var r = 0; r <= rings; r++)
		{
			var theta = MathF.PI * r / rings;
			var (sinTheta, cosTheta) = MathF.SinCos(theta);
			for (var s = 0; s <= segments; s++)
			{
				var phi = MathF.Tau * s / segments;
				var (sinPhi, cosPhi) = MathF.SinCos(phi);
				// Around the y axis; phi grows towards -z so that u grows counter-clockwise seen from above... and faces wind outward.
				var n = new Vector3(sinTheta * cosPhi, cosTheta, -sinTheta * sinPhi);
				var i = r * (segments + 1) + s;
				normals[i] = n;
				positions[i] = n * radius;
				uvs[i] = new Vector2((float)s / segments, (float)r / rings);
			}
		}

		var indices = new List<uint>(segments * rings * 6);
		for (var r = 0; r < rings; r++)
		{
			for (var s = 0; s < segments; s++)
			{
				var a = (uint)(r * (segments + 1) + s);
				var b = a + (uint)segments + 1;
				// a (upper left), b (lower left), b + 1 (lower right), a + 1 (upper right); skip the degenerate pole triangles.
				if (r != 0) indices.AddRange([a, b, a + 1]);
				if (r != rings - 1) indices.AddRange([a + 1, b, b + 1]);
			}
		}

		var mesh = new MeshData("Sphere", positions, [.. indices]) { Normals = normals, Uv0 = uvs };
		mesh.ComputeTangents();
		return mesh;
	}

	/// <summary>
	/// A square plane of edge <paramref name="size"/> in the XZ plane facing +Y, split into <paramref name="subdivisions"/>
	/// quads per side (UVs from 0 to 1 over the plane, v growing towards +Z).
	/// </summary>
	public static MeshData Plane(float size = 1f, int subdivisions = 1)
	{
		subdivisions = Math.Max(1, subdivisions);
		var n = subdivisions + 1;
		var positions = new Vector3[n * n];
		var normals = new Vector3[n * n];
		var uvs = new Vector2[n * n];
		for (var z = 0; z < n; z++)
		{
			for (var x = 0; x < n; x++)
			{
				var u = (float)x / subdivisions;
				var v = (float)z / subdivisions;
				var i = z * n + x;
				positions[i] = new Vector3((u - 0.5f) * size, 0, (v - 0.5f) * size);
				normals[i] = Vector3.UnitY;
				uvs[i] = new Vector2(u, v);
			}
		}

		var indices = new uint[subdivisions * subdivisions * 6];
		var k = 0;
		for (var z = 0; z < subdivisions; z++)
		{
			for (var x = 0; x < subdivisions; x++)
			{
				var v00 = (uint)(z * n + x);
				var v10 = v00 + 1;
				var v01 = v00 + (uint)n;
				var v11 = v01 + 1;
				indices[k++] = v00;
				indices[k++] = v01;
				indices[k++] = v11;
				indices[k++] = v00;
				indices[k++] = v11;
				indices[k++] = v10;
			}
		}

		var mesh = new MeshData("Plane", positions, indices) { Normals = normals, Uv0 = uvs };
		mesh.ComputeTangents();
		return mesh;
	}

	/// <summary>
	/// A cylinder along the y axis of <paramref name="radius"/> and <paramref name="height"/> with <paramref name="segments"/>
	/// around, closed by flat caps (the side has smooth normals, the caps flat ones).
	/// </summary>
	public static MeshData Cylinder(float radius = 0.5f, float height = 1f, int segments = 32)
	{
		segments = Math.Max(3, segments);
		var h = height * 0.5f;
		var positions = new List<Vector3>();
		var normals = new List<Vector3>();
		var uvs = new List<Vector2>();
		var indices = new List<uint>();

		// Side: two rings, the seam duplicated for the UVs.
		for (var s = 0; s <= segments; s++)
		{
			var phi = MathF.Tau * s / segments;
			var (sin, cos) = MathF.SinCos(phi);
			var n = new Vector3(cos, 0, -sin);
			positions.Add(n * radius + new Vector3(0, h, 0));
			normals.Add(n);
			uvs.Add(new Vector2((float)s / segments, 0));
			positions.Add(n * radius - new Vector3(0, h, 0));
			normals.Add(n);
			uvs.Add(new Vector2((float)s / segments, 1));
		}

		for (var s = 0; s < segments; s++)
		{
			var top = (uint)(s * 2);
			var bottom = top + 1;
			var nextTop = top + 2;
			var nextBottom = top + 3;
			indices.AddRange([top, bottom, nextTop, nextTop, bottom, nextBottom]);
		}

		// Caps: a center and a ring each.
		foreach (var sign in (ReadOnlySpan<float>)[1f, -1f])
		{
			var center = (uint)positions.Count;
			var normal = new Vector3(0, sign, 0);
			positions.Add(normal * h);
			normals.Add(normal);
			uvs.Add(new Vector2(0.5f, 0.5f));
			for (var s = 0; s <= segments; s++)
			{
				var phi = MathF.Tau * s / segments;
				var (sin, cos) = MathF.SinCos(phi);
				positions.Add(new Vector3(cos * radius, sign * h, -sin * radius));
				normals.Add(normal);
				uvs.Add(new Vector2(0.5f + cos * 0.5f, 0.5f + sin * 0.5f * sign));
			}

			for (var s = 0; s < segments; s++)
			{
				var a = center + 1 + (uint)s;
				var b = a + 1;
				// Counter-clockwise seen from outside: around +y the ring goes counter-clockwise from above, so the top cap is
				// (center, a, b) and the bottom one (center, b, a).
				if (sign > 0) indices.AddRange([center, a, b]);
				else indices.AddRange([center, b, a]);
			}
		}

		var mesh = new MeshData("Cylinder", [.. positions], [.. indices]) { Normals = [.. normals], Uv0 = [.. uvs] };
		mesh.ComputeTangents();
		return mesh;
	}
}
