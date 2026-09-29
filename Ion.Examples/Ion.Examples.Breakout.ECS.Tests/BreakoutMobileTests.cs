using Xunit;

using static Ion.Tests.TestConstants;

namespace Ion.Examples.Breakout.ECS.Tests;

/// <summary>
/// The shared entry point of the Android and iOS heads, run on the desktop: the mobile configuration, and a headless run
/// of the same code path the heads call.
/// </summary>
public class BreakoutMobileTests
{
	[Fact, Trait(CATEGORY, UNIT)]
	public void MobileArgumentsSelectSdlFullscreenAndTheContentRoot()
	{
		var args = BreakoutMobile.Arguments("/data/user/0/ion.breakout/files/game", ["--Ion:Seed=3"]);

		Assert.Contains("--Ion:Storage:GamePath=/data/user/0/ion.breakout/files/game", args);
		Assert.Contains("--Ion:Window:Platform=Sdl", args);
		Assert.Contains("--Ion:Window:Fullscreen=true", args);
		Assert.Contains("--Ion:Window:ShowCursor=false", args);
		Assert.Equal("--Ion:Seed=3", args[^1]);
		Assert.DoesNotContain(args, a => a.StartsWith("--Ion:Graphics:PreferredBackend", StringComparison.Ordinal));
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheMobileEntryPointRunsHeadless()
	{
		// The heads pass the folder they unpacked (Android) or bundled (iOS) the assets into; here the test's output folder.
		BreakoutMobile.Run(AppContext.BaseDirectory, ["--Ion:Headless=true", "--Ion:Run:Frames=30", "--Logging:LogLevel:Default=Warning"]);
	}
}
