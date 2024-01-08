namespace Kyber.Storage;

public interface IPersistentStorageProvider
{
	void CreateDirectory(params string[] path);
	void Write(string text, params string[] path);
	void Write(byte[] bytes, params string[] path);
	BinaryWriter OpenWrite(params string[] path);
	void Append(string text, params string[] path);
	Stream Read(params string[] path);
	IEnumerable<string> List(params string[] path);
	void DeleteFile(params string[] path);
	void DeleteDirectory(params string[] path);
}
