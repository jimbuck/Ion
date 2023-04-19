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

internal class PersistentStorageProvider : IPersistentStorageProvider
{
	private readonly string _rootPath;

	public PersistentStorageProvider(params string[] rootPath)
	{
		_rootPath = Path.Combine(rootPath);
	}

	public void Initialize()
	{
		// TODO: Determine if we really want to do this.
		//Directory.CreateDirectory(_rootPath);
	}

	public PersistentStorageProvider Subpath(params string[] path)
	{
		return new PersistentStorageProvider(_rootPath, Path.Combine(path));
	}

	public void CreateDirectory(params string[] path)
	{
		Directory.CreateDirectory(_getPath(path));
	}

	public void Write(string text, params string[] path)
	{
		File.WriteAllText(_getPath(path), text);
	}

	public void Write(byte[] bytes, params string[] path)
	{
		File.WriteAllBytes(_getPath(path), bytes);
	}

	public BinaryWriter OpenWrite(params string[] path)
	{
		var stream = File.OpenWrite(_getPath(path));
		return new BinaryWriter(stream);
	}

	public void Append(string text, params string[] path)
	{
		File.AppendAllText(_getPath(path), text);
	}

	public Stream Read(params string[] path)
	{
		return File.OpenRead(_getPath(path));
	}

	public IEnumerable<string> List(params string[] path)
	{
		return Directory.EnumerateFileSystemEntries(_getPath(path));
	}

	public void DeleteFile(params string[] path)
	{
		File.Delete(_getPath(path));
	}

	public void DeleteDirectory(params string[] path)
	{
		Directory.Delete(_getPath(path));
	}	

	private string _getPath(params string[] path)
	{
		return Path.Combine(_rootPath, Path.Combine(path));
	}
}
