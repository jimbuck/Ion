using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Ion.Tests;

/// <summary>
/// Where <see cref="IonApplication.CreateBuilder(string[])"/> reads <c>appsettings.json</c> from and what storage paths
/// resolve against: the executable's folder by default, whatever the working directory, or an explicit
/// <c>--contentRoot</c>.
/// </summary>
public class ContentRootTests : IDisposable
{
	private readonly string _root;

	public ContentRootTests()
	{
		_root = Path.Combine(Path.GetTempPath(), "ion-content-root-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_root);
	}

	public void Dispose()
	{
		try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
	}

	private static string Trim(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheDefaultContentRootIsTheBaseDirectory()
	{
		using var app = IonApplication.CreateBuilder().Build();

		var environment = app.Services.GetRequiredService<IHostEnvironment>();
		Assert.Equal(Trim(AppContext.BaseDirectory), Trim(environment.ContentRootPath));

		var storage = app.Services.GetRequiredService<IPersistentStorage>();
		Assert.Equal(Path.Combine(Trim(AppContext.BaseDirectory), "Assets", "a.png"), storage.Assets.GetPath("a.png"));
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AppSettingsAreReadFromTheBaseDirectoryNotTheWorkingDirectory()
	{
		// An environment of its own, so the settings file this test writes next to the test assembly is only read here.
		var environmentName = "ContentRootTest" + Guid.NewGuid().ToString("N");
		var settings = Path.Combine(AppContext.BaseDirectory, $"appsettings.{environmentName}.json");
		File.WriteAllText(settings, """{ "Ion": { "Title": "From the base directory" } }""");
		try
		{
			// The working directory is left alone (it is shared by the tests running in parallel); the base directory is
			// not the working directory in general, and the host's default would read from the latter.
			using var app = IonApplication.CreateBuilder([$"--environment={environmentName}"]).Build();
			Assert.Equal("From the base directory", app.Configuration["Ion:Title"]);
		}
		finally
		{
			File.Delete(settings);
		}
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AnExplicitContentRootWinsForSettingsAndStorage()
	{
		File.WriteAllText(Path.Combine(_root, "appsettings.json"), """{ "Ion": { "Title": "From the content root", "Storage": { "AssetsPath": "Content" } } }""");

		using var app = IonApplication.CreateBuilder(["--contentRoot", _root]).Build();

		Assert.Equal("From the content root", app.Configuration["Ion:Title"]);
		Assert.Equal(Trim(_root), Trim(app.Services.GetRequiredService<IHostEnvironment>().ContentRootPath));

		var storage = app.Services.GetRequiredService<IPersistentStorage>();
		Assert.Equal(Path.Combine(Trim(_root), "x.txt"), storage.Game.GetPath("x.txt"));
		Assert.Equal(Path.Combine(Trim(_root), "Content", "a.png"), storage.Assets.GetPath("a.png"));
	}

	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData("--contentRoot=/games/rocket")]
	[InlineData("--contentRoot", "/games/rocket")]
	[InlineData("/contentRoot", "/games/rocket")]
	[InlineData("--contentroot=/games/rocket")]
	public void ExplicitContentRootIsFoundInTheCommandLine(params string[] args)
	{
		Assert.Equal("/games/rocket", IonApplicationBuilder.FindExplicitContentRoot(args));
		Assert.Null(IonApplicationBuilder.CreateHostSettings(args).ContentRootPath);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void WithoutAnExplicitContentRootTheHostSettingsUseTheBaseDirectory()
	{
		string[] args = ["--Ion:Title=Rocket", "--headless"];
		Assert.Null(IonApplicationBuilder.FindExplicitContentRoot(args));
		Assert.Equal(AppContext.BaseDirectory, IonApplicationBuilder.CreateHostSettings(args).ContentRootPath);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void StorageResolvesRelativePathsAgainstTheContentRoot()
	{
		var storage = new PersistentStorage(Options.Create(new GameConfig()), Options.Create(new StorageConfig { GamePath = "Game", UserPath = "User" }),
			new TestEnvironment { ContentRootPath = _root });

		Assert.Equal(Path.Combine(_root, "Game", "Assets", "a.png"), storage.Assets.GetPath("a.png"));
		Assert.Equal(Path.Combine(_root, "User", "Saves", "s"), storage.Saves.GetPath("s"));
	}

	private sealed class TestEnvironment : IHostEnvironment
	{
		public string EnvironmentName { get; set; } = Environments.Production;
		public string ApplicationName { get; set; } = "Test";
		public string ContentRootPath { get; set; } = "";
		public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
	}
}
