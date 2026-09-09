using System.Text.Json;
using ServiceLib.Models;

namespace ServiceLib.Services.AiApi;

/// <summary>
/// Orchestrates the AI external API: builds the HTTP server, registers capabilities,
/// and exposes a simple Start/Stop surface for AppManager to call.
/// </summary>
public static class AiApiServer
{
    private static readonly string _tag = "AiApiServer";
    private static AiHttpServer? _server;
    private static readonly object _lock = new();

    public static bool IsRunning
    {
        get
        {
            lock (_lock) return _server?.IsRunning ?? false;
        }
    }

    public static void Start(Config config)
    {
        var ext = config.AIConfigItem?.ExternalApi;
        if (ext is null) return;
        // Force-enable the external API so the AI can always drive the app via HTTP.
        // Users can still bind to a non-loopback host or set a Token; the Enabled flag
        // is kept only to record the user's intent.
        ext.Enabled = true;

        lock (_lock)
        {
            if (_server?.IsRunning == true) return;

            Stop();
            RegisterBuiltins();

            var host = string.IsNullOrEmpty(ext.Host) ? Global.Loopback : ext.Host;
            var port = ext.Port > 0 && ext.Port < 65536 ? ext.Port : 26066;
            var token = ext.Token;

            _server = new AiHttpServer(host, port, token);
            _server.Start();

            Logging.SaveLog($"{_tag}: started on {host}:{port}, capabilities={AiCapabilityRegistry.All.Count}");
        }
    }

    public static void Stop()
    {
        lock (_lock)
        {
            _server?.Dispose();
            _server = null;
        }
    }

    public static void Restart(Config config)
    {
        Stop();
        Start(config);
    }

    private static void RegisterBuiltins()
    {
        AiCapabilityRegistry.Clear();
        AiCapabilityRegistry.Register(new StatusCapability());
        AiCapabilityRegistry.Register(new SubscriptionsCapability());
        AiCapabilityRegistry.Register(new ServersCapability());
        // Node management — lets an external AI drive the full analyze/test/add/delete flow.
        AiCapabilityRegistry.Register(new AnalyzeUrlCapability());
        AiCapabilityRegistry.Register(new TestNodeCapability());
        AiCapabilityRegistry.Register(new AddNodesCapability());
        AiCapabilityRegistry.Register(new DeleteNodeCapability());
        AiCapabilityRegistry.Register(new GroupsCapability());
        AiCapabilityRegistry.Register(new SelectGroupCapability());
        AiCapabilityRegistry.Register(new SystemProxyCapability());
        AiCapabilityRegistry.Register(new AiConfigCapability());
    }
}

/// <summary>
/// GET /ai/status - version, running core type, inbound ports, system proxy state.
/// </summary>
internal sealed class StatusCapability : IAiCapability
{
    public AiCapabilityDescriptor Descriptor { get; } = AiCapabilityDescriptor.Of(
        "status",
        "Return current application state: version, running core, inbound ports, system proxy type.",
        "GET",
        "/ai/status",
        readOnly: true);

    public Task<AiResult> InvokeAsync(JsonElement? body, CancellationToken ct)
    {
        var config = AppManager.Instance.Config;
        var inbound = config.Inbound.FirstOrDefault();
        var data = new
        {
            app = Global.AppName,
            version = Utils.GetVersion(),
            runningCore = AppManager.Instance.RunningCoreType.ToString(),
            socksPort = AppManager.Instance.GetLocalPort(EInboundProtocol.socks),
            socks2Port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks2),
            tunEnabled = config.TunModeItem.EnableTun,
            systemProxy = config.SystemProxyItem.SysProxyType.ToString(),
            currentIndexId = config.IndexId,
            currentSubIndexId = config.SubIndexId,
            inboundLocalPort = inbound?.LocalPort,
            allowLan = inbound?.AllowLANConn,
        };
        return Task.FromResult(AiResult.Success(JsonSerializer.SerializeToElement(data)));
    }
}

/// <summary>
/// GET /ai/subscriptions - list all subscription groups.
/// </summary>
internal sealed class SubscriptionsCapability : IAiCapability
{
    public AiCapabilityDescriptor Descriptor { get; } = AiCapabilityDescriptor.Of(
        "subscriptions",
        "List all subscription groups. Returns id, remarks, url, enabled, update interval.",
        "GET",
        "/ai/subscriptions",
        readOnly: true);

    public async Task<AiResult> InvokeAsync(JsonElement? body, CancellationToken ct)
    {
        var subs = await AppManager.Instance.SubItems();
        var list = (subs ?? []).Select(s => new
        {
            id = s.Id,
            remarks = s.Remarks,
            url = s.Url,
            enabled = s.Enabled,
            sort = s.Sort,
            autoUpdateInterval = s.AutoUpdateInterval,
            updateTime = s.UpdateTime,
        });
        return AiResult.Success(JsonSerializer.SerializeToElement(list));
    }
}

/// <summary>
/// GET /ai/servers - list all server profiles, optionally filtered by subId.
/// </summary>
internal sealed class ServersCapability : IAiCapability
{
    public AiCapabilityDescriptor Descriptor { get; } = AiCapabilityDescriptor.Of(
        "servers",
        "List all server profiles. Optionally filter by subId via JSON body {\"subId\":\"...\"} on GET (server reads body regardless of method).",
        "GET",
        "/ai/servers",
        readOnly: true,
        parameters: new Dictionary<string, AiParameterDescriptor>
        {
            ["subId"] = new("subId", "string", "Filter by subscription id. Send as JSON body even for GET.", false),
        });

    public async Task<AiResult> InvokeAsync(JsonElement? body, CancellationToken ct)
    {
        string? subId = null;
        if (body is { ValueKind: JsonValueKind.Object } b && b.TryGetProperty("subId", out var sid) && sid.ValueKind == JsonValueKind.String)
        {
            subId = sid.GetString();
        }

        var profiles = await AppManager.Instance.ProfileItems(subId);
        var list = (profiles ?? []).Select(p => new
        {
            indexId = p.IndexId,
            remarks = p.Remarks,
            configType = p.ConfigType.ToString(),
            address = p.Address,
            port = p.Port,
            subid = p.Subid,
            coreType = p.CoreType?.ToString(),
        });
        return AiResult.Success(JsonSerializer.SerializeToElement(list));
    }
}
