using System.Net.Sockets;
using System.Text;

using Ion.Extensions.Http;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Web.Tests;

/// <summary>HTTP/1.1 edge cases against a running server (raw sockets), and the parser on its own.</summary>
[Trait(CATEGORY, INTEGRATION)]
public class HttpParsingTests
{
	private static string Status(string response) => response.Length >= 12 ? response[9..12] : response;

	[Theory]
	[InlineData("GET /number HTTP/1.1\r\n\r\n", "400")] // no Host
	[InlineData("GET /number HTTP/1.1\r\nHost: 127.0.0.1\r\nHost: 127.0.0.1\r\n\r\n", "400")] // two Hosts
	[InlineData("POST /score/add?amount=1 HTTP/1.1\r\nHost: 127.0.0.1\r\nTransfer-Encoding: gzip, chunked\r\n\r\n", "411")]
	[InlineData("POST /score/add?amount=1 HTTP/1.1\r\nHost: 127.0.0.1\r\nTransfer-Encoding: chunked\r\nTransfer-Encoding: chunked\r\n\r\n", "411")]
	[InlineData("POST /score/add?amount=1 HTTP/1.0\r\nHost: 127.0.0.1\r\nTransfer-Encoding: chunked\r\n\r\n", "400")]
	[InlineData("PUT /players/1/name HTTP/1.1\r\nHost: 127.0.0.1\r\nTransfer-Encoding: chunked\r\n\r\nzz\r\nabc\r\n0\r\n\r\n", "400")] // bad chunk size
	[InlineData("POST /score/add?amount=1 HTTP/1.1\r\nHost: 127.0.0.1\r\nTransfer-Encoding: chunked\r\nContent-Length: 3\r\n\r\n", "400")] // smuggling
	[InlineData("POST /score/add?amount=1 HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 3\r\nContent-Length: 4\r\n\r\n", "400")]
	[InlineData("POST /score/add?amount=1 HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: -1\r\n\r\n", "400")]
	[InlineData("GET /number HTTP/2.0\r\nHost: 127.0.0.1\r\n\r\n", "505")]
	[InlineData("GET /number HTTP/1.1\r\nHost: 127.0.0.1\r\nX-Folded: a\r\n b\r\n\r\n", "400")] // obsolete line folding
	[InlineData("GET /number HTTP/1.1\r\nHost : 127.0.0.1\r\n\r\n", "400")] // space before the colon
	[InlineData("GET http://example.com/number HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n", "400")] // absolute form
	[InlineData("GET /num ber HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n", "400")]
	[InlineData("G(T /number HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n", "400")]
	public void MalformedRequestsAreRefusedAndClosed(string request, string status)
	{
		using var game = new WebGame();
		var response = game.Raw(request);
		Assert.Equal(status, Status(response));
		Assert.Contains("Connection: close", response, StringComparison.Ordinal);
	}

	[Fact]
	public void AHeadOverTheLimitIs431()
	{
		using var game = new WebGame();
		var response = game.Raw("GET /number HTTP/1.1\r\nHost: 127.0.0.1\r\nX-Big: " + new string('a', 20_000) + "\r\n\r\n");
		Assert.Equal("431", Status(response));
	}

	[Fact]
	public void ABodyOverTheLimitIs413()
	{
		using var game = new WebGame(new Dictionary<string, string?> { ["Ion:Web:MaxRequestBytes"] = "16" });
		var response = game.Raw("PUT /players/1/name HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 17\r\n\r\n12345678901234567");
		Assert.Equal("413", Status(response));
	}

	[Fact]
	public void PipelinedRequestsAreAnsweredInOrder()
	{
		using var game = new WebGame();
		game.System.Score = 5;
		var response = game.Raw(
			"\r\n\r\nGET /echo/one HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n" +
			"PUT /players/2/name HTTP/1.1\r\nHost: localhost\r\nContent-Length: 3\r\n\r\nBob" +
			"GET /number HTTP/1.0\r\nHost: 127.0.0.1\r\n\r\n");
		var one = response.IndexOf("\r\n\r\none", StringComparison.Ordinal);
		var bob = response.IndexOf("2=Bob", StringComparison.Ordinal);
		var five = response.LastIndexOf("\r\n\r\n5", StringComparison.Ordinal);
		Assert.True(one > 0 && bob > one && five > bob, response);
		// HTTP/1.0 without keep-alive closes after the third answer.
		Assert.EndsWith("5", response, StringComparison.Ordinal);
	}

	[Fact]
	public void ARequestArrivingByteByByteIsParsed()
	{
		using var game = new WebGame();
		using var client = new TcpClient { NoDelay = true };
		client.Connect("127.0.0.1", game.Port);
		client.ReceiveTimeout = 5000;
		var stream = client.GetStream();
		foreach (var b in "GET /echo/slow HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n"u8.ToArray())
		{
			stream.WriteByte(b);
			stream.Flush();
			Thread.Sleep(1);
		}

		var response = WebGame.ReadAll(stream);
		Assert.Equal("200", Status(response));
		Assert.EndsWith("slow", response, StringComparison.Ordinal);
	}

	[Fact]
	public void ChunkedBodiesAreDecoded()
	{
		using var game = new WebGame();
		var response = game.Raw("PUT /players/3/name HTTP/1.1\r\nHost: 127.0.0.1\r\nTransfer-Encoding: Chunked\r\n\r\n" +
			"4;ext=1\r\nAda \r\n8\r\nLovelace\r\n0\r\nX-Trailer: t\r\n\r\n" +
			"GET /echo/after HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
		Assert.Contains("\r\n\r\n3=Ada Lovelace", response, StringComparison.Ordinal);
		Assert.EndsWith("after", response, StringComparison.Ordinal);

		var large = game.Raw("PUT /players/3/name HTTP/1.1\r\nHost: 127.0.0.1\r\nTransfer-Encoding: chunked\r\n\r\n" + "FFFFFF\r\n");
		Assert.Equal("413", Status(large));
	}

	[Fact]
	public void KeepAliveCarriesManyRequestsOnOneConnection()
	{
		using var game = new WebGame();
		using var client = new TcpClient();
		client.Connect("127.0.0.1", game.Port);
		var stream = client.GetStream();
		var buffer = new byte[4096];
		for (var i = 0; i < 20; i++)
		{
			stream.Write("GET /number HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n"u8);
			var read = stream.Read(buffer);
			var text = Encoding.ASCII.GetString(buffer, 0, read);
			Assert.StartsWith("HTTP/1.1 200 OK", text, StringComparison.Ordinal);
			Assert.Contains("Connection: keep-alive", text, StringComparison.Ordinal);
		}

		Assert.True(game.Server.RequestCount >= 20);
	}

	[Fact]
	public void TheParserReadsHeadersQueriesAndUpgrades()
	{
		var head = "GET /a/b%20c?x=1&y=hello+there&flag HTTP/1.1\r\nHost: Example\r\nconnection: keep-alive, Upgrade\r\nUpgrade: WebSocket\r\nX-Pad:   v  \r\n\r\n"u8.ToArray();
		var request = new HttpRequest();
		Assert.Equal(HttpParseStatus.Ok, request.Parse(head, head.Length));
		Assert.Equal(HttpVerb.Get, request.Method);
		Assert.Equal("/a/b c", request.Path);
		Assert.Equal("hello there", request.Query("y"));
		Assert.True(request.TryGetQuery("flag"u8, out var flag) && flag.IsEmpty);
		Assert.Equal("v", request.Header("x-pad"));
		Assert.True(request.IsWebSocketUpgrade);
		Assert.True(request.KeepAlive);
		Assert.Equal(-1, request.ContentLength);
	}
}
