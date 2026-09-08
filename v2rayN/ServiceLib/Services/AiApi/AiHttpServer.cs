using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ServiceLib.Models;

namespace ServiceLib.Services.AiApi;

/// <summary>
/// Minimal HTTP/1.1 server that dispatches requests to the AI capability registry.
/// Uses TcpListener so no new package dependency is required.
/// </summary>
internal sealed class AiHttpServer : IDisposable
{
    private static readonly string Tag = "AiHttpServer";
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly string _token;
    private Task? _acceptLoop;
    private bool _disposed;

    public AiHttpServer(string host, int port, string? token)
    {
        _token = token ?? string.Empty;
        var bind = new IPEndPoint(ParseHost(host), port);
        _listener = new TcpListener(bind);
    }

    private static IPAddress ParseHost(string host)
    {
        if (string.IsNullOrEmpty(host)) return IPAddress.Loopback;
        if (IPAddress.TryParse(host, out var ip)) return ip;
        return IPAddress.Loopback;
    }

    public bool IsRunning => _acceptLoop is { IsCompleted: false } && !_disposed;

    public void Start()
    {
        try
        {
            _listener.Start();
            _acceptLoop = Task.Run(AcceptLoopAsync);
            Logging.SaveLog($"{Tag}: listening on {_listener.LocalEndpoint}");
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
        }
    }

    public void Stop()
    {
        _disposed = true;
        try { _cts.Cancel(); } catch { }
        try { _listener.Stop(); } catch { }
        try { _acceptLoop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Logging.SaveLog(Tag, ex);
                continue;
            }

            _ = HandleClientAsync(client);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            client.NoDelay = true;
            client.ReceiveTimeout = 10_000;
            await using var stream = client.GetStream();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            linked.CancelAfter(TimeSpan.FromSeconds(30));

            var (method, path, headers, body) = await ReadRequestAsync(stream, linked.Token);
            if (method is null || path is null) return;

            if (!CheckAuth(headers))
            {
                await WriteResponseAsync(stream, 401,
                    new { ok = false, message = "missing or invalid X-AI-Token" });
                return;
            }

            await DispatchAsync(stream, method, path, body, linked.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logging.SaveLog(Tag, ex);
        }
        finally
        {
            try { client.Close(); } catch { }
        }
    }

    private static async Task<(string? Method, string? Path, Dictionary<string, string> Headers, JsonElement? Body)>
        ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var requestLine = await ReadLineAsync(stream, ct);
        if (string.IsNullOrEmpty(requestLine)) return (null, null, new(), null);

        var parts = requestLine.Split(' ', 3);
        if (parts.Length < 2) return (null, null, new(), null);
        var method = parts[0];
        var path = parts[1];

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var line = await ReadLineAsync(stream, ct);
            if (string.IsNullOrEmpty(line)) break;
            var colon = line.IndexOf(':');
            if (colon > 0)
            {
                var name = line[..colon].Trim();
                var value = line[(colon + 1)..].Trim();
                headers[name] = value;
            }
        }

        JsonElement? body = null;
        if (headers.TryGetValue("Content-Length", out var cl) && int.TryParse(cl, out var length) && length > 0)
        {
            var buf = new byte[length];
            var read = 0;
            while (read < length)
            {
                var n = await stream.ReadAsync(buf.AsMemory(read, length - read), ct);
                if (n == 0) break;
                read += n;
            }
            var jsonText = Encoding.UTF8.GetString(buf, 0, read);
            try
            {
                using var doc = JsonDocument.Parse(jsonText);
                body = doc.RootElement.Clone();
            }
            catch { }
        }

        return (method, path, headers, body);
    }

    private static async Task<string> ReadLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buffer = new byte[1];
        while (true)
        {
            var n = await stream.ReadAsync(buffer, ct);
            if (n == 0) return sb.ToString();
            var c = (char)buffer[0];
            if (c == '\n')
            {
                var s = sb.ToString();
                return s.Length > 0 && s[^1] == '\r' ? s[..^1] : s;
            }
            sb.Append(c);
            if (sb.Length > 8192) return sb.ToString();
        }
    }

    private bool CheckAuth(Dictionary<string, string> headers)
    {
        if (_token.IsNullOrEmpty()) return true;
        return headers.TryGetValue("X-AI-Token", out var provided)
            && string.Equals(provided, _token, StringComparison.Ordinal);
    }

    private async Task DispatchAsync(NetworkStream stream, string method, string path, JsonElement? body, CancellationToken ct)
    {
        if (method == "GET" && (path == "/capabilities" || path == "/ai/capabilities"))
        {
            var caps = AiCapabilityRegistry.All.Select(c => new
            {
                name = c.Descriptor.Name,
                method = c.Descriptor.Method,
                path = c.Descriptor.Path,
                description = c.Descriptor.Description,
                readOnly = c.Descriptor.IsReadOnly,
                parameters = c.Descriptor.Parameters?.ToDictionary(
                    kv => kv.Key,
                    kv => new { type = kv.Value.Type, required = kv.Value.Required, description = kv.Value.Description }),
            });
            await WriteResponseAsync(stream, 200, new { ok = true, data = caps });
            return;
        }

        if (method == "GET" && (path == "/" || path == "/ai"))
        {
            await WriteResponseAsync(stream, 200, new
            {
                app = Global.AppName,
                version = Utils.GetVersion(),
                capabilities = AiCapabilityRegistry.All
                    .Select(c => new { name = c.Descriptor.Name, path = c.Descriptor.Path }),
            });
            return;
        }

        // Try exact (method, path) match first; fall back to (GET, path) for POST
        // so a GET-only capability can still be called with a request body.
        if (!AiCapabilityRegistry.TryMatchByPath(method, path, out var cap)
            && method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && AiCapabilityRegistry.TryMatchByPath("GET", path, out var postAsGet))
        {
            cap = postAsGet;
        }

        if (cap is not null)
        {
            try
            {
                var result = await cap.InvokeAsync(body, ct);
                if (result.Ok)
                {
                    await WriteResponseAsync(stream, 200,
                        new { ok = true, message = result.Message, data = result.Data });
                    return;
                }
                var code = 500;
                if (result.Data is { ValueKind: JsonValueKind.Object } err
                    && err.TryGetProperty("code", out var c)
                    && c.TryGetInt32(out var n))
                {
                    code = n;
                }
                await WriteResponseAsync(stream, code, result.Data);
            }
            catch (Exception ex)
            {
                Logging.SaveLog($"{Tag} capability={cap.Descriptor.Name}", ex);
                await WriteResponseAsync(stream, 500, new { ok = false, message = ex.Message });
            }
            return;
        }

        await WriteResponseAsync(stream, 404,
            new { ok = false, message = $"unknown endpoint: {method} {path}" });
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int statusCode, object payload)
    {
        var json = JsonSerializer.SerializeToElement(payload, JsonOpts);
        var jsonText = json.GetRawText();
        var bytes = Encoding.UTF8.GetBytes(jsonText);
        var reason = statusCode switch
        {
            200 => "OK",
            400 => "Bad Request",
            401 => "Unauthorized",
            404 => "Not Found",
            _ => "Error",
        };
        var head = $"HTTP/1.1 {statusCode} {reason}\r\n" +
                   $"Content-Type: application/json; charset=utf-8\r\n" +
                   $"Content-Length: {bytes.Length}\r\n" +
                   "Connection: close\r\n\r\n";
        var headBytes = Encoding.ASCII.GetBytes(head);
        await stream.WriteAsync(headBytes);
        await stream.WriteAsync(bytes);
    }
}
