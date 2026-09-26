using System.Numerics;
using System.Runtime.CompilerServices;

namespace Ion.Extensions.Graphics.Abstractions.Tests;

/// <summary>The 3D data types: transforms, bounds, frusta, cameras, primitives and mesh data.</summary>
public class ThreeDTypesTests
{
	private static void Near(Vector3 expected, Vector3 actual, float epsilon = 1e-4f) =>
		Assert.True(Vector3.Distance(expected, actual) < epsilon, $"Expected {expected}, got {actual}.");

	[Fact]
	public void TransformToMatrixScalesThenRotatesThenTranslates()
	{
		var t = new Transform(new Vector3(1, 2, 3), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2), new Vector3(2, 1, 1));
		var expected = Matrix4x4.CreateScale(t.Scale) * Matrix4x4.CreateFromQuaternion(t.Rotation) * Matrix4x4.CreateTranslation(t.Position);
		var actual = t.ToMatrix();
		for (var i = 0; i < 16; i++) Assert.Equal(expected[i / 4, i % 4], actual[i / 4, i % 4], 5);

		// +X scaled by 2, turned 90 degrees about y (to -Z), then moved.
		Near(new Vector3(1, 2, 1), Vector3.Transform(Vector3.UnitX, actual));
		Near(t.TransformPoint(Vector3.UnitX), Vector3.Transform(Vector3.UnitX, actual));
	}

	[Fact]
	public void NewTransformIsTheIdentityAndDefaultIsNot()
	{
		Assert.Equal(Matrix4x4.Identity, new Transform().ToMatrix());
		Assert.Equal(Matrix4x4.Identity, Transform.Identity.ToMatrix());
		Assert.NotEqual(Matrix4x4.Identity, default(Transform).ToMatrix());
	}

	[Fact]
	public void LookAtPointsTheMinusZAxisAtTheTarget()
	{
		var t = Transform.LookAt(new Vector3(0, 5, 10), new Vector3(0, 0, 0));
		Near(Vector3.Normalize(new Vector3(0, -5, -10)), t.Forward);
		Assert.True(t.Up.Y > 0);
		Near(Vector3.UnitX, t.Right);

		// Looking straight down still gives an orthonormal frame.
		var down = Transform.LookAt(Vector3.Zero, -Vector3.UnitY);
		Near(-Vector3.UnitY, down.Forward);
		Assert.Equal(0, Vector3.Dot(down.Forward, down.Up), 4);
	}

	[Fact]
	public void TransformFromMatrixRoundTrips()
	{
		var t = new Transform(new Vector3(-3, 4, 0.5f), Quaternion.Normalize(new Quaternion(0.2f, 0.4f, -0.1f, 0.9f)), new Vector3(1.5f, 2, 0.5f));
		var back = Transform.FromMatrix(t.ToMatrix());
		Near(t.Position, back.Position);
		Near(t.Scale, back.Scale);
		Assert.True(MathF.Abs(Quaternion.Dot(t.Rotation, back.Rotation)) > 0.9999f);
	}

	[Fact]
	public void TheComponentsAreUnmanaged()
	{
		Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<Transform>());
		Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<Camera>());
		Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<MeshRenderer>());
		Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<DirectionalLight>());
		Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<PointLight>());
		Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<SpotLight>());
		Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<SceneEnvironment>());
		Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<UnlitMaterial>());
		Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<PbrMaterial>());
		Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<Aabb>());
		Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<Frustum>());
		Assert.Equal(40, Unsafe.SizeOf<Transform>());
		Assert.Equal(MeshVertex.Size, Unsafe.SizeOf<MeshVertex>());
	}

	[Fact]
	public void AabbTransformBoundsTheTransformedCorners()
	{
		var box = new Aabb(new Vector3(-1, -2, -3), new Vector3(1, 2, 3));
		var matrix = Matrix4x4.CreateScale(2, 1, 0.5f) * Matrix4x4.CreateFromYawPitchRoll(0.7f, 0.3f, -0.2f) * Matrix4x4.CreateTranslation(5, -1, 2);
		Span<Vector3> corners = stackalloc Vector3[8];
		box.GetCorners(corners);
		var exact = Aabb.Empty;
		foreach (var corner in corners) exact.Encapsulate(Vector3.Transform(corner, matrix));

		var fast = box.Transform(matrix);
		Near(exact.Min, fast.Min);
		Near(exact.Max, fast.Max);
	}

	[Fact]
	public void AabbHelpers()
	{
		var box = Aabb.FromPoints([new Vector3(1, 2, 3), new Vector3(-1, 0, 5)]);
		Assert.Equal(new Vector3(-1, 0, 3), box.Min);
		Assert.Equal(new Vector3(1, 2, 5), box.Max);
		Assert.Equal(new Vector3(0, 1, 4), box.Center);
		Assert.True(box.Contains(new Vector3(0, 1, 4)));
		Assert.False(box.Contains(new Vector3(0, 3, 4)));
		Assert.True(box.Intersects(new Aabb(new Vector3(1, 2, 5), new Vector3(9, 9, 9))));
		Assert.False(Aabb.Empty.IsValid);
		Assert.Equal(box, Aabb.Union(box, Aabb.Empty));
		var sphere = BoundingSphere.FromAabb(box);
		Assert.Equal(box.Center, sphere.Center);
		Assert.Equal(box.Extents.Length(), sphere.Radius, 5);
	}

	[Fact]
	public void FrustumCullsBoxesOutsideAnyPlane()
	{
		var view = Camera.GetViewMatrix(Transform.LookAt(Vector3.Zero, -Vector3.UnitZ));
		var projection = new Camera { Near = 1, Far = 100 }.GetProjectionMatrix(1f);
		var frustum = Frustum.FromMatrix(view * projection);

		Assert.True(frustum.Intersects(Aabb.FromCenterExtents(new Vector3(0, 0, -10), Vector3.One)));
		Assert.False(frustum.Intersects(Aabb.FromCenterExtents(new Vector3(0, 0, 10), Vector3.One)), "behind the camera");
		Assert.False(frustum.Intersects(Aabb.FromCenterExtents(new Vector3(0, 0, -200), Vector3.One)), "beyond the far plane");
		Assert.False(frustum.Intersects(Aabb.FromCenterExtents(new Vector3(0, 0, -0.2f), new Vector3(0.1f))), "before the near plane");
		Assert.False(frustum.Intersects(Aabb.FromCenterExtents(new Vector3(30, 0, -10), Vector3.One)), "right of the 60 degree view");
		Assert.True(frustum.Intersects(Aabb.FromCenterExtents(new Vector3(6.2f, 0, -10), Vector3.One)), "straddling the right plane");
		Assert.True(frustum.Contains(new Vector3(0, 0, -50)));
		Assert.False(frustum.Contains(new Vector3(0, 0, 50)));
		Assert.True(frustum.Intersects(new BoundingSphere(new Vector3(0, 0, -0.8f), 0.5f)), "a sphere crossing the near plane");
		Assert.False(frustum.Intersects(new BoundingSphere(new Vector3(0, 0, 5f), 1f)));
	}

	[Fact]
	public void FrustumCornersInvertTheProjection()
	{
		var viewProjection = Camera.GetViewMatrix(new Transform(new Vector3(1, 2, 3))) * new Camera { Near = 1, Far = 10 }.GetProjectionMatrix(2f);
		Span<Vector3> corners = stackalloc Vector3[8];
		Frustum.GetCorners(viewProjection, corners);
		// Near corners at z = 3 - 1, far corners at z = 3 - 10.
		for (var i = 0; i < 4; i++) Assert.Equal(2f, corners[i].Z, 3);
		for (var i = 4; i < 8; i++) Assert.Equal(-7f, corners[i].Z, 3);
		var frustum = Frustum.FromMatrix(viewProjection);
		foreach (var corner in corners)
		{
			Assert.True(frustum.Intersects(new BoundingSphere(corner, 1e-3f)));
		}
	}

	[Fact]
	public void CameraDefaultsApplyToZeroValues()
	{
		var zero = default(Camera);
		Assert.Equal(0.1f, zero.EffectiveNear);
		Assert.Equal(1000f, zero.EffectiveFar);
		Assert.Equal(new RectangleF(0, 0, 1, 1), zero.EffectiveViewport);
		var projection = zero.GetProjectionMatrix(16 / 9f);
		Assert.Equal(new Camera().GetProjectionMatrix(16 / 9f), projection);

		var ortho = Camera.CreateOrthographic(5, 1, 50).GetProjectionMatrix(2f);
		var top = Vector4.Transform(new Vector4(0, 5, -10, 1), ortho);
		Assert.Equal(1f, top.Y / top.W, 5);
		var right = Vector4.Transform(new Vector4(10, 0, -10, 1), ortho);
		Assert.Equal(1f, right.X / right.W, 5);
	}

	[Fact]
	public void CameraViewMatrixIgnoresScaleAndInvertsThePlacement()
	{
		var placement = new Transform(new Vector3(3, 4, 5), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.8f), new Vector3(3));
		var fromWorld = Camera.GetViewMatrix(placement.ToMatrix());
		var fromTransform = Camera.GetViewMatrix(placement);
		for (var i = 0; i < 16; i++) Assert.Equal(fromTransform[i / 4, i % 4], fromWorld[i / 4, i % 4], 4);
		Near(Vector3.Zero, Vector3.Transform(placement.Position, fromWorld));
		Near(-Vector3.UnitZ, Vector3.Transform(placement.Position + placement.Forward, fromWorld));
	}

	public static TheoryData<string> Primitives => ["Cube", "Sphere", "Plane", "Cylinder"];

	private static MeshData Primitive(string name) => name switch
	{
		"Cube" => MeshPrimitives.Cube(2),
		"Sphere" => MeshPrimitives.Sphere(1, 24, 12),
		"Plane" => MeshPrimitives.Plane(2, 4),
		_ => MeshPrimitives.Cylinder(1, 2, 24),
	};

	[Theory]
	[MemberData(nameof(Primitives))]
	public void PrimitivesWindCounterClockwiseOutwardWithUnitNormalsAndTangents(string name)
	{
		var mesh = Primitive(name);
		mesh.Validate();
		Assert.Equal(VertexAttributes.Position | VertexAttributes.Normal | VertexAttributes.Tangent | VertexAttributes.Uv0, mesh.Attributes);
		for (var i = 0; i < mesh.Indices.Length; i += 3)
		{
			var a = mesh.Positions[mesh.Indices[i]];
			var b = mesh.Positions[mesh.Indices[i + 1]];
			var c = mesh.Positions[mesh.Indices[i + 2]];
			var faceNormal = Vector3.Cross(b - a, c - a);
			if (faceNormal.LengthSquared() < 1e-10f) continue;
			// Outward: agrees with the vertex normals (for the plane: +Y).
			var vertexNormal = mesh.Normals![mesh.Indices[i]] + mesh.Normals[mesh.Indices[i + 1]] + mesh.Normals[mesh.Indices[i + 2]];
			Assert.True(Vector3.Dot(faceNormal, vertexNormal) > 0, $"{name} triangle {i / 3} winds inward.");
		}

		for (var i = 0; i < mesh.VertexCount; i++)
		{
			Assert.Equal(1f, mesh.Normals![i].Length(), 3);
			var tangent = mesh.Tangents![i];
			Assert.Equal(1f, new Vector3(tangent.X, tangent.Y, tangent.Z).Length(), 3);
			Assert.Equal(0f, Vector3.Dot(mesh.Normals[i], new Vector3(tangent.X, tangent.Y, tangent.Z)), 3);
			Assert.True(tangent.W is 1f or -1f);
		}
	}

	[Fact]
	public void PrimitiveBoundsMatchTheirSize()
	{
		Assert.Equal(new Aabb(new Vector3(-1), new Vector3(1)), MeshPrimitives.Cube(2).ComputeBounds());
		var sphere = MeshPrimitives.Sphere(1.5f).ComputeBounds();
		Near(new Vector3(-1.5f), sphere.Min, 1e-3f);
		Near(new Vector3(1.5f), sphere.Max, 1e-3f);
		var plane = MeshPrimitives.Plane(4).ComputeBounds();
		Assert.Equal(new Aabb(new Vector3(-2, 0, -2), new Vector3(2, 0, 2)), plane);
		var cylinder = MeshPrimitives.Cylinder(0.5f, 3).ComputeBounds();
		Assert.Equal(-1.5f, cylinder.Min.Y, 5);
		Assert.Equal(1.5f, cylinder.Max.Y, 5);
		Assert.Equal(36, MeshPrimitives.Cube().Indices.Length);
		Assert.Equal(24, MeshPrimitives.Cube().VertexCount);
	}

	[Fact]
	public void MeshDataValidationNamesTheProblem()
	{
		var mesh = new MeshData("bad", [Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [0, 1, 3]);
		Assert.Contains("outside the 3 vertices", Assert.Throws<InvalidOperationException>(mesh.Validate).Message);
		mesh.Indices = [0, 1];
		Assert.Contains("whole number of triangles", Assert.Throws<InvalidOperationException>(mesh.Validate).Message);
		mesh.Indices = [0, 1, 2];
		mesh.SubMeshes = [new SubMesh(0, 3)];
		mesh.Normals = [Vector3.UnitZ];
		Assert.Contains("Normals has 1 entries", Assert.Throws<InvalidOperationException>(mesh.Validate).Message);
	}

	[Fact]
	public void ComputedTangentsFollowTheTextureU()
	{
		// A quad in the XY plane facing +Z, u along +X and v growing down (-Y): tangent +X, bitangent (increasing v) -Y.
		var mesh = new MeshData("quad", [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0)], [0, 1, 2, 0, 2, 3])
		{
			Uv0 = [new(0, 1), new(1, 1), new(1, 0), new(0, 0)],
		};
		mesh.ComputeTangents();
		Near(Vector3.UnitZ, mesh.Normals![0]);
		var t = mesh.Tangents![0];
		Near(Vector3.UnitX, new Vector3(t.X, t.Y, t.Z));
		// cross(n, t) = cross(+Z, +X) = +Y, but the bitangent is -Y: w = -1.
		Assert.Equal(-1f, t.W);
	}

	[Fact]
	public void ModelNodeLinkResolvesChildrenAndModelMatrices()
	{
		var nodes = new[]
		{
			new ModelNode("child", 1, new Transform(new Vector3(1, 0, 0)), []),
			new ModelNode("root", -1, new Transform(new Vector3(0, 10, 0), null, new Vector3(2)), []),
		};
		ModelNode.Link(nodes);
		Assert.Equal([0], nodes[1].Children);
		Near(new Vector3(2, 10, 0), nodes[0].ModelMatrix.Translation);
	}
}
