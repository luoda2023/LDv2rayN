using System.Net.Http;

namespace ServiceLib.Services;

/// <summary>URL 抓取结果，附带可用的失败明细，便于给用户可操作的提示。</summary>
public class AiUrlFetchResult
{
    public bool Success { get; set; }
    public string Content { get; set; } = string.Empty;
    public string UsedUrl { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

/// <summary>
/// AI 采集链路的抓取/提取/连通性测试服务。
///
/// 背景：原先「下载 → 提取 → 验证」三步各自都很脆：
///  1. 下载用的是裸 HttpClient —— 未开启自动解压（gzip 内容变乱码）、
///     不重试、不走镜像，GitHub 链接在国内经常直接超时；
///     而项目里的 HttpClientHelper 开了解压却关了自动重定向，
///     301/302 会被当成失败。这里两者都补上。
///  2. 提取按行匹配，节点挤在一行（HTML/JSON）时全部漏掉，
///     且 Base64 只处理「整段是 Base64」这一种情况。
///  3. 验证只做直连 TCP，国内环境下境外节点几乎必然失败，
///     导致「提取到 N 个，验证通过 0 个」。这里增加经本地 SOCKS 代理的复测。
/// </summary>
public static class AiUrlFetchService
{
    private static readonly string _tag = "AiUrlFetchService";

    private static readonly string[] UserAgents =
    {
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36 Edg/121.0.0.0",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15",
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
    };

    private static readonly string[] GithubPrefixMirrors =
    {
        "https://ghfast.top/",
        "https://gh-proxy.com/",
        "https://ghproxy.net/"
    };

    // 协议前缀：ssr 必须排在 ss 之前，socks5 排在 socks 之前，避免短前缀抢匹配。
    private static readonly Regex NodeLinkRegex = new(
        @"(?:vmess|vless|trojan|ssr|ss|hy2|hysteria2|hysteria|tuic|anytls|naive|socks5|socks|wireguard)://[A-Za-z0-9_\-\.\+/%\?=&#:;@~,!\*'\(\)]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Base64ChunkRegex = new(
        @"[A-Za-z0-9+/=_\-]{40,}",
        RegexOptions.Compiled);

    private static readonly char[] TrimTailChars =
    {
        '.', ',', ';', ':', ')', ']', '}', '>', '"', '\'',
        '。', '，', '；', '：', '）', '】', '》', '、', '！'
    };

    #region 下载

    /// <summary>把用户给的链接规范成可直接下载的地址（GitHub blob/raw → raw，gist → raw 端点）。</summary>
    public static string NormalizeUrl(string? url)
    {
        if (url.IsNullOrEmpty())
        {
            return string.Empty;
        }

        var s = url.Trim().Trim('"', '\'', '`', '<', '>', '(', ')', ' ', '\t');
        if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            s = "https://" + s;
        }

        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri))
        {
            return s;
        }

        var host = uri.Host.ToLowerInvariant();
        var segs = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        // https://github.com/owner/repo/blob/branch/a.txt → raw.githubusercontent.com/owner/repo/branch/a.txt
        if (host == "github.com" && segs.Length >= 5 &&
            (segs[2].Equals("blob", StringComparison.OrdinalIgnoreCase) ||
             segs[2].Equals("raw", StringComparison.OrdinalIgnoreCase)))
        {
            var rest = string.Join('/', segs.Skip(3));
            return $"https://raw.githubusercontent.com/{segs[0]}/{segs[1]}/{rest}";
        }

        // 仓库首页 → 依次猜常见订阅文件名由调用方处理，这里只原样返回
        if (host == "gist.github.com" && segs.Length >= 2)
        {
            return $"https://gist.githubusercontent.com/{segs[0]}/{segs[1]}/raw";
        }

        return s;
    }

    /// <summary>生成候选地址列表：原地址 + 可用的 GitHub 镜像。</summary>
    public static List<string> BuildCandidates(string? url)
    {
        var list = new List<string>();
        var norm = NormalizeUrl(url);
        if (norm.IsNullOrEmpty())
        {
            return list;
        }

        list.Add(norm);
        if (!Uri.TryCreate(norm, UriKind.Absolute, out var uri))
        {
            return list;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host == "raw.githubusercontent.com")
        {
            var path = uri.AbsolutePath.Trim('/');
            if (path.Length > 0)
            {
                list.Add("https://raw.gitmirror.com/" + path);
            }

            foreach (var m in GithubPrefixMirrors)
            {
                list.Add(m + norm);
            }
        }
        else if (host is "github.com" or "gist.github.com" or "gist.githubusercontent.com" or "raw.gitmirror.com")
        {
            foreach (var m in GithubPrefixMirrors)
            {
                list.Add(m + norm);
            }
        }

        return list.Distinct().ToList();
    }

    /// <summary>
    /// 抓取 URL 内容。按「原地址 → 镜像」，再按「走系统代理 → 绕过系统代理」依次尝试。
    /// 系统代理若指向本软件而核心未运行，下载会全挂；绕过代理这一轮可以救回来，反之亦然。
    /// </summary>
    public static async Task<AiUrlFetchResult> FetchAsync(string url, Func<string, Task>? progress = null, int maxAttempts = 8)
    {
        var res = new AiUrlFetchResult();
        var candidates = BuildCandidates(url);
        if (candidates.Count == 0)
        {
            res.Error = "链接格式无法识别";
            return res;
        }

        var errors = new List<string>();
        var tried = 0;

        foreach (var useSystemProxy in new[] { true, false })
        {
            foreach (var u in candidates)
            {
                if (tried >= maxAttempts)
                {
                    break;
                }

                tried++;
                try
                {
                    if (progress != null)
                    {
                        await progress($"尝试第 {tried} 次：{Short(u)}");
                    }

                    using var client = CreateClient(useSystemProxy);
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    using var resp = await client.GetAsync(u, cts.Token);
                    if (!resp.IsSuccessStatusCode)
                    {
                        errors.Add($"{(int)resp.StatusCode} {Short(u)}");
                        continue;
                    }

                    var content = await resp.Content.ReadAsStringAsync();
                    if (content.IsNullOrEmpty())
                    {
                        errors.Add($"空内容 {Short(u)}");
                        continue;
                    }

                    res.Success = true;
                    res.Content = content;
                    res.UsedUrl = u;
                    return res;
                }
                catch (Exception ex)
                {
                    errors.Add($"{Short(u)} {ex.Message}");
                }
            }

            if (res.Success)
            {
                break;
            }
        }

        res.Error = string.Join(" | ", errors.Take(3));
        Logging.SaveLog($"{_tag} fetch failed: {url} -> {res.Error}");
        return res;
    }

    private static HttpClient CreateClient(bool useSystemProxy)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            UseCookies = true,
            UseProxy = useSystemProxy,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };

        var client = new HttpClient(handler, true)
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgents[Random.Shared.Next(UserAgents.Length)]);
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8");
        return client;
    }

    private static string Short(string url)
    {
        if (url.Length <= 60)
        {
            return url;
        }

        return url[..30] + "..." + url[^20..];
    }

    #endregion

    #region 提取

    /// <summary>
    /// 从任意文本中提取节点链接。依次尝试：原文 → HTML 实体解码 → 整段 Base64
    /// → 逐行 Base64 → 片段 Base64 → URL 解码。
    /// </summary>
    public static List<string> ExtractNodes(string? content)
    {
        var found = new List<string>();
        if (content.IsNullOrEmpty())
        {
            return found;
        }

        void Collect(string? text)
        {
            if (text.IsNullOrEmpty())
            {
                return;
            }

            foreach (Match m in NodeLinkRegex.Matches(text))
            {
                var link = CleanLink(m.Value);
                if (link.Length > 12 && !found.Contains(link))
                {
                    found.Add(link);
                }
            }
        }

        // 1) 先做 HTML 实体解码再扫描。网页里的 &amp; 会让节点参数断裂，
        //    若先扫原文会把带 &amp; 的半截链接当成结果（参数解析必失败）。
        var text = WebUtility.HtmlDecode(content);
        Collect(text);
        if (found.Count > 0)
        {
            return found;
        }

        // 2) 原文（未解码）兜底
        if (!string.Equals(text, content, StringComparison.Ordinal))
        {
            Collect(content);
            if (found.Count > 0)
            {
                return found;
            }
        }

        // 3) 整段 Base64
        var whole = TryBase64(text);
        if (whole != null)
        {
            Collect(whole);
            if (found.Count > 0)
            {
                return found;
            }
        }

        // 4) 逐行 Base64（订阅文件常见：一行一个节点，各自编码）
        foreach (var line in text.Split('\n', '\r'))
        {
            var t = line.Trim();
            if (t.Length < 32)
            {
                continue;
            }

            var d = TryBase64(t);
            if (d != null)
            {
                Collect(d);
            }
        }

        if (found.Count > 0)
        {
            return found;
        }

        // 5) 片段 Base64：页面上长串编码里可能藏着整份订阅
        foreach (Match m in Base64ChunkRegex.Matches(text))
        {
            var d = TryBase64(m.Value);
            if (d != null)
            {
                Collect(d);
            }
        }

        if (found.Count > 0)
        {
            return found;
        }

        // 6) URL 解码（整段被 urlencode 的情况）
        try
        {
            var unescaped = Uri.UnescapeDataString(text);
            if (!string.Equals(unescaped, text, StringComparison.Ordinal))
            {
                Collect(unescaped);
            }
        }
        catch
        {
            // 含非法 % 序列时忽略
        }

        return found;
    }

    private static string CleanLink(string s)
    {
        var link = s.Trim();
        link = link.TrimEnd(TrimTailChars);
        return link;
    }

    private static string? TryBase64(string? input, int depth = 0)
    {
        try
        {
            if (input.IsNullOrEmpty())
            {
                return null;
            }

            var t = new string(input.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (t.Length < 16)
            {
                return null;
            }

            // 只接受纯 Base64 字符集（允许 URL-safe 且可能无 padding）
            if (!Regex.IsMatch(t, @"^[A-Za-z0-9+/=_\-]+$"))
            {
                return null;
            }

            var decoded = Utils.Base64Decode(t);
            if (decoded.IsNullOrEmpty() || decoded.Contains('\0'))
            {
                return null;
            }

            // 解出来必须像订阅内容，否则视作误判（防止把普通英文文本当编码解出乱码）
            if (decoded.Contains("://"))
            {
                return decoded;
            }

            // 可能是多重编码，再解一层
            if (depth < 2)
            {
                return TryBase64(decoded, depth + 1);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>剥掉 HTML 标签，减少喂给 AI 的噪音 token。</summary>
    public static string StripHtml(string? content)
    {
        if (content.IsNullOrEmpty())
        {
            return string.Empty;
        }

        var s = Regex.Replace(content, @"<(script|style)[\s\S]*?</\1>", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<[^>]+>", " ");
        s = WebUtility.HtmlDecode(s);
        return Regex.Replace(s, @"\s{2,}", " ");
    }

    #endregion

    #region 连通性

    /// <summary>
    /// 测试节点是否可达：先直连，失败再经本地 SOCKS 代理复测。
    /// 不做 DNS 兜底 —— 域名能解析不代表节点可用。
    /// </summary>
    public static async Task<bool> TestReachableAsync(string address, int port, int socksPort)
    {
        if (address.IsNullOrEmpty() || port <= 0)
        {
            return false;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            using var client = new TcpClient();
            await client.ConnectAsync(address, port, cts.Token);
            if (client.Connected)
            {
                return true;
            }
        }
        catch
        {
            // 直连不通，继续尝试代理
        }

        if (socksPort > 0)
        {
            try
            {
                return await SocksConnectAsync(address, port, socksPort);
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static async Task<bool> SocksConnectAsync(string address, int port, int socksPort)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", socksPort, cts.Token);

        var stream = client.GetStream();

        // 握手：SOCKS5，仅无认证
        await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 }, cts.Token);
        var greet = new byte[2];
        if (!await ReadAtLeastAsync(stream, greet, 2, cts.Token))
        {
            return false;
        }

        if (greet[0] != 0x05 || greet[1] != 0x00)
        {
            return false;
        }

        // CONNECT 请求，地址用域名类型（ATYP=0x03）
        var addrBytes = Encoding.ASCII.GetBytes(address);
        var req = new byte[7 + addrBytes.Length];
        req[0] = 0x05;
        req[1] = 0x01;
        req[2] = 0x00;
        req[3] = 0x03;
        req[4] = (byte)addrBytes.Length;
        Array.Copy(addrBytes, 0, req, 5, addrBytes.Length);
        req[^2] = (byte)(port >> 8);
        req[^1] = (byte)(port & 0xFF);
        await stream.WriteAsync(req, cts.Token);

        // 回复至少 2 字节：VER + REP，REP=0 表示成功
        var reply = new byte[4];
        if (!await ReadAtLeastAsync(stream, reply, 2, cts.Token))
        {
            return false;
        }

        return reply[1] == 0x00;
    }

    private static async Task<bool> ReadAtLeastAsync(NetworkStream stream, byte[] buffer, int count, CancellationToken token)
    {
        var read = 0;
        while (read < count)
        {
            var n = await stream.ReadAsync(buffer, read, buffer.Length - read, token);
            if (n <= 0)
            {
                return false;
            }

            read += n;
        }

        return true;
    }

    #endregion
}
