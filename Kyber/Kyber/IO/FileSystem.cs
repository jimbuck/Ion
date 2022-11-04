namespace Kyber.IO;

public interface IFileSystem
{
	void CreateDirectory(string path);
	void Write(string path, string text);
	void Write(string path, byte[] bytes);
	void Append(string path, string text);
	void Append(string path, byte[] bytes);
	byte[] Read(string path);
	void Delete(string path);
}


public class FileSystem
{

}
