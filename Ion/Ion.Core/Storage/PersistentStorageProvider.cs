namespace Ion;

internal class PersistentStorageProvider(params string[] rootPath) : IPersistentStorageProvider
{
	private readonly string _rootPath = Path.Combine(rootPath);

	public string RootPath => _rootPath;

	public void Initialize()
	{
		// Directories are created on first write (see _ensureParentDirectory) rather than eagerly.
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
		File.WriteAllText(_ensureParentDirectory(GetPath(path)), text);
	}

	public void Write(byte[] bytes, params string[] path)
	{
		File.WriteAllBytes(_ensureParentDirectory(GetPath(path)), bytes);
	}

	/// <summary>
	/// Opens the file for writing, creating it or truncating any existing content.
	/// </summary>
	public BinaryWriter OpenWrite(params string[] path)
	{
		var stream = new FileStream(_ensureParentDirectory(GetPath(path)), FileMode.Create, FileAccess.Write, FileShare.None);
		return new BinaryWriter(stream);
	}

	public void Append(string text, params string[] path)
	{
		File.AppendAllText(_ensureParentDirectory(GetPath(path)), text);
	}

	public Stream Read(params string[] path)
	{
		var fullPath = GetPath(path);
		if (!File.Exists(fullPath)) throw CreateFileNotFound(Path.Combine(path), fullPath);

		return File.OpenRead(fullPath);
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

	/// <summary>
	/// Builds a <see cref="FileNotFoundException"/> that names the requested file, the full resolved path and, when a file
	/// with the same name but different casing exists, points that out (paths are case-sensitive on Linux and macOS).
	/// </summary>
	internal static FileNotFoundException CreateFileNotFound(string name, string fullPath)
	{
		var message = $"File '{name}' was not found at '{fullPath}'.";

		var caseMatch = FindCaseInsensitiveMatch(fullPath);
		if (caseMatch != null)
		{
			message += $" A file with different casing exists: '{Path.GetFileName(caseMatch)}'. File names are case-sensitive on Linux and macOS.";
		}
		else
		{
			message += " Check the name and its casing (file names are case-sensitive on Linux and macOS) and that the file is copied to the output directory.";
		}

		return new FileNotFoundException(message, fullPath);
	}

	internal static string? FindCaseInsensitiveMatch(string fullPath)
	{
		try
		{
			var directory = Path.GetDirectoryName(fullPath);
			var fileName = Path.GetFileName(fullPath);
			if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName) || !Directory.Exists(directory)) return null;

			foreach (var candidate in Directory.EnumerateFiles(directory))
			{
				if (string.Equals(Path.GetFileName(candidate), fileName, StringComparison.OrdinalIgnoreCase)) return candidate;
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// Best effort only.
		}

		return null;
	}

	private static string _ensureParentDirectory(string fullPath)
	{
		var directory = Path.GetDirectoryName(fullPath);
		if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
		return fullPath;
	}
}
