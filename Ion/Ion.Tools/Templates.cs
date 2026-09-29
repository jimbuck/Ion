using System.Reflection;
using System.Text;

namespace Ion.Tools;

/// <summary>
/// The game templates embedded in the tool (the <c>templates/</c> folder of the repository): <c>2d</c>, <c>3d</c> and
/// <c>ecs</c>. Every occurrence of <c>MyIonGame</c> in paths and file contents becomes the game's name.
/// </summary>
internal static class Templates
{
	public const string Placeholder = "MyIonGame";

	/// <summary>Replaced by the Ion source checkout (or nothing): the dotnet new template's IonSource parameter.</summary>
	public const string IonSourceToken = "ION_SOURCE_PATH";

	public static readonly string[] Kinds = ["2d", "3d", "ecs"];

	/// <summary>The template files of <paramref name="kind"/>: relative path to resource name.</summary>
	public static IReadOnlyDictionary<string, string> Files(string kind)
	{
		var prefix = $"templates/{kind}/";
		var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
		foreach (var name in typeof(Templates).Assembly.GetManifestResourceNames())
		{
			if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
			var relative = name[prefix.Length..];
			if (relative.StartsWith(".template.config/", StringComparison.Ordinal)) continue;
			result[relative] = name;
		}

		return result;
	}

	/// <summary>
	/// Writes the <paramref name="kind"/> template named <paramref name="name"/> into <paramref name="output"/>. With
	/// <paramref name="ionSource"/> (an Ion repository checkout) the game builds against the sources instead of the packages.
	/// Returns the files written.
	/// </summary>
	public static List<string> Write(string kind, string name, string output, string? ionSource, bool force)
	{
		if (!Kinds.Contains(kind)) throw new ArgumentException($"Unknown template '{kind}'; use one of {string.Join(", ", Kinds)}.");
		if (!IsValidName(name)) throw new ArgumentException($"'{name}' is not a valid game name (letters, digits and '_', starting with a letter).");

		var files = Files(kind);
		if (files.Count == 0) throw new InvalidOperationException($"The {kind} template is not embedded in this build of the tool.");

		var root = Path.GetFullPath(output);
		if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any() && !force)
		{
			throw new IOException($"'{root}' is not empty; pass --force to write into it.");
		}

		var written = new List<string>();
		var assembly = typeof(Templates).Assembly;
		foreach (var (relative, resource) in files)
		{
			var target = Path.Combine(root, relative.Replace(Placeholder, name, StringComparison.Ordinal).Replace('/', Path.DirectorySeparatorChar));
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			using var stream = assembly.GetManifestResourceStream(resource)!;
			if (IsText(relative))
			{
				using var reader = new StreamReader(stream, Encoding.UTF8);
				var text = reader.ReadToEnd()
					.Replace(Placeholder, name, StringComparison.Ordinal)
					.Replace(IonSourceToken, ionSource is null ? "" : Path.GetFullPath(ionSource), StringComparison.Ordinal);

				File.WriteAllText(target, text, new UTF8Encoding(false));
			}
			else
			{
				using var file = File.Create(target);
				stream.CopyTo(file);
			}

			written.Add(target);
		}

		return written;
	}

	private static bool IsValidName(string name) =>
		name.Length > 0 && char.IsAsciiLetter(name[0]) && name.All(static c => char.IsAsciiLetterOrDigit(c) || c == '_');

	private static bool IsText(string path) => Path.GetExtension(path).ToLowerInvariant() is ".cs" or ".csproj" or ".props" or ".targets" or ".json" or ".md" or ".sln" or ".slnx" or ".gitignore" or ".editorconfig" or ".txt" or ""
		|| Path.GetFileName(path) is ".gitignore";
}
