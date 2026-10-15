---
title: Web endpoints
description: Declare HTTP endpoints as [Http] methods on systems, bind route, query and body parameters, write responses, and fix the ION401 to ION407 routing diagnostics.
sidebar:
  order: 3
---

An HTTP endpoint in Ion is a method on a system with an `[Http(method, route)]` attribute. The routing generator turns
every such method into an entry of a static route table, and the web server calls it on the game thread at the end of
the frame with its parameters already parsed. There is no controller base class, no middleware pipeline and no
reflection.

```csharp
using System.Text.Json.Serialization;
using Ion;
using Ion.Extensions.Web;

[WebJson(typeof(GameJson))]
public sealed class PlayerEndpoints(PlayerStore players)
{
    [Http("GET", "/score")]
    public ScoreInfo Score() => new(players.Score, players.Lives);

    [Http("GET", "/players")]
    public PlayerList List(int page = 0, string? filter = null) => players.Page(page, filter);

    [Http("POST", "/players/{id}/name")]
    public void Rename(int id, [FromBody] string name) => players[id].Name = name;
}

public readonly record struct ScoreInfo(int Score, int Lives);

[JsonSerializable(typeof(ScoreInfo))]
[JsonSerializable(typeof(PlayerList))]
internal sealed partial class GameJson : JsonSerializerContext;
```

Register the system and the module, then enable the server with `Ion:Web:Enabled` (see
[HTTP server core](/Ion/networking/http-server/)):

```csharp title="Program.cs"
var builder = IonApplication.CreateBuilder(args);
builder.AddIon().AddWeb().AddSystem<PlayerEndpoints>();
builder.Services.AddSingleton<PlayerStore>();

using var game = builder.Build();
game.UseIon().UseWeb().UseSystem<PlayerEndpoints>();
game.Run();
```

```bash
curl http://127.0.0.1:15780/score
# {"Score":1200,"Lives":3}
curl -X POST http://127.0.0.1:15780/players/2/name -d "Ada"
```

:::note[Endpoint systems]
An endpoint class is an ordinary system. It can also have `[Update]`, `[Render]` and other stage methods, and it is
resolved from the root services by type. Register it as a singleton (`builder.AddSystem<T>()` does). If it is not
registered, its routes answer 503 and the server logs a warning. Adding it to the schedule with `UseSystem<T>()` is only
needed if it has stage methods.
:::

## The `[Http]` attribute

```csharp
[Http("GET", "/score")]
[Http("HEAD", "/score")]                  // may appear several times on one method
[Http("POST", "/reset", Access = WebAccess.Mutate, Json = typeof(AdminJson))]
```

| Member | Meaning |
|---|---|
| `Method` (constructor) | `GET`, `HEAD`, `POST`, `PUT`, `PATCH` or `DELETE`. Anything else is `ION401`. |
| `Route` (constructor) | The path template, starting with `/`. |
| `Access` | `WebAccess.Auto` (default), `Read` or `Mutate`. Decides whether the bearer token is needed; see [Web security](/Ion/networking/web-security/). |
| `Json` | The `JsonSerializerContext` type for this endpoint's body and result. Overrides `[WebJson]`. |

`WebAccess.Auto` means `Read` for `GET` and `HEAD` and `Mutate` for every other method.

## Route templates

A template is a sequence of segments:

| Segment | Example | Matches |
|---|---|---|
| Literal | `/players` | That text, ASCII case-insensitively. |
| Parameter | `/players/{id}` | One path segment, bound to the method parameter named `id`. |
| Catch-all | `/files/{*path}` | The rest of the path (must be last), bound to a `string`. |

Matching rules:

- One trailing slash is ignored: `/score/` matches `/score`.
- When several routes match, the most specific wins: literal segments before parameters, parameters before a
  catch-all. `/players/me` beats `/players/{id}`.
- A path that matches only routes of other methods answers **405** with an `Allow` header.
- `HEAD` falls back to the `GET` route and sends no body.
- `OPTIONS` answers CORS preflights (see [Web security](/Ion/networking/web-security/)).
- Two routes with the same method and shape (`/a/{x}` and `/A/{y}/`) are `ION402` at compile time.

Route constraints (`{id:int}`) and optional segments are not supported. A query string in the template is `ION401`.

## Parameter binding

Each parameter is bound by its type, attributes and name:

| Parameter | Bound from |
|---|---|
| Named like a `{name}` segment (case-insensitive) | The route value. `string`, `bool`, a number type or `Guid`. A catch-all is always `string`. |
| `WebRequest` (by value or `in`) | The request itself. |
| `WebResponse` (by value or `ref`) | The response to write. |
| `[FromBody] string` | The body as UTF-8 text. |
| `[FromBody] ReadOnlySpan<byte>` or `byte[]` | The raw body. |
| `[FromBody] T` | The body as JSON, through the context's `JsonTypeInfo<T>`. A body that does not parse answers **400** with the reason. |
| Anything else | The query parameter of the same name: `string`, `bool`, a number type or `Guid`. |

Supported number types are `byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `float`, `double` and
`decimal`, and their nullable forms.

A query parameter is **optional** when it is nullable or has a default value; otherwise a missing one answers **400**
(`Missing query parameter 'count'.`). An unparsable value answers **400** too.

```csharp
[Http("GET", "/query")]
public string Query(int count, bool flag = false, string? name = null, double scale = 1.5) =>
    FormattableString.Invariant($"{count}|{flag}|{name ?? "null"}|{scale}");
```

```bash
curl "http://127.0.0.1:15780/query?count=3&flag&name=ion"
# 3|True|ion|1.5
```

Values are percent-decoded and parsed with the invariant culture through `IUtf8SpanParsable<T>`, without allocating.
`bool` accepts `true`/`false`, `1`/`0`, `on`/`off`, `yes`/`no`, and a bare key (`?flag`) as true.

At most one parameter may be `[FromBody]` (a second one is `ION404`), and `ref`, `out` or `in` are only allowed on
`WebRequest` (`in`) and `WebResponse` (`ref`).

### Reading the raw request

`WebRequest` is a read-only struct over the parsed request. Use it when binding by parameter is not enough:

| Member | What it is |
|---|---|
| `Method`, `MethodName` | The verb (`HttpVerb`) and its text. |
| `Path`, `RawPath`, `RawQuery` | The path (decoded string) and the raw bytes. |
| `Body` | The body bytes. |
| `IsAuthenticated` | Whether the request presented the token (always true when no token is configured). |
| `RemoteAddress` | The client's IP address. |
| `RouteValueCount`, `RouteValue(i)` | Raw route values. |
| `Header(name)`, `TryGetHeader(name, out value)` | Headers, as a string or as bytes. |
| `Query(name)`, `TryGetQuery(name, out value)` | Query parameters, as a string or as raw bytes. |

```csharp
[Http("GET", "/whoami")]
public string WhoAmI(in WebRequest request) =>
    $"{request.RemoteAddress} via {request.Header("User-Agent") ?? "unknown"}, token: {request.IsAuthenticated}";
```

## Results

The return type decides the response:

| Return type | Response |
|---|---|
| `void` | **204 No Content**, unless the method wrote the response through a `WebResponse` parameter. |
| `string` | `text/plain; charset=utf-8`, status 200. |
| `bool` | The JSON literal `true` or `false`. |
| A number type | A JSON number, written directly without a serializer. |
| A nullable of those | The value, or JSON `null`. |
| Any other type | JSON, serialized with the endpoint's JSON context. |

Types that cannot be written at all (`object`, interfaces, ref structs, pointers) and async return types are compile
errors (`ION405`, `ION407`).

### Writing the response yourself

Take a `WebResponse` (by value or `ref`) to control the status, headers and body:

```csharp
[Http("GET", "/custom")]
public void Custom(ref WebResponse response, in WebRequest request)
{
    response.Header("X-Game", "ion");
    response.Text("custom " + request.Query("q"), 202);
}

[Http("GET", "/snapshot.bin")]
public void Snapshot(WebResponse response) => response.Bytes("ION\x01"u8, "application/octet-stream");

[Http("GET", "/page")]
public void Page(WebResponse response) => response.Html("<h1>Hello from the game</h1>");
```

| `WebResponse` method | Writes |
|---|---|
| `Text(text, status = 200)` | `text/plain; charset=utf-8` |
| `Html(html, status = 200)` | `text/html; charset=utf-8` |
| `Bytes(body, contentType, status = 200)` | Raw bytes with your content type |
| `Json<T>(value, JsonTypeInfo<T>, status = 200)` | JSON through a source-generated type |
| `JsonNumber(value, status = 200)` | A number (`long`, `ulong`, `double`, `decimal`) |
| `JsonBool(value, status = 200)`, `JsonNull(status = 200)` | JSON literals |
| `Error(status, message)` | A plain-text error body |
| `SetStatus(status)`, `SetContentType(type)`, `Header(name, value)` | Status line and headers |
| `BodyWriter` | An `IBufferWriter<byte>` for streaming bytes into the (buffered) body |

`StatusCode`, `IsWritten`, `Body` and `ContentType` read back what was written.

## JSON with source generation

Bodies and results of types other than strings, numbers and `bool` go through System.Text.Json **source generation**,
so they stay NativeAOT-safe. Name the context in one of three places (the first found wins):

1. `[Http(..., Json = typeof(MyContext))]` on the method,
2. `[WebJson(typeof(MyContext))]` on the class (or an outer class),
3. `[assembly: WebJson(typeof(MyContext))]` on the assembly.

```csharp
[assembly: WebJson(typeof(GameJson))]

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ScoreInfo))]
[JsonSerializable(typeof(Player))]
public sealed partial class GameJson : JsonSerializerContext;
```

The generated code looks the type up once with `Ctx.Default.GetTypeInfo(typeof(T))`. If the context has no
`[JsonSerializable(typeof(T))]` for a type an endpoint needs, the generator warns with `ION406`; the endpoint fails at
run time until you add it.

```csharp
[Http("POST", "/players")]
public Player LevelUp([FromBody] Player player)
{
    player.Level++;
    return player;
}
```

## Errors and status codes

| Situation | Response |
|---|---|
| Handler throws | **500**, the exception logged. On a loopback bind the body includes the exception type and message; on a LAN bind it is `The endpoint failed.` |
| Missing or invalid parameter | **400** with a message naming the parameter. |
| JSON body that does not parse | **400** with the parser's reason. |
| Endpoint's system not registered | **503**. |
| Mutating endpoint without the token | **401** (see [Web security](/Ion/networking/web-security/)). |

The game keeps running after a handler error. See [HTTP server core](/Ion/networking/http-server/) for the full list of
status codes the server produces.

## What the generator emits

For each assembly with endpoints the generator writes an internal class `Ion.Generated.IonWebRoutes_<Assembly>` that
holds a `WebRouteTable` (one `WebRoute` per `[Http]` and one `WebSocketRoute` per `[WebSocket]`) and one invoker per
route. A module initializer registers the table with `WebRoutes.Register`, and the server reads `WebRoutes.Tables` when
it starts. Each invoker parses the parameters, casts the target, calls your method and writes the result; there is no
reflection anywhere, which is why web games publish with NativeAOT without warnings.

To see it, build with `EmitCompilerGeneratedFiles`:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

See [Source generators](/Ion/concepts/source-generators/) for how Ion's generators fit together.

## Diagnostics

The routing generator reports these under the category `Ion.Web`:

| Id | Severity | When |
|---|---|---|
| `ION401` | Error | Invalid route: an unsupported HTTP method, a template that does not start with `/`, an empty segment, a malformed or repeated parameter, a catch-all that is not last, a query in the template, a WebSocket path with parameters, or a `{name}` without a method parameter of that name. |
| `ION402` | Error | Duplicate route: same method and shape, or two WebSocket endpoints with the same path (case-insensitive). |
| `ION403` | Error | Unsupported method: static, generic, not public or internal, in a generic or non-accessible type, returning by reference, or a WebSocket handler that is not `void M(in WebSocketMessage)`. |
| `ION404` | Error | Unsupported parameter: a type that cannot bind from the route or query, `ref`/`out`/`in` on anything but `WebRequest`/`WebResponse`, or a second `[FromBody]`. |
| `ION405` | Error | A body or result type needs a JSON context and has none, the named context is not a `JsonSerializerContext`, or the type cannot be written (`object`, interfaces, ref structs). |
| `ION406` | Warning | The JSON context has no `[JsonSerializable(typeof(T))]` for a type the endpoint needs. |
| `ION407` | Error | An async endpoint (`async`, `Task`, `ValueTask`, `IAsyncEnumerable`). |

:::caution[No async handlers (ION407)]
Handlers run synchronously on the game thread at the end of a frame. If you need to call something slow (a database,
another web service), start it from a background task you own, store the result in game state, and let an endpoint
report it on a later request. Never block the game thread waiting on I/O inside a handler.
:::

Fixing the common ones:

```csharp
// ION401: {id} has no parameter named id
[Http("GET", "/players/{id}")]
public string Get(int playerId) => "";        // rename the parameter to id

// ION404: a class cannot come from the query string
[Http("POST", "/players")]
public void Add(Player player) { }             // add [FromBody]

// ION405: no JSON context for ScoreInfo
[Http("GET", "/score")]
public ScoreInfo Score() => default;           // add [WebJson(typeof(GameJson))] to the class
```

## See also

- [WebSockets](/Ion/networking/websockets/) for push and bidirectional messages.
- [Web security](/Ion/networking/web-security/) for tokens and `WebAccess`.
- [HTTP server core](/Ion/networking/http-server/) for threading and configuration.
- [Diagnostics reference](/Ion/reference/diagnostics/).
- [Companion example](/Ion/examples/companion/).
- Source: [Attributes.cs](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Extensions.Web.Abstractions/Attributes.cs),
  [WebRoutesGenerator.cs](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Extensions.Web.Generators/WebRoutesGenerator.cs)
