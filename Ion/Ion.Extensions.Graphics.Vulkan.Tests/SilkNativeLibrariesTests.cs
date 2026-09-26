using System.Runtime.InteropServices;

using Ion.Extensions.Windowing;

using Silk.NET.Core.Loader;

namespace Ion.Extensions.Graphics.Vulkan.Tests;

/// <summary>
/// The app-local runtimes resolver that lets Silk.NET find its NuGet natives on Linux distributions its own resolver
/// does not map to the portable runtime identifier.
/// </summary>
public class SilkNativeLibrariesTests
{
	[Fact, Trait(CATEGORY, UNIT)]
	public void TheRuntimeIdentifiersStartWithTheProcessOneAndEndWithThePortableOnes()
	{
		var rids = SilkNativeLibraries.RuntimeIdentifiers().ToArray();
		Assert.Equal(rids, rids.Distinct());
		Assert.Equal(RuntimeInformation.RuntimeIdentifier, rids[0]);
		var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
		Assert.Contains($"{os}-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}", rids);
		Assert.Equal(os, rids[^1]);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void BareNamesResolveToTheShippedNativesAndLoad()
	{
		// The GLFW and SDL natives ship in the Silk.NET packages under runtimes/{rid}/native of this test's output.
		var glfw = OperatingSystem.IsWindows() ? "glfw3.dll" : OperatingSystem.IsMacOS() ? "libglfw.3.dylib" : "libglfw.so.3";
		var candidates = SilkNativeLibraries.AppLocalRuntimesCandidates(glfw).ToArray();
		Assert.NotEmpty(candidates);
		Assert.All(candidates, c => Assert.True(File.Exists(c), c));
		Assert.All(candidates, c => Assert.StartsWith(Path.Combine(AppContext.BaseDirectory, "runtimes"), c, StringComparison.Ordinal));

		var handle = NativeLibrary.Load(candidates[0]);
		Assert.NotEqual(nint.Zero, handle);
		NativeLibrary.Free(handle);

		Assert.Empty(SilkNativeLibraries.AppLocalRuntimesCandidates(candidates[0]));
		Assert.Empty(SilkNativeLibraries.AppLocalRuntimesCandidates("libion-no-such-library.so"));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheResolverIsAppendedToSilkOnceAndSilkThenProbesTheRuntimesFolder()
	{
		SilkNativeLibraries.EnsureResolver();
		SilkNativeLibraries.EnsureResolver();
		var resolver = Assert.IsType<DefaultPathResolver>(PathResolver.Default);
		Assert.Single(resolver.Resolvers, r => r == SilkNativeLibraries.AppLocalRuntimesCandidates);

		var glfw = OperatingSystem.IsWindows() ? "glfw3.dll" : OperatingSystem.IsMacOS() ? "libglfw.3.dylib" : "libglfw.so.3";
		Assert.Contains(resolver.EnumeratePossibleLibraryLoadTargets(glfw), t => t.Contains(Path.Combine("runtimes", ""), StringComparison.Ordinal) && File.Exists(t));
	}
}
