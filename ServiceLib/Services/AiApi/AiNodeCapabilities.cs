using System.Net.Sockets;
using System.Text.Json;
using ServiceLib.Handler;

namespace ServiceLib.Services.AiApi;

/// <summary>
/// POST /ai/analyzeUrl — analyze a URL or raw text, extract nodes, test them, and add valid ones to a group.
/// Body: { "url": "...", "group": "AI自动获取", "maxNodes": 50, "autoTest": true }
/// </summary>
internal sealed class AnalyzeUrlCapability : IAiCapability
{
    public AiCapabilityDescriptor Descriptor { get; } = AiCapabilityDescriptor.Of(
        "analyzeUrl",
        "Analyze a URL or raw text, extract VPN node links, test each one, and add valid nodes to a target subscription group. Reuses the same pipeline as the AI chat assistant.",
        "POST", "/ai/analyzeUrl", readOnly: false,
        parameters: new Dictionary<string, AiParameterDescriptor>
        {
            ["url"] = new("url", "string", "URL or raw text (Base64 subscription or node links)", true),
            ["group"] = new("group", "string", "Target group remarks (created if missing)", false),
            ["maxNodes"] = new("maxNodes", "int", "Max nodes to keep after testing", false),
            ["autoTest"] = new("autoTest", "bool", "Whether to TCP-test each node", false),
        });

    public async Task<AiResult> InvokeAsync(JsonElement? body, CancellationToken ct)
    {
        if (body is not { ValueKind: JsonValueKind.Object } b) return AiResult.Error("JSON body required", 400);
        if (!b.TryGetProperty("url", out var urlEl) || urlEl.GetString() is not { Length: > 0 } url)
            return AiResult.Error("'url' is required", 400);

        var group = b.TryGetProperty("group", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() : "AI自动获取";
        var maxNodes = b.TryGetProperty("maxNodes", out var m) && m.ValueKind == JsonValueKind.Number ? Math.Max(1, m.GetInt32()) : 50;
        var autoTest = !b.TryGetProperty("autoTest", out var at) || at.ValueKind != JsonValueKind.False;

        var config = AppManager.Instance.Config;
        var lower = url.ToLowerInvariant();
        bool isDirectLink =
            lower.StartsWith("vmess://", StringComparison.Ordinal) ||
            lower.StartsWith("vless://", StringComparison.Ordinal) ||
            lower.StartsWith("trojan://", StringComparison.Ordinal) ||
            lower.StartsWith("ss://", StringComparison.Ordinal) ||
            lower.StartsWith("hy2://", StringComparison.Ordinal) ||
            lower.StartsWith("hysteria2://", StringComparison.Ordinal) ||
            lower.StartsWith("tuic://", StringComparison.Ordinal) ||
            lower.StartsWith("socks://", StringComparison.Ordinal) ||
            lower.StartsWith("socks5://", StringComparison.Ordinal) ||
            lower.StartsWith("wireguard://", StringComparison.Ordinal) ||
            lower.StartsWith("anytls://", StringComparison.Ordinal);

        int added;
        string? fastPathMsg = null;

        if (isDirectLink)
        {
            // Fast path: URL is itself a node link. Skip the download/extract
            // round-trip and go straight to test + add.
            var ok = !autoTest || await AiNodeTester.TestAsync(url);
            var nodesAdded = 0;
            if (ok)
            {
                var subId = await AiGroupHelper.GetOrCreateGroup(config, group);
                nodesAdded = await ConfigHandler.AddBatchServers(config, url, subId, true);
            }
            added = nodesAdded;
            var verb = ok ? "added" : "rejected";
            fastPathMsg = autoTest && !ok
                ? "Direct link failed TCP test, not added"
                : $"Direct link {verb}";
        }
        else
        {
            var svc = new AIFetchService(config, async (_, msg) =>
            {
                Logging.SaveLog($"AiApi/analyzeUrl: {msg}");
                await Task.CompletedTask;
            });
            added = await svc.AnalyzeUrlAsync(url, group, maxNodes);
        }

        var data = new { url, group, maxNodes, autoTest, added };
        return AiResult.Success(JsonSerializer.SerializeToElement(data),
            added > 0 ? $"Added {added} nodes to {group}" : (fastPathMsg ?? "No valid nodes"));
    }
}

/// <summary>
/// POST /ai/testNode — TCP/DNS probe a single node link without adding it.
/// Body: { "link": "vmess://..." } or { "links": ["...", "..."] }
/// </summary>
internal sealed class TestNodeCapability : IAiCapability
{
    public AiCapabilityDescriptor Descriptor { get; } = AiCapabilityDescriptor.Of(
        "testNode",
        "Test whether one or more VPN node links are reachable (TCP connect to host:port, DNS fallback).",
        "POST", "/ai/testNode", readOnly: true,
        parameters: new Dictionary<string, AiParameterDescriptor>
        {
            ["link"] = new("link", "string", "Single node link to test", false),
            ["links"] = new("links", "array<string>", "Multiple node links to test", false),
        });

    public async Task<AiResult> InvokeAsync(JsonElement? body, CancellationToken ct)
    {
        if (body is not { ValueKind: JsonValueKind.Object } b) return AiResult.Error("JSON body required", 400);

        var links = new List<string>();
        if (b.TryGetProperty("link", out var one) && one.ValueKind == JsonValueKind.String)
        {
            var s = one.GetString();
            if (!string.IsNullOrWhiteSpace(s)) links.Add(s);
        }
        if (b.TryGetProperty("links", out var many) && many.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in many.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String)
                {
                    var s = el.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) links.Add(s);
                }
            }
        }
        if (links.Count == 0) return AiResult.Error("Provide 'link' or 'links'", 400);

        var results = new List<object>();
        foreach (var link in links)
        {
            var ok = await AiNodeTester.TestAsync(link);
            results.Add(new { link = link.Length > 120 ? link[..120] + "..." : link, ok });
        }
        return AiResult.Success(JsonSerializer.SerializeToElement(results));
    }
}

/// <summary>
/// POST /ai/addNodes — add raw node links directly to a group (no testing, no AI).
/// Body: { "links": ["vmess://...", "..."], "group": "AI自动获取" }
/// </summary>
internal sealed class AddNodesCapability : IAiCapability
{
    public AiCapabilityDescriptor Descriptor { get; } = AiCapabilityDescriptor.Of(
        "addNodes",
        "Add raw VPN node links directly to a subscription group. Links are parsed and stored without additional testing.",
        "POST", "/ai/addNodes", readOnly: false,
        parameters: new Dictionary<string, AiParameterDescriptor>
        {
            ["links"] = new("links", "array<string>", "Node links (vmess/vless/trojan/ss/hy2/tuic)", true),
            ["group"] = new("group", "string", "Target group remarks (created if missing)", false),
        });

    public async Task<AiResult> InvokeAsync(JsonElement? body, CancellationToken ct)
    {
        if (body is not { ValueKind: JsonValueKind.Object } b) return AiResult.Error("JSON body required", 400);
        if (!b.TryGetProperty("links", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return AiResult.Error("'links' array is required", 400);

        var links = new List<string>();
        foreach (var el in arr.EnumerateArray())
        {
            if (el.ValueKind == JsonValueKind.String)
            {
                var s = el.GetString();
                if (!string.IsNullOrWhiteSpace(s)) links.Add(s);
            }
        }
        if (links.Count == 0) return AiResult.Error("'links' must not be empty", 400);

        var group = b.TryGetProperty("group", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() : "AI自动获取";
        var config = AppManager.Instance.Config;

        var subId = await AiGroupHelper.GetOrCreateGroup(config, group);
        var count = await ConfigHandler.AddBatchServers(config, string.Join("\n", links), subId, true);

        var data = new { group, subId, requested = links.Count, added = count };
        return AiResult.Success(JsonSerializer.SerializeToElement(data));
    }
}

/// <summary>
/// POST /ai/deleteNode — remove a profile by indexId (or remarks) from a group.
/// Body: { "indexId": "..." } or { "remarks": "...", "subId": "..." }
/// </summary>
internal sealed class DeleteNodeCapability : IAiCapability
{
    public AiCapabilityDescriptor Descriptor { get; } = AiCapabilityDescriptor.Of(
        "deleteNode",
        "Delete a VPN node profile by indexId or remarks. Removes from the database.",
        "POST", "/ai/deleteNode", readOnly: false,
        parameters: new Dictionary<string, AiParameterDescriptor>
        {
            ["indexId"] = new("indexId", "string", "Profile indexId to delete", false),
            ["remarks"] = new("remarks", "string", "Profile remarks to match", false),
            ["subId"] = new("subId", "string", "Restrict search to a subscription id", false),
        });

    public async Task<AiResult> InvokeAsync(JsonElement? body, CancellationToken ct)
    {
        if (body is not { ValueKind: JsonValueKind.Object } b) return AiResult.Error("JSON body required", 400);
        var indexId = b.TryGetProperty("indexId", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
        var remarks = b.TryGetProperty("remarks", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
        var subId = b.TryGetProperty("subId", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;

        var config = AppManager.Instance.Config;
        List<ProfileItem>? candidates;

        if (!string.IsNullOrEmpty(indexId))
        {
            var item = await AppManager.Instance.GetProfileItem(indexId);
            candidates = item == null ? null : new List<ProfileItem> { item };
        }
        else if (!string.IsNullOrEmpty(remarks))
        {
            var all = await AppManager.Instance.ProfileItems(subId);
            candidates = (all ?? []).Where(p =>
                string.Equals(p.Remarks, remarks, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        else
        {
            return AiResult.Error("Provide 'indexId' or 'remarks'", 400);
        }

        if (candidates is null || candidates.Count == 0)
            return AiResult.Success("No matching node");

        await ConfigHandler.RemoveServers(config, candidates);
        var data = new { deleted = candidates.Count, indexIds = candidates.Select(p => p.IndexId).ToList() };
        return AiResult.Success(JsonSerializer.SerializeToElement(data));
    }
}

/// <summary>
/// GET /ai/groups — list subscription groups with counts.
/// </summary>
internal sealed class GroupsCapability : IAiCapability
{
    public AiCapabilityDescriptor Descriptor { get; } = AiCapabilityDescriptor.Of(
        "groups",
        "List all subscription groups with server counts.",
        "GET", "/ai/groups", readOnly: true);

    public async Task<AiResult> InvokeAsync(JsonElement? body, CancellationToken ct)
    {
        var subs = await AppManager.Instance.SubItems();
        var config = AppManager.Instance.Config;
        var list = new List<object>();
        foreach (var s in subs ?? [])
        {
            var profiles = await AppManager.Instance.ProfileItems(s.Id);
            list.Add(new
            {
                id = s.Id,
                remarks = s.Remarks,
                url = s.Url,
                enabled = s.Enabled,
                sort = s.Sort,
                current = string.Equals(s.Id, config.SubIndexId, StringComparison.Ordinal),
                serverCount = profiles?.Count ?? 0,
            });
        }
        return AiResult.Success(JsonSerializer.SerializeToElement(list));
    }
}

/// <summary>
/// POST /ai/selectGroup — switch the active subscription group.
/// Body: { "subId": "..." } or { "remarks": "..." }
/// </summary>
internal sealed class SelectGroupCapability : IAiCapability
{
    public AiCapabilityDescriptor Descriptor { get; } = AiCapabilityDescriptor.Of(
        "selectGroup",
        "Set the active subscription group.",
        "POST", "/ai/selectGroup", readOnly: false,
        parameters: new Dictionary<string, AiParameterDescriptor>
        {
            ["subId"] = new("subId", "string", "Subscription id", false),
            ["remarks"] = new("remarks", "string", "Match subscription by remarks", false),
        });

    public async Task<AiResult> InvokeAsync(JsonElement? body, CancellationToken ct)
    {
        if (body is not { ValueKind: JsonValueKind.Object } b) return AiResult.Error("JSON body required", 400);
        var subId = b.TryGetProperty("subId", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
        var remarks = b.TryGetProperty("remarks", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;

        var subs = await AppManager.Instance.SubItems();
        SubItem? target = null;
        if (!string.IsNullOrEmpty(subId))
            target = subs?.FirstOrDefault(s => string.Equals(s.Id, subId, StringComparison.Ordinal));
        else if (!string.IsNullOrEmpty(remarks))
            target = subs?.FirstOrDefault(s => string.Equals(s.Remarks, remarks, StringComparison.OrdinalIgnoreCase));
        if (target == null) return AiResult.Error("group not found", 404);

        var config = AppManager.Instance.Config;
        config.SubIndexId = target.Id;
        await ConfigHandler.SaveConfig(config);
        return AiResult.Success(JsonSerializer.SerializeToElement(new { selected = target.Id, remarks = target.Remarks }));
    }
}

/// <summary>
/// POST /ai/systemProxy — set the system proxy mode.
/// Body: { "mode": "set" | "clear" | "pac" }
/// </summary>
internal sealed class SystemProxyCapability : IAiCapability
{
    public AiCapabilityDescriptor Descriptor { get; } = AiCapabilityDescriptor.Of(
        "systemProxy",
        "Set system proxy mode: 'set' (proxy all traffic), 'clear' (direct), 'pac' (auto-route).",
        "POST", "/ai/systemProxy", readOnly: false,
        parameters: new Dictionary<string, AiParameterDescriptor>
        {
            ["mode"] = new("mode", "string", "One of: set, clear, pac", true),
        });

    public async Task<AiResult> InvokeAsync(JsonElement? body, CancellationToken ct)
    {
        if (body is not { ValueKind: JsonValueKind.Object } b) return AiResult.Error("JSON body required", 400);
        if (!b.TryGetProperty("mode", out var mode) || mode.ValueKind != JsonValueKind.String)
            return AiResult.Error("'mode' is required", 400);

        var value = mode.GetString()?.ToLowerInvariant() switch
        {
            "set" or "on" or "enabled" => ESysProxyType.ForcedChange,
            "clear" or "off" or "disabled" => ESysProxyType.ForcedClear,
            "pac" => ESysProxyType.Pac,
            "unchanged" or "keep" => ESysProxyType.Unchanged,
            _ => throw new ArgumentException($"Invalid mode '{mode.GetString()}'. Use: set, clear, pac, unchanged"),
        };

        var config = AppManager.Instance.Config;
        config.SystemProxyItem.SysProxyType = value;
        await ConfigHandler.SaveConfig(config);
        AppEvents.SysProxyChangeRequested.Publish(value);
        return AiResult.Success(JsonSerializer.SerializeToElement(new { mode = value.ToString() }));
    }
}

/// <summary>
/// POST /ai/aiConfig — read/update AI config (toggle auto-crawl, change interval, change API).
/// Body: any subset of { "enabled", "autoCrawlEnabled", "intervalMinutes", "apiUrl", "apiKey", "modelId", "group", "maxNodes" }
/// </summary>
internal sealed class AiConfigCapability : IAiCapability
{
    public AiCapabilityDescriptor Descriptor { get; } = AiCapabilityDescriptor.Of(
        "aiConfig",
        "Read or update AI configuration. If body is empty, returns current values. Otherwise applies the given fields.",
        "POST", "/ai/aiConfig", readOnly: false);

    public async Task<AiResult> InvokeAsync(JsonElement? body, CancellationToken ct)
    {
        var config = AppManager.Instance.Config;
        var ai = config.AIConfigItem ??= new AIConfigItem();

        if (body is { ValueKind: JsonValueKind.Object } b)
        {
            if (b.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True) ai.Enabled = true;
            else if (b.TryGetProperty("enabled", out var e2) && e2.ValueKind == JsonValueKind.False) ai.Enabled = false;
            if (b.TryGetProperty("autoCrawlEnabled", out var a) && a.ValueKind == JsonValueKind.True) ai.AutoCrawlEnabled = true;
            else if (b.TryGetProperty("autoCrawlEnabled", out var a2) && a2.ValueKind == JsonValueKind.False) ai.AutoCrawlEnabled = false;
            if (b.TryGetProperty("intervalMinutes", out var i) && i.ValueKind == JsonValueKind.Number) ai.AutoCrawlIntervalMinutes = Math.Max(5, i.GetInt32());
            if (b.TryGetProperty("apiUrl", out var u) && u.ValueKind == JsonValueKind.String) ai.ApiUrl = u.GetString();
            if (b.TryGetProperty("apiKey", out var k) && k.ValueKind == JsonValueKind.String) ai.ApiKey = k.GetString();
            if (b.TryGetProperty("modelId", out var m) && m.ValueKind == JsonValueKind.String) ai.ModelId = m.GetString();
            if (b.TryGetProperty("group", out var g) && g.ValueKind == JsonValueKind.String) ai.AiGroupRemarks = g.GetString();
            if (b.TryGetProperty("maxNodes", out var mx) && mx.ValueKind == JsonValueKind.Number) ai.MaxNodesPerSearch = mx.GetInt32();

            await ConfigHandler.SaveConfig(config);
            // Restart scheduler to pick up changes
            AISchedulerService.Restart(config);
        }

        var data = new
        {
            enabled = ai.Enabled,
            autoCrawlEnabled = ai.AutoCrawlEnabled,
            intervalMinutes = ai.AutoCrawlIntervalMinutes,
            apiUrl = ai.ApiUrl,
            modelId = ai.ModelId,
            group = ai.AiGroupRemarks,
            maxNodes = ai.MaxNodesPerSearch,
        };
        return AiResult.Success(JsonSerializer.SerializeToElement(data));
    }
}

// Shared helper: get-or-create a subscription group by remarks.
internal static class AiGroupHelper
{
    public static async Task<string> GetOrCreateGroup(Config config, string remarks)
    {
        var subs = await AppManager.Instance.SubItems();
        var existing = subs?.FirstOrDefault(s =>
            string.Equals(s.Remarks, remarks, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing.Id;

        var newSub = new SubItem
        {
            Id = Utils.GetGuid(),
            Remarks = remarks,
            Url = "ai-auto-search://github.com",
            Enabled = true,
            AutoUpdateInterval = 0,
            Sort = 0,
            Memo = "由 AI 添加的节点分组"
        };
        await ConfigHandler.AddSubItem(config, newSub);
        return newSub.Id;
    }
}
