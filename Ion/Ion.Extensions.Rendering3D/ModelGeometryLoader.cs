using System.Numerics;

using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering3D.Gltf;

namespace Ion.Extensions.Rendering3D;

/// <summary>
/// Loads the triangles of a glTF 2.0 model (<c>.gltf</c> or <c>.glb</c>) as <see cref="ModelGeometry"/>: no textures are
/// decoded and nothing is uploaded. Every triangle primitive of every node reachable from the default scene is placed by
/// the node's model matrix (as <see cref="IModel"/> places it), with the winding of mirrored nodes turned back so front
/// faces stay counterclockwise.
/// </summary>
internal sealed class ModelGeometryLoader(IPersistentStorage storage) : IAssetLoader<ModelGeometry>
{
	public Type AssetType { get; } = typeof(ModelGeometry);

	public ModelGeometry Load(string path)
	{
		ArgumentException.ThrowIfNullOrEmpty(path);
		var directory = Path.GetDirectoryName(path.Replace('\\', '/'))?.Replace('\\', '/') ?? "";
		byte[] Resolve(string relative)
		{
			var file = directory.Length == 0 ? relative : directory + "/" + relative;
			using var stream = storage.Assets.Read(file);
			return ReadAll(stream);
		}

		byte[] bytes;
		using (var stream = storage.Assets.Read(path)) bytes = ReadAll(stream);
		return Create(path, GltfReader.Read(bytes, Resolve));
	}

	/// <summary>Merges the placed triangles of a parsed document.</summary>
	internal static ModelGeometry Create(string name, GltfDocument document)
	{
		var matrices = new Matrix4x4[document.Nodes.Count];
		var visited = new bool[document.Nodes.Count];
		var positions = new List<Vector3>();
		var indices = new List<uint>();

		var stack = new Stack<(int Node, Matrix4x4 Parent)>();
		foreach (var root in document.SceneRoots) stack.Push((root, Matrix4x4.Identity));
		while (stack.Count > 0)
		{
			var (n, parent) = stack.Pop();
			if (n < 0 || n >= visited.Length || visited[n]) continue;
			visited[n] = true;
			var node = document.Nodes[n];
			var matrix = node.Transform.ToMatrix() * parent;
			matrices[n] = matrix;
			foreach (var child in node.Children) stack.Push((child, matrix));
			if (node.Mesh < 0 || node.Mesh >= document.Meshes.Count) continue;

			var mirrored = matrix.GetDeterminant() < 0;
			foreach (var primitive in document.Meshes[node.Mesh])
			{
				var mesh = primitive.Mesh;
				var first = (uint)positions.Count;
				foreach (var position in mesh.Positions) positions.Add(Vector3.Transform(position, matrix));
				foreach (var range in mesh.SubMeshes)
				{
					for (var i = range.FirstIndex; i + 2 < range.FirstIndex + range.IndexCount && i + 2 < mesh.Indices.Length; i += 3)
					{
						var a = first + (uint)(mesh.Indices[i] + range.BaseVertex);
						var b = first + (uint)(mesh.Indices[i + 1] + range.BaseVertex);
						var c = first + (uint)(mesh.Indices[i + 2] + range.BaseVertex);
						indices.Add(a);
						indices.Add(mirrored ? c : b);
						indices.Add(mirrored ? b : c);
					}
				}
			}
		}

		return new ModelGeometry(name, [.. positions], [.. indices]);
	}

	private static byte[] ReadAll(Stream stream)
	{
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
