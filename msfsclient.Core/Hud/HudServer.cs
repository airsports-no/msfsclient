using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace msfsclient.Core.Hud;

/// <summary>
/// A small local server for in-sim displays (the MSFS toolbar panel) and a second-monitor view in a browser. It listens on
/// the loopback address only and refuses requests from web pages, so no website the pilot visits can read the state or send
/// commands. Endpoints: <c>GET /</c> (the display page), <c>GET /state</c>, <c>POST /command</c> and a WebSocket at
/// <c>/ws</c> that pushes the state whenever it changes and takes commands.
/// </summary>
public sealed class HudServer(IHudHost host, int port = HudServer.DefaultPort) : IDisposable
{
    public const int DefaultPort = 47911;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ConcurrentDictionary<Guid, Client> _clients = new();
    private readonly CancellationTokenSource _stop = new();
    private TcpListener? _listener;
    private TcpListener? _listenerV6;
    private string? _lastState;

    public int Port { get; private set; } = port;

    /// <summary>Why the server is not running (the port is taken, ...); null while it runs or before it started.</summary>
    public string? Error { get; private set; }

    public bool Running => _listener != null && Error == null;

    public int ClientCount => _clients.Count;

    public void Start()
    {
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            Error = null;
            _ = Task.Run(() => AcceptLoopAsync(_listener));
            // "localhost" is ::1 first on Windows; without a listener there every connection waits for the IPv4 fallback.
            try
            {
                _listenerV6 = new TcpListener(IPAddress.IPv6Loopback, Port);
                _listenerV6.Start();
                _ = Task.Run(() => AcceptLoopAsync(_listenerV6));
            }
            catch (SocketException)
            {
                _listenerV6 = null;
            }
        }
        catch (SocketException e)
        {
            Error = $"Port {Port} is not available: {e.Message}";
            _listener = null;
        }
    }

    /// <summary>Sends the current state to every connected display, when it changed since the last time.</summary>
    public void Publish()
    {
        if (_clients.IsEmpty) return;
        var state = StateMessage();
        if (state == _lastState) return;
        _lastState = state;
        foreach (var client in _clients.Values) _ = client.SendAsync(state);
    }

    private string StateMessage() => JsonSerializer.Serialize(new { type = "state", state = host.Snapshot() }, Json);

    private async Task AcceptLoopAsync(TcpListener listener)
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = await listener.AcceptTcpClientAsync(_stop.Token); }
            catch (Exception) { return; }
            _ = Task.Run(() => HandleAsync(tcp));
        }
    }

    private async Task HandleAsync(TcpClient tcp)
    {
        var upgraded = false;
        try
        {
            var stream = tcp.GetStream();
            var request = await ReadRequestAsync(stream);
            if (request == null) return;
            if (!IsAllowed(request.Header("origin"), request.Header("host"), Port))
            {
                await RespondAsync(stream, 403, "text/plain", "Not allowed");
                return;
            }
            var path = request.Path.Split('?')[0];
            if (request.Method == "GET" && path == "/ws" && request.Header("upgrade")?.Equals("websocket", StringComparison.OrdinalIgnoreCase) == true)
            {
                upgraded = true;
                await WebSocketAsync(tcp, stream, request);
                return;
            }
            switch (request.Method, path)
            {
                case ("GET", "/" or "/index.html"):
                    await RespondAsync(stream, 200, "text/html; charset=utf-8", Page());
                    break;
                case ("GET", "/hud.js"):
                    await RespondAsync(stream, 200, "text/javascript; charset=utf-8", Text("hud.js"));
                    break;
                case ("GET", "/hud.css"):
                    await RespondAsync(stream, 200, "text/css; charset=utf-8", Text("hud.css"));
                    break;
                case ("GET", _) when path.StartsWith("/fonts/", StringComparison.Ordinal) && Font(path[1..]) is { } font:
                    await RespondAsync(stream, 200, "font/ttf", font);
                    break;
                case ("GET", "/state"):
                    await RespondAsync(stream, 200, "application/json", JsonSerializer.Serialize(host.Snapshot(), Json));
                    break;
                case ("POST", "/command"):
                    var result = await ExecuteAsync(request.Body);
                    await RespondAsync(stream, result.Ok ? 200 : 400, "application/json", JsonSerializer.Serialize(result, Json));
                    break;
                default:
                    await RespondAsync(stream, 404, "text/plain", "Not found");
                    break;
            }
        }
        catch (Exception)
        {
            // A display that went away mid-request.
        }
        finally
        {
            if (!upgraded) tcp.Dispose();
        }
    }

    private async Task<HudResult> ExecuteAsync(string body)
    {
        HudCommand? command;
        try { command = JsonSerializer.Deserialize<HudCommand>(body, Json); }
        catch (JsonException) { return new HudResult(false, "Not a command."); }
        if (command == null || string.IsNullOrWhiteSpace(command.Action)) return new HudResult(false, "Not a command.");
        try { return await host.ExecuteAsync(command); }
        catch (Exception e) { return new HudResult(false, e.Message); }
    }

    private async Task WebSocketAsync(TcpClient tcp, NetworkStream stream, Request request)
    {
        var key = request.Header("sec-websocket-key");
        if (string.IsNullOrWhiteSpace(key))
        {
            await RespondAsync(stream, 400, "text/plain", "Missing Sec-WebSocket-Key");
            tcp.Dispose();
            return;
        }
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key.Trim() + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        var head = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" + $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        using var socket = WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.FromSeconds(30));
        var client = new Client(socket);
        var id = Guid.NewGuid();
        _clients[id] = client;
        try
        {
            await client.SendAsync(StateMessage());
            var buffer = new byte[16 * 1024];
            while (socket.State == WebSocketState.Open && !_stop.IsCancellationRequested)
            {
                var message = new StringBuilder();
                WebSocketReceiveResult received;
                do
                {
                    received = await socket.ReceiveAsync(buffer, _stop.Token);
                    if (received.MessageType == WebSocketMessageType.Close) return;
                    message.Append(Encoding.UTF8.GetString(buffer, 0, received.Count));
                    if (message.Length > 64 * 1024) return;
                } while (!received.EndOfMessage);
                var result = await ExecuteAsync(message.ToString());
                await client.SendAsync(JsonSerializer.Serialize(new { type = "result", ok = result.Ok, message = result.Message }, Json));
                // The command changed the state: everyone sees it at once.
                _lastState = null;
                Publish();
            }
        }
        catch (Exception)
        {
            // Closed by the display, or the client shut down.
        }
        finally
        {
            _clients.TryRemove(id, out _);
            tcp.Dispose();
        }
    }

    /// <summary>
    /// Only the client's own page, an in-sim panel (MSFS's coui:// pages) and programs without an Origin may connect. A web
    /// page on any other site sends its own Origin and is refused; a Host other than this machine's loopback name is refused
    /// too (DNS rebinding).
    /// </summary>
    public static bool IsAllowed(string? origin, string? host, int port)
    {
        if (host != null)
        {
            var h = host.Trim().ToLowerInvariant();
            if (h != $"localhost:{port}" && h != $"127.0.0.1:{port}" && h != $"[::1]:{port}" && h != "localhost" && h != "127.0.0.1" && h != "[::1]") return false;
        }
        if (string.IsNullOrWhiteSpace(origin) || origin == "null") return true;
        var o = origin.Trim().ToLowerInvariant();
        return o.StartsWith("coui://", StringComparison.Ordinal) || o == $"http://localhost:{port}" || o == $"http://127.0.0.1:{port}" || o == $"http://[::1]:{port}";
    }

    private static readonly ConcurrentDictionary<string, string> Texts = new();

    /// <summary>The display page (Hud/hud.html, embedded).</summary>
    public static string Page() => Text("hud.html");

    /// <summary>An embedded text file of the display: hud.html, hud.js or hud.css (also copied into the MSFS toolbar panel).</summary>
    public static string Text(string name) => Texts.GetOrAdd(name, n =>
    {
        using var stream = typeof(HudServer).Assembly.GetManifestResourceStream(n) ?? throw new FileNotFoundException(n);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    /// <summary>A font the page uses (Barlow, as in the client), from the embedded resources; null for anything else.</summary>
    private static byte[]? Font(string name)
    {
        if (name.Contains("..", StringComparison.Ordinal) || !name.EndsWith(".ttf", StringComparison.Ordinal)) return null;
        using var stream = typeof(HudServer).Assembly.GetManifestResourceStream(name);
        if (stream == null) return null;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private sealed record Request(string Method, string Path, Dictionary<string, string> Headers, string Body)
    {
        public string? Header(string name) => Headers.GetValueOrDefault(name);
    }

    private static async Task<Request?> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new byte[16 * 1024];
        var total = 0;
        int end;
        while ((end = IndexOfHeaderEnd(buffer, total)) < 0)
        {
            if (total == buffer.Length) return null;
            var read = await stream.ReadAsync(buffer.AsMemory(total));
            if (read == 0) return null;
            total += read;
        }
        var head = Encoding.ASCII.GetString(buffer, 0, end).Split("\r\n");
        var first = head[0].Split(' ');
        if (first.Length < 2) return null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in head.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0) headers[line[..colon].Trim().ToLowerInvariant()] = line[(colon + 1)..].Trim();
        }
        var bodyStart = end + 4;
        var length = headers.TryGetValue("content-length", out var cl) && int.TryParse(cl, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        if (length is < 0 or > 64 * 1024) return null;
        var body = new byte[length];
        var have = Math.Min(length, total - bodyStart);
        Array.Copy(buffer, bodyStart, body, 0, have);
        while (have < length)
        {
            var read = await stream.ReadAsync(body.AsMemory(have));
            if (read == 0) return null;
            have += read;
        }
        return new Request(first[0].ToUpperInvariant(), first[1], headers, Encoding.UTF8.GetString(body));
    }

    private static int IndexOfHeaderEnd(byte[] buffer, int count)
    {
        for (var i = 0; i + 3 < count; i++)
        {
            if (buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n') return i;
        }
        return -1;
    }

    private static Task RespondAsync(NetworkStream stream, int status, string type, string body) => RespondAsync(stream, status, type, Encoding.UTF8.GetBytes(body));

    private static async Task RespondAsync(NetworkStream stream, int status, string type, byte[] bytes)
    {
        var reason = status switch { 200 => "OK", 400 => "Bad Request", 403 => "Forbidden", 404 => "Not Found", _ => "Error" };
        var head = $"HTTP/1.1 {status} {reason}\r\nContent-Type: {type}\r\nContent-Length: {bytes.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        await stream.WriteAsync(bytes);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener?.Stop();
        _listenerV6?.Stop();
        foreach (var client in _clients.Values) client.Abort();
    }

    private sealed class Client(WebSocket socket)
    {
        private readonly SemaphoreSlim _send = new(1, 1);

        public async Task SendAsync(string text)
        {
            await _send.WaitAsync();
            try
            {
                if (socket.State == WebSocketState.Open) await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception)
            {
                // The display went away; the receive loop removes it.
            }
            finally
            {
                _send.Release();
            }
        }

        public void Abort() => socket.Abort();
    }
}
