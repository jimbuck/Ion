namespace Ion;

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

	public string GetPath(params string[] path)
	{
		return Path.Combine(_rootPath, Path.Combine(path));
	}

	public void CreateDirectory(params string[] path)
	{
		Directory.CreateDirectory(GetPath(path));
	}

	public void Write(string text, params string[] path)
	{
		File.WriteAllText(GetPath(path), text);
	}

	public void Write(byte[] bytes, params string[] path)
	{
		File.WriteAllBytes(GetPath(path), bytes);
	}

	public BinaryWriter OpenWrite(params string[] path)
	{
		var stream = File.OpenWrite(GetPath(path));
		return new BinaryWriter(stream);
	}

	public void Append(string text, params string[] path)
	{
		File.AppendAllText(GetPath(path), text);
	}

	public Stream Read(params string[] path)
	{
		return File.OpenRead(GetPath(path));
	}

	public IEnumerable<string> List(params string[] path)
	{
		return Directory.EnumerateFileSystemEntries(GetPath(path));
	}

	public void DeleteFile(params string[] path)
	{
		File.Delete(GetPath(path));
	}

	public void DeleteDirectory(params string[] path)
	{
		Directory.Delete(GetPath(path));
	}	
}
