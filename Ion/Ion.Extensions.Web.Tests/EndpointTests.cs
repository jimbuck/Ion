using System.Net;
using System.Net.Http.Json;
using System.Text;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Web.Tests;

/// <summary>The generated routes served end to end: binding, results, errors and the route table.</summary>
[Trait(CATEGORY, INTEGRATION)]
public class EndpointTests
{
	[Fact]
	public void TheServerIsOffUnlessEnabled()
	{
		using var game = new WebGame(new Dictionary<string, string?> { ["Ion:Web:Enabled"] = "false" }, start: false);
		game.Host.Start();
		Assert.Null(game.Host.Services.GetService(typeof(IWebServer)));
	}

	[Fact]
	public async Task JsonResultsNumbersAndText()
	{
		using var game = new WebGame();
		game.System.Score = 41;

		var score = await game.Http.GetFromJsonAsync("/score", TestJson.Default.ScoreInfo);
		Assert.Equal(41, score.Score);
		Assert.True(score.Frame > 0);

		using var number = await game.Http.GetAsync("/number");
		Assert.Equal("application/json", number.Content.Headers.ContentType!.MediaType);
		Assert.Equal("41", await number.Content.ReadAsStringAsync());
		Assert.Equal("no-store", number.Headers.CacheControl!.ToString());

		using var added = await game.Http.PostAsync("/score/add?amount=2", null);
		Assert.Equal(HttpStatusCode.NoContent, added.StatusCode);
		Assert.Equal(43, game.System.Score);

		Assert.Equal("hello world", await game.Http.GetStringAsync("/echo/hello%20world"));
		Assert.Equal("path:a/b/c.txt", await game.Http.GetStringAsync("/files/a/b/c.txt"));
		Assert.Equal("path:", await game.Http.GetStringAsync("/files"));
		Assert.Equal("abc", await game.Http.GetStringAsync("/bytes"));
	}

	[Fact]
	public async Task QueryParametersBindWithDefaults()
	{
		using var game = new WebGame();
		Assert.Equal("3|False|null|1.5", await game.Http.GetStringAsync("/query?count=3"));
		Assert.Equal("-7|True|Ada Lovelace|0.25", await game.Http.GetStringAsync("/query?name=Ada+Lovelace&flag&scale=0.25&count=-7"));
		Assert.Equal("1|False|a&b|1.5", await game.Http.GetStringAsync("/query?count=1&flag=off&name=a%26b"));

		using var missing = await game.Http.GetAsync("/query");
		Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
		Assert.Equal("Missing query parameter 'count'.", await missing.Content.ReadAsStringAsync());

		using var invalid = await game.Http.GetAsync("/query?count=abc");
		Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
		Assert.Equal("Invalid value for query parameter 'count'.", await invalid.Content.ReadAsStringAsync());
	}

	[Fact]
	public async Task BodiesBindAsTextAndJson()
	{
		using var game = new WebGame();
		using var renamed = await game.Http.PutAsync("/players/7/name", new StringContent("Grace"));
		Assert.Equal("7=Grace", await renamed.Content.ReadAsStringAsync());

		using var levelled = await game.Http.PostAsJsonAsync("/players", new Player { Name = "Ada", Level = 4 }, TestJson.Default.Player);
		var player = await levelled.Content.ReadFromJsonAsync(TestJson.Default.Player);
		Assert.Equal("Ada", player!.Name);
		Assert.Equal(5, player.Level);

		using var bad = await game.Http.PostAsync("/players", new StringContent("{not json", Encoding.UTF8, "application/json"));
		Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
		Assert.StartsWith("The body is not valid JSON for Player", await bad.Content.ReadAsStringAsync(), StringComparison.Ordinal);

		using var empty = await game.Http.PostAsync("/players", null);
		Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
	}

	[Fact]
	public async Task ResponsesCanBeWrittenByTheHandler()
	{
		using var game = new WebGame();
		using var response = await game.Http.GetAsync("/custom?q=x");
		Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
		Assert.Equal("yes", response.Headers.GetValues("X-Test").Single());
		Assert.Equal("custom x", await response.Content.ReadAsStringAsync());
	}

	[Fact]
	public async Task ErrorsAreAnsweredAndTheGameGoesOn()
	{
		using var game = new WebGame();
		using var failed = await game.Http.GetAsync("/fail");
		Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
		Assert.Contains("Handler exploded.", await failed.Content.ReadAsStringAsync(), StringComparison.Ordinal);

		using var notFound = await game.Http.GetAsync("/nothing/here");
		Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);

		using var wrongMethod = await game.Http.DeleteAsync("/score");
		Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
		Assert.Equal(["GET"], wrongMethod.Content.Headers.Allow);

		using var badRoute = await game.Http.PutAsync("/players/x/name", new StringContent("a"));
		Assert.Equal(HttpStatusCode.BadRequest, badRoute.StatusCode);
		Assert.Equal("Invalid value for route parameter 'id'.", await badRoute.Content.ReadAsStringAsync());

		Assert.Equal("0", await game.Http.GetStringAsync("/number"));
	}

	[Fact]
	public async Task HeadAnswersGetRoutesWithoutABody()
	{
		using var game = new WebGame();
		game.System.Score = 12345;
		using var head = await game.Http.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/number"));
		Assert.Equal(HttpStatusCode.OK, head.StatusCode);
		Assert.Equal(5, head.Content.Headers.ContentLength);
		Assert.Empty(await head.Content.ReadAsByteArrayAsync());
	}

	[Fact]
	public void TheRouteTableIsGeneratedAndOrderedBySpecificity()
	{
		using var game = new WebGame(background: false);
		var routes = game.Server.Routes;
		Assert.Contains(routes, static r => r.Template == "/score" && r.Method == "GET" && r.Access == WebAccess.Read && r.SystemType == typeof(TestWebSystem));
		Assert.Contains(routes, static r => r.Template == "/score/add" && r.Access == WebAccess.Mutate);
		Assert.Equal(["/events", "/echo", "/control", "/count"], game.Server.WebSockets.Select(static s => s.Path));
		Assert.Equal(WebAccess.Read, game.Server.WebSockets[0].Access);
		Assert.Equal(WebAccess.Mutate, game.Server.WebSockets[2].Access);
		Assert.Contains(WebRoutes.Tables, static t => t.Name == "Ion.Extensions.Web.Tests");

		// Literal segments are tried before parameters, parameters before a catch-all.
		static WebRoute Route(string template) => new("GET", template, typeof(object), static _ => null, static (object _, in WebRequest _, WebResponse _) => { });
		string[] ordered = [.. new[] { "/a/{*rest}", "/a/{x}", "/a/b", "/a/{x}/c" }.Select(Route).Order(Comparer<WebRoute>.Create(WebRoute.CompareSpecificity)).Select(static r => r.Template)];
		Assert.Equal(["/a/b", "/a/{x}/c", "/a/{x}", "/a/{*rest}"], ordered);
	}

	[Fact]
	public void WebRouteMatchesTemplates()
	{
		static WebRoute Route(string template) => new("GET", template, typeof(object), static _ => null, static (object _, in WebRequest _, WebResponse _) => { });
		var values = new Range[4];

		Assert.True(Route("/").TryMatch("/"u8, values));
		Assert.False(Route("/").TryMatch("/a"u8, values));
		Assert.True(Route("/a/{id}").TryMatch("/A/42/"u8, values));
		Assert.Equal("42", Encoding.ASCII.GetString("/A/42/"u8[values[0]]));
		Assert.False(Route("/a/{id}").TryMatch("/a/"u8, values));
		Assert.False(Route("/a/{id}").TryMatch("/a/1/2"u8, values));
		Assert.True(Route("/a/{x}/b/{y}").TryMatch("/a/1/b/2"u8, values));
		Assert.Equal("2", Encoding.ASCII.GetString("/a/1/b/2"u8[values[1]]));
		Assert.True(Route("/f/{*rest}").TryMatch("/f/x/y"u8, values));
		Assert.Equal("x/y", Encoding.ASCII.GetString("/f/x/y"u8[values[0]]));
		Assert.Equal(Route("/a/{x}").ShapeKey, Route("/A/{y}/").ShapeKey);
		Assert.Throws<ArgumentException>(() => Route("/a/{*x}/b"));
		Assert.Throws<ArgumentException>(() => new WebRoute("TRACE", "/a", typeof(object), static _ => null, static (object _, in WebRequest _, WebResponse _) => { }));
	}

	[Fact]
	public void DuplicateRoutesAcrossTablesFailAtStart()
	{
		var extra = new WebRouteTable("Extra", [new WebRoute("get", "/SCORE/", typeof(object), static _ => null, static (object _, in WebRequest _, WebResponse _) => { }, name: "Extra.Score")], []);
		using var game = new WebGame(start: false, services: s => s.AddWebRoutes(extra));
		var error = Assert.ThrowsAny<Exception>(() => game.Host.Start());
		var inner = error as InvalidOperationException ?? error.InnerException as InvalidOperationException;
		Assert.NotNull(inner);
		Assert.Contains("Extra.Score", inner.Message, StringComparison.Ordinal);
	}
}
