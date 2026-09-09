namespace ServiceLib.Services;

/// <summary>
/// In-memory record of the last AI search → test → import run, so the AI
/// log window can show the whole pipeline (repos found, candidates by
/// protocol, passed/failed, imported) without re-parsing encrypted logs.
/// </summary>
public static class AISearchTracker
{
    private static readonly object _lock = new();

    public static DateTime LastRunAtUtc { get; private set; } = DateTime.MinValue;
    public static int ReposFound { get; private set; }
    public static int CandidatesFound { get; private set; }
    public static int Passed { get; private set; }
    public static int Failed { get; private set; }
    public static int Imported { get; private set; }
    public static int MirrorFallbacks { get; private set; }
    public static int FetchedUrls { get; private set; }
    public static int FetchErrors { get; private set; }

    public static Dictionary<string, int> CandidatesByProtocol { get; } = [];
    public static Dictionary<string, int> PassedByProtocol { get; } = [];

    private static readonly List<string> _timeline = [];
    public static IReadOnlyList<string> Timeline => _timeline;

    public static bool HasRun => LastRunAtUtc != DateTime.MinValue;

    public static void BeginRun(int reposFound)
    {
        lock (_lock)
        {
            LastRunAtUtc = DateTime.UtcNow;
            ReposFound = reposFound;
            CandidatesFound = 0;
            Passed = 0;
            Failed = 0;
            Imported = 0;
            MirrorFallbacks = 0;
            FetchedUrls = 0;
            FetchErrors = 0;
            CandidatesByProtocol.Clear();
            PassedByProtocol.Clear();
            _timeline.Clear();
            AddStepLocked($"开始搜索（命中 {reposFound} 个仓库）");
        }
    }

    public static void AddStep(string step)
    {
        lock (_lock)
        {
            AddStepLocked(step);
        }
    }

    public static void RecordCandidates(List<string> nodes)
    {
        lock (_lock)
        {
            CandidatesFound = nodes.Count;
            CandidatesByProtocol.Clear();
            foreach (var n in nodes)
            {
                var proto = GetProtocol(n);
                CandidatesByProtocol[proto] = CandidatesByProtocol.GetValueOrDefault(proto) + 1;
            }
            AddStepLocked($"解析到 {nodes.Count} 个候选节点（{string.Join(" / ", CandidatesByProtocol.Select(kv => $"{kv.Key}×{kv.Value}"))}）");
        }
    }

    public static void RecordTestResult(List<string> passedNodes, int totalTested)
    {
        lock (_lock)
        {
            Passed = passedNodes.Count;
            Failed = totalTested - passedNodes.Count;
            PassedByProtocol.Clear();
            foreach (var n in passedNodes)
            {
                var proto = GetProtocol(n);
                PassedByProtocol[proto] = PassedByProtocol.GetValueOrDefault(proto) + 1;
            }
            AddStepLocked($"测试完成：{Passed} 个通过 / {Failed} 个失败（通过: {string.Join(" / ", PassedByProtocol.Select(kv => $"{kv.Key}×{kv.Value}"))}）");
        }
    }

    public static void RecordImported(int count)
    {
        lock (_lock)
        {
            Imported = count;
            AddStepLocked($"导入完成：{count} 个节点入组");
        }
    }

    public static void RecordMirrorFallback(string url)
    {
        lock (_lock)
        {
            MirrorFallbacks++;
            AddStepLocked($"镜像回退: {url[..Math.Min(url.Length, 60)]}...");
        }
    }

    public static void RecordFetchResult(bool success)
    {
        lock (_lock)
        {
            FetchedUrls++;
            if (!success) FetchErrors++;
        }
    }

    public static void RecordError(string message)
    {
        lock (_lock)
        {
            AddStepLocked($"❌ {message}");
        }
    }

    private static void AddStepLocked(string step)
    {
        _timeline.Add($"{DateTime.Now:HH:mm:ss}  {step}");
        if (_timeline.Count > 200)
        {
            _timeline.RemoveRange(0, _timeline.Count - 200);
        }
    }

    private static string GetProtocol(string nodeLink)
    {
        var lower = nodeLink.ToLowerInvariant();
        if (lower.StartsWith("vmess://")) return "vmess";
        if (lower.StartsWith("vless://")) return "vless";
        if (lower.StartsWith("trojan://")) return "trojan";
        if (lower.StartsWith("ss://")) return "ss";
        if (lower.StartsWith("hy2://") || lower.StartsWith("hysteria2://")) return "hy2";
        if (lower.StartsWith("tuic://")) return "tuic";
        if (lower.StartsWith("socks5://") || lower.StartsWith("socks://")) return "socks";
        if (lower.StartsWith("wireguard://")) return "wireguard";
        if (lower.StartsWith("anytls://")) return "anytls";
        if (lower.StartsWith("naive")) return "naive";
        return "其他";
    }
}