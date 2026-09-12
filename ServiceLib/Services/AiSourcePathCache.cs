using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServiceLib.Services;

/// <summary>
/// AI 采集源路径缓存：记住每个 raw 地址上一轮「成功拿到节点 / 404 / 网络失败」。
///
/// 为什么要做：GitHub 仓库探测阶段是「仓库 × 候选路径」的组合，一轮要打两三百个地址，
/// 绝大多数是 404。没有缓存的话每一轮都要把这些 404 重新打一遍；
/// 把已知 404 跳过、已知可用源排到最前，第二轮起抓取耗时会大幅缩短。
///
/// 持久化到 guiConfigs/aiSourceCache.json。404 记录 3 天后过期（仓库内容会变，
/// 上周不存在的路径这周可能出现了）；成功记录 14 天后过期。
/// 线程安全：采集阶段多任务并发调用 Record，内部加锁；Flush 落盘。
/// </summary>
public static class AiSourcePathCache
{
    private sealed class CacheEntry
    {
        [JsonPropertyName("lastOkAt")] public DateTime LastOkAt { get; set; }
        [JsonPropertyName("lastFailAt")] public DateTime LastFailAt { get; set; }
        [JsonPropertyName("lastNotFoundAt")] public DateTime LastNotFoundAt { get; set; }
    }

    private static readonly object _sync = new();
    private static Dictionary<string, CacheEntry>? _entries;
    private static bool _dirty;

    private static readonly TimeSpan OkTtl = TimeSpan.FromDays(14);
    private static readonly TimeSpan NotFoundTtl = TimeSpan.FromDays(3);
    private static readonly TimeSpan FailTtl = TimeSpan.FromHours(6);

    private static string CachePath => Utils.GetConfigPath("aiSourceCache.json");

    private static Dictionary<string, CacheEntry> Load()
    {
        if (_entries != null)
        {
            return _entries;
        }
        try
        {
            if (File.Exists(CachePath))
            {
                _entries = JsonUtils.Deserialize<Dictionary<string, CacheEntry>>(File.ReadAllText(CachePath)) ?? [];
                return _entries;
            }
        }
        catch
        {
            // 缓存坏了就当没有，采集流程不受影响
        }
        _entries = [];
        return _entries;
    }

    /// <summary>记录一次抓取结果。ok=true 表示拿到了节点；notFound 表示确定 404；否则按网络失败记。</summary>
    public static void Record(string url, bool ok, bool notFound = false)
    {
        if (url.IsNullOrEmpty())
        {
            return;
        }
        lock (_sync)
        {
            var entries = Load();
            var entry = entries.TryGetValue(url, out var e) ? e : entries[url] = new CacheEntry();
            var now = DateTime.UtcNow;
            if (ok)
            {
                entry.LastOkAt = now;
            }
            else if (notFound)
            {
                entry.LastNotFoundAt = now;
            }
            else
            {
                entry.LastFailAt = now;
            }
            _dirty = true;
        }
    }

    /// <summary>
    /// 把待探测地址按缓存状态重排：最近成功的排最前，最近确定 404 的直接剔除。
    /// 无缓存的地址保持原有相对顺序，排在「缓存里只有失败记录」的地址之前。
    /// </summary>
    public static List<string> Prioritize(IEnumerable<string> urls)
    {
        lock (_sync)
        {
            var entries = Load();
            var now = DateTime.UtcNow;

            var fresh = new List<string>();
            var known = new List<string>();
            var skipped = 0;

            foreach (var url in urls)
            {
                if (!entries.TryGetValue(url, out var e))
                {
                    fresh.Add(url);
                    continue;
                }

                if (e.LastNotFoundAt > DateTime.MinValue && now - e.LastNotFoundAt < NotFoundTtl)
                {
                    skipped++;
                    continue;
                }

                if (e.LastOkAt > DateTime.MinValue && now - e.LastOkAt < OkTtl)
                {
                    known.Add(url);
                }
                else if (e.LastFailAt > DateTime.MinValue && now - e.LastFailAt < FailTtl)
                {
                    // 近期网络失败过的排最后，但保留（失败可能是暂时性的）
                    known.Add(url);
                    // 保持顺序：追加即可
                }
                else
                {
                    fresh.Add(url);
                }
            }

            if (skipped > 0)
            {
                Logging.SaveLog($"AiSourcePathCache: skipped {skipped} known-404 probe urls");
            }

            // 成功源优先于「无记录」，失败源殿后：按 LastOkAt 倒排
            known.Sort((a, b) =>
            {
                var oa = entries.TryGetValue(a, out var ea) ? ea.LastOkAt : DateTime.MinValue;
                var ob = entries.TryGetValue(b, out var eb) ? eb.LastOkAt : DateTime.MinValue;
                return ob.CompareTo(oa);
            });
            var result = new List<string>(known.Count + fresh.Count);
            result.AddRange(known);
            result.AddRange(fresh);
            return result;
        }
    }

    /// <summary>有未落盘更新时写回文件。每轮采集结束调用一次。</summary>
    public static void Flush()
    {
        lock (_sync)
        {
            if (!_dirty || _entries == null)
            {
                return;
            }
            try
            {
                File.WriteAllText(CachePath, JsonUtils.Serialize(_entries));
                _dirty = false;
            }
            catch
            {
                // 落盘失败不影响采集
            }
        }
    }
}
