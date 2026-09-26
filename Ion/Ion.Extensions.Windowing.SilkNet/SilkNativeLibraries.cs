using System.Runtime.InteropServices;

using Silk.NET.Core.Loader;

namespace Ion.Extensions.Windowing;

/// <summary>
/// Makes Silk.NET find the native libraries that its NuGet packages ship (GLFW, SDL, and any other Silk.NET binding)
/// in the application's <c>runtimes/{rid}/native</c> folders.
/// </summary>
/// <remarks>
/// Silk.NET's own resolver derives the runtime identifier from <c>/etc/os-release</c> and maps distribution-specific
/// identifiers to the portable <c>linux-x64</c> folder only for a fixed list of distributions, which does not include
/// Ubuntu; a framework-dependent game on Ubuntu (or any unlisted distribution) without the system GLFW and SDL packages
/// then reports every window platform as not applicable. <see cref="EnsureResolver"/> appends a resolver that maps a bare
/// library name to <c>{AppContext.BaseDirectory}/runtimes/{rid}/native/{name}</c> for the process's runtime identifier
/// and its portable fallbacks, the same folders the .NET host uses for <c>DllImport</c>. It runs before the first
/// platform registration and is a no-op under NativeAOT, where the publish puts the natives next to the executable.
/// </remarks>
public static class SilkNativeLibraries
{
	private static int _installed;

	/// <summary>Appends the app-local runtimes resolver to Silk.NET's default path resolver, once.</summary>
	public static void EnsureResolver()
	{
		if (Interlocked.Exchange(ref _installed, 1) == 1) return;
		if (PathResolver.Default is DefaultPathResolver resolver) resolver.Resolvers.Add(AppLocalRuntimesCandidates);
	}

	/// <summary>
	/// The existing app-local <c>runtimes/{rid}/native</c> files for a bare library <paramref name="name"/>; nothing for a
	/// name that already has a directory.
	/// </summary>
	public static IEnumerable<string> AppLocalRuntimesCandidates(string name)
	{
		if (string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(Path.GetDirectoryName(name))) return [];
		var baseDirectory = AppContext.BaseDirectory;
		if (string.IsNullOrEmpty(baseDirectory)) return [];
		var results = new List<string>();
		foreach (var rid in RuntimeIdentifiers())
		{
			var candidate = Path.Combine(baseDirectory, "runtimes", rid, "native", name);
			if (File.Exists(candidate)) results.Add(candidate);
		}

		return results;
	}

	/// <summary>The process's runtime identifier followed by its portable fallbacks, most specific first, distinct.</summary>
	public static IEnumerable<string> RuntimeIdentifiers()
	{
		var os = OperatingSystem.IsWindows() ? "win"
			: OperatingSystem.IsMacOS() ? "osx"
			: OperatingSystem.IsAndroid() ? "android"
			: OperatingSystem.IsIOS() ? "ios"
			: OperatingSystem.IsFreeBSD() ? "freebsd"
			: "linux";
		var arch = RuntimeInformation.ProcessArchitecture switch
		{
			Architecture.X64 => "x64",
			Architecture.X86 => "x86",
			Architecture.Arm64 => "arm64",
			Architecture.Arm => "arm",
			var other => other.ToString().ToLowerInvariant(),
		};

		var seen = new HashSet<string>(StringComparer.Ordinal);
		var runtimeRid = RuntimeInformation.RuntimeIdentifier;
		if (!string.IsNullOrEmpty(runtimeRid) && seen.Add(runtimeRid)) yield return runtimeRid;
		if (seen.Add($"{os}-{arch}")) yield return $"{os}-{arch}";
		if (seen.Add(os)) yield return os;
	}
}
