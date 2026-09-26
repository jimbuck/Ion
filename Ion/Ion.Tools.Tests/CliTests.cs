using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using Ion.Tests;

namespace Ion.Tools.Tests;

[Trait(TestConstants.CATEGORY, TestConstants.UNIT)]
public sealed class CliTests
{
	[Fact]
	public void ArgumentsSplitPositionalsOptionsFlagsAndRest()
	{
		var a = new Arguments(["game", "--frames", "600", "--headless", "--seed=4", "--", "--Ion:X=1", "extra"]);
		Assert.Equal("game", a.Positional(0));
		Assert.Equal(600, a.IntOption("--frames"));
		Assert.Equal(4, a.IntOption("--seed"));
		Assert.True(a.Flag("--headless"));
		Assert.False(a.Flag("--remote"));
		Assert.Equal(["--Ion:X=1", "extra"], a.Rest);
		a.ThrowOnUnknown();

		var unknown = new Arguments(["--frmaes", "3"]);
		Assert.Throws<ArgumentException>(unknown.ThrowOnUnknown);
		Assert.Throws<FormatException>(() => new Arguments(["--frames", "many"]).IntOption("--frames"));
	}

	[Fact]
	public void PublishForwardsThePresetToDotnetPublish()
	{
		var args = Cli.PublishArguments("/g/Game.csproj", "r36s", null, "out/r36s", "sysroot", ["-v", "m"]);
		Assert.Equal(["publish", "/g/Game.csproj", "-c", "Release", "-p:IonTarget=r36s", "-o", Path.GetFullPath("out/r36s"), $"-p:IonArm64SysRoot={Path.GetFullPath("sysroot")}", "-v", "m"], args);

		Assert.Equal(["publish", "/g/Game.csproj", "-c", "Debug", "-p:IonTarget=linux-x64"], Cli.PublishArguments("/g/Game.csproj", "linux-x64", "Debug", null, null, []));
		Assert.Throws<ArgumentException>(() => Cli.PublishArguments("/g/Game.csproj", "r35s", null, null, null, []));
		Assert.Equal(2, Cli.Execute(["publish", "--target", "nope"]));
	}

	[Fact]
	public void RunOptionsBecomeEngineConfiguration()
	{
		var args = GameRunner.GameArguments(new GameRunOptions
		{
			Headless = true,
			Frames = 600,
			Seed = 42,
			Screenshot = "out/f.png",
			Summary = "out/run.json",
			AllowMutations = true,
			PauseAtFrame = 10,
			ExtraArgs = ["--Custom=1"],
		}, "/runs");

		Assert.Contains("--Ion:Headless=true", args);
		Assert.Contains("--Ion:Headless:Render=true", args); // a screenshot needs rendering
		Assert.Contains("--Ion:Run:Frames=600", args);
		Assert.Contains("--Ion:Seed=42", args);
		Assert.Contains($"--Ion:Run:Screenshot={Path.GetFullPath("out/f.png")}", args);
		Assert.Contains($"--Ion:Run:Summary={Path.GetFullPath("out/run.json")}", args);
		Assert.Contains("--Ion:Remote:Enabled=true", args);
		Assert.Contains("--Ion:Remote:AllowMutations=true", args);
		Assert.Contains("--Ion:Remote:PauseAtFrame=10", args);
		Assert.Contains("--Ion:Remote:RunDirectory=/runs", args);
		Assert.Contains("--Ion:Run:FixedStep=true", args);
		Assert.Equal("--Custom=1", args[^1]);

		var plain = GameRunner.GameArguments(new GameRunOptions(), "/runs");
		Assert.Empty(plain);
	}

	[Fact]
	public void ResolveProjectFindsTheGameProject()
	{
		var dir = Repo.TempDirectory("resolve");
		Directory.CreateDirectory(Path.Combine(dir, "Game"));
		Directory.CreateDirectory(Path.Combine(dir, "Game.Tests"));
		File.WriteAllText(Path.Combine(dir, "Game", "Game.csproj"), "<Project />");
		File.WriteAllText(Path.Combine(dir, "Game.Tests", "Game.Tests.csproj"), "<Project />");

		Assert.Equal(Path.Combine(dir, "Game", "Game.csproj"), GameRunner.ResolveProject(dir));
		Assert.Equal(Path.Combine(dir, "Game", "Game.csproj"), GameRunner.ResolveProject(Path.Combine(dir, "Game")));
		Assert.Throws<FileNotFoundException>(() => GameRunner.ResolveProject(Path.Combine(dir, "Nope")));

		File.WriteAllText(Path.Combine(dir, "Game", "Other.csproj"), "<Project />");
		Assert.Throws<FileNotFoundException>(() => GameRunner.ResolveProject(Path.Combine(dir, "Game")));
		Directory.Delete(dir, recursive: true);
	}

	[Theory]
	[InlineData("2d", "state-300.json")]
	[InlineData("3d", "state-120.json")]
	[InlineData("ecs", "world-120.json")]
	public void NewWritesTheTemplateWithTheGameName(string kind, string snapshot)
	{
		var dir = Repo.TempDirectory("new");
		var output = Path.Combine(dir, "Rocket");
		var written = Templates.Write(kind, "Rocket", output, ionSource: null, force: false);

		Assert.Contains(Path.Combine(output, "CLAUDE.md"), written);
		Assert.True(File.Exists(Path.Combine(output, "Rocket", "Rocket.csproj")));
		Assert.True(File.Exists(Path.Combine(output, "Rocket", "appsettings.json")));
		Assert.True(File.Exists(Path.Combine(output, "Rocket.Tests", "Rocket.Tests.csproj")));
		Assert.True(File.Exists(Path.Combine(output, "Rocket.Tests", "Golden", snapshot)));
		Assert.True(File.Exists(Path.Combine(output, "Rocket.slnx")));
		Assert.False(Directory.Exists(Path.Combine(output, ".template.config")));

		foreach (var file in written)
		{
			var text = File.ReadAllText(file);
			Assert.DoesNotContain(Templates.Placeholder, text, StringComparison.Ordinal);
			Assert.DoesNotContain(Templates.IonSourceToken, text, StringComparison.Ordinal);
		}

		Assert.Contains("namespace Rocket;", File.ReadAllText(Path.Combine(output, "Rocket", "Game.cs")), StringComparison.Ordinal);
		Assert.Contains("# Rocket", File.ReadAllText(Path.Combine(output, "CLAUDE.md")), StringComparison.Ordinal);
		Assert.Contains("<IonSource></IonSource>", File.ReadAllText(Path.Combine(output, "Directory.Build.props")), StringComparison.Ordinal);

		// Not over an existing game without --force; an Ion checkout goes into IonSource.
		Assert.Throws<IOException>(() => Templates.Write(kind, "Rocket", output, null, force: false));
		Templates.Write(kind, "Rocket", output, ionSource: "/src/ion", force: true);
		Assert.Contains($"<IonSource>{Path.GetFullPath("/src/ion")}</IonSource>", File.ReadAllText(Path.Combine(output, "Directory.Build.props")), StringComparison.Ordinal);
		Directory.Delete(dir, recursive: true);
	}

	[Fact]
	public void NewRejectsBadNamesAndKinds()
	{
		var dir = Repo.TempDirectory("bad");
		Assert.Throws<ArgumentException>(() => Templates.Write("4d", "Game", dir, null, true));
		Assert.Throws<ArgumentException>(() => Templates.Write("2d", "1Game", dir, null, true));
		Assert.Throws<ArgumentException>(() => Templates.Write("2d", "My-Game", dir, null, true));
		Directory.Delete(dir, recursive: true);
	}

	[Fact]
	public void DiffComparesWithToleranceAndWritesTheDiff()
	{
		var dir = Repo.TempDirectory("diff");
		var a = Path.Combine(dir, "a.png");
		var b = Path.Combine(dir, "b.png");
		var c = Path.Combine(dir, "c.png");
		Write(a, 8, 8, (x, y) => new Rgba32(10, 20, 30, 255));
		Write(b, 8, 8, (x, y) => x == 3 && y == 4 ? new Rgba32(200, 20, 30, 255) : new Rgba32(11, 21, 31, 255));
		Write(c, 4, 4, (x, y) => new Rgba32(0, 0, 0, 255));

		var same = ImageDiff.Compare(a, a);
		Assert.True(same.Matches());
		var off = ImageDiff.Compare(b, a, tolerance: 2, diffPath: Path.Combine(dir, "d.png"));
		Assert.Equal(1, off.MismatchedPixels);
		Assert.Equal(190, off.MaxChannelDifference);
		Assert.False(off.Matches());
		Assert.True(off.Matches(maxMismatchRatio: 1.0 / 64));
		using (var diff = Image.Load<Rgba32>(off.DiffPath!)) Assert.Equal(new Rgba32(255, 0, 0, 255), diff[3, 4]);
		Assert.False(ImageDiff.Compare(c, a).SameSize);

		var (code, output) = Repo.Ion(dir, "diff", b, a, "--tolerance", "2");
		Assert.Equal(1, code);
		Assert.Contains("MISMATCH", output, StringComparison.Ordinal);
		Assert.Equal(0, Repo.Ion(dir, "diff", b, a, "--max-ratio", "0.02").ExitCode);
		Directory.Delete(dir, recursive: true);

		static void Write(string path, int w, int h, Func<int, int, Rgba32> pixel)
		{
			using var image = new Image<Rgba32>(w, h);
			for (var y = 0; y < h; y++) for (var x = 0; x < w; x++) image[x, y] = pixel(x, y);
			image.SaveAsPng(path);
		}
	}

	[Fact]
	public void HelpAndUnknownCommands()
	{
		var (help, text) = Repo.Ion(Path.GetTempPath(), "--help");
		Assert.Equal(0, help);
		Assert.Contains("ion new <2d|3d|ecs>", text, StringComparison.Ordinal);
		Assert.Equal(2, Repo.Ion(Path.GetTempPath(), "fly").ExitCode);
	}
}
