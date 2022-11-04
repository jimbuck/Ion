using Veldrid;

namespace Kyber.Assets;

internal class ProcessedModelSerializer : BinaryAssetSerializer<ProcessedModel>
{
	public override ProcessedModel ReadT(BinaryReader reader)
	{
		ProcessedMeshPart[] parts = reader.ReadObjectArray(ReadMeshPart);

		return new ProcessedModel()
		{
			MeshParts = parts
		};
	}

	public override void WriteT(BinaryWriter writer, ProcessedModel value)
	{
		writer.WriteObjectArray(value.MeshParts, WriteMeshPart);
	}

	private void WriteMeshPart(BinaryWriter writer, ProcessedMeshPart part)
	{
		writer.WriteByteArray(part.VertexData);
		writer.WriteObjectArray(part.VertexElements, WriteVertexElementDesc);
		writer.WriteByteArray(part.IndexData);
		writer.WriteEnum(part.IndexFormat);
		writer.Write(part.IndexCount);
		//writer.WriteDictionary(part.BoneIDsByName);
		writer.WriteBlittableArray(part.BoneOffsets);
	}

	private ProcessedMeshPart ReadMeshPart(BinaryReader reader)
	{
		byte[] vertexData = reader.ReadByteArray();
		VertexElementDescription[] vertexDescs = reader.ReadObjectArray(ReadVertexElementDesc);
		byte[] indexData = reader.ReadByteArray();
		IndexFormat format = reader.ReadEnum<IndexFormat>();
		uint indexCount = reader.ReadUInt32();
		//Dictionary<string, uint> dict = reader.ReadDictionary<string, uint>();
		Matrix4x4[] boneOffsets = reader.ReadBlittableArray<Matrix4x4>();

		return new ProcessedMeshPart(
			vertexData,
			vertexDescs,
			indexData,
			format,
			indexCount,
			new Dictionary<string, uint>(),
			boneOffsets);
	}


	private void WriteVertexElementDesc(BinaryWriter writer, VertexElementDescription desc)
	{
		writer.Write(desc.Name);
		writer.WriteEnum(desc.Semantic);
		writer.WriteEnum(desc.Format);
	}

	public VertexElementDescription ReadVertexElementDesc(BinaryReader reader)
	{
		string name = reader.ReadString();
		VertexElementSemantic semantic = reader.ReadEnum<VertexElementSemantic>();
		VertexElementFormat format = reader.ReadEnum<VertexElementFormat>();
		return new VertexElementDescription(name, format, semantic);
	}
}
