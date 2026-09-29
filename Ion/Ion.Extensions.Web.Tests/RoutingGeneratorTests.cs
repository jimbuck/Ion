using Microsoft.CodeAnalysis;

namespace Ion.Extensions.Web.Tests;

/// <summary>The routing generator: the table it emits for valid endpoints, and its ION4xx diagnostics.</summary>
public class RoutingGeneratorTests
{
	private const string ValidGame = """
		namespace Game;

		[WebJson(typeof(GameJson))]
		public sealed class ScoreSystem
		{
			public int Score;

			[Http("GET", "/score")]
			public Info GetScore() => new(Score, 3);

			[Http("GET", "/score/value")]
			public int Value() => Score;

			[Http("POST", "/score/add")]
			public void Add(int amount, bool bonus = false, string? reason = null, double factor = 1.5) => Score += amount;

			[Http("GET", "/players/{id}")]
			[Http("GET", "/players/{id}/name", Access = WebAccess.Read)]
			public string Player(int id, in WebRequest request) => "player " + id;

			[Http("PUT", "/players/{id}/name")]
			public void Rename(Guid id, [FromBody] string name, ref WebResponse response) => response.SetStatus(202);

			[Http("POST", "/players")]
			public Payload? Create([FromBody] Payload payload) => payload;

			[Http("DELETE", "/files/{*path}")]
			public long? Delete(string path, int? limit) => limit;

			[Http("POST", "/raw", Access = WebAccess.Read)]
			public void Raw([FromBody] ReadOnlySpan<byte> body, WebResponse response) => response.Bytes(body, "application/octet-stream");

			[WebSocket("/events", Access = WebAccess.Read)]
			public void OnEvents(in WebSocketMessage message) { }

			[WebSocket("/paddle/")]
			public void OnPaddle(WebSocketMessage message) { }
		}

		internal sealed class Outer
		{
			internal sealed class Nested
			{
				[Http("HEAD", "/ping")]
				internal void Ping() { }
			}
		}
		""" + GeneratorHarness.JsonContext;

	[Fact]
	public void EmitsTheRouteTableForValidEndpoints()
	{
		var result = GeneratorHarness.Run(ValidGame);
		Assert.Empty(result.Diagnostics);
		Assert.True(result.CompilationProblems.Count == 0, string.Join(Environment.NewLine, result.CompilationProblems) + Environment.NewLine + result.Generated);
		GeneratorHarness.AssertGolden("ValidGame", result.Generated);
	}

	[Fact]
	public void EmitsNothingWithoutEndpoints()
	{
		var result = GeneratorHarness.Run("namespace Game; public sealed class Empty { public void M() { } }");
		Assert.Empty(result.Diagnostics);
		Assert.Equal("", result.Generated);
	}

	[Theory]
	[InlineData("""[Http("FETCH", "/a")] public void M() { }""", "not a supported HTTP method")]
	[InlineData("""[Http("GET", "a")] public void M() { }""", "must start with '/'")]
	[InlineData("""[Http("GET", "/a//b")] public void M() { }""", "empty segment")]
	[InlineData("""[Http("GET", "/a/{id}")] public void M() { }""", "route parameter 'id' has no parameter")]
	[InlineData("""[Http("GET", "/a/{*rest}/b")] public void M(string rest) { }""", "must be the last segment")]
	[InlineData("""[Http("GET", "/a/{x}/{X}")] public void M(string x) { }""", "appears twice")]
	[InlineData("""[Http("GET", "/a/x{y}")] public void M(string y) { }""", "mixes a literal and a parameter")]
	[InlineData("""[Http("GET", "/a?b=1")] public void M() { }""", "query or a fragment")]
	[InlineData("""[WebSocket("/ws/{room}")] public void M(in WebSocketMessage m) { }""", "WebSocket paths are literal")]
	public void ReportsInvalidRoutes(string member, string message) =>
		AssertSingle(member, "ION401", message);

	[Fact]
	public void ReportsDuplicateRoutes()
	{
		var result = GeneratorHarness.Run("""
			namespace Game;
			public sealed class A
			{
				[Http("GET", "/items/{id}")] public void One(int id) { }
				[Http("GET", "/Items/{key}/")] public void Two(string key) { }
				[Http("POST", "/items/{id}")] public void Three(int id) { }
				[WebSocket("/live")] public void Live(in WebSocketMessage m) { }
				[WebSocket("/LIVE")] public void Live2(in WebSocketMessage m) { }
			}
			""");
		var duplicates = result.Web("ION402");
		Assert.Equal(2, duplicates.Count);
		Assert.Contains(duplicates, d => d.GetMessage().Contains("A.Two", StringComparison.Ordinal) && d.GetMessage().Contains("A.One", StringComparison.Ordinal));
		Assert.Contains(duplicates, d => d.GetMessage().Contains("A.Live2", StringComparison.Ordinal));
		Assert.Equal(DiagnosticSeverity.Error, duplicates[0].Severity);
		// The first of each pair is still emitted, and the POST is a different route.
		Assert.Contains("\"/items/{id}\"", result.Generated, StringComparison.Ordinal);
		Assert.DoesNotContain("A.Two", result.Generated, StringComparison.Ordinal);
		Assert.Contains("\"POST\"", result.Generated, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("""[Http("GET", "/a")] public static void M() { }""", "is static")]
	[InlineData("""[Http("GET", "/a")] public void M<T>() { }""", "is generic")]
	[InlineData("""[Http("GET", "/a")] private void M() { }""", "not public or internal")]
	[InlineData("""[Http("GET", "/a")] public ref int M() => ref _x; private int _x;""", "returns by reference")]
	[InlineData("""[WebSocket("/ws")] public void M(string text) { }""", "must be 'void M(in WebSocketMessage message)'")]
	[InlineData("""[WebSocket("/ws")] public int M(in WebSocketMessage m) => 1;""", "must be 'void M(in WebSocketMessage message)'")]
	public void ReportsUnsupportedMethods(string member, string message) =>
		AssertSingle(member, "ION403", message);

	[Fact]
	public void ReportsEndpointsInPrivateOrGenericTypes()
	{
		var result = GeneratorHarness.Run("""
			namespace Game;
			public sealed class Outer
			{
				private sealed class Hidden { [Http("GET", "/a")] public void M() { } }
			}
			public sealed class Box<T> { [Http("GET", "/b")] public void M() { } }
			""");
		var errors = result.Web("ION403");
		Assert.Equal(2, errors.Count);
		Assert.Contains(errors, static d => d.GetMessage().Contains("not public or internal", StringComparison.Ordinal));
		Assert.Contains(errors, static d => d.GetMessage().Contains("generic type", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("""[Http("GET", "/a")] public void M(System.Collections.Generic.List<int> ids) { }""", "cannot come from the query string")]
	[InlineData("""[Http("GET", "/a/{when}")] public void M(DateTime when) { }""", "route values bind to string")]
	[InlineData("""[Http("GET", "/a/{*rest}")] public void M(int rest) { }""", "catch-all is always a string")]
	[InlineData("""[Http("GET", "/a")] public void M(ref int x) { }""", "passed by reference")]
	[InlineData("""[Http("POST", "/a")] public void M([FromBody] string a, [FromBody] string b) { }""", "second [FromBody]")]
	[InlineData("""[Http("GET", "/a")] public void M(ref WebRequest request) { }""", "WebRequest passed by value or 'in'")]
	[InlineData("""[Http("GET", "/a")] public void M(in WebResponse response) { }""", "WebResponse passed by value or 'ref'")]
	public void ReportsUnsupportedParameters(string member, string message) =>
		AssertSingle(member, "ION404", message);

	[Theory]
	[InlineData("""[Http("GET", "/a")] public Info M() => default;""", "needs a JsonSerializerContext")]
	[InlineData("""[Http("POST", "/a")] public void M([FromBody] Payload p) { }""", "needs a JsonSerializerContext")]
	[InlineData("""[Http("GET", "/a")] public object M() => 1;""", "cannot be written")]
	[InlineData("""[Http("GET", "/a", Json = typeof(string))] public Info M() => default;""", "is not a System.Text.Json JsonSerializerContext")]
	public void ReportsTypesWithoutJson(string member, string message) =>
		AssertSingle(member, "ION405", message, GeneratorHarness.JsonContext);

	[Fact]
	public void WarnsWhenTheJsonContextLacksAType()
	{
		var result = GeneratorHarness.Run("""
			namespace Game;
			public record struct Other(int X);
			[WebJson(typeof(GameJson))]
			public sealed class A { [Http("GET", "/other")] public Other M() => default; }
			""" + GeneratorHarness.JsonContext);
		var warning = Assert.Single(result.Diagnostics);
		Assert.Equal("ION406", warning.Id);
		Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
		Assert.Contains("[JsonSerializable(typeof(Game.Other))]", warning.GetMessage(), StringComparison.Ordinal);
		Assert.Contains("\"/other\"", result.Generated, StringComparison.Ordinal);
	}

	[Fact]
	public void TheAssemblyJsonContextApplies()
	{
		var result = GeneratorHarness.Run("""
			[assembly: WebJson(typeof(Game.GameJson))]
			namespace Game;
			public sealed class A { [Http("GET", "/info")] public Info M() => default; }
			""" + GeneratorHarness.JsonContext);
		Assert.Empty(result.Diagnostics);
		Assert.Empty(result.CompilationProblems);
		Assert.Contains("global::Game.GameJson.Default", result.Generated, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("""[Http("GET", "/a")] public Task M() => Task.CompletedTask;""")]
	[InlineData("""[Http("GET", "/a")] public ValueTask<int> M() => default;""")]
	[InlineData("""[Http("GET", "/a")] public async void M() { await Task.Yield(); }""")]
	[InlineData("""[WebSocket("/ws")] public async void M(WebSocketMessage m) { await Task.Yield(); }""")]
	public void ReportsAsyncEndpoints(string member) =>
		AssertSingle(member, "ION407", "no async in the frame");

	private static void AssertSingle(string member, string id, string message, string extra = "")
	{
		var result = GeneratorHarness.Run("namespace Game;\npublic sealed class Sys\n{\n\t" + member + "\n}\n" + extra);
		var diagnostic = Assert.Single(result.Diagnostics);
		Assert.Equal(id, diagnostic.Id);
		Assert.Contains(message, diagnostic.GetMessage(), StringComparison.Ordinal);
		Assert.Equal("Game.cs", Path.GetFileName(diagnostic.Location.GetLineSpan().Path));
	}
}
