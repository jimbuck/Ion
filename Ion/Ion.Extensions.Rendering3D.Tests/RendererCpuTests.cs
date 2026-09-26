global using System.Numerics;

global using Microsoft.Extensions.DependencyInjection;

global using Xunit;

global using Ion.Extensions.Graphics;
global using Ion.Testing;

global using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Rendering3D.Tests;

/// <summary>
/// The renderer's CPU pipeline without a GPU (the CPU-only renderer): culling, sorting, batching, instance data,
/// statistics and allocations.
/// </summary>
public class RendererCpuTests
{
	private static Renderer3D NewRenderer(Rendering3DOptions? options = null) => new(null, options) { CpuTargetSize = (100, 100) };

	private static void LookDownMinusZ(Renderer3D renderer, float far = 100f) =>
		renderer.SetCamera(new Camera { Near = 0.5f, Far = far }, Transform.LookAt(Vector3.Zero, -Vector3.UnitZ));

	[Fact, Trait(CATEGORY, UNIT)]
	public void FrustumCullingDrawsOnlyWhatTheCameraSees()
	{
		using var renderer = NewRenderer();
		var cube = renderer.CreateMesh(MeshPrimitives.Cube());
		var material = renderer.CreateMaterial(new PbrMaterial());

		renderer.BeginFrame();
		LookDownMinusZ(renderer);
		renderer.Draw(cube, material, Matrix4x4.CreateTranslation(0, 0, -10));   // in front
		renderer.Draw(cube, material, Matrix4x4.CreateTranslation(0, 0, 10));    // behind
		renderer.Draw(cube, material, Matrix4x4.CreateTranslation(50, 0, -10));  // far to the right
		renderer.Draw(cube, material, Matrix4x4.CreateTranslation(0, 0, -150));  // beyond the far plane
		renderer.Draw(cube, material, Matrix4x4.CreateScale(40) * Matrix4x4.CreateTranslation(25, 0, -10)); // huge, straddling the right plane
		renderer.RunCpuPipeline();

		var stats = renderer.LastFrameStatistics;
		Assert.Equal(5, stats.Submitted);
		Assert.Equal(2, stats.Visible);
		Assert.Equal(3, stats.Culled);
		Assert.Equal(1, stats.Views);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void LayerMasksSelectWhatEachCameraDraws()
	{
		using var renderer = NewRenderer();
		var cube = renderer.CreateMesh(MeshPrimitives.Cube());
		renderer.BeginFrame();
		renderer.AddCamera(new Camera { CullingMask = 0b01 }, Matrix4x4.Identity);
		renderer.AddCamera(new Camera { CullingMask = 0b10, Priority = 1 }, Matrix4x4.Identity);
		var inFront = Matrix4x4.CreateTranslation(0, 0, -5);
		renderer.Submit(new MeshRenderer(cube, default) { LayerMask = 0b01 }, inFront);
		renderer.Submit(new MeshRenderer(cube, default) { LayerMask = 0b10 }, inFront);
		renderer.Submit(new MeshRenderer(cube, default) { LayerMask = 0b11 }, inFront);
		renderer.Submit(new MeshRenderer(cube, default) { LayerMask = 0 }, inFront); // 0 means layer 1
		renderer.RunCpuPipeline();

		Assert.Equal(2, renderer.ViewCount);
		Assert.Equal(3, renderer.Views[0].OpaqueObjects);
		Assert.Equal(2, renderer.Views[1].OpaqueObjects);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void OneThousandCubesInTwoMaterialsAreTwoInstancedBatches()
	{
		using var renderer = NewRenderer(new Rendering3DOptions { Shadows = false });
		var cube = renderer.CreateMesh(MeshPrimitives.Cube(0.5f));
		var red = renderer.CreateMaterial(new PbrMaterial(Color.Red));
		var blue = renderer.CreateMaterial(new PbrMaterial(Color.Blue));

		renderer.BeginFrame();
		renderer.SetCamera(new Camera { Far = 500 }, Transform.LookAt(new Vector3(0, 40, 40), Vector3.Zero));
		for (var i = 0; i < 1000; i++)
		{
			var x = i % 40 - 20;
			var z = i / 40 - 12;
			renderer.Draw(cube, (i & 1) == 0 ? red : blue, Matrix4x4.CreateTranslation(x, 0, z));
		}

		renderer.RunCpuPipeline();

		var view = renderer.Views[0];
		Assert.Equal(1000, view.OpaqueObjects);
		var batches = view.Opaque.Span;
		Assert.Equal(2, batches.Length);
		Assert.Equal(500, batches[0].InstanceCount);
		Assert.Equal(500, batches[1].InstanceCount);
		Assert.NotEqual(batches[0].Material, batches[1].Material);
		Assert.Equal(1000, batches[1].FirstInstance + batches[1].InstanceCount);
		var stats = renderer.LastFrameStatistics;
		Assert.Equal(2, stats.Batches);
		Assert.Equal(2, stats.DrawCalls);
		Assert.Equal(12_000, stats.Triangles);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void OpaqueDrawsAreBinnedThenFrontToBackAndBlendedDrawsBackToFront()
	{
		using var renderer = NewRenderer(new Rendering3DOptions { Shadows = false });
		var cube = renderer.CreateMesh(MeshPrimitives.Cube());
		var sphere = renderer.CreateMesh(MeshPrimitives.Sphere());
		var opaque = renderer.CreateMaterial(new PbrMaterial());
		var glass = renderer.CreateMaterial(new PbrMaterial(new Color(1f, 1f, 1f, 0.5f)) { AlphaMode = AlphaMode.Blend });

		renderer.BeginFrame();
		LookDownMinusZ(renderer);
		// Opaque: two bins (cube, sphere), each submitted far to near.
		renderer.Draw(cube, opaque, Matrix4x4.CreateTranslation(0, 0, -30));
		renderer.Draw(sphere, opaque, Matrix4x4.CreateTranslation(0, 0, -25));
		renderer.Draw(cube, opaque, Matrix4x4.CreateTranslation(0, 0, -5));
		// Blended: near to far, alternating meshes.
		renderer.Draw(cube, glass, Matrix4x4.CreateTranslation(1, 0, -3));
		renderer.Draw(sphere, glass, Matrix4x4.CreateTranslation(1, 0, -8));
		renderer.Draw(cube, glass, Matrix4x4.CreateTranslation(1, 0, -12));
		renderer.RunCpuPipeline();

		var view = renderer.Views[0];
		var instances = renderer.Instances;
		// Opaque: the cube bin (two instances, nearest first), then the sphere.
		var opaqueBatches = view.Opaque.Span;
		Assert.Equal(2, opaqueBatches.Length);
		Assert.Equal(cube.Id, opaqueBatches[0].Mesh);
		Assert.Equal(2, opaqueBatches[0].InstanceCount);
		Assert.Equal(-5f, instances[opaqueBatches[0].FirstInstance].World2.W);
		Assert.Equal(-30f, instances[opaqueBatches[0].FirstInstance + 1].World2.W);
		Assert.Equal(sphere.Id, opaqueBatches[1].Mesh);

		// Blended: farthest first, one batch each (the meshes alternate).
		var blended = view.Transparent.Span;
		Assert.Equal(3, blended.Length);
		Assert.Equal(-12f, instances[blended[0].FirstInstance].World2.W);
		Assert.Equal(-8f, instances[blended[1].FirstInstance].World2.W);
		Assert.Equal(-3f, instances[blended[2].FirstInstance].World2.W);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void SortKeysOrderPipelineThenMaterialThenMeshThenDepth()
	{
		Assert.True(Renderer3D.OpaqueKey(0.9f, 1, 5, 5) < Renderer3D.OpaqueKey(0.1f, 2, 0, 0), "pipeline first");
		Assert.True(Renderer3D.OpaqueKey(0.9f, 1, 4, 9) < Renderer3D.OpaqueKey(0.1f, 1, 5, 0), "then material");
		Assert.True(Renderer3D.OpaqueKey(0.9f, 1, 4, 3) < Renderer3D.OpaqueKey(0.1f, 1, 4, 4), "then mesh");
		Assert.True(Renderer3D.OpaqueKey(0.1f, 1, 4, 4) < Renderer3D.OpaqueKey(0.2f, 1, 4, 4), "then front to back");
		Assert.True(Renderer3D.TransparentKey(0.9f, 9, 9, 9) < Renderer3D.TransparentKey(0.1f, 0, 0, 0), "back to front first");
		Assert.True(Renderer3D.TransparentKey(0.5f, 1, 2, 3) < Renderer3D.TransparentKey(0.5f, 1, 2, 4), "equal depths group by mesh");
		Assert.True(Renderer3D.TransparentKey(1f, 0, 0, 0) < Renderer3D.TransparentKey(0f, 0, 0, 0));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void MissingOrDestroyedMaterialsDrawWithTheDefaultMaterial()
	{
		using var renderer = NewRenderer(new Rendering3DOptions { Shadows = false });
		var cube = renderer.CreateMesh(MeshPrimitives.Cube());
		var material = renderer.CreateMaterial(new UnlitMaterial(Color.Red));
		renderer.DestroyMaterial(material);

		renderer.BeginFrame();
		LookDownMinusZ(renderer);
		renderer.Draw(cube, material, Matrix4x4.CreateTranslation(0, 0, -5));
		renderer.Draw(cube, default, Matrix4x4.CreateTranslation(1, 0, -5));
		renderer.RunCpuPipeline();

		var batches = renderer.Views[0].Opaque.Span;
		Assert.Equal(1, batches.Length);
		Assert.Equal(renderer.DefaultMaterial.Id, batches[0].Material);
		Assert.Equal(2, batches[0].InstanceCount);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void DestroyedAndUnknownMeshesAreSkipped()
	{
		using var renderer = NewRenderer();
		var cube = renderer.CreateMesh(MeshPrimitives.Cube());
		var gone = renderer.CreateMesh(MeshPrimitives.Cube());
		renderer.DestroyMesh(gone);
		Assert.Throws<ArgumentException>(() => renderer.GetMeshInfo(gone));

		renderer.BeginFrame();
		LookDownMinusZ(renderer);
		renderer.Draw(gone, default, Matrix4x4.CreateTranslation(0, 0, -5));
		renderer.Draw(new MeshHandle(999), default, Matrix4x4.CreateTranslation(0, 0, -5));
		renderer.Draw(cube, default, Matrix4x4.CreateTranslation(0, 0, -5));
		renderer.RunCpuPipeline();
		Assert.Equal(1, renderer.LastFrameStatistics.Visible);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void MeshInfoDescribesTheUploadedMesh()
	{
		using var renderer = NewRenderer();
		var info = renderer.GetMeshInfo(renderer.CreateMesh(MeshPrimitives.Cube(2)));
		Assert.Equal(new Aabb(new Vector3(-1), new Vector3(1)), info.Bounds);
		Assert.Equal(24, info.VertexCount);
		Assert.Equal(36, info.IndexCount);
		Assert.Equal(1, info.SubMeshCount);

		// Normals and tangents are computed when missing.
		var bare = new MeshData("triangle", [Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [0, 1, 2]);
		var handle = renderer.CreateMesh(bare);
		Assert.NotNull(bare.Normals);
		Assert.NotNull(bare.Tangents);
		Assert.Equal(VertexAttributes.Position | VertexAttributes.Normal | VertexAttributes.Tangent, renderer.GetMeshInfo(handle).Attributes);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void InstanceNormalsFollowTheInverseTransposeUnderNonUniformScale()
	{
		var world = Matrix4x4.CreateScale(4, 1, 1) * Matrix4x4.CreateRotationZ(0.6f) * Matrix4x4.CreateTranslation(3, 2, 1);
		var instance = default(InstanceData);
		InstanceData.Write(ref instance, world, InstanceData.ReceiveShadows);

		// A 45 degree normal in the xy plane; the reference: n * transpose(inverse(M3)), normalized.
		var n = Vector3.Normalize(new Vector3(1, 1, 0));
		Matrix4x4.Invert(world, out var inverse);
		var expected = Vector3.Normalize(Vector3.TransformNormal(n, Matrix4x4.Transpose(inverse)));
		var actual = Vector3.Normalize(new Vector3(
			Vector3.Dot(new Vector3(instance.Normal0.X, instance.Normal0.Y, instance.Normal0.Z), n),
			Vector3.Dot(new Vector3(instance.Normal1.X, instance.Normal1.Y, instance.Normal1.Z), n),
			Vector3.Dot(new Vector3(instance.Normal2.X, instance.Normal2.Y, instance.Normal2.Z), n)));
		Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"{expected} != {actual}");
		Assert.Equal(1f, instance.Normal0.W);

		// Positions through the packed columns.
		var p = new Vector4(1, 2, 3, 1);
		var world0 = Vector3.Transform(new Vector3(1, 2, 3), world);
		Assert.Equal(world0.X, Vector4.Dot(instance.World0, p), 4);
		Assert.Equal(world0.Y, Vector4.Dot(instance.World1, p), 4);
		Assert.Equal(world0.Z, Vector4.Dot(instance.World2, p), 4);

		// A mirrored object keeps outward normals.
		InstanceData.Write(ref instance, Matrix4x4.CreateScale(-1, 1, 1), 0);
		Assert.Equal(-1f, instance.Normal0.X);
		Assert.Equal(1f, instance.Normal1.Y);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ShadowCastersAreBatchedByMeshAndBlendedObjectsDoNotCast()
	{
		using var renderer = NewRenderer();
		var cube = renderer.CreateMesh(MeshPrimitives.Cube());
		var sphere = renderer.CreateMesh(MeshPrimitives.Sphere());
		var red = renderer.CreateMaterial(new PbrMaterial(Color.Red));
		var blue = renderer.CreateMaterial(new PbrMaterial(Color.Blue));
		var glass = renderer.CreateMaterial(new PbrMaterial { AlphaMode = AlphaMode.Blend });

		renderer.BeginFrame();
		renderer.SetCamera(new Camera { Far = 100 }, Transform.LookAt(new Vector3(0, 10, 10), Vector3.Zero));
		renderer.AddLight(new DirectionalLight(), new Vector3(-0.3f, -1, -0.2f));
		renderer.Draw(cube, red, Matrix4x4.CreateTranslation(-1, 0, 0));
		renderer.Draw(sphere, red, Matrix4x4.CreateTranslation(0, 0, 0));
		renderer.Draw(cube, blue, Matrix4x4.CreateTranslation(1, 0, 0));
		renderer.Draw(cube, glass, Matrix4x4.CreateTranslation(2, 0, 0));
		renderer.Submit(new MeshRenderer(cube, red) { CastShadows = false }, Matrix4x4.CreateTranslation(3, 0, 0));
		renderer.RunCpuPipeline();

		Assert.True(renderer.ShadowActive);
		var shadow = renderer.ShadowBatches;
		Assert.Equal(2, shadow.Length);
		Assert.Equal(3, shadow[0].InstanceCount + shadow[1].InstanceCount);
		Assert.Equal(3, renderer.LastFrameStatistics.ShadowCasters);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void NoShadowWithoutAShadowCastingLight()
	{
		using var renderer = NewRenderer();
		var cube = renderer.CreateMesh(MeshPrimitives.Cube());
		renderer.BeginFrame();
		LookDownMinusZ(renderer);
		renderer.AddLight(new DirectionalLight { CastShadows = false }, -Vector3.UnitY);
		renderer.Draw(cube, default, Matrix4x4.CreateTranslation(0, 0, -5));
		renderer.RunCpuPipeline();
		Assert.False(renderer.ShadowActive);

		using var noShadows = NewRenderer(new Rendering3DOptions { Shadows = false });
		var cube2 = noShadows.CreateMesh(MeshPrimitives.Cube());
		noShadows.BeginFrame();
		LookDownMinusZ(noShadows);
		noShadows.AddLight(new DirectionalLight(), -Vector3.UnitY);
		noShadows.Draw(cube2, default, Matrix4x4.CreateTranslation(0, 0, -5));
		noShadows.RunCpuPipeline();
		Assert.False(noShadows.ShadowActive);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void CamerasRenderByPriorityAndPointLightsAreCulledPerView()
	{
		using var renderer = NewRenderer();
		renderer.BeginFrame();
		renderer.AddCamera(new Camera { Priority = 5, ClearColor = Color.Red }, Matrix4x4.Identity);
		renderer.AddCamera(new Camera { Priority = -1, ClearColor = Color.Blue }, Matrix4x4.Identity);
		renderer.AddCamera(new Camera { Priority = 5, ClearColor = Color.Green, Viewport = new RectangleF(0.5f, 0, 0.5f, 1) }, Matrix4x4.Identity);
		renderer.AddLight(new PointLight(Color.White, 1, 2), new Vector3(0, 0, -10));
		renderer.AddLight(new PointLight(Color.White, 1, 2), new Vector3(0, 0, 10));
		renderer.RunCpuPipeline();

		Assert.Equal(3, renderer.ViewCount);
		Assert.Equal(Color.Blue, renderer.Views[0].Camera.ClearColor);
		Assert.Equal(Color.Red, renderer.Views[1].Camera.ClearColor);
		Assert.Equal(Color.Green, renderer.Views[2].Camera.ClearColor);
		Assert.True(renderer.Views[0].FirstOnTarget);
		Assert.False(renderer.Views[1].FirstOnTarget);
		Assert.True(renderer.Views[1].DrawClearQuad);
		Assert.Equal((50f, 0f, 50f, 100f), (renderer.Views[2].ViewportX, renderer.Views[2].ViewportY, renderer.Views[2].ViewportWidth, renderer.Views[2].ViewportHeight));
		Assert.Equal(1, renderer.Views[0].LightCount);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void SubmissionsAreForgottenAtTheNextFrame()
	{
		using var renderer = NewRenderer();
		var cube = renderer.CreateMesh(MeshPrimitives.Cube());
		renderer.BeginFrame();
		LookDownMinusZ(renderer);
		renderer.Draw(cube, default, Matrix4x4.CreateTranslation(0, 0, -5));
		renderer.RunCpuPipeline();
		Assert.Equal(1, renderer.LastFrameStatistics.Visible);

		renderer.BeginFrame();
		renderer.RunCpuPipeline();
		Assert.Equal(0, renderer.LastFrameStatistics.Submitted);
		Assert.Equal(0, renderer.ViewCount);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheCpuPipelineAllocatesNothingOnceWarm()
	{
		using var renderer = NewRenderer();
		var cube = renderer.CreateMesh(MeshPrimitives.Cube());
		var materials = new[] { renderer.CreateMaterial(new PbrMaterial(Color.Red)), renderer.CreateMaterial(new PbrMaterial(Color.Blue)), renderer.CreateMaterial(new UnlitMaterial(Color.Green)) };
		var transforms = new Matrix4x4[10_000];
		for (var i = 0; i < transforms.Length; i++) transforms[i] = Matrix4x4.CreateTranslation(i % 100 - 50, 0, i / 100 - 50);

		void Frame()
		{
			renderer.BeginFrame();
			renderer.SetCamera(new Camera { Far = 300 }, Transform.LookAt(new Vector3(0, 60, 80), Vector3.Zero));
			renderer.AddLight(new DirectionalLight(), new Vector3(-0.3f, -1, -0.2f));
			renderer.AddLight(new PointLight(), new Vector3(0, 2, 0));
			for (var i = 0; i < transforms.Length; i++) renderer.Draw(cube, materials[i % 3], transforms[i]);
			renderer.RunCpuPipeline();
		}

		for (var i = 0; i < 3; i++) Frame();
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 5; i++) Frame();
		Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
		Assert.Equal(10_000, renderer.LastFrameStatistics.Submitted);
	}
}
