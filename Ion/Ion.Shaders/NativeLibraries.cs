using System.Runtime.InteropServices;

using Silk.NET.Core.Contexts;
using Silk.NET.Shaderc;

using Cross = Silk.NET.SPIRV.Cross.Cross;

namespace Ion.Shaders;

/// <summary>
/// Loads the Shaderc and SPIRV-Cross native libraries that ship in the Silk.NET native packages. The libraries are
/// loaded by absolute path from this tool's own <c>runtimes/{rid}/native</c> folder first, so that the tool does not
/// depend on how the host resolves runtime-specific assets when it runs as a build step (that resolution differed
/// between machines and left the generic "could not load from any of the possible library names" error behind).
/// When the file is not there, Silk.NET's default probing is used, and a failure names every path that was tried
/// together with the loader's own error.
/// </summary>
internal static class NativeLibraries
{
	private static readonly Lazy<Shaderc> _shaderc = new(() => Load("shaderc_shared", ctx => new Shaderc(ctx), Shaderc.GetApi));
	private static readonly Lazy<Cross> _cross = new(() => Load("spirv-cross", ctx => new Cross(ctx), Cross.GetApi));

	public static Shaderc Shaderc => _shaderc.Value;
	public static Cross Cross => _cross.Value;

	private static T Load<T>(string baseName, Func<INativeContext, T> create, Func<T> createDefault)
	{
		var fileName = FileNameFor(baseName);
		var candidates = new List<string>();
		foreach (var rid in RuntimeIdentifiers())
		{
			candidates.Add(Path.Combine(AppContext.BaseDirectory, "runtimes", rid, "native", fileName));
		}

		candidates.Add(Path.Combine(AppContext.BaseDirectory, fileName));

		var errors = new List<string>();
		foreach (var path in candidates)
		{
			if (!File.Exists(path)) continue;
			try
			{
				return create(new DefaultNativeContext(path));
			}
			catch (Exception ex)
			{
				errors.Add($"{path}: {Describe(path, ex)}");
			}
		}

		try
		{
			return createDefault();
		}
		catch (Exception ex)
		{
			var tried = string.Join("; ", candidates.Select(p => File.Exists(p) ? p : $"{p} (missing)"));
			var details = errors.Count == 0 ? "" : " Errors: " + string.Join(" | ", errors);
			throw new ShaderCompilationException(
				$"cannot load the native library {fileName} ({ex.Message.Trim()}). Tried: {tried}.{details}");
		}
	}

	private static string Describe(string path, Exception ex)
	{
		// NativeLibrary.Load reports the platform loader's own message (dlopen, LoadLibrary), which Silk.NET hides.
		try
		{
			var handle = NativeLibrary.Load(path);
			NativeLibrary.Free(handle);
			return ex.Message.Trim();
		}
		catch (Exception loadEx)
		{
			return loadEx.Message.Trim();
		}
	}

	private static string FileNameFor(string baseName)
	{
		if (OperatingSystem.IsWindows()) return baseName + ".dll";
		if (OperatingSystem.IsMacOS()) return "lib" + baseName + ".dylib";
		return "lib" + baseName + ".so";
	}

	private static IEnumerable<string> RuntimeIdentifiers()
	{
		var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
		var arch = RuntimeInformation.ProcessArchitecture switch
		{
			Architecture.X64 => "x64",
			Architecture.X86 => "x86",
			Architecture.Arm64 => "arm64",
			Architecture.Arm => "arm",
			_ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
		};
		yield return $"{os}-{arch}";
		var runtimeRid = RuntimeInformation.RuntimeIdentifier;
		if (!string.IsNullOrEmpty(runtimeRid) && runtimeRid != $"{os}-{arch}") yield return runtimeRid;
	}
}
