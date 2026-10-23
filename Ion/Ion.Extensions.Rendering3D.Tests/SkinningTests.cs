using System.Text;

using Ion.Extensions.Rendering3D.Gltf;

namespace Ion.Extensions.Rendering3D.Tests;

/// <summary>Skins and animations from glTF, and skinned submissions through the CPU pipeline (bounds, palette, batches).</summary>
public class SkinningTests
{
	private static readonly string PuppetPath = Path.Combine(AppContext.BaseDirectory, "Assets", "puppet.gltf");

	private static Renderer3D NewRenderer() => new(null) { CpuTargetSize = (100, 100) };

	private static IModel LoadPuppet(Renderer3D renderer) =>
		ModelLoader.Create(renderer, null, "puppet.gltf", GltfReader.Read(File.ReadAllBytes(PuppetPath), _ => throw new FileNotFoundException()));

	/// <summary>A unit cube whose vertices all follow joint <paramref name="joint"/> (of <paramref name="joints"/>).</summary>
	private static MeshData SkinnedCube(int joint = 0)
	{
		var mesh = MeshPrimitives.Cube();
		mesh.Joints = [.. mesh.Positions.Select(_ => new JointIndices((ushort)joint, 0, 0, 0))];
		mesh.Weights = [.. mesh.Positions.Select(_ => Vector4.UnitX)];
		return mesh;
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ThePuppetLoadsWithItsSkeletonClipsAndSkinnedMesh()
	{
		using var renderer = NewRenderer();
		var model = LoadPuppet(renderer);

		var skeleton = Assert.Single(model.Skins);
		Assert.Equal(12, skeleton.JointCount);
		Assert.Equal(-1, skeleton.Joints[skeleton.IndexOf("Hips")].Parent);
		Assert.Equal(skeleton.IndexOf("Chest"), skeleton.Joints[skeleton.IndexOf("UpperArm.L")].Parent);
		Assert.Equal(skeleton.IndexOf("UpperArm.L"), skeleton.Joints[skeleton.IndexOf("LowerArm.L")].Parent);

		Assert.Equal(["Idle", "Walk", "Wave"], model.Animations.Select(a => a.Name));
		Assert.Equal(1f, model.FindAnimation("Walk")!.Duration, 4);
		Assert.All(model.Animations, clip => Assert.Equal(0, model.SkinOf(clip)));

		var primitive = Assert.Single(model.Nodes.SelectMany(n => n.Primitives));
		Assert.Equal(0, primitive.Skin);
		Assert.True(renderer.GetMeshInfo(primitive.Mesh).Attributes.HasFlag(VertexAttributes.Skin));

		// The inverse bind matrices are the rest pose's: at rest every vertex stays where it was bound.
		var player = new AnimationPlayer(skeleton);
		foreach (var m in player.SkinningMatrices)
		{
			for (var i = 0; i < 16; i++) Assert.Equal(Matrix4x4.Identity[i / 4, i % 4], m[i / 4, i % 4], 4);
		}

		// Walking moves the feet: the lower legs swing apart from their rest positions.
		player.Play(model.FindAnimation("Walk")!);
		player.Update(0.25f);
		var left = player.JointMatrices[skeleton.IndexOf("LowerLeg.L")].Translation;
		var right = player.JointMatrices[skeleton.IndexOf("LowerLeg.R")].Translation;
		Assert.True(MathF.Abs(left.Z - right.Z) > 0.2f, $"left knee {left}, right knee {right}");
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheReaderTakesIntegerJointsNormalizedWeightsAndEveryInterpolation()
	{
		// A triangle on two joints: JOINTS_0 as unsigned bytes, WEIGHTS_0 as normalized unsigned bytes (rounded, so the
		// reader renormalizes them). Node 0 the mesh, 1 the root joint (child: 2), 2 the second joint, 3 a plain node.
		var buffer = new List<byte>();
		void Floats(params float[] values) { foreach (var v in values) buffer.AddRange(BitConverter.GetBytes(v)); }
		Floats(0, 0, 0, 1, 0, 0, 0, 1, 0);                              // positions: 0..36
		buffer.AddRange([0, 1, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0]);          // joints: 36..48
		buffer.AddRange([255, 0, 0, 0, 128, 128, 0, 0, 0, 255, 0, 0]);  // weights: 48..60
		Floats(0, 1);                                                   // times: 60..68
		Floats(0, 0, 0, 0, 5, 0);                                       // step translations: 68..92
		// Cubic rotations, key 0 then key 1, each in-tangent, value, out-tangent (tangents zero).
		var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
		Floats(0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, turn.X, turn.Y, turn.Z, turn.W, 0, 0, 0, 0);   // 92..188
		var base64 = Convert.ToBase64String([.. buffer]);
		var gltf = $$"""
		{
		  "asset": { "version": "2.0" },
		  "scenes": [ { "nodes": [0, 1, 3] } ],
		  "nodes": [
		    { "name": "mesh", "mesh": 0, "skin": 0 },
		    { "name": "root", "translation": [0, 1, 0], "children": [2] },
		    { "name": "tip", "translation": [1, 0, 0] },
		    { "name": "prop" }
		  ],
		  "skins": [ { "joints": [2, 1] } ],
		  "meshes": [ { "primitives": [ { "attributes": { "POSITION": 0, "JOINTS_0": 1, "WEIGHTS_0": 2 } } ] } ],
		  "animations": [
		    { "name": "move", "samplers": [
		        { "input": 3, "output": 4, "interpolation": "STEP" },
		        { "input": 3, "output": 5, "interpolation": "CUBICSPLINE" } ],
		      "channels": [
		        { "sampler": 0, "target": { "node": 1, "path": "translation" } },
		        { "sampler": 1, "target": { "node": 2, "path": "rotation" } },
		        { "sampler": 0, "target": { "node": 3, "path": "translation" } },
		        { "sampler": 0, "target": { "node": 0, "path": "weights" } } ] },
		    { "name": "prop only", "samplers": [ { "input": 3, "output": 4 } ], "channels": [ { "sampler": 0, "target": { "node": 3, "path": "translation" } } ] }
		  ],
		  "accessors": [
		    { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3", "min": [0, 0, 0], "max": [1, 1, 0] },
		    { "bufferView": 1, "componentType": 5121, "count": 3, "type": "VEC4" },
		    { "bufferView": 2, "componentType": 5121, "normalized": true, "count": 3, "type": "VEC4" },
		    { "bufferView": 3, "componentType": 5126, "count": 2, "type": "SCALAR" },
		    { "bufferView": 4, "componentType": 5126, "count": 2, "type": "VEC3" },
		    { "bufferView": 5, "componentType": 5126, "count": 6, "type": "VEC4" }
		  ],
		  "bufferViews": [
		    { "buffer": 0, "byteOffset": 0, "byteLength": 36 },
		    { "buffer": 0, "byteOffset": 36, "byteLength": 12 },
		    { "buffer": 0, "byteOffset": 48, "byteLength": 12 },
		    { "buffer": 0, "byteOffset": 60, "byteLength": 8 },
		    { "buffer": 0, "byteOffset": 68, "byteLength": 24 },
		    { "buffer": 0, "byteOffset": 92, "byteLength": 96 }
		  ],
		  "buffers": [ { "byteLength": {{buffer.Count}}, "uri": "data:application/octet-stream;base64,{{base64}}" } ]
		}
		""";

		var document = GltfReader.Read(Encoding.UTF8.GetBytes(gltf), _ => throw new FileNotFoundException());
		var mesh = document.Meshes[0][0].Mesh;
		Assert.Equal(new JointIndices(0, 1, 0, 0), mesh.Joints![0]);
		Assert.Equal(new Vector4(0.5f, 0.5f, 0, 0), mesh.Weights![1]);
		Assert.Equal(1f, mesh.Weights[2].Y);

		using var renderer = NewRenderer();
		var model = ModelLoader.Create(renderer, null, "joints.gltf", document);
		var skeleton = Assert.Single(model.Skins);
		// The skin's order is kept: joint 0 is the tip, whose parent (joint 1, the root) comes after it.
		Assert.Equal("tip", skeleton.Joints[0].Name);
		Assert.Equal(1, skeleton.Joints[0].Parent);
		Assert.Equal(-1, skeleton.Joints[1].Parent);
		Assert.True(skeleton.Joints[0].InverseBind.IsIdentity);

		// The prop-only animation is skipped; the other keeps its two joint channels (the prop's and the weights' dropped).
		var clip = Assert.Single(model.Animations);
		Assert.Equal("move", clip.Name);
		Assert.Equal(2, clip.Channels.Count);
		var step = clip.Channels.Single(c => c.Path == AnimationPath.Translation);
		Assert.Equal((1, AnimationInterpolation.Step), (step.Joint, step.Interpolation));
		Assert.Equal(0f, step.Sample(0.99f).Y);
		Assert.Equal(5f, step.Sample(1f).Y);
		var cubic = clip.Channels.Single(c => c.Path == AnimationPath.Rotation);
		Assert.Equal((0, AnimationInterpolation.CubicSpline), (cubic.Joint, cubic.Interpolation));
		var end = cubic.Sample(1f);
		Assert.Equal(1f, MathF.Abs(Quaternion.Dot(turn, new Quaternion(end.X, end.Y, end.Z, end.W))), 4);

		var primitive = Assert.Single(model.Nodes.SelectMany(n => n.Primitives));
		Assert.Equal(0, primitive.Skin);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ASkinnedSubmissionIsBoundedByItsPosedJoints()
	{
		using var renderer = NewRenderer();
		var cube = renderer.CreateMesh(SkinnedCube(joint: 1));
		renderer.BeginFrame();
		renderer.SetCamera(new Camera(), Transform.LookAt(new Vector3(0, 0, 10), Vector3.Zero));
		Matrix4x4[] joints = [Matrix4x4.Identity, Matrix4x4.CreateTranslation(3, 0, 0)];
		renderer.Submit(new MeshRenderer(cube, default), Matrix4x4.CreateTranslation(0, 2, 0), joints);
		renderer.RunCpuPipeline();

		// The union of the cube under each joint, then placed by the world matrix.
		var bounds = renderer.WorldBounds[0];
		Assert.Equal(new Vector3(-0.5f, 1.5f, -0.5f), bounds.Min);
		Assert.Equal(new Vector3(3.5f, 2.5f, 0.5f), bounds.Max);
		var skin = Assert.Single(renderer.Skins.ToArray());
		Assert.Equal((0, 2), (skin.Palette, skin.Joints));

		// The palette rows are the joints' matrices as the instance rows are the world's.
		var rows = renderer.PaletteRows;
		Assert.Equal(6, rows.Length);
		Assert.Equal(new Vector4(1, 0, 0, 3), rows[3]);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void SkinnedAndRigidDrawsOfOneMeshBatchApartAndInstancesKnowTheirPalette()
	{
		using var renderer = NewRenderer();
		var cube = renderer.CreateMesh(SkinnedCube());
		var material = renderer.CreateMaterial(new PbrMaterial());
		renderer.BeginFrame();
		renderer.SetCamera(new Camera(), Transform.LookAt(new Vector3(0, 0, 10), Vector3.Zero));
		renderer.AddLight(new DirectionalLight(), -Vector3.UnitY);
		var renderer0 = new MeshRenderer(cube, material);
		Matrix4x4[] three = [Matrix4x4.Identity, Matrix4x4.Identity, Matrix4x4.Identity];
		Matrix4x4[] one = [Matrix4x4.CreateTranslation(0.1f, 0, 0)];
		renderer.Submit(renderer0, Matrix4x4.Identity, three);
		renderer.Submit(renderer0, Matrix4x4.CreateTranslation(1, 0, 0));
		renderer.Submit(renderer0, Matrix4x4.CreateTranslation(-1, 0, 0), one);
		// An empty palette draws rigid.
		renderer.Submit(renderer0, Matrix4x4.CreateTranslation(0, 1, 0), ReadOnlySpan<Matrix4x4>.Empty);
		renderer.RunCpuPipeline();

		var view = renderer.Views[0];
		var batches = view.Opaque.Span.ToArray();
		Assert.Equal(2, batches.Length);
		var skinned = Assert.Single(batches, b => b.Skinned);
		Assert.Equal(2, skinned.InstanceCount);
		Assert.Equal(2, Assert.Single(batches, b => !b.Skinned).InstanceCount);

		// The skinned instances point at their palettes: the first at joint 0, the other after the first's three joints.
		var palettes = renderer.Instances.Slice(skinned.FirstInstance, 2).ToArray().Select(i => i.Normal1.W).Order().ToArray();
		Assert.Equal([0f, 3f], palettes);

		// The shadow pass batches them apart too.
		Assert.Contains(renderer.ShadowBatches.ToArray(), b => b.Skinned);
		Assert.Contains(renderer.ShadowBatches.ToArray(), b => !b.Skinned);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void APaletteTooShortForTheMeshIsRefusedAndARigidMeshIgnoresOne()
	{
		using var renderer = NewRenderer();
		var skinned = renderer.CreateMesh(SkinnedCube(joint: 4));
		var rigid = renderer.CreateMesh(MeshPrimitives.Cube());
		renderer.BeginFrame();
		var thrown = Assert.Throws<ArgumentException>(() => renderer.Submit(new MeshRenderer(skinned, default), Matrix4x4.Identity, new Matrix4x4[2]));
		Assert.Contains("joint 4", thrown.Message);

		renderer.BeginFrame();
		renderer.Submit(new MeshRenderer(rigid, default), Matrix4x4.Identity, [Matrix4x4.CreateTranslation(9, 0, 0)]);
		Assert.Equal(0, renderer.Skins.Length);
		Assert.Equal(0, renderer.PaletteRows.Length);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void DrawingAModelPosesItsSkinnedPrimitivesAtTheModelsWorldMatrix()
	{
		var batch = new RecordingBatch();
		using var renderer = NewRenderer();
		var model = LoadPuppet(renderer);
		var player = new AnimationPlayer(model.Skins[0]);
		var world = Matrix4x4.CreateTranslation(5, 0, 0);
		batch.Draw(model, world, player);

		var (_, at, joints) = Assert.Single(batch.Submissions);
		Assert.Equal(world, at);
		Assert.Equal(12, joints);

		// Without a player the skin is drawn at its rest pose.
		batch.Submissions.Clear();
		batch.Draw(model, world, ReadOnlySpan<AnimationPlayer?>.Empty);
		Assert.Equal(12, Assert.Single(batch.Submissions).Joints);
	}

	private sealed class RecordingBatch : IMeshBatch
	{
		public List<(MeshRenderer Renderer, Matrix4x4 World, int Joints)> Submissions { get; } = [];

		public void Submit(in MeshRenderer renderer, in Matrix4x4 world) => Submissions.Add((renderer, world, 0));

		public void Submit(in MeshRenderer renderer, in Matrix4x4 world, ReadOnlySpan<Matrix4x4> joints) => Submissions.Add((renderer, world, joints.Length));

		public void AddCamera(in Camera camera, in Matrix4x4 world) { }

		public void SetCamera(in Camera camera, in Transform transform) { }

		public void Draw(MeshHandle mesh, MaterialHandle material, in Matrix4x4 world) { }

		public void AddLight(in DirectionalLight light, in Matrix4x4 world) { }

		public void AddLight(in DirectionalLight light, Vector3 direction) { }

		public void AddLight(in PointLight light, Vector3 position) { }

		public void AddLight(in SpotLight light, in Matrix4x4 world) { }

		public void SetEnvironment(in SceneEnvironment environment) { }
	}
}
