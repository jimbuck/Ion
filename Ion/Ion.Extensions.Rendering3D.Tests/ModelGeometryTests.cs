using System.Buffers.Binary;
using System.Text;

using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Assets;
using Ion.Extensions.Rendering3D.Gltf;
using Ion.Testing;

namespace Ion.Extensions.Rendering3D.Tests;

/// <summary><see cref="ModelGeometry"/>: a model's triangles on the CPU, placed as the model places them.</summary>
public class ModelGeometryTests
{
	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheSampleModelsTrianglesLoadWithoutAGpu()
	{
		using var host = new IonTestHost().Configure(services => services.AddRendering3D()).Start();
		using var geometry = host.Get<IAssetManager>().Load<ModelGeometry>("Avocado/Avocado.gltf");

		Assert.Equal(682, geometry.TriangleCount);
		Assert.Equal(406, geometry.Positions.Length);

		// The node turns the mesh 180 degrees about y: x and z change sign.
		var folder = Path.Combine(AppContext.BaseDirectory, "Assets", "Avocado");
		var document = GltfReader.Read(File.ReadAllBytes(Path.Combine(folder, "Avocado.gltf")), relative => File.ReadAllBytes(Path.Combine(folder, relative)));
		var source = document.Meshes[0][0].Mesh;
		for (var i = 0; i < source.Positions.Length; i++)
		{
			var expected = new Vector3(-source.Positions[i].X, source.Positions[i].Y, -source.Positions[i].Z);
			Assert.True(Vector3.Distance(expected, geometry.Positions[i]) < 1e-6f, $"{i}: {geometry.Positions[i]} vs {expected}");
		}

		Assert.Equal(source.Indices, geometry.Indices);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void NodesPlaceTheirMeshesAndMirroredNodesKeepTheirFrontFaces()
	{
		// One triangle, used by two children of a scaled and moved root: one moved, one mirrored in x.
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
		  "nodes": [
		    { "name": "root", "children": [ 1, 2 ], "scale": [ 2, 2, 2 ], "translation": [ 5, 6, 7 ] },
		    { "name": "moved", "mesh": 0, "translation": [ 1, 0, 0 ] },
		    { "name": "mirrored", "mesh": 0, "scale": [ -1, 1, 1 ] },
		    { "name": "unused", "mesh": 0 }
		  ],
		  "scene": 0,
		  "scenes": [ { "nodes": [ 0 ] } ]
		}
		""";

		var geometry = ModelGeometryLoader.Create("tri.gltf", GltfReader.Read(Encoding.UTF8.GetBytes(gltf), _ => throw new FileNotFoundException()));

		// Two placed triangles (the node outside the scene is not), in some order.
		Assert.Equal(2, geometry.TriangleCount);
		var moved = new[] { new Vector3(7, 6, 7), new Vector3(9, 6, 7), new Vector3(7, 8, 7) };
		var mirrored = new[] { new Vector3(5, 6, 7), new Vector3(3, 6, 7), new Vector3(5, 8, 7) };
		for (var t = 0; t < 2; t++)
		{
			var a = geometry.Positions[geometry.Indices[t * 3]];
			var b = geometry.Positions[geometry.Indices[t * 3 + 1]];
			var c = geometry.Positions[geometry.Indices[t * 3 + 2]];
			var expected = a == moved[0] ? moved : mirrored;
			Assert.Equal(expected[0], a);

			// Both face +z (counterclockwise seen from +z), the mirrored one too: its winding was turned back.
			Assert.True(Vector3.Cross(b - a, c - a).Z > 0, $"triangle {t}: {a} {b} {c}");
			Assert.Contains(b, expected);
			Assert.Contains(c, expected);
		}

		Assert.Equal(new Vector3(3, 6, 7), geometry.Bounds.Min);
		Assert.Equal(new Vector3(9, 8, 7), geometry.Bounds.Max);
	}
}
