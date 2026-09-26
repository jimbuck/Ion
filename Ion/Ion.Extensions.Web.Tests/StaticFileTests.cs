using System.Net;
using System.Net.Http.Headers;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Web.Tests;

/// <summary>Static files from the configured folder.</summary>
[Trait(CATEGORY, INTEGRATION)]
public sealed class StaticFileTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "ion-web-static", Guid.NewGuid().ToString("N"));
	private readonly string _outside;

	public StaticFileTests()
	{
		Directory.CreateDirectory(Path.Combine(_root, "www", "sub"));
		File.WriteAllText(Path.Combine(_root, "www", "index.html"), "<h1>home</h1>");
		File.WriteAllText(Path.Combine(_root, "www", "app.js"), "console.log(1);");
		File.WriteAllText(Path.Combine(_root, "www", ".secret"), "hidden");
		File.WriteAllText(Path.Combine(_root, "www", "sub", "index.html"), "<p>sub</p>");
		File.WriteAllText(Path.Combine(_root, "www", "sub", "data.bin"), "01");
		_outside = Path.Combine(_root, "outside.txt");
		File.WriteAllText(_outside, "outside");
	}

	private WebGame Game() => new(new Dictionary<string, string?> { ["Ion:Web:StaticFiles"] = Path.Combine(_root, "www") });

	[Fact]
	public async Task ServesFilesWithContentTypes()
	{
		using var game = Game();
		using var index = await game.Http.GetAsync("/");
		Assert.Equal(HttpStatusCode.OK, index.StatusCode);
		Assert.Equal("text/html", index.Content.Headers.ContentType!.MediaType);
		Assert.Equal("<h1>home</h1>", await index.Content.ReadAsStringAsync());

		using var script = await game.Http.GetAsync("/app.js");
		Assert.Equal("text/javascript", script.Content.Headers.ContentType!.MediaType);
		using var binary = await game.Http.GetAsync("/sub/data.bin");
		Assert.Equal("application/octet-stream", binary.Content.Headers.ContentType!.MediaType);
		Assert.Equal("<p>sub</p>", await game.Http.GetStringAsync("/sub/"));

		// Routes win over files, and files are only read.
		Assert.Equal("0", await game.Http.GetStringAsync("/number"));
		using var post = await game.Http.PostAsync("/app.js", null);
		Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
	}

	[Fact]
	public void AFolderWithoutItsSlashIsRedirected()
	{
		using var game = Game();
		var response = game.Raw("GET /sub HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
		Assert.StartsWith("HTTP/1.1 301", response, StringComparison.Ordinal);
		Assert.Contains("Location: /sub/\r\n", response, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ETagsAnswerNotModified()
	{
		using var game = Game();
		using var first = await game.Http.GetAsync("/app.js");
		var etag = first.Headers.ETag;
		Assert.NotNull(etag);
		var request = new HttpRequestMessage(HttpMethod.Get, "/app.js");
		request.Headers.IfNoneMatch.Add(etag);
		using var second = await game.Http.SendAsync(request);
		Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);

		using var head = await game.Http.SendAsync(new HttpRequestMessage(HttpMethod.Head, "/app.js"));
		Assert.Equal(15, head.Content.Headers.ContentLength);
		Assert.Empty(await head.Content.ReadAsByteArrayAsync());
	}

	[Theory]
	[InlineData("/../outside.txt")]
	[InlineData("/%2e%2e/outside.txt")]
	[InlineData("/sub/%2e%2e/%2e%2e/outside.txt")]
	[InlineData("/..%2foutside.txt")]
	[InlineData("/.secret")]
	[InlineData("/sub/..%5c..%5coutside.txt")]
	[InlineData("/app.js%00.png")]
	[InlineData("/missing.html")]
	public void NothingOutsideTheFolderOrHiddenIsServed(string path)
	{
		using var game = Game();
		var response = game.Raw($"GET {path} HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
		Assert.StartsWith("HTTP/1.1 404", response, StringComparison.Ordinal);
		Assert.DoesNotContain("outside", response.Split("\r\n\r\n")[^1], StringComparison.Ordinal);
		Assert.DoesNotContain("hidden", response, StringComparison.Ordinal);
	}

	[Fact]
	public void ContentTypesByExtension()
	{
		Assert.Equal("text/css; charset=utf-8", StaticFiles.ContentType("a.CSS"));
		Assert.Equal("image/png", StaticFiles.ContentType("a.png"));
		Assert.Equal("application/wasm", StaticFiles.ContentType("a.wasm"));
		Assert.Equal("application/octet-stream", StaticFiles.ContentType("a"));
	}

	public void Dispose()
	{
		try
		{
			Directory.Delete(_root, recursive: true);
		}
		catch (IOException)
		{
		}
	}
}
