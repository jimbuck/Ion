using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;

using Ion.Extensions.Rendering3D.Gltf;

namespace Ion.Extensions.Rendering3D.Tests;

/// <summary>The glTF 2.0 reader and the model creation, on the model sample's asset (Microsoft's CC0 Avocado).</summary>
public class GltfTests
{
	private static readonly string AvocadoFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "Avocado");

	private static byte[] Resolve(string relative) => File.ReadAllBytes(Path.Combine(AvocadoFolder, relative));

	private static GltfDocument ReadAvocado() => GltfReader.Read(File.ReadAllBytes(Path.Combine(AvocadoFolder, "Avocado.gltf")), Resolve);

	[Fact, Trait(CATEGORY, UNIT)]
	public void ParsesTheSampleModel()
	{
		var document = ReadAvocado();

		var mesh = Assert.Single(document.Meshes);
		var primitive = Assert.Single(mesh);
		var data = primitive.Mesh;
		Assert.Equal(406, data.VertexCount);
		Assert.Equal(2046, data.Indices.Length);
		Assert.Equal(VertexAttributes.Position | VertexAttributes.Normal | VertexAttributes.Tangent | VertexAttributes.Uv0, data.Attributes);
		data.Validate();

		// The accessor's min and max.
		var bounds = data.ComputeBounds();
		Assert.Equal(-0.02128091f, bounds.Min.X, 5);
		Assert.Equal(0.06284806f, bounds.Max.Y, 5);
		Assert.Equal(0.013809f, bounds.Max.Z, 5);

		// Unit normals, tangents with a handedness.
		foreach (var n in data.Normals!) Assert.Equal(1f, n.Length(), 2);
		foreach (var t in data.Tangents!) Assert.True(MathF.Abs(t.W) == 1f);

		var material = Assert.Single(document.Materials);
		Assert.Equal(0, primitive.Material);
		Assert.Equal("2256_Avocado_d", material.Name);
		Assert.Equal(0, material.BaseColorTexture);
		Assert.Equal(1, material.MetallicRoughnessTexture);
		Assert.Equal(2, material.NormalTexture);
		Assert.Equal(-1, material.OcclusionTexture);
		Assert.Equal(1f, material.MetallicFactor);
		Assert.Equal(1f, material.RoughnessFactor);
		Assert.Equal(AlphaMode.Opaque, material.AlphaMode);
		Assert.Equal(Vector4.One, material.BaseColorFactor);

		Assert.Equal([0, 1, 2], document.Textures);
		Assert.Equal(3, document.Images.Count);
		Assert.All(document.Images, image => Assert.Equal(0x89, image.Data[0])); // PNG signature

		var node = Assert.Single(document.Nodes);
		Assert.Equal("Avocado", node.Name);
		Assert.Equal(0, node.Mesh);
		// rotation [0, 1, 0, 0]: 180 degrees about y.
		Assert.True(Vector3.Distance(new Vector3(-1, 0, 0), node.Transform.TransformPoint(Vector3.UnitX)) < 1e-5f);
		Assert.Equal([0], document.SceneRoots);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ReadsTheSameModelFromAGlb()
	{
		// Pack the .gltf into a GLB: the buffer becomes the binary chunk, images stay external files.
		var json = JsonNode.Parse(File.ReadAllText(Path.Combine(AvocadoFolder, "Avocado.gltf")))!;
		json["buffers"]![0]!.AsObject().Remove("uri");
		var jsonBytes = Encoding.UTF8.GetBytes(json.ToJsonString());
		var bin = File.ReadAllBytes(Path.Combine(AvocadoFolder, "Avocado.bin"));
		var glb = Glb(jsonBytes, bin);

		var document = GltfReader.Read(glb, Resolve);
		var data = Assert.Single(Assert.Single(document.Meshes)).Mesh;
		var reference = Assert.Single(Assert.Single(ReadAvocado().Meshes)).Mesh;
		Assert.Equal(reference.Positions, data.Positions);
		Assert.Equal(reference.Indices, data.Indices);
		Assert.Equal(reference.Uv0, data.Uv0);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ReadsDataUrisMatricesHierarchiesAndDefaults()
	{
		// A triangle (three float positions and ushort indices) in a base64 buffer; a root node with a matrix and a child
		// with the mesh; a primitive without material; a KHR_materials_unlit material that nothing uses.
		var buffer = new byte[36 + 6 + 2];
		var positions = new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f };
		for (var i = 0; i < positions.Length; i++) BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(i * 4), positions[i]);
		BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(36), 0);
		BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(38), 1);
		BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(40), 2);
		var gltf = $$"""
		{
		  "asset": { "version": "2.0" },
		  "buffers": [ { "byteLength": {{buffer.Length}}, "uri": "data:application/octet-stream;base64,{{Convert.ToBase64String(buffer)}}" } ],
		  "bufferViews": [ { "buffer": 0, "byteLength": 36 }, { "buffer": 0, "byteOffset": 36, "byteLength": 6 } ],
		  "accessors": [
		    { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3" },
		    { "bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR" }
		  ],
		  "meshes": [ { "name": "tri", "primitives": [ { "attributes": { "POSITION": 0 }, "indices": 1 } ] } ],
		  "materials": [ { "name": "flat", "extensions": { "KHR_materials_unlit": {} }, "alphaMode": "MASK", "alphaCutoff": 0.25, "doubleSided": true } ],
		  "nodes": [
		    { "name": "root", "children": [ 1 ], "matrix": [ 2,0,0,0, 0,2,0,0, 0,0,2,0, 5,6,7,1 ] },
		    { "name": "child", "mesh": 0, "translation": [ 1, 0, 0 ] }
		  ],
		  "scene": 0,
		  "scenes": [ { "nodes": [ 0 ] } ]
		}
		""";

		var document = GltfReader.Read(Encoding.UTF8.GetBytes(gltf), _ => throw new FileNotFoundException());
		Assert.Equal(-1, document.Meshes[0][0].Material);
		Assert.True(document.Materials[0].Unlit);
		Assert.Equal(AlphaMode.Mask, document.Materials[0].AlphaMode);
		Assert.Equal(0.25f, document.Materials[0].AlphaCutoff);
		Assert.True(document.Materials[0].DoubleSided);
		Assert.Equal(new Vector3(5, 6, 7), document.Nodes[0].Transform.Position);
		Assert.Equal(new Vector3(2), document.Nodes[0].Transform.Scale);

		using var renderer = new Renderer3D(null);
		using var model = ModelLoader.Create(renderer, null, "tri.gltf", document);
		Assert.Equal(2, model.Nodes.Count);
		Assert.Equal([0], model.RootNodes);
		var child = model.Nodes[1];
		Assert.Equal("child", child.Name);
		Assert.Equal(0, child.Parent);
		// The child's model matrix: its translation, then the root's scale by 2 and translation.
		Assert.Equal(new Vector3(7, 6, 7), child.ModelMatrix.Translation);
		Assert.Equal(new Aabb(new Vector3(7, 6, 7), new Vector3(9, 8, 7)), model.Bounds);
		// The unlit material plus glTF's default material for the primitive without one.
		Assert.Equal(2, model.Materials.Count);
		Assert.Single(child.Primitives);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void CreatesTheSampleModelWithoutAGpu()
	{
		using var renderer = new Renderer3D(null);
		using var model = ModelLoader.Create(renderer, null, "Avocado.gltf", ReadAvocado());
		Assert.Single(model.Meshes);
		Assert.Single(model.Materials);
		Assert.Equal(3, model.Textures.Count);
		Assert.Equal(406, renderer.GetMeshInfo(model.Meshes[0]).VertexCount);
		// Rotated 180 degrees about y: x and z mirrored.
		Assert.Equal(-0.013809f, model.Bounds.Min.Z, 5);
		Assert.True(model.Bounds.Max.Y > 0.06f);

		// Drawing the model submits its primitives with the node matrices.
		renderer.CpuTargetSize = (64, 64);
		renderer.BeginFrame();
		renderer.SetCamera(new Camera(), Transform.LookAt(new Vector3(0, 0.03f, 0.2f), new Vector3(0, 0.03f, 0)));
		renderer.Draw(model, Matrix4x4.Identity);
		renderer.RunCpuPipeline();
		Assert.Equal(1, renderer.LastFrameStatistics.Visible);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void RejectsUnsupportedFeaturesClearly()
	{
		var required = """{ "asset": { "version": "2.0" }, "extensionsRequired": [ "KHR_draco_mesh_compression" ] }""";
		Assert.Contains("KHR_draco_mesh_compression", Assert.Throws<InvalidDataException>(() => GltfReader.Read(Encoding.UTF8.GetBytes(required), _ => [])).Message);

		var version = """{ "asset": { "version": "1.0" } }""";
		Assert.Throws<InvalidDataException>(() => GltfReader.Read(Encoding.UTF8.GetBytes(version), _ => []));

		var sparse = """
		{ "asset": { "version": "2.0" },
		  "accessors": [ { "componentType": 5126, "count": 3, "type": "VEC3", "sparse": { "count": 1 } } ],
		  "meshes": [ { "primitives": [ { "attributes": { "POSITION": 0 } } ] } ] }
		""";
		Assert.Contains("Sparse", Assert.Throws<InvalidDataException>(() => GltfReader.Read(Encoding.UTF8.GetBytes(sparse), _ => [])).Message);
	}

	private static byte[] Glb(byte[] json, byte[] bin)
	{
		var jsonPadded = (json.Length + 3) & ~3;
		var binPadded = (bin.Length + 3) & ~3;
		var total = 12 + 8 + jsonPadded + 8 + binPadded;
		var glb = new byte[total];
		BinaryPrimitives.WriteUInt32LittleEndian(glb, 0x46546C67);
		BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(4), 2);
		BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(8), (uint)total);
		BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(12), (uint)jsonPadded);
		BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(16), 0x4E4F534A);
		json.CopyTo(glb.AsSpan(20));
		for (var i = 20 + json.Length; i < 20 + jsonPadded; i++) glb[i] = (byte)' ';
		var binStart = 20 + jsonPadded;
		BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(binStart), (uint)binPadded);
		BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(binStart + 4), 0x004E4942);
		bin.CopyTo(glb.AsSpan(binStart + 8));
		return glb;
	}
}
