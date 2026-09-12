namespace ServiceLib.Common;

/// <summary>
/// 本地 geosite.dat / geoip.dat 中可用分类代码的校验器。
///
/// 背景：路由规则里写了 `geosite:wikipedia` 这类“当前数据文件里已不存在”的分类时，
/// xray 会在启动阶段直接报 `code not found` 并退出，表现为“节点有延迟有速度，但一个都连不上”。
/// 这类数据漂移（数据文件升级后分类被合并/改名）不应该让内核起不来，
/// 因此在这里做一次校验，配置生成时把无效分类丢掉，只保留有效项。
///
/// 读文件比较重（~10MB），所以按文件路径 + 最后写入时间做缓存。
/// 校验失败（文件缺失 / 解析异常）一律返回 true —— 宁可放过，不可错杀。
/// </summary>
public static class GeoAssetHelper
{
    private const int MaxEntries = 200000; // 防御性上限，避免坏文件把内存吃光

    private static readonly object _lock = new();
    private static readonly Dictionary<string, (DateTime LastWrite, HashSet<string> Codes)> _cache = new();

    /// <summary>
    /// 判断 geosite / geoip 代码在本地数据文件中是否存在。
    /// </summary>
    /// <param name="prefix">Global.GeoSitePrefix 或 Global.GeoIPPrefix</param>
    /// <param name="code">分类代码，支持 xray 的 `code@attribute` 写法（只校验 @ 前面的部分）</param>
    public static bool IsKnownCode(string prefix, string code)
    {
        if (code.IsNullOrEmpty())
        {
            return false;
        }

        var plainCode = code.Split('@')[0].Trim();
        if (plainCode.IsNullOrEmpty())
        {
            return true; // 只写了属性没有分类，交给内核自己判断
        }

        var fileName = prefix == Global.GeoIPPrefix ? "geoip.dat" : "geosite.dat";
        var codes = LoadCodes(fileName);
        return codes is null || codes.Count == 0 || codes.Contains(plainCode);
    }

    /// <summary>
    /// 过滤一组域名/地址，丢掉本地数据文件里不存在的 geosite:/geoip: 项。
    /// </summary>
    /// <returns>被丢弃的项（用于日志）</returns>
    public static List<string> SanitizeGeoItems(List<string>? items)
    {
        var dropped = new List<string>();
        if (items is null || items.Count == 0)
        {
            return dropped;
        }

        for (var i = items.Count - 1; i >= 0; i--)
        {
            var item = items[i];
            if (item.IsNullOrEmpty())
            {
                continue;
            }

            string? prefix = null;
            if (item.StartsWith(Global.GeoSitePrefix, StringComparison.OrdinalIgnoreCase))
            {
                prefix = Global.GeoSitePrefix;
            }
            else if (item.StartsWith(Global.GeoIPPrefix, StringComparison.OrdinalIgnoreCase))
            {
                prefix = Global.GeoIPPrefix;
            }

            if (prefix is null)
            {
                continue;
            }

            var code = item[prefix.Length..].Trim();
            if (IsKnownCode(prefix, code))
            {
                continue;
            }

            dropped.Add(item);
            items.RemoveAt(i);
        }

        return dropped;
    }

    private static HashSet<string>? LoadCodes(string fileName)
    {
        var path = ResolvePath(fileName);
        if (path.IsNullOrEmpty())
        {
            return null;
        }

        DateTime lastWrite;
        try
        {
            lastWrite = File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return null;
        }

        lock (_lock)
        {
            if (_cache.TryGetValue(path, out var cached) && cached.LastWrite == lastWrite)
            {
                return cached.Codes;
            }

            var codes = ParseCodes(path);
            if (codes is null)
            {
                return null;
            }

            _cache[path] = (lastWrite, codes);
            return codes;
        }
    }

    /// <summary>
    /// xray 的资源目录优先取 bin/，其次取 bin/&lt;core&gt;/（自动下载的旧路径）。
    /// 只探测已存在的文件，不创建目录（GetBinPath 会建目录，这里不能用）。
    /// </summary>
    private static string? ResolvePath(string fileName)
    {
        var candidates = new List<string>();
        try
        {
            var binPath = Path.Combine(Utils.StartupPath(), "bin");
            candidates.Add(Path.Combine(binPath, fileName));
            candidates.Add(Path.Combine(binPath, ECoreType.Xray.ToString().ToLower(), fileName));
            candidates.Add(Path.Combine(binPath, ECoreType.sing_box.ToString().ToLower(), fileName));
        }
        catch
        {
            return null;
        }

        foreach (var candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // ignore
            }
        }

        return null;
    }

    /// <summary>
    /// 极简 protobuf 读取：只取顶层 repeated 字段 1（Entry）里嵌套的字段 1（country_code）。
    /// GeoSiteList  { repeated GeoSite entry = 1 }  GeoSite { string country_code = 1; ... }
    /// GeoIPList    { repeated GeoIP   entry = 1 }  GeoIP   { string country_code = 1; ... }
    /// </summary>
    private static HashSet<string>? ParseCodes(string path)
    {
        try
        {
            var raw = File.ReadAllBytes(path);
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pos = 0;

            while (pos < raw.Length && codes.Count < MaxEntries)
            {
                if (!TryReadVarint(raw, ref pos, out var tag))
                {
                    break;
                }

                var fieldNo = (int)(tag >> 3);
                var wireType = (int)(tag & 0x7);

                if (fieldNo == 1 && wireType == 2)
                {
                    if (!TryReadVarint(raw, ref pos, out var entryLenRaw) || entryLenRaw > (ulong)(raw.Length - pos))
                    {
                        break;
                    }

                    var entryLen = (int)entryLenRaw;
                    var code = ReadFirstString(raw, pos, entryLen);
                    if (code.IsNotEmpty())
                    {
                        codes.Add(code);
                    }

                    pos += entryLen;
                }
                else if (!SkipField(raw, ref pos, wireType))
                {
                    break;
                }
            }

            return codes;
        }
        catch (Exception ex)
        {
            Logging.SaveLog("GeoAssetHelper.ParseCodes failed", ex);
            return null;
        }
    }

    private static string ReadFirstString(byte[] raw, int start, int length)
    {
        var end = start + length;
        var pos = start;

        while (pos < end)
        {
            if (!TryReadVarint(raw, ref pos, out var tag))
            {
                return string.Empty;
            }

            var fieldNo = (int)(tag >> 3);
            var wireType = (int)(tag & 0x7);

            if (fieldNo == 1 && wireType == 2)
            {
                if (!TryReadVarint(raw, ref pos, out var strLen) || strLen > (ulong)(end - pos))
                {
                    return string.Empty;
                }

                return Encoding.UTF8.GetString(raw, pos, (int)strLen);
            }

            if (!SkipField(raw, ref pos, wireType))
            {
                return string.Empty;
            }
        }

        return string.Empty;
    }

    private static bool TryReadVarint(byte[] raw, ref int pos, out ulong value)
    {
        value = 0;
        var shift = 0;

        while (pos < raw.Length && shift < 64)
        {
            var b = raw[pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return true;
            }

            shift += 7;
        }

        return false;
    }

    private static bool SkipField(byte[] raw, ref int pos, int wireType)
    {
        switch (wireType)
        {
            case 0: // varint
                return TryReadVarint(raw, ref pos, out _);

            case 1: // 64-bit
                pos += 8;
                return pos <= raw.Length;

            case 2: // length-delimited
                if (!TryReadVarint(raw, ref pos, out var len) || len > (ulong)(raw.Length - pos))
                {
                    return false;
                }
                pos += (int)len;
                return true;

            case 5: // 32-bit
                pos += 4;
                return pos <= raw.Length;

            default:
                return false;
        }
    }
}
