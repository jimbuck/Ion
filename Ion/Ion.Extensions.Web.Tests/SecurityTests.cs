using System.Net;
using System.Net.WebSockets;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Web.Tests;

/// <summary>
/// The web module's security policy: off and loopback by default, the LAN opt-in, Host and Origin checks, the token for
/// mutating endpoints, and rate limits.
/// </summary>
[Trait(CATEGORY, INTEGRATION)]
public class SecurityTests
{
	private static Exception? Find<T>(Exception? error) where T : Exception
	{
		for (var e = error; e is not null; e = e.InnerException)
		{
			if (e is T) return e;
		}

		return null;
	}

	[Theory]
	[InlineData("0.0.0.0")]
	[InlineData("*")]
	public void ALanBindIsRefusedByDefault(string bind)
	{
		using var game = new WebGame(new Dictionary<string, string?> { ["Ion:Web:Bind"] = bind }, start: false);
		var error = Assert.ThrowsAny<Exception>(() => game.Host.Start());
		var security = Find<WebSecurityException>(error);
		Assert.NotNull(security);
		Assert.Contains("Ion:Web:AllowNonLoopback", security.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ALanBindWithTheOptInGetsATokenForMutations()
	{
		using var game = new WebGame(new Dictionary<string, string?> { ["Ion:Web:Bind"] = "0.0.0.0", ["Ion:Web:AllowNonLoopback"] = "true" });
		Assert.False(game.Server.Address!.Equals(IPAddress.Loopback));
		var token = game.Server.Token;
		Assert.NotNull(token);
		Assert.Equal(43, token.Length);

		// Reads need no token; mutations do.
		Assert.Equal("0", await game.Http.GetStringAsync("/number"));
		using var refused = await game.Http.PostAsync("/score/add?amount=1", null);
		Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
		Assert.Equal("Bearer", refused.Headers.WwwAuthenticate.Single().Scheme);
		using var wrong = await game.Http.SendAsync(game.Request(HttpMethod.Post, "/score/add?amount=1", token: token[..^1] + "x"));
		Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
		using var accepted = await game.Http.SendAsync(game.Request(HttpMethod.Post, "/score/add?amount=1", token: token));
		Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
		Assert.Equal(1, game.System.Score);
	}

	[Fact]
	public async Task AnonymousLanMutationsAreAnExplicitChoice()
	{
		using var game = new WebGame(new Dictionary<string, string?> { ["Ion:Web:Bind"] = "0.0.0.0", ["Ion:Web:AllowNonLoopback"] = "true", ["Ion:Web:AllowAnonymousMutations"] = "true" });
		Assert.Null(game.Server.Token);
		using var accepted = await game.Http.PostAsync("/score/add?amount=3", null);
		Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
	}

	[Fact]
	public async Task AConfiguredTokenGuardsMutatingRoutesAndWebSockets()
	{
		using var game = new WebGame(new Dictionary<string, string?> { ["Ion:Web:Token"] = "s3cret" });
		Assert.Equal("0", await game.Http.GetStringAsync("/number"));
		using var refused = await game.Http.PutAsync("/players/1/name", new StringContent("x"));
		Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
		using var accepted = await game.Http.SendAsync(game.Request(HttpMethod.Put, "/players/1/name", "s3cret", new StringContent("x")));
		Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

		// A mutating WebSocket endpoint: header, or the subprotocol a browser can send; read endpoints are open.
		var error = await Assert.ThrowsAsync<WebSocketException>(() => WebSocketTests.Connect(game, "/control"));
		Assert.Contains("401", error.Message, StringComparison.Ordinal);
		using (var header = await WebSocketTests.Connect(game, "/control", o => o.SetRequestHeader("Authorization", "Bearer s3cret"))) Assert.Equal(WebSocketState.Open, header.State);
		using (var browser = await WebSocketTests.Connect(game, "/control", o => { o.AddSubProtocol("ion"); o.AddSubProtocol("bearer.s3cret"); }))
		{
			Assert.Equal("ion", browser.SubProtocol);
			await WebSocketTests.Send(browser, "go");
			var deadline = DateTime.UtcNow.AddSeconds(10);
			while (!game.System.Messages.Contains("control go") && DateTime.UtcNow < deadline) await Task.Delay(5);
			Assert.Contains("control go", game.System.Messages);
		}

		using var open = await WebSocketTests.Connect(game, "/events");
		Assert.Equal(WebSocketState.Open, open.State);
	}

	[Fact]
	public void BrowserOriginsAreRefusedUnlessSameOrAllowed()
	{
		using var game = new WebGame(new Dictionary<string, string?> { ["Ion:Web:AllowedOrigins:0"] = "http://localhost:5173" });
		var host = $"127.0.0.1:{game.Port}";

		var evil = game.Raw($"GET /number HTTP/1.1\r\nHost: {host}\r\nOrigin: http://evil.example\r\n\r\n");
		Assert.StartsWith("HTTP/1.1 403", evil, StringComparison.Ordinal);

		var same = game.Raw($"GET /number HTTP/1.1\r\nHost: {host}\r\nOrigin: http://{host}\r\nConnection: close\r\n\r\n");
		Assert.StartsWith("HTTP/1.1 200", same, StringComparison.Ordinal);
		Assert.DoesNotContain("Access-Control-Allow-Origin", same, StringComparison.Ordinal);

		var allowed = game.Raw($"GET /number HTTP/1.1\r\nHost: {host}\r\nOrigin: http://localhost:5173\r\nConnection: close\r\n\r\n");
		Assert.StartsWith("HTTP/1.1 200", allowed, StringComparison.Ordinal);
		Assert.Contains("Access-Control-Allow-Origin: http://localhost:5173\r\n", allowed, StringComparison.Ordinal);

		var preflight = game.Raw($"OPTIONS /score/add HTTP/1.1\r\nHost: {host}\r\nOrigin: http://localhost:5173\r\nAccess-Control-Request-Method: POST\r\nConnection: close\r\n\r\n");
		Assert.StartsWith("HTTP/1.1 204", preflight, StringComparison.Ordinal);
		Assert.Contains("Access-Control-Allow-Headers: Authorization, Content-Type", preflight, StringComparison.Ordinal);

		// WebSocket handshakes are checked too.
		var socket = game.Raw($"GET /events HTTP/1.1\r\nHost: {host}\r\nOrigin: http://evil.example\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n");
		Assert.StartsWith("HTTP/1.1 403", socket, StringComparison.Ordinal);
	}

	[Fact]
	public void TheHostMustBeLoopbackOnALoopbackBind()
	{
		using var game = new WebGame();
		var rebound = game.Raw("GET /number HTTP/1.1\r\nHost: attacker.example\r\n\r\n");
		Assert.StartsWith("HTTP/1.1 403", rebound, StringComparison.Ordinal);
		var local = game.Raw("GET /number HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n");
		Assert.StartsWith("HTTP/1.1 200", local, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ClientsOverTheRateLimitGet429()
	{
		using var game = new WebGame(new Dictionary<string, string?> { ["Ion:Web:RateLimit"] = "2", ["Ion:Web:RateLimitBurst"] = "5" });
		var statuses = new List<HttpStatusCode>();
		for (var i = 0; i < 10; i++)
		{
			using var response = await game.Http.GetAsync("/number");
			statuses.Add(response.StatusCode);
			if (response.StatusCode == HttpStatusCode.TooManyRequests) Assert.Equal(TimeSpan.FromSeconds(1), response.Headers.RetryAfter!.Delta);
		}

		Assert.True(statuses.Take(5).All(static s => s == HttpStatusCode.OK), string.Join(",", statuses));
		Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
	}

	[Fact]
	public void TooManyConnectionsAreRefused()
	{
		using var game = new WebGame(new Dictionary<string, string?> { ["Ion:Web:MaxConnections"] = "2" });
		using var a = new System.Net.Sockets.TcpClient("127.0.0.1", game.Port);
		using var b = new System.Net.Sockets.TcpClient("127.0.0.1", game.Port);
		// Let the accept thread take both.
		Thread.Sleep(300);
		var third = game.Raw("GET /number HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n");
		Assert.StartsWith("HTTP/1.1 503", third, StringComparison.Ordinal);
	}
}
