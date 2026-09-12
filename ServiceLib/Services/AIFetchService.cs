using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ServiceLib.Enums;
using ServiceLib.Models.Dto;

namespace ServiceLib.Services;

public class AIFetchService
{
    private static readonly string _tag = "AIFetchService";
    private readonly Config _config;
    private readonly Func<bool, string, Task> _updateFunc;

    public AIFetchService(Config config, Func<bool, string, Task> updateFunc)
    {
        _config = config;
        _updateFunc = updateFunc;
    }        // User-specified URLs to check daily
    private static readonly string[] UserSpecifiedUrls =
    {
            "https://github.com/0xRadikal/Free-v2ray-Configs",
            "https://github.com/cbusifabcap/daily_free_vpn",
            "https://github.com/kanaltvyt-dev/FreeForYoung",
            "https://github.com/hello-world-1989/cn-news",
            "https://github.com/Pawdroid/Free-servers",
            "https://github.com/free-nodes/v2rayfree",
            "https://github.com/hwanz/SSR-V2ray-Trojan-vpn",
            "https://github.com/mahdibland/V2RayAggregator",
            "https://github.com/ripaojiedian/freenode",
            "https://github.com/aiboboxx/v2rayfree",
            "https://github.com/mfuu/v2ray",
            "https://github.com/ermaozi/get_subscribe",
            "https://github.com/peasoft/NoMoreWalls",
            "https://github.com/barry-far/V2ray-Configs",
            "https://github.com/Epodonios/v2ray-configs",
            // —— 用户 2026-09 指定追加的来源 ——
            "https://github.com/itgoyo/Free-SSR-V2ray",
            "https://github.com/Huibq/TrojanLinks",
            "https://github.com/licheng527/Free-servers",
            "https://github.com/ermaozi/ermao.net",
            "https://github.com/John19187/The-40-Best-VPNs",
            "https://hidashimora.github.io/free-vpn-anti-rkn/",
            // 免费 node 聚合页：故意用不带日期的入口（带日期的 URL 很快失效）。
            // 页面是 HTML，通用抓取路径会扫出页面里的节点链接和最新订阅地址。
            "https://clashsuburl.com/free-nodes/",
            "https://clashstair.com/freenode/",
            "https://v2rayshare.com/",
            "https://v2cross.com/en/free-v2ray-nodes/",
            "https://topvpnlist.github.io/",
            "https://www.mibei77.com/",
            "https://end-gfw.com/",
 "https://www.vpngate.net/cn/",
 "https://www.youtube.com/hashtag/%E5%85%8D%E8%B4%B9%E8%8A%82%E7%82%B9",
 // —— 2026-09 第二批补充：更多聚合站 + 订阅直链 ——
 "https://github.com/yebekhe/TVC",
 "https://github.com/soroushmirzaei/telegram-configs-collector",
 "https://github.com/mafet/uniastis",
 "https://github.com/404-not-found-node/v2ray-free-node",
 "https://github.com/MhdiTaheri/V2rayCollector",
 "https://github.com/vxiaov/free-proxies",
 "https://github.com/free18/v2ray_nodes",
 "https://github.com/ripaojiedian/freenode/raw/main/README.md",
 "https://raw.githubusercontent.com/Pawdroid/Free-servers/main/sub",
 "https://raw.githubusercontent.com/aiboboxx/v2rayfree/main/v2",
 "https://raw.githubusercontent.com/barry-far/V2ray-Configs/main/All_Configs_Sub.txt",
 "https://raw.githubusercontent.com/mfuu/v2ray/main/v2",
 "https://raw.githubusercontent.com/Epodonios/v2ray-configs/main/All_Configs_Sub.txt",
 "https://raw.githubusercontent.com/mahdibland/V2RayAggregator/master/sub/sub_merge.txt",
 "https://raw.githubusercontent.com/peasoft/NoMoreWalls/master/list_raw.txt",
 "https://raw.githubusercontent.com/ermaozi/get_subscribe/main/subscribe/v2ray.txt",
 "https://raw.githubusercontent.com/ripaojiedian/freenode/main/sub",
 "https://raw.githubusercontent.com/free-nodes/v2rayfree/main/sub",
 };

    /// <summary>
    /// 采集全程共用一个 HttpClient。
    /// 原实现每抓一个 URL 就 new 一个（每个 URL 还要乘上镜像重试次数），
    /// 一轮采集能造出上百个连接池，既慢又容易把本机端口耗尽（TIME_WAIT 堆积）。
    /// </summary>
    private static readonly HttpClient _http = CreateSharedClient();

    private static HttpClient CreateSharedClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            MaxConnectionsPerServer = 32,
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
        return client;
    }

    /// <summary>
    /// 节点链接正则：整段扫描，不再要求节点必须顶在行首。
    /// 这样 HTML / JSON / Markdown 里夹带的节点也能捞出来。
    /// 只列出 FmtHandler 真正认识的协议，ssr 之类已不受支持的不再收。
    /// </summary>
    private static readonly Regex _nodeLinkRegex = new(
        @"(?:vmess|vless|trojan|hysteria2\+realm\+http|hysteria2\+realm|hysteria2|hy2|tuic|anytls|wireguard|naive\+https|naive\+quic|naive|ss|socks5|socks4|socks)://[^\s""'<>\\\u0000-\u001f]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 判断一条链接是不是真的能解析成节点。
    /// 抓来的正文里混着大量 HTML 链接、邮箱、带凭据的普通 URL，
    /// 它们在旧实现里因为「含 :// 和 @」被当成节点收下，
    /// 白白占满后面的真实延迟验证名额。这里先过一遍解析器，解析不出来就丢。
    /// </summary>
    private static bool IsUsableNodeLink(string link)
    {
        if (link.IsNullOrEmpty() || link.Length > 4096)
        {
            return false;
        }

        try
        {
            var profile = FmtHandler.ResolveConfig(link, out _);
            return profile != null && profile.IsValid()
                && !profile.Address.IsNullOrEmpty() && profile.Port > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 尽力把一段文本当 Base64 解出来。失败返回 null。
    /// 订阅常见两种编码：整段 Base64、以及每行一个 Base64。
    /// </summary>
    private static string? TryBase64Decode(string text)
    {
        if (text.IsNullOrEmpty())
        {
            return null;
        }

        var cleaned = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (cleaned.Length < 16)
        {
            return null;
        }

        // 订阅链接有两种很常见的变体，都得认，否则整份订阅解不出来 → 0 节点：
        //   1) URL-safe：用 - 和 _ 代替 + 和 /（机场面板生成的订阅基本都这样）
        //   2) 省略 padding：末尾的 = 被去掉
        // 旧实现只认标准 Base64 且要求长度是 4 的倍数，这两种都直接被判非法。
        cleaned = cleaned.Replace('-', '+').Replace('_', '/');
        switch (cleaned.Length % 4)
        {
            case 2:
                cleaned += "==";
                break;
            case 3:
                cleaned += "=";
                break;
            case 1:
                return null; // 长度模 4 余 1 不可能是合法 Base64
        }

        try
        {
            var bytes = Convert.FromBase64String(cleaned);
            var decoded = Encoding.UTF8.GetString(bytes);
            // 解出来必须是可打印文本，否则是二进制误判
            return decoded.Count(c => c < 0x20 && c != '\n' && c != '\r' && c != '\t') > 0 ? null : decoded;
        }
        catch
        {
            return null;
        }
    }

    public async Task<int> FetchAndAddNodesAsync()
    {
        var aiConfig = _config.AIConfigItem;
        if (aiConfig == null || !aiConfig.Enabled)
        {
            await _updateFunc(false, "AI功能未启用");
            return 0;
        }

        // 没配 AI 接口也要能采集：URL 直采和 GitHub 搜索走的都是 GitHub API，
        // 根本不需要 AI，只有「让 AI 读网页提取节点」那一步用得到。
        // 以前这里把 ApiUrl 为空当成致命错误直接 return 0，
        // 用户没填 API 地址时「每天自动采集」就永远是一次空跑。
        if (aiConfig.ApiUrl.IsNullOrEmpty())
        {
            await _updateFunc(false, "未配置AI接口，本次跳过AI分析，改用直连抓取");
        }

        try
        {
            await _updateFunc(false, "AI正在搜索免费VPN节点...");
            AISearchTracker.BeginRun(0);

 var maxNodes = Math.Min(aiConfig.MaxNodesPerSearch, 500);
 if (maxNodes <= 0)
 {
 maxNodes = 100;
 }
 var validateCap = Math.Min(Math.Max(maxNodes * 8, 200), 2000);

            // Step 1: crawl4ai（本地服务，通常不在）与指定链接抓取并行执行。
            // crawl4ai 的健康检查在最坏情况要等 5 秒超时，串行时这笔时间
            // 每轮都白付；并行后它的失败不再拖慢主路径。
            var crawlTask = FetchFromCrawl4AI();
            var specifiedTask = FetchFromSpecifiedUrls();
            await Task.WhenAll(crawlTask, specifiedTask);

            var allNodes = new List<string>();
            var crawl4aiNodes = crawlTask.Result;
            if (crawl4aiNodes.Count > 0)
            {
                allNodes.AddRange(crawl4aiNodes);
                await _updateFunc(false, $"[成功] 从crawl4ai获取到 {crawl4aiNodes.Count} 个节点");
            }

            var specifiedNodes = specifiedTask.Result;
            if (specifiedNodes.Count > 0)
            {
                MergeByFingerprint(allNodes, specifiedNodes);
                await _updateFunc(false, $"[成功] 从指定链接获取到 {specifiedNodes.Count} 个节点");
            }

            // 先把候选跟组内已有节点比一遍指纹（地址:端口），全是老节点就直接收工。
            // 用户要的是「没有新的就不更新、就不采集」——每天为了 0 个新节点起一次内核、
            // 真实 ping 几百个地址，既慢又白白占网速，还把 AI 图标亮上几分钟。
            // 只找、不建：分组还不存在时当作「组内为空」，这样一次都没采成功时
            // 不会平白留下一个空分组。
            string? subId = null;
            HashSet<string> seen;
            try
            {
                subId = await FindExistingAIGroup(aiConfig.AiGroupRemarks);
                seen = subId.IsNullOrEmpty()
                    ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    : await GetExistingFingerprints(subId);
            }
            catch (Exception ex)
            {
                // 拿不到组内清单就按「全是新的」处理，走完整验证流程。
                // 这只是省一次耗时测试的优化，不该反过来成为采集中断的理由。
                Logging.SaveLog($"{_tag}: pre-dedupe unavailable, fall back to full validation: {ex.Message}");
                subId = null;
                seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var freshCandidates = new List<string>();
            var repeated = 0;
            foreach (var n in allNodes)
            {
                if (n.IsNullOrEmpty())
                {
                    continue;
                }
                if (!seen.Add(NodeFingerprint(n)))
                {
                    repeated++;
                    continue;
                }
                freshCandidates.Add(n);
            }

            // Step 2: 指定源 + crawl4ai 的候选已够验证配额时，跳过 GitHub 搜索。
            // 搜索阶段（GitHub API + 仓库探测）是国内网络最慢、最不可靠的一段；
            // 而精选源单轮往往就有几百个新候选，够用了。只有候选不足时才补搜。
            if (freshCandidates.Count < validateCap)
            {
                await _updateFunc(false, $"🔍 指定源新候选 {freshCandidates.Count} 个（目标 {validateCap}），正在搜索GitHub补充...");
                var searchNodes = await SearchGitHubForFreeNodes(aiConfig);
                if (searchNodes != null && searchNodes.Count > 0)
                {
                    MergeByFingerprint(allNodes, searchNodes);
                    foreach (var n in searchNodes)
                    {
                        if (n.IsNullOrEmpty())
                        {
                            continue;
                        }
                        if (!seen.Add(NodeFingerprint(n)))
                        {
                            repeated++;
                            continue;
                        }
                        freshCandidates.Add(n);
                    }
 await _updateFunc(false, $"[成功] GitHub搜索补充到 {allNodes.Count} 个候选");
 }
 }

 // DuckDuckGo 全网搜索：不受 GitHub 20 仓库限制，能搜到博客、聚合站等新源
 if (freshCandidates.Count < validateCap)
 {
 try
 {
 var ddgNodes = await SearchDuckDuckGoForNodes();
 if (ddgNodes.Count > 0)
 {
 MergeByFingerprint(allNodes, ddgNodes);
 foreach (var n in ddgNodes)
 {
 if (n.IsNullOrEmpty()) continue;
 if (!seen.Add(NodeFingerprint(n))) { repeated++; continue; }
 freshCandidates.Add(n);
 }
 await _updateFunc(false, $"[成功] DuckDuckGo 全网搜索补充到 {allNodes.Count} 个候选");
 }
 }
 catch (Exception ex)
 {
 Logging.SaveLog($"{_tag}: DuckDuckGo search step failed: {ex.Message}");
 }
 }

 if (allNodes.Count == 0)
            {
                AISearchTracker.RecordError("未找到可用的免费VPN节点");
                await _updateFunc(false, "[失败] AI未找到可用的免费VPN节点");
                return 0;
            }

            AISearchTracker.RecordCandidates(allNodes);

            if (freshCandidates.Count == 0)
            {
                await _updateFunc(true, $"[跳过] 本次抓到 {allNodes.Count} 个候选，但组内已全部存在（{repeated} 个重复），未做任何改动。");
                return 0;
            }

            await _updateFunc(false, $"[搜索] 共 {allNodes.Count} 个候选（去重后），其中 {freshCandidates.Count} 个是组内没有的，正在起临时内核做真实延迟验证...");

            // Step 2: Validate nodes via real HTTP ping through a temporary core instance.
            // TCP port-open is NOT enough — a port can be open while the VPN service is
            // dead, expired, or protocol-mismatched. Only a real HTTP request through the
            // node's SOCKS5 proxy proves the node can actually tunnel traffic.

            // 「淘金式」分批验证：免费池的真实有效率可能低于 1%，一次性抽样
            // 很容易全军覆没——实测 250 抽 0 中，而同一时间老组 50 个里还有 9 个活着，
            // 说明池里有活节点、只是抽样没抽中。改成把洗好的候选池按批淘：
            // 每批 200 个，一批有活节点立刻收工；最多淘 2000 个（预算内仍按批隔离，
            // 单批配置异常只损失该批）。洗牌保证各来源均匀分摊名额。
            // 淘到 maxNodes 个（或预算用尽）才收工，让每轮采集尽量多带有效节点回来；
            // 连续 2 批颗粒无收就提前停——那说明池子剩余部分大概率也是死的，别浪费时间。
            Shuffle(freshCandidates);
 var budget = Math.Min(freshCandidates.Count, 5000);
 var waveSize = 300;
            var validDelays = new List<(string Link, int Delay)>();
            var tested = 0;
            var emptyWaves = 0;
            for (var offset = 0; offset < budget && validDelays.Count < maxNodes && emptyWaves < 2; offset += waveSize)
            {
                var wave = freshCandidates.Skip(offset).Take(waveSize).ToList();
                if (wave.Count == 0)
                {
                    break;
                }
                tested += wave.Count;
                await _updateFunc(false, $"[验证] 第 {offset / waveSize + 1} 批：真实穿透测试 {wave.Count} 个候选（累计 {tested}，已淘到 {validDelays.Count}）...");
                var waveValid = await ValidateNodesWithDelayAsync(wave, wave.Count);
                validDelays.AddRange(waveValid);
                emptyWaves = waveValid.Count > 0 ? 0 : emptyWaves + 1;
                if (waveValid.Count > 0)
                {
                    await _updateFunc(false, $"[验证] 本批 +{waveValid.Count} 个可用，累计 {validDelays.Count}。");
                }
            }

            validDelays = validDelays
                .GroupBy(v => v.Link)
                .Select(g => g.OrderBy(v => v.Delay).First())
                .OrderBy(v => v.Delay)
                .ToList();
            var validNodes = validDelays.Select(v => v.Link).ToList();

            if (validNodes.Count > maxNodes)
            {
                validNodes = validNodes.Take(maxNodes).ToList();
            }

            AISearchTracker.RecordTestResult(validNodes, tested);

            if (validNodes.Count == 0)
            {
                await _updateFunc(false, $"[警告] 淘金式验证了 {tested} 个候选节点，没有一个能真正转发流量（免费节点失效率本就很高，下一轮会换一批候选再试）。");
                return 0;
            }

            await _updateFunc(false, $"[验证] 淘金式共测 {tested} 个候选，{validNodes.Count} 个真实可用（最快 {validDelays[0].Delay} ms），按延迟从快到慢入库。");

            // Step 3: 增量入库 —— 只补进「组内还没有」的节点，绝不先清空整组。
            // 早期实现每次都 RemoveServersViaSubid 再重建，等于把昨天采到、今天还活着的
            // 节点先全删一遍再加回来：分组里永远只剩当天的，失效清理也就永远看不出效果。
            // 到这一步才确实要写库了，分组不存在就建一个
            subId ??= await GetOrCreateAISubscriptionGroup(aiConfig.AiGroupRemarks);
            var (added, duplicated) = await AddNewNodesToGroupAsync(_config, subId, validNodes);

            // 入库即回填真实延迟：不写的话新节点在列表里是「未测」状态，
            // 用户要自己再测一遍才能看出快慢；延迟在验证阶段已经拿到了，直接对号入座。
            if (added > 0)
            {
                await ApplyDelaysToGroupAsync(subId, validDelays);
            }

            AISearchTracker.RecordImported(added);
            if (added > 0)
            {
                await _updateFunc(true, $"[成功] 新增 {added} 个有效节点到「{aiConfig.AiGroupRemarks}」分组（已跳过组内已存在的 {duplicated} 个）");
            }
            else
            {
                await _updateFunc(true, $"[跳过] 本次找到 {validNodes.Count} 个可用节点，但组内已全部存在，未做改动。");
            }
            return added;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            AISearchTracker.RecordError($"搜索失败: {ex.Message}");
            await _updateFunc(false, $"[失败] AI搜索失败: {ex.Message}");
            return 0;
        }
        finally
        {
            // 源路径缓存无论成败都落盘，下一轮起跑就能吃到本轮的命中/剔除信息
            AiSourcePathCache.Flush();
        }
    }

    /// <summary>把验证阶段拿到的延迟按「地址:端口」对号入座写进组内节点。</summary>
    private static async Task ApplyDelaysToGroupAsync(string subId, List<(string Link, int Delay)> validDelays)
    {
        try
        {
            if (validDelays.Count == 0)
            {
                return;
            }
            var delayMap = validDelays
                .GroupBy(v => NodeFingerprint(v.Link))
                .ToDictionary(g => g.Key, g => g.Min(v => v.Delay));

            var servers = await AppManager.Instance.ProfileItems(subId);
            if (servers == null)
            {
                return;
            }
            foreach (var s in servers)
            {
                var addr = (s.Address ?? string.Empty).Trim().ToLowerInvariant();
                if (addr.IsNullOrEmpty() || s.Port <= 0)
                {
                    continue;
                }
                if (delayMap.TryGetValue($"{addr}:{s.Port}", out var delay) && delay > 0)
                {
                    ProfileExManager.Instance.SetTestDelay(s.IndexId, delay);
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog("AIFetchService: ApplyDelaysToGroupAsync failed", ex);
        }
    }

    /// <summary>
    /// 一次完整闭环：先清掉组内已经失效的，再补充本次新采到的有效节点。
    /// 定时器和各个手动入口统一走这里，避免出现「自动跑会清理、手动点不清理」
    /// 这种两边行为不一致的情况。
    /// </summary>
    public async Task<(int Added, int Cleaned)> RunFullCycleAsync()
    {
        var cleaned = 0;
        try
        {
            cleaned = await CleanInvalidNodesInAIGroups();
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}: Clean step in full cycle failed: {ex.Message}");
        }

        var added = await FetchAndAddNodesAsync();
        return (added, cleaned);
    }

    /// <summary>
    /// Try to fetch nodes from crawl4ai server if it's running.
    /// crawl4ai is a Python-based web crawler that can better handle dynamic pages.
    /// </summary>
    private async Task<List<string>> FetchFromCrawl4AI()
    {
        var nodes = new List<string>();

        try
        {
            // 共用 HttpClient；本地 crawl4ai 没起时连接会被立刻拒绝，
            // 用 5 秒超时兜住「端口被防火墙丢包」这种需要等超时的情况。
            using var healthCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var healthResp = await _http.GetAsync("http://127.0.0.1:18888/health", healthCts.Token);
            if (!healthResp.IsSuccessStatusCode)
            {
                return nodes; // Server not running
            }

            Logging.SaveLog($"{_tag}: crawl4ai server detected, using it for crawling");

            // Fetch all repos via crawl4ai
            using var crawlCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var response = await _http.GetStringAsync("http://127.0.0.1:18888/crawl-all", crawlCts.Token);
            var doc = JsonDocument.Parse(response);

            if (doc.RootElement.TryGetProperty("nodes", out var nodesArray))
            {
                foreach (var node in nodesArray.EnumerateArray())
                {
                    var nodeStr = node.GetString();
                    if (!string.IsNullOrEmpty(nodeStr))
                    {
                        nodes.Add(nodeStr);
                    }
                }
            }

            Logging.SaveLog($"{_tag}: crawl4ai returned {nodes.Count} nodes");
        }
        catch (HttpRequestException)
        {
            // crawl4ai server not running, use fallback
            Logging.SaveLog($"{_tag}: crawl4ai server not running, using built-in crawler");
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}: crawl4ai error: {ex.Message}");
        }

        return nodes;
    }

    private async Task<List<string>> FetchFromSpecifiedUrls()
    {
        var nodes = new List<string>();
        var urlLock = new object();

        using var semaphore = new SemaphoreSlim(6);
        var tasks = UserSpecifiedUrls.Select(async url =>
        {
            await semaphore.WaitAsync();
            try
            {
                var fetched = await FetchNodesFromGitHubRepo(url);
                if (fetched != null && fetched.Count > 0)
                {
                    lock (urlLock)
                    {
                        foreach (var n in fetched)
                        {
                            if (!nodes.Contains(n))
                                nodes.Add(n);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.SaveLog($"{_tag}: FetchFromSpecifiedUrl {url} failed: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return nodes;
    }
    private async Task<List<string>> FetchNodesFromGitHubRepo(string repoUrl)
    {
        var nodes = new List<string>();

        // Non-GitHub URL (plain website): fall through to a generic content fetch.
        if (!repoUrl.Contains("github.com/", StringComparison.OrdinalIgnoreCase))
        {
            var fetched = await FetchWithMirrorFast(repoUrl, useCache: true);
            return fetched ?? new List<string>();
        }
        var path = repoUrl.Replace("https://github.com/", "").TrimEnd('/');
        var parts = path.Split('/');
        if (parts.Length < 2)
            return nodes;

        var owner = parts[0];
        var repo = parts[1];
        var repoKey = $"{owner}/{repo}";

        // Repo-specific file paths (based on actual repo structure analysis)
        var paths = GetRepoSpecificPaths(repoKey);

        // raw.githubusercontent.com does not accept "HEAD" as a ref segment (it
        // returns 404 for every file). 显式试两个最常见的默认分支：main 与 master。
        // 先跑 main；一个节点都没捞到再回退 master，避免无谓地把请求量翻倍。
        foreach (var branch in new[] { "main", "master" })
        {
            foreach (var p in paths)
            {
                try
                {
                    var rawUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{p}";
                    var fetched = await FetchWithMirrorFast(rawUrl, useCache: true);
                    if (fetched != null && fetched.Count > 0)
                    {
                        nodes.AddRange(fetched);
                        if (nodes.Count >= 400)
                            break; // Stop after enough
                    }
                }
                catch { }
            }

            if (nodes.Count > 0)
            {
                break;
            }
        }

        // 按「地址:端口」去重：同一个节点换了备注名不该算两条
        var seenFp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return nodes.Where(n => seenFp.Add(NodeFingerprint(n))).ToList();
    }

    /// <summary>
    /// Returns repo-specific file paths to check for nodes.
    /// Based on actual analysis of each repo's structure.
    /// </summary>
    private static List<string> GetRepoSpecificPaths(string repoKey)
    {
        return repoKey switch
        {
            // 0xRadikal: aggregate file holds every node; fast/light are subsets
            "0xRadikal/Free-v2ray-Configs" => new List<string>
            {
                "all/configs.txt",
                "fast/configs.txt",
                "light/configs.txt",
            },
            // cbusifabcap: Z.txt is the full list; sub/*.yml are Clash/singbox subs
            "cbusifabcap/daily_free_vpn" => new List<string>
            {
                "Z.txt",
                "sub/sub.yml",
                "sub/URI.yml",
            },
            // kanaltvyt: has singapore.txt and output/*.txt
            "kanaltvyt-dev/FreeForYoung" => new List<string>
            {
                "singapore.txt",
                "output/singapore.txt",
                "output/subscription.txt",
            },
            // hello-world-1989: has end-gfw-together-ss
            "hello-world-1989/cn-news" => new List<string>
            {
                "end-gfw-together-ss",
                "server.txt",
            },
            // Default: try common paths
            _ => new List<string>
            {
                "sub/sub_merge.txt",
                "sub/sub.txt",
                "sub.txt",
                "subscribe",
                "sub",
                "v2ray",
                "nodes.txt",
                "list.txt",
                "sub/base64.txt",
                "subscribe.txt",
                "node",
                "free.txt",
            },
        };
    }

    /// <summary>
    /// Clean invalid nodes in AI-managed groups. Only removes nodes from groups
    /// that were created by AI (identified by specific markers).
    /// User-created groups are NEVER touched.
    /// </summary>
    public async Task<int> CleanInvalidNodesInAIGroups()
    {
        int totalRemoved = 0;

        try
        {
            var subItems = await AppManager.Instance.SubItems();
            if (subItems == null)
                return 0;

            // Only clean AI-created groups (identified by multiple markers)
            var aiGroups = subItems.Where(s => IsAICreatedGroup(s)).ToList();

            foreach (var group in aiGroups)
            {
                var removed = await CleanInvalidNodesInGroup(group.Id);
                totalRemoved += removed;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}: CleanInvalidNodes failed: {ex.Message}");
        }

        return totalRemoved;
    }

    /// <summary>
    /// Check if a subscription group was created by AI.
    /// Uses multiple markers to ensure we only touch AI-created groups.
    /// </summary>
    private bool IsAICreatedGroup(SubItem sub)
    {
        // 只在「能确证是 AI 建的组」时才动它。这个判断决定了要不要删节点，
        // 宁可漏判（少清一个组）也不能误判（删掉用户自己的节点）。
        // 早期版本只要备注里含 "AI"/"自动"/"auto" 就认作 AI 组，
        // 用户自建的「自动备份」「Auto-日本」这类分组会被整组清空——已收紧。

        // 1) 建组时写入的 Memo 标记（最可靠）
        if (sub.Memo?.Contains("由AI自动搜索", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        // 2) URL 标记
        if ((sub.Url ?? string.Empty).Contains("ai-auto", StringComparison.OrdinalIgnoreCase))
            return true;

        // 3) 当前配置里指定的 AI 分组名（完全匹配，不做包含匹配）
        var aiGroup = _config.AIConfigItem?.AiGroupRemarks;
        if (!aiGroup.IsNullOrEmpty() &&
            string.Equals(sub.Remarks ?? string.Empty, aiGroup, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private async Task<int> CleanInvalidNodesInGroup(string subId)
    {
        int removed = 0;

        try
        {
            // 内核管理器未初始化时（应用刚启动、配置还没载入），
            // LoadCoreConfigSpeedtest 会拿到空配置抛 NullReferenceException——
            // 实测日志里连续出现 Arg_NullReferenceException 就是这个时序问题。
            // 直接跳过本轮清理，下一轮再清。
            if (!CoreManager.Instance.IsInitialized)
            {
                Logging.SaveLog($"{_tag}: Clean skipped for group {subId}: CoreManager not initialized yet");
                return 0;
            }

            // Get servers in this group
            var servers = await AppManager.Instance.ProfileItems(subId);
            if (servers == null || servers.Count == 0)
                return 0;

            // Run real latency test (realping) to update Delay values in ProfileEx.
            // This actually tries to tunnel traffic through each node — a TCP port
            // being open is NOT sufficient to prove a node is usable.
            var testItems = new List<ServerTestItem>();
            foreach (var server in servers)
            {
                if (server.ConfigType == EConfigType.Custom)
                    continue;
                if (!server.ConfigType.IsComplexType() && server.Port <= 0)
                    continue;

                var coreType = AppManager.Instance.GetCoreType(server, server.ConfigType);
                testItems.Add(new ServerTestItem
                {
                    IndexId = server.IndexId,
                    Address = server.Address,
                    Port = server.Port,
                    ConfigType = server.ConfigType,
                    AllowTest = true,
                    Profile = server,
                    CoreType = coreType,
                });
            }

            int successCount = 0;

            if (testItems.Count > 0)
            {
                // 分块做真实穿透测试（每块起一个临时内核）。
                // 一次性整组测的话，一个坏节点过去就能让整份配置被内核拒绝
                //（LengthMin can't be 0 事件），于是「0/N 全测不通」触发保险丝，
                // 死节点永远清不掉——组里 41/50 失效却一直删不掉就是这个链条。
                using var semaphore = new SemaphoreSlim(10);
                foreach (var coreGroup in testItems.GroupBy(it => it.CoreType))
                {
                    foreach (var chunk in coreGroup.Chunk(RealPingChunkSize))
                    {
                        var batch = chunk.ToList();
                        ProcessService processService = null;
                        try
                        {
                            processService = await CoreManager.Instance.LoadCoreConfigSpeedtest(batch);
                            if (processService == null)
                            {
                                Logging.SaveLog($"{_tag}: clean batch of {batch.Count} failed to start core, batch skipped");
                                continue;
                            }

                            await WaitForSpeedtestPorts(batch);
                            var batchSuccess = 0;
                            var results = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
                            var tasks = batch.Select(async it =>
                            {
                                await semaphore.WaitAsync();
                                try
                                {
                                    var webProxy = new WebProxy($"socks5://{Global.Loopback}:{it.Port}");
                                    results[it.IndexId] = await ConnectionHandler.GetRealPingTime(webProxy, 5);
                                }
                                catch { }
                                finally { semaphore.Release(); }
                            }).ToList();
                            await Task.WhenAll(tasks);

                            // 二次复测（两连挂才判死）：单次超时可能是瞬时抖动。
                            // 实测教训：本机网络抖一下，一次 ping 就把「正在使用、隧道完好」的
                            // 激活节点标成 -1 删掉。对首轮失败者再给一次机会再下结论。
                            var retryList = batch.Where(it => results.GetValueOrDefault(it.IndexId) <= 0).ToList();
                            if (retryList.Count > 0)
                            {
                                await Task.Delay(500);
                                var retryTasks = retryList.Select(async it =>
                                {
                                    await semaphore.WaitAsync();
                                    try
                                    {
                                        var webProxy = new WebProxy($"socks5://{Global.Loopback}:{it.Port}");
                                        var delay = await ConnectionHandler.GetRealPingTime(webProxy, 5);
                                        if (delay > 0)
                                        {
                                            results[it.IndexId] = delay;
                                        }
                                    }
                                    catch { }
                                    finally { semaphore.Release(); }
                                }).ToList();
                                await Task.WhenAll(retryTasks);
                            }

                            foreach (var it in batch)
                            {
                                var delay = results.GetValueOrDefault(it.IndexId);
                                ProfileExManager.Instance.SetTestDelay(it.IndexId, delay);
                                if (delay > 0)
                                {
                                    Interlocked.Increment(ref batchSuccess);
                                }
                            }
                            successCount += batchSuccess;
                        }
                        finally
                        {
                            if (processService != null)
                            {
                                try
                                { await processService.StopAsync(); }
                                catch { }
                            }
                        }
                    }
                }

                // 保险丝：整组一个都没测通，就不许删任何节点。
                // 全组同时失效几乎不可能，真正的解释是本机断网 / 内核没起来 / 代理端口被占。
                // 这种情况下按 Delay==-1 去删，等于把用户几天攒下来的节点一次网络抖动清空。
                // 宁可这次不清理（下次再试），也不能误删整组。
                if (successCount == 0)
                {
                    Logging.SaveLog($"{_tag}: Clean aborted for group {subId}: 0/{testItems.Count} nodes reachable, treated as local network issue, nothing removed");
                    await _updateFunc(false, $"[跳过] 组内 {testItems.Count} 个节点本轮全部未测通，判定为本地网络异常，本次不清理");
                    return 0;
                }
            }

            // Now remove nodes whose Delay == -1 (failed real-ping test)
            removed = await ConfigHandler.RemoveInvalidServerResult(_config, subId, _config.IndexId);
            // RemoveInvalidServerResult 在「组内没有可测项」时返回 -1（表示无效输入，
            // 不是「删了 -1 个」）。不夹住的话，界面上会显示「清理 -1 个失效节点」。
            if (removed < 0)
            {
                removed = 0;
            }
            if (removed > 0)
            {
                await ConfigHandler.SaveConfig(_config);
                Logging.SaveLog($"{_tag}: Removed {removed} invalid nodes from group {subId}");
            }
        }
        catch (Exception ex)
        {
            // 记完整异常而不是 ex.Message：.NET 在缺本地化资源时会返回资源键
            // （实测日志里只有一句 `Arg_NullReferenceException`，等于没有信息），
            // 带上堆栈才定位得到出错位置。
            Logging.SaveLog($"{_tag}: CleanInvalidNodesInGroup failed", ex);
        }

        return removed;
    }

    public async Task<int> AnalyzeUrlAsync(string url, string targetGroup, int maxNodes)
    {
        var aiConfig = _config.AIConfigItem;
        if (aiConfig == null || !aiConfig.Enabled || aiConfig.ApiUrl.IsNullOrEmpty())
        {
            await _updateFunc(false, "AI功能未启用或API地址未配置");
            return 0;
        }

        try
        {
            await _updateFunc(false, $"🔍 AI正在分析链接: {url}");

            // Step 1: Download content from the URL
            var content = await DownloadUrlContent(url);
            if (content.IsNullOrEmpty())
            {
                await _updateFunc(false, "[失败] 无法下载链接内容");
                return 0;
            }

            await _updateFunc(false, $"📥 已下载内容 ({content.Length} 字符)，AI正在分析...");

            // Step 2: Ask AI to extract nodes from the content
            var nodes = await ExtractNodesFromContent(aiConfig, content, url);
            if (nodes == null || nodes.Count == 0)
            {
                AISearchTracker.RecordError("未能从内容中提取到有效的节点链接");
                await _updateFunc(false, "[失败] AI未能从内容中提取到有效的节点链接");
                return 0;
            }

            AISearchTracker.BeginRun(0);
            AISearchTracker.RecordCandidates(nodes);
            await _updateFunc(false, $"🔍 AI提取到 {nodes.Count} 个候选节点");

            // Step 3: 与主采集流程用同一套「真实穿透」验证。
            // 旧实现走的是 TestNode()：先拿程序自身正在运行的 SOCKS 端口去测，
            // 测的其实是「当前节点」而不是候选节点；兜底又是裸 TCP 连通，
            // 而端口开着 ≠ 服务可用（可能是死节点、协议不匹配、已过期）。
            // 结果就是「程序说有效、用户一测就废」，这正是 5% 有效性的来源。
            // 现在统一走 ValidateNodesViaRealPing：起临时内核 + 每个节点独立本地入站，
            // 只有真实 HTTP 请求穿透成功（delay > 0）才算通过。
            var validateCap = Math.Min(Math.Max(maxNodes * 8, 100), 500);
            var candidates = nodes;
            if (candidates.Count > validateCap)
            {
                Shuffle(candidates);
                candidates = candidates.Take(validateCap).ToList();
            }

            var validNodes = await ValidateNodesViaRealPing(candidates, maxNodes);

            if (validNodes.Count > maxNodes)
            {
                validNodes = validNodes.Take(maxNodes).ToList();
            }

            AISearchTracker.RecordTestResult(validNodes, nodes.Count);

            if (validNodes.Count == 0)
            {
                await _updateFunc(false, "[警告] 所有节点验证均失败");
                return 0;
            }

            // Step 4: 增量入库 —— 只补进「组内还没有」的节点，不动已有节点
            var subId = await GetOrCreateAISubscriptionGroup(targetGroup);
            var (added, duplicated) = await AddNewNodesToGroupAsync(_config, subId, validNodes);

            AISearchTracker.RecordImported(added);
            if (added > 0)
            {
                await _updateFunc(true, $"🎉 成功添加 {added} 个有效节点到「{targetGroup}」分组（已跳过组内已存在的 {duplicated} 个）");
            }
            else
            {
                await _updateFunc(true, $"[跳过] 链接里的 {validNodes.Count} 个可用节点在「{targetGroup}」分组已全部存在，未做改动。");
            }
            return added;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            await _updateFunc(false, $"[失败] 链接分析失败: {ex.Message}");
            return 0;
        }
    }

    private async Task<string> DownloadUrlContent(string url)
    {
        // 复用 AiUrlFetchService：自动重定向 + 自动解压 + 重试 + GitHub 镜像
        // + 系统代理/直连双通道。这里只取内容，保持原返回约定（失败返回空串）。
        var res = await AiUrlFetchService.FetchAsync(url);
        return res.Success ? res.Content : string.Empty;
    }
    private async Task<List<string>?> ExtractNodesFromContent(AIConfigItem aiConfig, string content, string sourceUrl)
    {
        // First try to extract nodes directly from content (fast path)
        var directNodes = ParseNodesFromResponse(content);
        if (directNodes.Count > 0)
        {
            return directNodes;
        }

        // Subscriptions are frequently Base64-encoded (often with newlines).
        // Decode before falling back to the LLM — otherwise the LLM gets a
        // blob of base64 it cannot parse and "0 nodes" is the result.
        if (content.Trim().Length > 40)
        {
            try
            {
                var cleaned = new string(content.Where(c => !char.IsWhiteSpace(c)).ToArray());
                var decodedBytes = Convert.FromBase64String(cleaned);
                var decoded = Encoding.UTF8.GetString(decodedBytes);
                var decodedNodes = ParseNodesFromResponse(decoded);
                if (decodedNodes.Count > 0)
                {
                    return decodedNodes;
                }
            }
            catch { /* not valid base64; ignore */ }
        }

        // If no direct nodes found, ask AI to analyze the content
        var prompt = $"""
请分析以下URL内容，从中提取所有有效的VPN/代理节点链接。

来源URL: {sourceUrl}

内容（截取前24000字符）:
{content[..Math.Min(content.Length, 24000)]}

请执行以下步骤：
1. 检查内容中是否包含 vmess://, vless://, trojan://, ss://, hysteria2://, hy2://, tuic://, anytls://, socks5://, wireguard://, naive+https:// 等节点链接
2. 如果是订阅链接，尝试解析其中的节点
3. 如果是网页，提取页面中包含的节点信息
4. 如果内容是 Clash / mihomo 的 YAML（proxies: 列表）或 sing-box 的 JSON（outbounds 列表），把其中每个节点转换成上面这些标准链接形式后返回
5. 最多返回 50 个节点，每行一个，不要添加编号、解释或代码块标记
6. 如果没有找到节点，返回 "NO_NODES_FOUND"

返回格式：每行一个节点链接
""";

        return await CallAIForNodes(aiConfig, prompt);
    }

    private async Task<List<string>?> CallAIForNodes(AIConfigItem aiConfig, string prompt)
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(60);

        var requestBody = new
        {
            model = aiConfig.ModelId,
            messages = new[]
            {
                new { role = "system", content = "你是一个专业的VPN节点解析助手，专门从各种来源提取VPN节点链接。" },
                new { role = "user", content = prompt }
            },
            temperature = 0.3,
            max_tokens = 4000
        };

        var json = JsonSerializer.Serialize(requestBody);
        var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        if (aiConfig.ApiKey.IsNotEmpty())
        {
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", aiConfig.ApiKey);
        }

        var response = await httpClient.PostAsync($"{aiConfig.ApiUrl}/chat/completions", httpContent);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(responseJson);

        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var message = choices[0].GetProperty("message").GetProperty("content").GetString();
            if (message != null && message.Contains("NO_NODES_FOUND"))
            {
                return null;
            }
            return ParseNodesFromResponse(message ?? string.Empty);
        }
        return null;
    }

    private async Task<List<string>?> SearchGitHubForFreeNodes(AIConfigItem aiConfig)
    {
        var found = new List<string>();

        string[] SearchQueries =
        {
            "free v2ray nodes",
            "free vless",
            "free clash subscription",
"free trojan",
"free hysteria2",
"v2ray free subscribe",
"free vpn subscription github",
"free vmess",
"v2ray configs",
"free proxy nodes",
"shadowsocks free",
"机场 免费 节点",
"free v2ray config",
"vless free",
"trojan config free",
"clash config free",
"free ss subscribe",
"vpn free nodes daily",
"v2ray 订阅",
"免费 代理 节点",
"free wireguard config",
"tuic free nodes",
"anytls free",
};

        string[] CandidatePaths =
        {
            "sub/sub_merge.txt",
            "sub/sub.txt",
            "sub.txt",
            "subscribe",
            "sub",
            "v2ray",
            "nodes.txt",
            "list.txt",
            "sub/base64.txt",
            "subscribe.txt",
            "node",
            "free.txt",
            "ss.txt",
            "vless.txt",
        };

        using var searchClient = new HttpClient();
        searchClient.Timeout = TimeSpan.FromSeconds(10);
        searchClient.DefaultRequestHeaders.UserAgent.ParseAdd("LDv2rayN/1.0 (github-search)");
        searchClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");

        // Parallel GitHub repo search (3 concurrent)
        var repos = new List<(string Owner, string Repo)>();
        var repoLock = new object();
        using var searchSemaphore = new SemaphoreSlim(3);
        var searchTasks = SearchQueries.Select(async query =>
        {
            await searchSemaphore.WaitAsync();
            try
            {
if (repos.Count >= 40)
 return;
 var url = $"https://api.github.com/search/repositories?q={Uri.EscapeDataString(query)}&sort=updated&order=desc&per_page=10";
                var resp = await searchClient.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                    return;
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("items", out var items))
                    return;
                foreach (var item in items.EnumerateArray())
                {
                    if (!item.TryGetProperty("full_name", out var fn))
                        continue;
                    var full = fn.GetString();
                    if (full is null)
                        continue;
                    var parts = full.Split('/');
                    if (parts.Length != 2)
                        continue;
                    var pair = (parts[0], parts[1]);
                    lock (repoLock)
                    {
                        if (!repos.Contains(pair) && repos.Count < 40)
                            repos.Add(pair);
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.SaveLog($"{_tag}: repo search '{query}' failed: {ex.Message}");
            }
            finally
            {
                searchSemaphore.Release();
            }
        });
        await Task.WhenAll(searchTasks);

        AISearchTracker.AddStep($"GitHub 搜索命中 {repos.Count} 个仓库");

        // Fallback repos (always reliable)
        string[] FallbackRaw =
        {
            "https://raw.githubusercontent.com/mahdibland/V2RayAggregator/master/sub/sub_merge.txt",
            "https://raw.githubusercontent.com/mahdibland/ShadowsocksAggregator/master/Eternity.txt",
            "https://raw.githubusercontent.com/Pawdroid/Free-servers/main/sub",
            "https://raw.githubusercontent.com/ripaojiedian/freenode/main/sub",
            "https://raw.githubusercontent.com/aiboboxx/v2rayfree/main/v2",
            "https://raw.githubusercontent.com/mfuu/v2ray/master/v2ray",
            "https://raw.githubusercontent.com/peasoft/NoMoreWalls/master/list.txt",
            "https://raw.githubusercontent.com/barry-far/V2ray-Configs/main/All_Configs_Sub.txt",
            "https://raw.githubusercontent.com/Epodonios/v2ray-configs/main/All_Configs_Sub.txt",
            "https://raw.githubusercontent.com/freefq/free/master/v2",
        };

        var probeUrls = new List<string>();
        foreach (var (owner, repo) in repos)
        {
            foreach (var p in CandidatePaths.Take(10))
            {
                probeUrls.Add($"https://raw.githubusercontent.com/{owner}/{repo}/main/{p}");
            }
        }
        probeUrls.AddRange(FallbackRaw);

        // 缓存重排：上轮成功过的源排最前、上轮确定 404 的直接剔除。
        // 探测组合动辄两三百个地址，绝大多数 404，这一步能把后续轮次的抓取时间砍掉一大半。
        probeUrls = AiSourcePathCache.Prioritize(probeUrls);

        // Parallel fetch with higher concurrency (12)
        using var fetchSemaphore = new SemaphoreSlim(12);
        var fetchTasks = probeUrls.Select(async rawUrl =>
        {
            List<string>? nodes = null;
            try
            {
                await fetchSemaphore.WaitAsync();
                try
                {
                    nodes = await FetchWithMirrorFast(rawUrl, useCache: true);
                }
                finally
                {
                    fetchSemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                Logging.SaveLog($"{_tag}: raw fetch {rawUrl} failed: {ex.Message}");
            }
            return nodes;
        }).ToList();

        var fetchResults = await Task.WhenAll(fetchTasks);
        // 按指纹去重：不同仓库转发的往往是同一批节点，按整条链接去重会
        // 把「同机器不同备注名」当成不同节点，虚增候选数量、浪费验证名额。
        var seenFp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var nodes in fetchResults)
        {
            if (nodes is null)
                continue;
            foreach (var n in nodes)
            {
                if (found.Count >= 400)
                    break;
                if (seenFp.Add(NodeFingerprint(n)))
                    found.Add(n);
            }
            if (found.Count >= 400)
                break;
        }

        return found.Count > 0 ? found : null;
    }


/// <summary>
/// 用 DuckDuckGo HTML 接口搜「免费节点」相关页面，从结果页里扫节点链接。
/// 不需要 API key，不受 GitHub 搜索 20 仓库的硬上限约束。
/// 能搜到博客、聚合站、论坛帖等 GitHub 之外的新鲜来源。
/// </summary>
private async Task<List<string>> SearchDuckDuckGoForNodes()
{
    var found = new List<string>();
    try
    {
        await _updateFunc(false, "🔍 DuckDuckGo 搜索免费节点中...");

        string[] queries =
        {
            "free v2ray nodes " + DateTime.UtcNow.ToString("yyyy-MM"),
            "免费 v2ray 节点 " + DateTime.UtcNow.ToString("yyyy-MM"),
            "free vless config github",
            "free trojan nodes daily",
            "clash free subscription " + DateTime.UtcNow.ToString("yyyy-MM-dd"),
            "hysteria2 free nodes",
            "vmess 免费 订阅",
            "ss free proxy " + DateTime.UtcNow.ToString("yyyy-MM"),
        };

using var client = new HttpClient(new HttpClientHandler
{
 UseProxy = true,
 Proxy = System.Net.WebRequest.DefaultWebProxy,
 AutomaticDecompression = System.Net.DecompressionMethods.All,
});
client.Timeout = TimeSpan.FromSeconds(12);
client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) LDv2rayN");

        var urls = new List<string>();
        foreach (var q in queries.Take(4))
        {
            try
            {
                var u = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(q)}";
                var resp = await client.GetAsync(u);
                if (!resp.IsSuccessStatusCode) continue;
                var html = await resp.Content.ReadAsStringAsync();
                var matches = System.Text.RegularExpressions.Regex.Matches(
                    html, @"uddg=([^&""]+)", System.Text.RegularExpressions.RegexOptions.Compiled);
                foreach (System.Text.RegularExpressions.Match m in matches)
                {
                    var dec = Uri.UnescapeDataString(m.Groups[1].Value);
                    if (dec.StartsWith("http")) urls.Add(dec);
                }
            }
            catch { }
        }

        foreach (var pageUrl in urls.Distinct().Take(15))
        {
            try
            {
                var resp = await client.GetAsync(pageUrl);
                if (!resp.IsSuccessStatusCode) continue;
 var content = await resp.Content.ReadAsStringAsync();
 var pageNodes = new List<string>();
 var pageSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
 CollectNodes(content, pageNodes, pageSeen);
 if (pageNodes.Count > 0)
 {
 found.AddRange(pageNodes);
 var shortUrl = pageUrl.Length > 50 ? pageUrl.Substring(0, 50) + "…" : pageUrl;
 await _updateFunc(false, $"  [DuckDuckGo] {shortUrl} → {pageNodes.Count} 个节点");
 }
            }
            catch { }
        }
    }
    catch (Exception ex)
    {
        Logging.SaveLog($"{_tag}: DuckDuckGo search failed: {ex.Message}");
    }

    return found;
}

    /// <summary>
    /// raw.githubusercontent.com 在国内经常连不上，这里准备一组镜像前缀。
    /// 按国内连通性大致排序：前两个会参与第一波竞速。
    /// </summary>
    private static readonly string[] GitHubMirrors =
    {
        "https://ghfast.top/",
        "https://gh-proxy.com/",
        "https://ghproxy.net/",
        "https://gh.llkk.cc/",
        "https://github.moeyy.xyz/",
        "https://ghproxy.cc/",
    };

    /// <summary>
    /// 抓一个 URL 并解析出节点，失败时走镜像。
    /// 抓取采用「竞速」而不是旧实现的串行重试：直连 + 最快的两个镜像并发发起，
    /// 谁先成功用谁。国内访问 raw.githubusercontent.com 经常要等超时，
    /// 串行时一个地址最坏要 7 次尝试 × 8 秒 ≈ 一分钟；竞速后通常一个超时周期内就出结果。
    /// </summary>
    /// <param name="useCache">把结果记入源路径缓存（下轮优先命中/剔除 404）。</param>
    private async Task<List<string>?> FetchWithMirrorFast(string rawUrl, bool useCache = false)
    {
        return await FetchWithMirrorCore(rawUrl, TimeSpan.FromSeconds(8), recordStats: true, retry: true, useCache: useCache);
    }

    private enum FetchOutcome
    {
        Success,
        NotFound,
        Failed,
    }

    private async Task<List<string>?> FetchWithMirrorCore(string rawUrl, TimeSpan perAttemptTimeout,
        bool recordStats, bool retry, bool useCache = false)
    {
        var attempts = new List<string> { rawUrl };
        attempts.AddRange(GitHubMirrors.Select(m => m + rawUrl));

        var rounds = retry ? 2 : 1;
        for (var round = 0; round < rounds; round++)
        {
            // 第一波：直连 + 前两个镜像并发竞速
            var firstWave = attempts.Take(3).ToList();
            var (outcome, nodes) = await RaceFetchBatch(firstWave, perAttemptTimeout);
            if (outcome == FetchOutcome.Success)
            {
                RecordMirrorStats(rawUrl, nodes, recordStats);
                if (useCache)
                {
                    AiSourcePathCache.Record(rawUrl, ok: true);
                }
                return nodes;
            }
            if (outcome == FetchOutcome.NotFound)
            {
                // 404 = 资源确实不存在（镜像代理的是同一份内容），不再重试
                if (useCache)
                {
                    AiSourcePathCache.Record(rawUrl, ok: false, notFound: true);
                }
                return null;
            }

            // 第一波全失败：剩余镜像逐个快速尝试
            foreach (var url in attempts.Skip(3))
            {
                (outcome, nodes) = await TryFetchOnce(url, perAttemptTimeout);
                if (outcome == FetchOutcome.Success)
                {
                    RecordMirrorStats(rawUrl, nodes, recordStats);
                    if (useCache)
                    {
                        AiSourcePathCache.Record(rawUrl, ok: true);
                    }
                    return nodes;
                }
                if (outcome == FetchOutcome.NotFound)
                {
                    if (useCache)
                    {
                        AiSourcePathCache.Record(rawUrl, ok: false, notFound: true);
                    }
                    return null;
                }
            }

            if (round + 1 < rounds)
            {
                await Task.Delay(600);
            }
        }

        if (recordStats)
        {
            AISearchTracker.RecordFetchResult(false);
        }
        if (useCache)
        {
            AiSourcePathCache.Record(rawUrl, ok: false);
        }
        return null;
    }

    /// <summary>同一批地址并发竞速：任一成功/404 立即取消其余并返回；全部失败才算失败。</summary>
    private async Task<(FetchOutcome Outcome, List<string>? Nodes)> RaceFetchBatch(List<string> urls, TimeSpan perAttemptTimeout)
    {
        using var raceCts = new CancellationTokenSource();
        var pending = urls.Select(u => TryFetchOnce(u, perAttemptTimeout, raceCts.Token)).ToList();
        while (pending.Count > 0)
        {
            var done = await Task.WhenAny(pending);
            pending.Remove(done);
            var (outcome, nodes) = done.Result;
            if (outcome == FetchOutcome.Success)
            {
                await SafeCancelAsync(raceCts);
                return (FetchOutcome.Success, nodes);
            }
            if (outcome == FetchOutcome.NotFound)
            {
                await SafeCancelAsync(raceCts);
                return (FetchOutcome.NotFound, null);
            }
        }
        return (FetchOutcome.Failed, null);
    }

    private static async Task SafeCancelAsync(CancellationTokenSource cts)
    {
        try
        {
            await cts.CancelAsync();
        }
        catch { }
    }

    /// <summary>单次抓取尝试。绝不抛异常，用 Outcome 表达结果。</summary>
    private async Task<(FetchOutcome Outcome, List<string>? Nodes)> TryFetchOnce(string url, TimeSpan timeout, CancellationToken external = default)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, external);
            using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseContentRead, linked.Token);
            if (!resp.IsSuccessStatusCode)
            {
                return resp.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? (FetchOutcome.NotFound, null)
                    : (FetchOutcome.Failed, null);
            }

            var text = await resp.Content.ReadAsStringAsync(linked.Token);
            if (text.IsNullOrEmpty() || text.Length > 4_000_000)
            {
                return (FetchOutcome.Failed, null);
            }

            var nodes = ParseNodesFromResponse(text);
            return nodes.Count > 0 ? (FetchOutcome.Success, nodes) : (FetchOutcome.Failed, null);
        }
        catch
        {
            return (FetchOutcome.Failed, null);
        }
    }

    private void RecordMirrorStats(string rawUrl, List<string>? nodes, bool recordStats)
    {
        if (!recordStats)
        {
            return;
        }
        AISearchTracker.RecordFetchResult(nodes is { Count: > 0 });
        if (nodes is { Count: > 0 })
        {
            AISearchTracker.RecordMirrorFallback(rawUrl);
        }
    }

    /// <summary>
    /// 从抓取到的正文里提取节点链接。
    ///
    /// 相比旧实现的三点加强：
    /// 1. 不再只认「行首」。旧实现要求节点链接顶在行首，订阅被塞进 HTML 表格、
    ///    JSON 字段或 Markdown 列表时一个都捞不到。现在整段扫描 scheme://。
    /// 2. 补上逐行 Base64。旧实现只试了「整段 Base64」这一种，而不少订阅是
    ///    每行一个 Base64 节点。
    /// 3. 逐条过解析器。旧实现有个兜底分支「含 :// 且含 @ 就当节点」，
    ///    于是网页里的 HTML 链接、邮箱、带凭据的普通 URL 全被当成候选，
    ///    把后面的真实延迟验证名额挤光。现在解析不出来的直接丢。
    /// </summary>
    private List<string> ParseNodesFromResponse(string response)
    {
        var nodes = new List<string>();
        if (response.IsNullOrEmpty())
        {
            return nodes;
        }

        var seenFp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectNodes(response, nodes, seenFp);

        // 整段 Base64（订阅最常见形态）：只在直接扫描一无所获时才试，避免误判
        if (nodes.Count == 0)
        {
            var decoded = TryBase64Decode(response);
            if (decoded != null)
            {
                CollectNodes(decoded, nodes, seenFp);
            }
        }

        // 逐行 Base64：每行一个独立编码的节点
        if (nodes.Count == 0)
        {
            foreach (var line in response.Split('\n'))
            {
                var decoded = TryBase64Decode(line);
                if (decoded != null)
                {
                    CollectNodes(decoded, nodes, seenFp);
                }
            }
        }

        // Clash / mihomo 订阅（YAML）：proxies 数组里的节点不是 URI 形态，
        // 上面所有正则 / Base64 分支一条都抓不到，必须走结构化解析。
        // Extract 内部有廉价的 "proxies:" 前置判断，非 YAML 内容会立刻返回。
        foreach (var link in ClashYamlNodeExtractor.Extract(response))
        {
            if (seenFp.Add(NodeFingerprint(link)))
            {
                nodes.Add(link);
            }
        }

        return nodes;
    }

    /// <summary>把一段文本里所有能解析成功的节点链接收集进 nodes（按地址:端口去重）。</summary>
    private void CollectNodes(string text, List<string> nodes, HashSet<string> seenFp)
    {
        foreach (Match m in _nodeLinkRegex.Matches(text))
        {
            var link = m.Value.Trim().TrimEnd(',', ';', ')', ']', '}', '"', '\'');

            // 网页里的节点链接常被 HTML 实体转义过（&amp; 最典型）。
            // 不解开的话 vless / trojan 的 query 会参数名错位，解析器认不出，
            // 于是「页面上明明有节点」却一条都收不到。
            if (link.Contains('&'))
            {
                link = WebUtility.HtmlDecode(link);
            }

            if (!IsUsableNodeLink(link))
            {
                continue;
            }

            // 同一个节点换个备注名/参数顺序仍是同一台机器，按指纹去重，
            // 免得同一个地址在候选里出现几十次、反复占用验证名额。
            if (seenFp.Add(NodeFingerprint(link)))
            {
                nodes.Add(link);
            }
        }
    }

    /// <summary>
    /// Validate candidate nodes via real HTTP ping through a temporary core instance.
    /// Only nodes that can actually tunnel an HTTP request are kept — TCP port-open
    /// is NOT sufficient (a port can be open while the VPN service is dead/expired).
    /// </summary>
    private async Task<List<string>> ValidateNodesViaRealPing(List<string> candidates, int maxValid)
    {
        var valid = await ValidateNodesWithDelayAsync(candidates, maxValid);
        return valid.Select(v => v.Link).ToList();
    }

    /// <summary>
    /// 带延迟版本的验证：返回「链接 + 真实延迟」，已按延迟升序排好。
    /// 调用方（自动采集）按这个顺序入库，保证名额有限时留下的是最快的节点。
    /// </summary>
    private async Task<List<(string Link, int Delay)>> ValidateNodesWithDelayAsync(List<string> candidates, int maxValid)
    {
        var validPairs = new List<(string Link, int Delay)>();
        if (candidates.Count == 0)
            return validPairs;

        // Resolve node links into ProfileItems and ServerTestItems
        var testItems = new List<ServerTestItem>();
        for (var i = 0; i < candidates.Count; i++)
        {
            try
            {
                var profile = FmtHandler.ResolveConfig(candidates[i], out _);
                if (profile == null || !profile.IsValid())
                    continue;
                if (profile.Address.IsNullOrEmpty() || profile.Port <= 0)
                    continue;

                // Accept loopback as-is (local user-configured nodes)
                if (profile.Address is "127.0.0.1" or "localhost")
                {
                    validPairs.Add((candidates[i], 0));
                    if (validPairs.Count >= maxValid)
                        return validPairs;
                    continue;
                }

                var indexId = Utils.GetGuid(false);
                profile.IndexId = indexId;
                testItems.Add(new ServerTestItem
                {
                    IndexId = indexId,
                    Address = profile.Address,
                    Port = profile.Port,
                    ConfigType = profile.ConfigType,
                    AllowTest = true,
                    QueueNum = i,
                    Profile = profile,
                    CoreType = AppManager.Instance.GetCoreType(profile, profile.ConfigType),
                });
            }
            catch { }
        }

        if (testItems.Count == 0)
            return validPairs;

        // 内核管理器还没初始化（应用启动早期）时起不了临时内核。
        // 不做任何 TCP 兜底——端口开着 ≠ 服务可用，裸 TCP 探测放行的
        // 正是「协议挂了/已过期」的死节点，这是入库节点大量失效的老根。
        // 放弃本轮，等下一轮或手动触发再验证。
        if (!CoreManager.Instance.IsInitialized)
        {
            await _updateFunc(false, "[警告] 内核尚未就绪，本轮跳过真实验证，未采用任何候选节点");
            Logging.SaveLog($"{_tag}: validation skipped, CoreManager not initialized ({testItems.Count} candidates dropped)");
            return validPairs;
        }

        // 按内核类型分组：sing-box 才支持 hysteria2 / tuic / anytls 等，
        // 而 LoadCoreConfigSpeedtest 只用「第一个节点」的内核类型决定整批配置。
        // 混批时不受支持的类型会被静默跳过（Port 保持远端值）→ 必然全部测不通，
        // 这正是「明明有可用节点却验证通过 0 个」的原因。
        // 同类型内再按 RealPingChunkSize 切块：万一某块配置仍被内核拒绝，
        // 损失上限就是那一块，而不是整批候选一起作废。
        // 不同内核类型的组互相独立（各起各的临时内核进程），并行执行互不干扰。
        var groupTasks = testItems
            .GroupBy(it => it.CoreType)
            .Select(coreGroup => Task.Run(async () =>
            {
                foreach (var chunk in coreGroup.Chunk(RealPingChunkSize))
                {
                    // 读不 lock 也没关系：少并行收一批只是提前收工，不影响正确性
                    if (validPairs.Count >= maxValid)
                        break;
                    await RunRealPingBatchAsync(chunk.ToList(), candidates, validPairs, maxValid);
                }
            }))
            .ToList();
        await Task.WhenAll(groupTasks);

        // 同一节点可能在并行组里重复出现：取各自的最小延迟，按延迟升序截断到配额。
        // 这样入库顺序就是「最快的先进组」。
        return validPairs
            .GroupBy(v => v.Link)
            .Select(g => g.OrderBy(v => v.Delay).First())
            .OrderBy(v => v.Delay)
            .Take(maxValid)
            .ToList();
    }

    /// <summary>
    /// 每个临时内核实例承载的候选节点数。
    /// 生成侧已经做了「单节点异常只跳过该节点」的隔离，块可以放大到 100——
    /// 内核起得更少、整体验证更快；单块出问题时再拆成单节点定位。
    /// </summary>
    private const int RealPingChunkSize = 100;

    /// <summary>
    ///     对一批「同一内核类型」的候选节点做真实延迟验证：
    ///     起一个临时内核，给每个节点分配独立的本地混合入站，
    ///     再通过各自的 SOCKS5 入站发真实 HTTP 请求测延迟。
    ///     只有 delay &gt; 0（请求真的穿透成功）才算通过，通过者带延迟记入 validPairs。
    /// </summary>
    private async Task RunRealPingBatchAsync(List<ServerTestItem> batch, List<string> candidates,
        List<(string Link, int Delay)> validPairs, int maxValid)
    {
        ProcessService processService = null;
        try
        {
            processService = await CoreManager.Instance.LoadCoreConfigSpeedtest(batch);
            if (processService is null)
            {
                // 核心起不来：整批（>1）时拆成单节点重试，把真正能用的救回来；
                // 单节点仍起不来就是该节点自身让配置无法生成，直接放弃。
                // 绝不做「端口开着就算通过」的 TCP 兜底——那等于跳过检测把死节点放进组。
                if (batch.Count > 1)
                {
                    await _updateFunc(false, $"[提示] 一批 {batch.Count} 个候选的配置无法启动，拆成单节点逐一验证");
                    foreach (var single in batch)
                    {
                        if (validPairs.Count >= maxValid)
                            break;
                        await RunRealPingBatchAsync([single], candidates, validPairs, maxValid);
                    }
                }
                else
                {
                    var link = candidates[batch[0].QueueNum];
                    await _updateFunc(false, $"[提示] 候选 {ExtractNodeName(link)} 无法生成可用配置，已跳过");
                }
                return;
            }

            // 等测速入站端口真正可连再开测。
            // 旧实现盲等 1.5 秒：慢机器上内核没就绪就开始测，整批误判超时；
            // 快机器上又白白等满 1.5 秒。
            await WaitForSpeedtestPorts(batch);

            // 通过各节点自己的本地 SOCKS5 入站发真实 HTTP 请求
            using var semaphore = new SemaphoreSlim(32);
            var tasks = batch.Select(async it =>
            {
                // 名额已满就不再发起新的测试，省掉无意义的等待
                if (validPairs.Count >= maxValid)
                {
                    return;
                }
                await semaphore.WaitAsync();
                try
                {
                    if (validPairs.Count >= maxValid)
                    {
                        return;
                    }
                    var webProxy = new WebProxy($"socks5://{Global.Loopback}:{it.Port}");
                    var delay = await ConnectionHandler.GetRealPingTime(webProxy, 5);
                    if (delay > 0)
                    {
                        lock (validPairs)
                        {
                            var link = candidates[it.QueueNum];
                            if (validPairs.Count < maxValid && !validPairs.Exists(v => v.Link == link))
                            {
                                validPairs.Add((link, delay));
                            }
                        }
                    }
                }
                catch { }
                finally { semaphore.Release(); }
            }).ToList();
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        finally
        {
            if (processService != null)
            {
                try
                { await processService.StopAsync(); }
                catch { }
            }
        }
    }

    /// <summary>
    /// 等临时内核的测速入站端口全部就绪（能建立 TCP 连接）。
    /// 端口 listen 后连接立即成功，通常几百毫秒内就绪；最坏 6 秒兜底，
    /// 避免内核卡死时验证流程挂着不走。
    /// </summary>
    private static async Task WaitForSpeedtestPorts(IEnumerable<ServerTestItem> batch, int timeoutMs = 6000)
    {
        var pending = batch.Select(it => it.Port).Where(p => p > 0).Distinct().ToList();
        if (pending.Count == 0)
        {
            return;
        }
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (pending.Count > 0 && DateTime.UtcNow < deadline)
        {
            foreach (var port in pending.ToArray())
            {
                try
                {
                    using var tcp = new TcpClient();
                    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
                    await tcp.ConnectAsync(Global.Loopback, port, cts.Token);
                    pending.Remove(port);
                }
                catch { }
            }
            if (pending.Count > 0)
            {
                await Task.Delay(100);
            }
        }
    }

    /// <summary>
    ///     对外暴露的真实延迟验证，供 AI 聊天窗口等调用。
    ///     只有能真正转发流量的节点才会返回；TCP 端口开着不算通过。
    /// </summary>
    public Task<List<string>> ValidateNodesAsync(List<string> candidates, int maxValid)
        => ValidateNodesViaRealPing(candidates, maxValid);

    /// <summary>
    /// 把 incoming 里「地址:端口」还没出现在 target 的节点并进 target。
    /// 不同源之间高度重叠（同几个仓库互相转发），按整条链接去重会漏掉
    /// 「同机器不同备注名」的重复，导致候选数量虚高、验证名额被浪费。
    /// </summary>
    private static void MergeByFingerprint(List<string> target, IEnumerable<string> incoming)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in target)
        {
            seen.Add(NodeFingerprint(n));
        }

        foreach (var n in incoming)
        {
            if (n.IsNullOrEmpty())
            {
                continue;
            }
            if (seen.Add(NodeFingerprint(n)))
            {
                target.Add(n);
            }
        }
    }

    /// <summary>
    /// Fisher-Yates 原地洗牌。候选池是按来源顺序拼接的，直接取前 N 个会
    /// 长期偏向头部来源；洗牌后再截断等价于对整个池子做均匀抽样。
    /// </summary>
    private static void Shuffle<T>(IList<T> list)
    {
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>
    /// 节点指纹：地址 + 端口。
    /// 同一个节点换个备注名（#后面的名字）或参数顺序不同，仍是同一个节点，
    /// 所以不能用整条链接做去重，否则每天都会把老节点当成新的重复加一遍。
    /// 解析失败时退化成整条链接比较——宁可少去重，也不能误判成重复而漏加。
    /// </summary>
    private static string NodeFingerprint(string link)
    {
        var raw = (link ?? string.Empty).Trim();
        if (raw.IsNullOrEmpty())
        {
            return string.Empty;
        }

        try
        {
            var p = FmtHandler.ResolveConfig(raw, out _);
            var addr = (p?.Address ?? string.Empty).Trim().ToLowerInvariant();
            if (p != null && !addr.IsNullOrEmpty() && p.Port > 0)
            {
                return $"{addr}:{p.Port}";
            }
        }
        catch
        {
            // 落到下面的 raw 比较
        }

        return "raw:" + raw;
    }

    /// <summary>组内已有节点的指纹集合，用于增量比对。</summary>
    public static async Task<HashSet<string>> GetExistingFingerprints(string subId)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var servers = await AppManager.Instance.ProfileItems(subId);
            if (servers == null)
            {
                return set;
            }

            foreach (var s in servers)
            {
                var addr = (s.Address ?? string.Empty).Trim().ToLowerInvariant();
                if (!addr.IsNullOrEmpty() && s.Port > 0)
                {
                    set.Add($"{addr}:{s.Port}");
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}: GetExistingFingerprints failed: {ex.Message}");
        }

        return set;
    }

    /// <summary>
    /// 只把「组内还没有」的节点追加进去，不清空原有节点。
    /// 返回 (实际入库数, 跳过的重复数)。没有新节点时返回 0，调用方据此提示
    /// 「无新增」并且完全不写数据库。
    /// </summary>
    public static async Task<(int Added, int Duplicated)> AddNewNodesToGroupAsync(Config config, string subId, List<string> validNodes)
    {
        if (subId.IsNullOrEmpty() || validNodes == null || validNodes.Count == 0)
        {
            return (0, 0);
        }

        var existing = await GetExistingFingerprints(subId);

        var newNodes = new List<string>();
        var duplicated = 0;
        foreach (var link in validNodes)
        {
            if (link.IsNullOrEmpty())
            {
                continue;
            }

            // Add 返回 false 表示指纹已存在 → 重复，跳过
            if (!existing.Add(NodeFingerprint(link)))
            {
                duplicated++;
                continue;
            }

            newNodes.Add(link.Trim());
        }

        if (newNodes.Count == 0)
        {
            return (0, duplicated);
        }

        var added = await ConfigHandler.AddServersToGroup(config, string.Join("\n", newNodes), subId);
        return (added > 0 ? added : 0, duplicated);
    }

    /// <summary>只查找 AI 分组，找不到返回 null，不创建。</summary>
    private async Task<string?> FindExistingAIGroup(string remarks)
    {
        var subItems = await AppManager.Instance.SubItems();
        var groupName = remarks.IsNullOrEmpty() ? "AI自动获取" : remarks;

        return subItems?.FirstOrDefault(s =>
            string.Equals(s.Remarks ?? string.Empty, groupName, StringComparison.OrdinalIgnoreCase) ||
            (s.Url ?? string.Empty).Contains("ai-auto", StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private async Task<string> GetOrCreateAISubscriptionGroup(string remarks)
    {
        var existingId = await FindExistingAIGroup(remarks);
        if (!existingId.IsNullOrEmpty())
        {
            return existingId;
        }

        var groupName = remarks.IsNullOrEmpty() ? "AI自动获取" : remarks;

        // Create new AI subscription group
        var newSub = new SubItem
        {
            Id = Utils.GetGuid(),
            Remarks = groupName,
            Url = "https://github.com/search?q=free+vpn+nodes&type=code",
            Enabled = true,
            AutoUpdateInterval = 0, // AI manages updates, not auto-update
            Sort = 0,
            Memo = "由AI自动搜索GitHub获取的免费VPN节点"
        };

        await ConfigHandler.AddSubItem(_config, newSub);
        return newSub.Id;
    }

    private string ExtractNodeName(string nodeLink)
    {
        try
        {
            if (nodeLink.Contains("#"))
            {
                var name = Uri.UnescapeDataString(nodeLink.Split('#').Last());
                return name.Length > 30 ? name[..30] + "..." : name;
            }

            // Try to extract from address
            if (nodeLink.Contains("@"))
            {
                var parts = nodeLink.Split('@');
                if (parts.Length > 1)
                {
                    var host = parts.Last().Split(':').First().Split('?').First();
                    return host.Length > 30 ? host[..30] + "..." : host;
                }
            }
        }
        catch { }

        return "unknown";
    }
}
