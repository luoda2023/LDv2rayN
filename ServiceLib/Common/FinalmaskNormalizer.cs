using System.Text.Json.Nodes;

namespace ServiceLib.Common;

/// <summary>
/// 规范化节点自带的 finalmask（来自节点 URI 的 <c>fm</c> 参数）。
///
/// 为什么必须做：xray 的 fragment mask 只认 <c>length</c> 这个<b>单值字段</b>来算 LengthMin，
/// 算出来 &lt;= 0 就报 <c>LengthMin can't be 0</c> 并<b>拒绝整个配置文件</b>。
/// 而 <c>fm</c> 参数里常见的写法只有 <c>lengths</c> 数组、没有 <c>length</c>：
/// <code>
/// {"packets":"tlshello","lengths":["5","94","1"],"delays":["0"],"maxSplit":"0"}
/// </code>
/// 后果不是一个节点坏掉，而是<b>整批测速 / 启动全挂</b> —— 实测 50 个节点全判不可达
/// （日志 <c>0/50 nodes reachable</c>）。这也正是「测出来的有效性偏低」的一个直接来源。
///
/// 处理规则：
/// <list type="number">
/// <item>fragment mask 缺 <c>length</c>、或它的下界不是正数 → 用 <c>lengths</c> 里第一个合法值补；</item>
/// <item>补不出来 → 丢掉这一段 mask（宁可不分片，也不能拖垮整批）；</item>
/// <item>tcp / udp 都空了 → 返回 null，调用方不写 finalmask，
///       让 <c>ApplyOutboundFragment</c> 去补全局分片设置。</item>
/// </list>
/// </summary>
public static class FinalmaskNormalizer
{
    private static readonly string[] _maskKeys = ["tcp", "udp"];

    /// <summary>
    /// 规范化一段 finalmask JSON。无法修复时返回 null（调用方应视为「没有 finalmask」）。
    /// </summary>
    public static JsonNode? Normalize(string? finalmaskJson)
    {
        if (finalmaskJson.IsNullOrEmpty())
        {
            return null;
        }

        JsonNode? node;
        try
        {
            node = JsonUtils.ParseJson(finalmaskJson);
        }
        catch
        {
            return null;
        }

        if (node is not JsonObject root)
        {
            return null;
        }

        foreach (var key in _maskKeys)
        {
            if (root[key] is not JsonArray masks)
            {
                continue;
            }

            // 倒序删，避免索引错位
            for (var i = masks.Count - 1; i >= 0; i--)
            {
                if (masks[i] is not JsonObject mask)
                {
                    masks.RemoveAt(i);
                    continue;
                }

                // 非 fragment 的 mask（noise 等）不是本次要处理的，原样保留
                if (!string.Equals(mask["type"]?.ToString(), "fragment", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (mask["settings"] is not JsonObject settings)
                {
                    masks.RemoveAt(i);
                    continue;
                }

                var length = PickUsableRange(settings["length"]?.ToString(), settings["lengths"]);
                if (length is null)
                {
                    // 补不出合法的 length，xray 必然拒绝整个配置 → 直接放弃这一段
                    masks.RemoveAt(i);
                    continue;
                }

                settings["length"] = length;

                // delay 允许为 0，这里只做「缺失时顺带补一个」的规范化
                if (settings["delay"] is null)
                {
                    var delay = FirstOf(settings["delays"]);
                    if (delay.IsNotEmpty())
                    {
                        settings["delay"] = delay;
                    }
                }
            }

            if (masks.Count == 0)
            {
                root.Remove(key);
            }
        }

        return root.Count == 0 ? null : root;
    }

    /// <summary>先看单值字段，再在数组里找第一个能用的。</summary>
    private static string? PickUsableRange(string? single, JsonNode? listNode)
    {
        if (IsUsableRange(single))
        {
            return single;
        }

        if (listNode is JsonArray arr)
        {
            foreach (var item in arr)
            {
                var range = item?.ToString();
                if (IsUsableRange(range))
                {
                    return range;
                }
            }
        }

        return null;
    }

    private static string? FirstOf(JsonNode? listNode)
        => listNode is JsonArray arr && arr.Count > 0 ? arr[0]?.ToString() : null;

    /// <summary>范围串（"50-100" / "5"）能不能解析出 &gt;0 的下界。</summary>
    private static bool IsUsableRange(string? range)
    {
        if (range.IsNullOrEmpty())
        {
            return false;
        }

        var parts = range.Split('-');
        return int.TryParse(parts[0], out var min) && min > 0;
    }

    /// <summary>
    /// 对外暴露的范围串检查：下界必须能解析出正数。
    /// xray 用 range 的下界算 LengthMin，为 0 时拒绝整份配置，
    /// 生成侧（全局 fragment 设置等）用它做前置兜底。
    /// </summary>
    public static bool HasUsableRange(string? range) => IsUsableRange(range);
}
