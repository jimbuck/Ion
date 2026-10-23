---
title: Web security
description: The web server's security model, from off-by-default and loopback-only binds to bearer tokens, the Host and Origin checks, CORS, rate limits and request bounds, and what is not supported (TLS, HTTP/2).
sidebar:
  order: 5
---

An embedded web server turns a game into something other programs can reach. Ion's web module assumes that is
dangerous and makes every widening of exposure explicit. The policy is shared with the
[remote protocol](/Ion/tooling/remote-protocol/) through `Ion.Extensions.Http`, plus an origin allow-list for browser
clients.

The short version:

| Layer | Default | Opt-in |
|---|---|---|
| Server running | Off | `Ion:Web:Enabled=true` |
| Bind address | `127.0.0.1` only | `Bind` plus `AllowNonLoopback=true` (logged) |
| Mutating endpoints | Open on loopback; token required on a LAN bind (generated per run) | `Token`, `GenerateToken`, or `AllowAnonymousMutations` (logged) |
| `Host` header | Must name a loopback host on a loopback bind | None |
| Browser `Origin` | Only the server's own origin | `AllowedOrigins` (with CORS headers) |
| Rate limit | 100 per second, burst 200, per address | `RateLimit`, `RateLimitBurst` (`0` disables) |
| TLS, HTTP/2 | Not supported | Put a reverse proxy in front |

## Off by default

Nothing listens unless `Ion:Web:Enabled` is `true`. `AddWeb()` and `UseWeb()` register and schedule nothing otherwise,
so shipping them in a build costs nothing and exposes nothing.

## Loopback by default

`Bind` defaults to `127.0.0.1`. Any non-loopback value (`0.0.0.0`, `*`, a LAN address) makes the server throw
`WebSecurityException` at `Init`:

```text
Refusing to bind the web server to the non-loopback address '0.0.0.0'. Set Ion:Web:AllowNonLoopback=true to allow it
explicitly (anyone on the network can then reach the game's endpoints).
```

To serve a LAN (a phone on the same Wi-Fi, a second PC), set both:

```bash
dotnet run -- --Ion:Web:Bind=0.0.0.0 --Ion:Web:AllowNonLoopback=true
```

The server then logs a warning naming the address, and prints one URL per network interface with the token in the
fragment (`http://192.168.1.20:15780/#token=...`). Browsers never send the fragment to the server, so it stays out of
logs and proxies; a page served by the game reads it from `location.hash`.

`Bind` accepts an IP address (IPv4 or IPv6), `localhost` (loopback) or `*` (also `+`) for every interface. Anything
else fails at startup with `InvalidOperationException`.

## Tokens for mutating endpoints

The token separates "can look at the game" from "can change the game". It is **not** a user system: there are no
accounts, cookies or sessions. Treat it as a shared secret on a trusted network.

### When a token is required

| Configuration | Token |
|---|---|
| `Token` set | That token. |
| Loopback bind, no `Token`, `GenerateToken=false` | **None**: every endpoint is open to local processes. |
| Loopback bind, `GenerateToken=true` | A random token per run. |
| Non-loopback bind, no `Token` | A random token per run. |
| Non-loopback bind, no `Token`, `AllowAnonymousMutations=true` | **None**: anyone on the network can mutate (logged as a warning). |

Generated tokens are 32 random bytes, base64url without padding (43 characters), printed once to standard error when
`PrintUrl` is true:

```text
Ion web: token 3q2-7w...
```

### Which endpoints need it

Each endpoint has a `WebAccess`:

| Value | Needs the token |
|---|---|
| `WebAccess.Auto` (default) | For `[Http]`: no for `GET` and `HEAD`, yes for `POST`, `PUT`, `PATCH`, `DELETE`. For `[WebSocket]`: yes. |
| `WebAccess.Read` | Never. The endpoint only observes. |
| `WebAccess.Mutate` | Always, when a token is configured. |

```csharp
[Http("GET", "/admin/players", Access = WebAccess.Mutate)]   // a GET that leaks private data: require the token
public PlayerList Players() => _players.All();

[Http("POST", "/telemetry", Access = WebAccess.Read)]        // a POST that changes nothing important
public void Telemetry([FromBody] string line) => _log.Add(line);

[WebSocket("/spectate", Access = WebAccess.Read)]            // a read-only feed anyone may watch
public void Spectate(in WebSocketMessage message) { }
```

A request to a mutating endpoint without the token answers **401**:

```text
This endpoint changes the game: send 'Authorization: Bearer <token>' (Ion:Web:Token).
```

### Presenting the token

- **HTTP**: `Authorization: Bearer <token>`.

  ```bash
  curl -X POST -H "Authorization: Bearer $TOKEN" http://192.168.1.20:15780/players/2/name -d "Ada"
  ```

- **WebSocket from a browser**: browsers cannot set headers on a WebSocket, so offer the token as a subprotocol
  `bearer.<token>` next to `ion`:

  ```js
  new WebSocket(url, ["ion", "bearer." + token]);
  ```

- **WebSocket from other clients**: either form works.

Tokens are compared in constant time. Inside a handler, `WebRequest.IsAuthenticated` and
`WebSocketClient.IsAuthenticated` tell you whether the caller presented it (both are always true when no token is
configured), so a `Read` endpoint can still show more to authenticated callers.

## The Host check (DNS rebinding)

On a loopback bind, every request must carry a `Host` header naming a loopback host: `localhost`, `127.0.0.1` or
`[::1]`, with any port. Otherwise it answers **403**. This defeats DNS rebinding, where a page on another site
re-resolves its own name to `127.0.0.1` to reach local servers from the victim's browser. On a non-loopback bind the
check is skipped.

## Origins and CORS

Browsers send an `Origin` header on cross-origin requests and on every WebSocket handshake. The server:

1. lets requests **without** `Origin` through (they are not from a browser page: `curl`, scripts, native apps);
2. allows the server's **own origin** (`http://` plus the `Host` header), so pages it serves as static files can call
   it;
3. allows origins listed **exactly** in `AllowedOrigins`;
4. refuses anything else with **403** (`Requests from this browser origin are refused (Ion:Web:AllowedOrigins).`).

The same check applies to WebSocket handshakes.

For a tool developed on its own dev server (for example Vite on port 5173), list its origin:

```json title="appsettings.Development.json"
{
  "Ion": {
    "Web": {
      "Enabled": true,
      "AllowedOrigins": [ "http://localhost:5173" ]
    }
  }
}
```

Responses to a listed origin carry `Access-Control-Allow-Origin: <origin>` and `Vary: Origin`. `OPTIONS` preflights
answer **204** with:

```text
Access-Control-Allow-Origin: http://localhost:5173
Access-Control-Allow-Methods: GET, HEAD, POST, PUT, PATCH, DELETE
Access-Control-Allow-Headers: Authorization, Content-Type
Access-Control-Max-Age: 600
Allow: GET, HEAD, POST, PUT, PATCH, DELETE, OPTIONS
```

:::tip[Serve your tool from the game]
The simplest setup needs no CORS at all: build the page to static files and point `StaticFiles` at the folder. The page
is then same-origin with the API and the WebSocket.
:::

Cookies and credentialed CORS are not supported; send the token explicitly.

## Rate limiting

Every client address gets a token bucket shared by all its connections. Each HTTP request and each WebSocket message
takes one token:

| Key | Default | Meaning |
|---|---|---|
| `Ion:Web:RateLimit` | `100` | Tokens added per second (sustained rate). `0` or less disables the limit. |
| `Ion:Web:RateLimitBurst` | `200` | Bucket size: how many requests may arrive at once after a quiet period. |

Over the limit, HTTP requests answer **429 Too Many Requests** with `Retry-After: 1`, and WebSocket messages are
dropped silently (the connection stays open). The limiter keeps up to 4,096 addresses and resets its table when that
fills.

```json
{ "Ion": { "Web": { "RateLimit": 30, "RateLimitBurst": 60 } } }
```

:::note
Limits are per client address, not per route. Behind a reverse proxy every client shares the proxy's address; raise the
limit or disable it (`RateLimit: 0`) and rate-limit at the proxy instead. Per-route limits are not implemented.
:::

## Bounds on everything

The server never lets a client make it allocate without limit:

| Bound | Default | Response |
|---|---|---|
| Request head | 16 KiB and 64 headers | **431** |
| Request body (`Content-Length` or chunked) | `MaxRequestBytes`, 1 MiB | **413** |
| WebSocket message | `MaxWebSocketMessageBytes`, 64 KiB | close **1009** |
| Concurrent connections | `MaxConnections`, 32 | **503** |
| Messages of one WebSocket client waiting for the game | `MaxPendingMessagesPerClient`, 256 | close **1008** |
| Queue into the game thread | `QueueCapacity`, 1024 | **503** (messages dropped) |
| Outgoing messages to a client that does not read | 1,024 | disconnect |
| Waiting for the game thread | `RequestTimeoutMs`, 10 s | **504** |
| Idle connection | 120 s | closed |

Request smuggling defenses: only origin-form request targets are accepted; `Transfer-Encoding` other than a lone
`chunked` on HTTP/1.1 answers **411**; a request with both `Content-Length` and `Transfer-Encoding` is **400**.

Static files refuse `.` and `..` segments, hidden files (a segment starting with `.`), backslashes and NUL bytes after
percent-decoding, and anything that resolves outside the folder.

## The remote protocol on the same port

With both `AddRemote()` and `AddWeb()`, the web server mounts the remote protocol at `/rpc` after its own Host, Origin
and rate-limit checks. Hosting it never widens the remote policy: the remote protocol keeps its own read and mutate
tokens, and on a LAN-bound web server it still refuses clients that reach it on a non-loopback interface unless
`Ion:Remote:AllowNonLoopback` is set. Disable mounting with `Ion:Web:HostEndpoints=false`. See
[Remote protocol](/Ion/tooling/remote-protocol/).

## Not supported

- **TLS (HTTPS, WSS).** The server speaks plain HTTP only. For anything beyond a trusted LAN, put a reverse proxy
  (Caddy, nginx) in front of it and keep the game bound to loopback.
- **HTTP/2 and HTTP/3.**
- Request and response streaming (bodies are buffered), server-sent events, compression, range requests.
- Cookies, user accounts and sessions.
- Per-route rate limits.

:::danger[Do not expose the game to the internet directly]
A LAN bind with a generated token is meant for a phone or a laptop on your own network. Without TLS the token travels in
clear text, so anyone who can observe the network can read it. If players on the internet must reach the web server,
terminate TLS at a reverse proxy, keep `Bind` on loopback, and set a strong `Token`.
:::

## A checklist

- Leave `Ion:Web:Enabled` false in builds that do not need it.
- Keep `Bind` on loopback unless a device on the LAN needs it; then set `AllowNonLoopback` deliberately.
- Mark endpoints by what they do: `Access = WebAccess.Mutate` for sensitive reads, `Read` only for harmless writes.
- Set a fixed `Token` (from an environment variable, not `appsettings.json` in source control) for long-running
  servers; let it be generated for ad-hoc LAN sessions.
- List dev-server origins in `AllowedOrigins` only in development configuration.
- Keep `PrintUrl` on while developing, off in production logs if the token must not appear there.

```bash
# A token from the environment (the .NET configuration maps Ion__Web__Token to Ion:Web:Token)
export Ion__Web__Token="$(openssl rand -base64 32)"
```

## See also

- [HTTP server core](/Ion/networking/http-server/) for the full configuration table.
- [Web endpoints](/Ion/networking/web-endpoints/) and [WebSockets](/Ion/networking/websockets/).
- [Services and configuration](/Ion/concepts/services-and-configuration/) for configuration sources.
- [Remote protocol](/Ion/tooling/remote-protocol/).
- Source: [HttpSecurity.cs](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Extensions.Http/HttpSecurity.cs),
  [HttpRateLimiter.cs](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Extensions.Http/HttpRateLimiter.cs)
