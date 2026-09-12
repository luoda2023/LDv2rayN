using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using ServiceLib.Models;
using ServiceLib.Services.AiApi;

namespace ServiceLib.ViewModels;

public partial class AIChatViewModel : MyReactiveObject
{
    private static readonly string _tag = "AIChatViewModel";

    private static readonly object _chatTraceLock = new();

    // Full system prompt sent to the model, hoisted so it can be echoed
    // verbatim to ai_chat_trace.txt for diagnosing garbled replies.
    private const string AiSystemPrompt = @"你是嵌入在 LDv2rayN 客户端中的 AI 节点猎手助手（模型 hermesAPI）。用户在 Windows 桌面软件的聊天窗口里跟你对话。

## 你的职责
帮用户找到**真实可用**的免费代理节点，并保证入库的每一个节点都通过真实连通性验证。你有一套全自动能力，不需要用户手把手操作。

## 你认识的节点格式（本地解析器自动识别，无需用户整理格式）
1. 分享链接：vmess:// vless:// trojan:// ss:// ssr:// hy2:// hysteria2:// tuic:// anytls:// naive:// socks:// wireguard://
   - 链接混在 HTML/Markdown/JSON/代码块里、前面带说明文字、一行多个，都能扫出来
2. 整段 Base64 订阅（最常见形态），以及每行一个 Base64
3. Clash/mihomo YAML（proxies: 列表）——会自动转成标准链接
4. sing-box JSON（outbounds 列表）
5. HTML 实体转义过的链接（&amp; 等）自动还原
6. 节点链接合法性判定要点（你判断内容质量时参考）：
   - vless+reality 必须带 pbk= 公钥参数，缺 pbk 的 reality 链接是废的
   - security=tls 且 insecure=1 且无证书指纹的节点可能被新版 Xray 拒绝
   - 节点=地址+端口，同一个「地址:端口」重复出现只是同一台机器

## 你的固定采集地点（可信来源，用户让你找节点时优先从这里抓）
每日定时采集已固定抓取这些站点；聊天里用户说「采集」「找节点」时你会执行同样的闭环：
- GitHub 精选仓库 + 实时搜索（20+ 仓库，每次搜索最新更新的）
- **DuckDuckGo 全网搜索**：不限 GitHub，还能搜到博客、聚合站、论坛帖等新鲜来源
- 网页聚合站：clashsuburl.com、clashstair.com、v2rayshare.com、v2cross.com、mibei77.com、end-gfw.com、vpngate.net
- GitHub raw 订阅直链：V2RayAggregator、Pawdroid/Free-servers、ripaojiedian/freenode、barry-far/V2ray-Configs 等 15+ 条
- 用户粘贴的任何 URL / 订阅 / 节点链接

## 你的标准工作流（每一步都自动完成）
抓取（直连+镜像竞速）→ 提取（上述全部格式）→ 去重（按地址:端口指纹）→ **真实延迟验证**（起临时内核，给每个节点发真实 HTTP 请求，端口通≠可用）→ **只有验证通过的节点才加入分组** → 向用户报告「通过数/总数、最快延迟」。
失败处理：如果全部没通过，告诉用户可能原因，并提示可以回复「导入」跳过验证强插（用户明确选择才执行）。

## 你的自主性规则
1. 用户说「找节点」「采集」「来几个节点」「抓取」之类，直接开始自动采集，不要反问。
2. 用户粘贴内容时（无论多乱），直接走分析流程；处理完报告结果。
3. 每天定时任务会自动采集+清理失效节点，用户问「什么时候采集」时说明：每天 03:00 自动运行。
4. 入库永远只放验证通过的节点——宁可汇报「0 个通过」，也不放没验证的节点进分组。

## 可用命令（用户在对话框输入即可，你也可以主动提示）
「列表」查节点、「分组」查分组、「状态」查应用状态、「切换 xxx」切换分组、「删除 xxx」删除节点、「添加 <链接>」添加节点、「测试 <链接>」测活、「代理 clear/set」切换系统代理、「采集」开始全自动找节点。

## 回答要求
1. 用中文回复，简洁但有帮助。
2. 当用户的问题可以通过上述命令完成时，主动告诉用户「你可以在对话框输入 XXX」。
3. 涉及节点推荐时，基于验证结果说话：只有验证通过的节点才推荐给用户连接。
4. 当用户粘贴了一个 URL 或节点链接，程序会自动处理，你不要重复处理，只需要确认。
5. 支持 Markdown 格式（**粗体**、`code`、列表）。";

    // Append a multi-line block verbatim to the plain-text chat trace.
    private static void TraceWire(string title, string text)
    {
        try
        {
            lock (_chatTraceLock)
            {
                var dir = Utils.StartupPath();
                if (dir.IsNullOrEmpty())
                    return;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, "ai_chat_trace.txt");
                File.AppendAllText(file, "\r\n=== " + title + " ===\r\n" + text + "\r\n");
            }
        }
        catch { }
    }
    // 注意：本窗口是「常驻复用」的浮动窗（关闭按钮只 Hide，不销毁），
    // 不走 WindowDialog 的模态通道，因此不实现 ICloseable。
    // 之前挂了个 ICloseable.RequestClose 但没有任何地方订阅，只是留了个空事件。

    [Reactive] public partial string ChatInput { get; set; } = string.Empty;
    [Reactive] public partial bool IsProcessing { get; set; }
    [Reactive] public partial string TargetGroup { get; set; } = "AI自动获取";
    [Reactive] public partial int MaxNodes { get; set; } = 50;
    [Reactive] public partial bool AutoTest { get; set; } = true;
    [Reactive] public partial bool AutoCrawlEnabled { get; set; }
    [Reactive] public partial int AutoCrawlIntervalMinutes { get; set; } = 3;
    [Reactive] public partial bool IsRepoListVisible { get; set; }

    /// <summary>
    /// 连通性测试全部失败时暂存的候选节点。用户回复「导入」即可跳过验证直接入库，
    /// 避免「提取到 N 个、验证通过 0 个」时前功尽弃。
    /// </summary>
    private List<string>? _pendingImport;

    /// <summary>Observable collection of chat messages for the UI</summary>
    public ObservableCollection<AIChatMessage> Messages { get; } = new();

    /// <summary>Observable collection of GitHub repositories for user selection</summary>
    public ObservableCollection<GitHubRepoItem> GitHubRepos { get; } = new();

    public AIChatViewModel()
    {
        var aiConfig = _config.AIConfigItem ?? new AIConfigItem();
        TargetGroup = aiConfig.AiGroupRemarks ?? "AI自动获取";
        MaxNodes = aiConfig.MaxNodesPerSearch;
        AutoCrawlEnabled = aiConfig.AutoCrawlEnabled;
        AutoCrawlIntervalMinutes = aiConfig.AutoCrawlIntervalMinutes;

        // Show welcome message
        AddMessage(AIChatRole.AI, @"**欢迎使用 AI智能代理助手** 🤖

我可以帮你完成以下任务：

🔗 **分析链接** — 粘贴一个URL（GitHub仓库、订阅链接、节点分享页面等），我会自动下载内容、提取VPN节点、验证可用性，然后添加到你的分组中。

📋 **批量导入** — 直接粘贴一批节点链接（每行一个），我会自动识别、逐个验证后导入到指定分组。支持Base64编码的订阅内容。

🔍 **自主找节点** — 点击「自动搜索」或直接输入 `采集` / `找节点`，我会自动跑完整闭环：抓取固定来源（GitHub 精选仓库 + 免费节点聚合站）→ 提取全部格式 → 逐个真实延迟验证 → **只把验证通过的节点**导入分组，并汇报通过数和最快延迟。

📦 **选择性导入** — 点击「搜索仓库」按钮，我会搜索GitHub上的免费节点仓库并列出清单，你可以勾选要导入的仓库，然后点击「导入所选」。

🗣️ **自由问答** — 任何与VPN/代理/网络相关的问题都可以直接问我（由 hermesAPI 回答）。

🎛️ **对话式控制** — 直接在对话框输入关键词就能操作程序：
- 「列」 / 「分组」 / 「状态」 — 查询
- 「切换 xxx」 — 切换分组
- 「删除 xxx」 / 「添加 <链接>」 / 「测试 <链接>」
- 「代理 clear」 — 关闭系统代理

💡 **使用方法**：在下方输入框粘贴链接、节点或问题后按回车。

支持的节点协议：`vmess://` `vless://` `trojan://` `ss://` `hy2://` `tuic://`");
    }

    /// <summary>Toggle periodic auto-crawl on/off</summary>
    public void ToggleAutoCrawl()
    {
        _config.AIConfigItem ??= new AIConfigItem();
        _config.AIConfigItem.AutoCrawlEnabled = AutoCrawlEnabled;
        _config.AIConfigItem.AutoCrawlIntervalMinutes = AutoCrawlIntervalMinutes;
        _ = ConfigHandler.SaveConfig(_config);

        if (AutoCrawlEnabled)
        {
            AISchedulerService.Restart(_config);
            AddMessage(AIChatRole.System, $"已开启每日定时爬取，每天 {AutoCrawlIntervalMinutes:00}:00 自动搜索免费节点并清理失效节点");
        }
        else
        {
            AISchedulerService.Stop();
            AddMessage(AIChatRole.System, "已关闭每日定时爬取");
        }
    }

    /// <summary>Search GitHub for free node repositories and show selection list</summary>
    public async Task SearchGitHubReposAsync()
    {
        if (IsProcessing)
        {
            return;
        }

        IsProcessing = true;
        GitHubRepos.Clear();
        IsRepoListVisible = true;

        try
        {
            AddMessage(AIChatRole.User, "🔍 搜索GitHub免费节点仓库");
            AddMessage(AIChatRole.AI, "正在搜索GitHub上的免费节点仓库...\n\n请稍候，我会列出找到的仓库供你选择。");

            var aiConfig = _config.AIConfigItem ?? new AIConfigItem();

            // Search queries for free VPN nodes
            string[] searchQueries =
            {
                "free v2ray nodes",
                "free vless",
                "free clash subscription",
                "free trojan",
                "free hysteria2",
                "v2ray free subscribe",
                "free vpn subscription github",
            };

            using var searchClient = new HttpClient();
            searchClient.Timeout = TimeSpan.FromSeconds(10);
            searchClient.DefaultRequestHeaders.UserAgent.ParseAdd("LDv2rayN/1.0 (github-search)");
            searchClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");

            var repos = new List<(string Owner, string Repo, string Description, int Stars)>();
            var repoLock = new object();

            // Parallel GitHub repo search
            using var searchSemaphore = new SemaphoreSlim(3);
            var searchTasks = searchQueries.Select(async query =>
            {
                await searchSemaphore.WaitAsync();
                try
                {
                    if (repos.Count >= 20)
                        return;
                    var url = $"https://api.github.com/search/repositories?q={Uri.EscapeDataString(query)}&sort=updated&order=desc&per_page=5";
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

                        var description = item.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "";
                        var stars = item.TryGetProperty("stargazers_count", out var st) ? st.GetInt32() : 0;

                        var pair = (parts[0], parts[1], description, stars);
                        lock (repoLock)
                        {
                            if (!repos.Contains(pair) && repos.Count < 20)
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

            // Add to observable collection
            foreach (var (owner, repo, description, stars) in repos)
            {
                GitHubRepos.Add(new GitHubRepoItem
                {
                    Owner = owner,
                    Repo = repo,
                    Description = description.Length > 80 ? description[..80] + "..." : description,
                    Stars = stars,
                    IsSelected = true, // Default selected
                    Url = $"https://github.com/{owner}/{repo}"
                });
            }

            if (GitHubRepos.Count == 0)
            {
                AddMessage(AIChatRole.AI, "[警告]️ 未找到免费节点仓库。\n\n可能原因：\n- GitHub API限制\n- 网络连接问题\n\n建议稍后重试。");
                IsRepoListVisible = false;
            }
            else
            {
                AddMessage(AIChatRole.AI, $"[成功] 找到 **{GitHubRepos.Count}** 个仓库\n\n请在下方列表中勾选要导入的仓库，然后点击「导入所选」按钮。");
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            AddMessage(AIChatRole.AI, $"[失败] 搜索过程中出现错误：\n\n`{ex.Message}`");
            IsRepoListVisible = false;
        }
        finally
        {
            IsProcessing = false;
        }
    }

    /// <summary>Import nodes from selected GitHub repositories</summary>
    public async Task ImportSelectedReposAsync()
    {
        if (IsProcessing)
        {
            return;
        }

        var selectedRepos = GitHubRepos.Where(r => r.IsSelected).ToList();
        if (selectedRepos.Count == 0)
        {
            AddMessage(AIChatRole.AI, "[警告]️ 请先勾选要导入的仓库。");
            return;
        }

        IsProcessing = true;
        IsRepoListVisible = false;

        try
        {
            AddMessage(AIChatRole.User, $"📦 导入 {selectedRepos.Count} 个选中仓库的节点");
            AddMessage(AIChatRole.AI, $"开始从 {selectedRepos.Count} 个仓库获取节点...\n\n这可能需要几分钟时间，请耐心等待。");

            var aiConfig = _config.AIConfigItem ?? new AIConfigItem();
            if (!aiConfig.Enabled || aiConfig.ApiUrl.IsNullOrEmpty())
            {
                AddMessage(AIChatRole.AI, "[失败] **AI功能未启用**\n\n请先在「设置 → AI智能获取设置」中配置API地址和密钥。");
                return;
            }

            // Override settings
            aiConfig.AiGroupRemarks = TargetGroup;
            aiConfig.MaxNodesPerSearch = MaxNodes;
            _config.AIConfigItem = aiConfig;

            var allNodes = new List<string>();
            var fetchLock = new object();

            // Parallel fetch from all selected repos
            using var fetchSemaphore = new SemaphoreSlim(5);
            var fetchTasks = selectedRepos.Select(async repo =>
            {
                await fetchSemaphore.WaitAsync();
                try
                {
                    AddMessage(AIChatRole.AI, $"📥 正在从 {repo.Owner}/{repo.Repo} 获取节点...");

                    var nodes = await FetchNodesFromGitHubRepo(repo.Owner, repo.Repo);
                    if (nodes != null && nodes.Count > 0)
                    {
                        lock (fetchLock)
                        {
                            allNodes.AddRange(nodes);
                        }
                        AddMessage(AIChatRole.AI, $"[成功] 从 {repo.Owner}/{repo.Repo} 获取到 {nodes.Count} 个节点");
                    }
                    else
                    {
                        AddMessage(AIChatRole.AI, $"[警告]️ 从 {repo.Owner}/{repo.Repo} 未获取到节点");
                    }
                }
                catch (Exception ex)
                {
                    Logging.SaveLog($"{_tag}: fetch from {repo.Owner}/{repo.Repo} failed: {ex.Message}");
                    AddMessage(AIChatRole.AI, $"[失败] 从 {repo.Owner}/{repo.Repo} 获取失败：{ex.Message}");
                }
                finally
                {
                    fetchSemaphore.Release();
                }
            });
            await Task.WhenAll(fetchTasks);

            if (allNodes.Count == 0)
            {
                AddMessage(AIChatRole.AI, "[警告]️ 未从任何仓库获取到节点。\n\n建议尝试其他仓库或稍后重试。");
                return;
            }

            // Deduplicate
            allNodes = allNodes.Distinct().ToList();
            AddMessage(AIChatRole.AI, $"🔍 共获取到 **{allNodes.Count}** 个不重复节点，开始验证...");

            // 真实延迟验证：起临时内核，逐节点走各自 SOCKS5 入站发真实 HTTP 请求。
            // TCP 端口连通不算通过——否则「端口开着但代理早已失效」的节点会大量混入。
            var validSet = await ValidateRealAsync(allNodes);
            var validNodes = allNodes.Where(n => validSet.Contains(n)).ToList();

            // Cap at MaxNodes
            if (validNodes.Count > MaxNodes)
            {
                validNodes = validNodes.Take(MaxNodes).ToList();
            }

            if (validNodes.Count == 0)
            {
                AddMessage(AIChatRole.AI, "[警告]️ 所有节点验证均失败。\n\n建议稍后重试或尝试其他仓库。");
                return;
            }

            // Add valid nodes to target group（增量：只补新节点，不清空组内已有节点）
            AddMessage(AIChatRole.AI, $"📦 正在将 {validNodes.Count} 个有效节点添加到「{TargetGroup}」分组...");

            var subId = await GetOrCreateAISubscriptionGroup(TargetGroup);
            var (addedCount, dupCount) = await AIFetchService.AddNewNodesToGroupAsync(_config, subId, validNodes);

            AddMessage(AIChatRole.AI,
                addedCount > 0
                    ? $"🎉 **完成！** 成功添加 **{addedCount}** 个有效节点到「**{TargetGroup}**」分组（跳过已存在的 {dupCount} 个）。\n\n你可以在主界面的分组列表中查看和使用这些节点。"
                    : $"ℹ️ **没有新节点。** 本次 {validNodes.Count} 个可用节点在「**{TargetGroup}**」分组里已经全部存在，未做改动。");
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            AddMessage(AIChatRole.AI, $"[失败] 导入过程中出现错误：\n\n`{ex.Message}`");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    /// <summary>Fetch nodes from a specific GitHub repository</summary>
    private async Task<List<string>?> FetchNodesFromGitHubRepo(string owner, string repo)
    {
        var nodes = new List<string>();

        // Common file paths that might contain nodes
        string[] candidatePaths =
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
            "Z.txt",
            "singapore.txt",
            "output/singapore.txt",
            "end-gfw-together-ss",
            "server.txt",
            "all/configs.txt",
            "Countries/USA.txt",
            "Countries/Germany.txt",
            "Countries/UK.txt",
            "Countries/Japan.txt",
            "Countries/Singapore.txt",
            "Countries/Hong Kong.txt",
        };

        foreach (var p in candidatePaths)
        {
            try
            {
                var rawUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/HEAD/{p}";
                var fetched = await FetchWithMirrorFast(rawUrl);
                if (fetched != null && fetched.Count > 0)
                {
                    nodes.AddRange(fetched);
                    if (nodes.Count >= 100)
                        break; // Stop after enough
                }
            }
            catch { }
        }

        return nodes.Count > 0 ? nodes.Distinct().ToList() : null;
    }

    /// <summary>Fetch a raw.githubusercontent.com URL with mirror fallback</summary>
    private async Task<List<string>?> FetchWithMirrorFast(string rawUrl)
    {
        string[] Mirrors =
        {
            "https://ghfast.top/",
            "https://gh-proxy.com/",
            "https://ghproxy.net/",
        };

        var attempts = new List<string> { rawUrl };
        attempts.AddRange(Mirrors.Select(m => m + rawUrl));

        foreach (var url in attempts)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(8);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                var resp = await client.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                    continue;
                var text = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(text) || text.Length > 2_000_000)
                    continue;

                var nodes = ParseNodesFromText(text);
                if (nodes.Count == 0 && text.Trim().Length > 40)
                {
                    try
                    {
                        var cleaned = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
                        var decodedBytes = Convert.FromBase64String(cleaned);
                        var decoded = System.Text.Encoding.UTF8.GetString(decodedBytes);
                        nodes = ParseNodesFromText(decoded);
                    }
                    catch { }
                }

                if (nodes.Count > 0)
                {
                    return nodes;
                }
            }
            catch
            {
                // Try next mirror
            }
        }

        return null;
    }

    /// <summary>Analyze a user-submitted URL: download → extract nodes → test → add to group</summary>
    /// <summary>
    /// 上一批任务还在处理时给用户的可见反馈。
    /// 老实现直接静默 return——用户粘贴一大段节点后毫无反应，
    /// 会以为「粘贴没被识别」，实际是上一次验证还在跑（一批要 1~2 分钟）。
    /// </summary>
    public void NotifyBusySend()
    {
        AddMessage(AIChatRole.System,
            "⏳ 上一批任务还在处理中（真实延迟验证一批通常需要 1~2 分钟），请等它完成后再发送。");
    }

    public async Task AnalyzeUrlAsync()
    {
        if (string.IsNullOrWhiteSpace(ChatInput))
        {
            return;
        }
        if (IsProcessing)
        {
            NotifyBusySend();
            ChatInput = string.Empty;
            return;
        }

        var raw = ChatInput.Trim();
        ChatInput = string.Empty;
        IsProcessing = true;

        // Fast path 0: 上一步「跳过验证直接导入」的确认
        if (_pendingImport is { Count: > 0 } && IsConfirmWord(raw))
        {
            AddMessage(AIChatRole.User, $"💬 {raw}");
            try
            { await ImportPendingAsync(); }
            finally { IsProcessing = false; }
            return;
        }

        // 采集请求识别：句中「任意位置」出现节点链接 / HTTP 地址都算。
        // 老写法只认「整句以 http 开头」，用户说「帮我采集 https://xxx」时整句会被
        // 当成闲聊丢给 AI —— AI 回一段文字了事，节点一个都没采。这是"有时不行"的主因。
        var embeddedUrls = ExtractHttpUrls(raw);
        var hasNodeLink = Array.Exists(NodeLinkPrefixes,
            p => raw.Contains(p, StringComparison.OrdinalIgnoreCase));
        var isDirectNodeLink = Array.Exists(NodeLinkPrefixes,
            p => raw.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        var isHttpUrl = raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        // Fast path 1: local command router（句中含链接时不抢，交给采集流程处理）
        if (!hasNodeLink && embeddedUrls.Count == 0)
        {
            var cmd = TryMatchLocalCommand(raw);
            if (cmd is not null)
            {
                AddMessage(AIChatRole.User, $"💬 {raw}");
                try
                { await ExecuteLocalCommandAsync(cmd, raw); }
                finally { IsProcessing = false; }
                return;
            }
        }

        // Fast path 2: free-form question — always reset IsProcessing so a
        // follow-up message is never swallowed by a stale busy flag.
        if (!isDirectNodeLink && !isHttpUrl && !hasNodeLink && embeddedUrls.Count == 0)
        {
            try
            {
                AddMessage(AIChatRole.User, $"💬 {raw}");
                var aiConfig = _config.AIConfigItem ?? new AIConfigItem();
                if (!aiConfig.Enabled || aiConfig.ApiUrl.IsNullOrEmpty())
                {
                    AddMessage(AIChatRole.AI, "[失败] **AI功能未启用**\n\n请先在「设置 → AI智能获取设置」中配置API地址和密钥。");
                    return;
                }
                var answer = await AskHermesAsync(aiConfig, raw);
                AddMessage(AIChatRole.AI, answer ?? "[失败] AI 未返回任何内容。");
            }
            finally
            {
                IsProcessing = false;
            }
            return;
        }

        AddMessage(AIChatRole.User, $"🔗 分析这个内容：{(raw.Length > 200 ? raw[..200] + "..." : raw)}");

        try
        {
            var aiConfig = _config.AIConfigItem ?? new AIConfigItem();
            var aiReady = aiConfig.Enabled && !aiConfig.ApiUrl.IsNullOrEmpty();

            // 采集节点靠的是本地解析（正则 / HTML实体 / Base64 / URL解码），AI 只是兜底。
            // 以前把「AI 已配置」当成采集的前置条件，没配 AI 就直接报失败——
            // 这是纯粹的人为阻断：明明本地能解析出来，却连试都不让试。已去掉。
            if (!aiReady)
            {
                AddMessage(AIChatRole.AI,
                    "ℹ️ 未启用 AI，本次只用**本地解析**。若本地取不到，可到「设置 → AI智能获取设置」配置后让 AI 兜底分析。");
            }

            // 1) 直接粘贴的节点（也可能是 Base64 订阅内容）
            var directNodes = ParseNodesFromText(raw);
            if (directNodes.Count == 0 && Utils.IsBase64String(raw))
            {
                directNodes = ParseNodesFromText(Utils.Base64Decode(raw));
            }
            if (directNodes.Count == 0)
            {
                directNodes = AiUrlFetchService.ExtractNodes(raw);
            }

            List<string> nodes;
            if (directNodes.Count > 0)
            {
                nodes = directNodes;
                AddMessage(AIChatRole.AI, $"📋 检测到 **{nodes.Count}** 个直接粘贴的节点链接，跳过下载步骤，直接验证...");
            }
            else
            {
                // 一次可能给了多个地址（句中任意位置的 URL 都能抽出来）
                var urlList = embeddedUrls.Count > 0 ? embeddedUrls : SplitUrls(raw);
                if (urlList.Count == 0)
                {
                    urlList.Add(raw);
                }

                var merged = new List<string>();
                var failedUrls = new List<string>();

                foreach (var one in urlList.Take(5))
                {
                    AddMessage(AIChatRole.AI, $"🔍 正在抓取：{ShortUrl(one)}");

                    // 抓取：自动重定向 + 自动解压 + 重试 + GitHub 镜像 + 系统代理/直连双通道
                    var fetch = await AiUrlFetchService.FetchAsync(one);
                    if (!fetch.Success)
                    {
                        failedUrls.Add($"{ShortUrl(one)} — {fetch.Error}");
                        continue;
                    }

                    AddMessage(AIChatRole.AI, $"📥 已下载 {fetch.Content.Length:N0} 字符，正在提取节点...");

                    var extracted = await ExtractNodesFromContent(aiConfig, fetch.Content, one);
                    if (extracted is { Count: > 0 })
                    {
                        merged.AddRange(extracted);
                    }
                }

                if (merged.Count == 0)
                {
                    var detail = failedUrls.Count > 0
                        ? "\n\n**失败明细**：\n- " + string.Join("\n- ", failedUrls)
                        : string.Empty;
                    AddMessage(AIChatRole.AI,
                        "[失败] 没能从这个链接取到节点。\n\n" +
                        "已尝试：直连、绕过系统代理、GitHub 镜像；提取时覆盖了明文、HTML 实体、多层 Base64、URL 解码。"
                        + detail +
                        "\n\n可以试试：\n- 先在浏览器打开，确认链接能访问且内容里有节点\n- 把订阅内容整段粘贴进来（支持 Base64）\n- 换一个订阅地址");
                    return;
                }

                nodes = merged.Distinct().ToList();
                AddMessage(AIChatRole.AI, $"🔍 共提取到 **{nodes.Count}** 个候选节点");
            }

            // Cap the candidate pool
            var pool = nodes.Count > MaxNodes * 3
                ? nodes.Take(MaxNodes * 3).ToList()
                : nodes;
            if (pool.Count > 150)
                pool = pool.Take(150).ToList();
            AddMessage(AIChatRole.AI, $"🧪 开始验证前 **{pool.Count}** 个节点（起临时内核，逐节点发真实请求测延迟）...");

            // Step 3: 真实延迟验证（起临时内核逐节点发真实 HTTP 请求，不再做 TCP 端口探测）
            var results = new List<AIChatNodeResult>();
            var validSet = await ValidateRealAsync(pool);
            var validNodes = pool.Where(n => validSet.Contains(n)).ToList();

            foreach (var nodeLink in pool)
            {
                var passed = validSet.Contains(nodeLink);
                results.Add(new AIChatNodeResult
                {
                    NodeLink = nodeLink,
                    DisplayName = ExtractNodeName(nodeLink),
                    Protocol = ExtractProtocol(nodeLink),
                    Address = ExtractAddress(nodeLink),
                    Status = passed ? AIChatNodeStatus.Passed : AIChatNodeStatus.Failed,
                    StatusText = passed ? "[成功] 验证通过" : "[失败] 验证失败"
                });
            }

            // Restore original order
            var orderMap = new Dictionary<string, int>(pool.Count);
            for (int i = 0; i < pool.Count; i++)
                orderMap[pool[i]] = i;
            results = results.OrderBy(r => orderMap.TryGetValue(r.NodeLink, out var oi) ? oi : int.MaxValue).ToList();

            // Cap at MaxNodes
            if (validNodes.Count > MaxNodes)
            {
                validNodes = validNodes.Take(MaxNodes).ToList();
            }

            // Show node results
            AddNodeResultMessage(results, validNodes.Count);

            if (validNodes.Count == 0)
            {
                // 节点格式合法但当前网络下不可达 —— 给用户保留「跳过验证直接导入」的退路，
                // 而不是让他白等一轮采集。
                _pendingImport = pool;
                AddMessage(AIChatRole.AI,
                    $"[警告]️ {pool.Count} 个节点都没通过连通性测试（直连和本地代理都试过）。\n\n" +
                    "这些节点**格式是合法的**，失败通常是因为：\n" +
                    "- 节点本身已失效，或所在机房暂时不通\n" +
                    "- 软件当前没有开启可用的代理，直连出不去\n\n" +
                    $"要跳过验证直接导入这 {pool.Count} 个节点吗？回复「**导入**」即可加入「{TargetGroup}」分组" +
                    "（导入后可自行批量测速筛选）。");
                return;
            }

            // Step 4: Add valid nodes to target group（增量：只补新节点，不清空组内已有节点）
            AddMessage(AIChatRole.AI, $"📦 正在将 {validNodes.Count} 个有效节点添加到「{TargetGroup}」分组...");

            var subId = await GetOrCreateAISubscriptionGroup(TargetGroup);
            var (addedCount, dupCount) = await AIFetchService.AddNewNodesToGroupAsync(_config, subId, validNodes);

            AddMessage(AIChatRole.AI,
                addedCount > 0
                    ? $"🎉 **完成！** 成功添加 **{addedCount}** 个有效节点到「**{TargetGroup}**」分组（跳过已存在的 {dupCount} 个）。\n\n你可以在主界面的分组列表中查看和使用这些节点。"
                    : $"ℹ️ **没有新节点。** 本次 {validNodes.Count} 个可用节点在「**{TargetGroup}**」分组里已经全部存在，未做改动。");
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            AddMessage(AIChatRole.AI, $"[失败] 分析过程中出现错误：\n\n`{ex.Message}`");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    /// <summary>Auto-search GitHub for free VPN nodes</summary>
    public async Task AutoSearchAsync()
    {
        if (IsProcessing)
        {
            return;
        }

        IsProcessing = true;
        AddMessage(AIChatRole.User, "🤖 启动自动搜索模式");

        try
        {
            var aiConfig = _config.AIConfigItem ?? new AIConfigItem();
            if (!aiConfig.Enabled || aiConfig.ApiUrl.IsNullOrEmpty())
            {
                AddMessage(AIChatRole.AI, "[失败] **AI功能未启用**\n\n请先在「设置 → AI智能获取设置」中配置API地址和密钥。");
                return;
            }

            // Override settings
            aiConfig.AiGroupRemarks = TargetGroup;
            aiConfig.MaxNodesPerSearch = MaxNodes;
            _config.AIConfigItem = aiConfig;

            AddMessage(AIChatRole.AI, "🔍 正在搜索GitHub上的免费VPN节点...\n\n我会搜索以下关键词：\n- `free v2ray nodes`\n- `free clash proxy`\n- `free vpn subscription github`\n- `v2ray free share`\n- `free hysteria2 nodes`");

            // 手动搜索入口绕过调度器，需要主动通知主窗口的 AI 图标动画
            AISchedulerService.NotifyBusy(true);

            var aiService = new AIFetchService(_config, async (success, msg) =>
            {
                AddMessage(AIChatRole.AI, msg);
                await Task.CompletedTask;
            });

            // 手动入口同样先清失效再补新的，跟每天自动采集保持一致
            var (result, cleaned) = await aiService.RunFullCycleAsync();
            if (result > 0)
            {
                AddMessage(AIChatRole.AI, $"[成功] **自动搜索完成！**\n\n共添加 **{result}** 个有效节点到「**{TargetGroup}**」分组。" + (cleaned > 0 ? $"\n\n同时清理了 **{cleaned}** 个已失效的节点。" : "") + "\n\n💡 你可以点击「自动搜索」按钮定期刷新，获取最新节点。");
            }
            else
            {
                AddMessage(AIChatRole.AI, (cleaned > 0
                ? $"[完成] 本次没有新的有效节点可添加，但清理了 **{cleaned}** 个已失效的节点。\n\n"
                : "[警告]️ 本次搜索未找到有效节点。\n\n")
                + "可能原因：\n- GitHub上暂时没有新的免费节点分享\n- 新抓到的节点没通过真实连通性验证\n- 网络连接问题\n\n建议稍后重试。");
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            AddMessage(AIChatRole.AI, $"[失败] 搜索过程中出现错误：\n\n`{ex.Message}`");
        }
        finally
        {
            IsProcessing = false;
            AISchedulerService.NotifyBusy(false, "aichat-fetch");
        }
    }

    #region Message Helpers

    private void AddMessage(AIChatRole role, string content)
    {
        Messages.Add(new AIChatMessage
        {
            Role = role,
            Content = content,
            Timestamp = DateTime.Now
        });
    }

    private void AddNodeResultMessage(List<AIChatNodeResult> results, int passedCount)
    {
        var lastMsg = Messages.LastOrDefault();
        // Render at most 30 rows in the bubble
        var shown = results.Count > 30 ? results.Take(30).ToList() : results;
        Messages.Add(new AIChatMessage
        {
            Role = AIChatRole.System,
            Content = $"节点验证结果：{passedCount}/{results.Count} 通过" + (results.Count > 30 ? $"（显示前 30 条）" : ""),
            Timestamp = DateTime.Now,
            NodeResults = shown
        });
    }

    #endregion

    #region Local Command Router

    private sealed class LocalCommand
    {
        public string Name = string.Empty;
        public JsonElement Args = default;
    }

    private static readonly string[] NodeLinkPrefixes =
    {
        "vmess://", "vless://", "trojan://", "ss://", "hy2://", "hysteria2://",
        "tuic://", "socks://", "socks5://", "wireguard://", "anytls://",
        "naive://", "naive+https://", "naive+quic://",
    };

    /// <summary>
    /// Recognise a chat line as a local command. Return null when the input is
    /// a URL or raw text that should go through the normal analyse pipeline.
    /// </summary>
    private static LocalCommand? TryMatchLocalCommand(string raw)
    {
        // A URL or direct node link — normal analyse path
        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            Array.Exists(NodeLinkPrefixes, p => raw.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return null;

        // 知识型问句保护：「怎么找节点」「为什么连不上」「什么是reality」这类是
        // 在提问，不是在下达指令——交给模型回答（现在注入了实时状态，它答得了）。
        // 明确的祈使句（帮我/给我开头）仍走指令。
        var isQuestion = ContainsAny(raw, "怎么", "如何", "为什么", "什么是", "是什么", "什么意思", "哪些", "哪个", "什么", "吗", "么区别", "介绍", "哪里");
        var isImperative = ContainsAny(raw, "帮我", "给我", "请", "开始", "立刻", "马上");
        if (isQuestion && !isImperative)
        {
            return null;
        }

        // 自主找节点指令：用户说「采集 / 找节点 / 抓节点 / 自动搜索 / 搜索」，
        // 直接触发全自动闭环（抓固定来源 → 提取 → 真实验证 → 只入库有效节点）。
        // 这是 AI 自主性的主入口：不用用户点按钮，一句话开工。
        // 动词放宽：「找几个节点」「来几个节点」这类不连续表述也要命中；
        // 但「看看有哪些节点」这类纯查询不带动作动词，仍走「列表」查询。
        if (raw.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
            Array.Exists(NodeLinkPrefixes, p => raw.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            // 句中带链接/节点 → 走分析管线（本地解析+验证），不触发全量搜索
            return null;
        }

        if (ContainsAny(raw, "采集", "找节点", "抓节点", "自动搜索", "搜索", "autosearch", "auto search"))
        {
            return new LocalCommand { Name = "autoSearch" };
        }
        // 「节点+动作动词」的宽匹配（找几个/来几个/搞点）：
        // 必须排除已被其他指令专用的动词（测试/删除/添加/切换），否则会抢走
        // 「测试 这个节点」「删除 旧节点」这类指令——这是真实路由 bug。
        if (raw.Contains("节点", StringComparison.Ordinal)
            && !raw.StartsWith("测试", StringComparison.Ordinal)
            && !raw.StartsWith("删除", StringComparison.Ordinal)
            && !raw.StartsWith("添加", StringComparison.Ordinal)
            && !raw.StartsWith("切换", StringComparison.Ordinal)
            && !raw.StartsWith("test", StringComparison.OrdinalIgnoreCase)
            && !raw.StartsWith("delete", StringComparison.OrdinalIgnoreCase)
            && ContainsAny(raw, "找", "来", "抓", "搜", "采", "搞", "弄", "要", "给"))
        {
            return new LocalCommand { Name = "autoSearch" };
        }

        // Chinese + English command vocabulary
        var lsName = "servers";
        string? lsArg = null;
        // 注意：第二个参数既是英文匹配词、也会被当成命令名返回，
        // 必须与 AiCapabilityRegistry 里的能力名一致（实测「list」会报未找到命令）。
        if (TryKeyword(raw, "列表", "servers", out lsName, out lsArg))
            return new LocalCommand { Name = lsName, Args = BuildArgs("remarks", lsArg) };

        if (TryKeyword(raw, "分组", "groups", out _, out _))
            return new LocalCommand { Name = "groups" };

        if (TryKeyword(raw, "状态", "status", out _, out _))
            return new LocalCommand { Name = "status" };

        // 「节点」单独作为查询词时返回列表——但必须排在动词指令之后，
        // 否则「测试 这个节点」「删除 旧节点」会被它抢先匹配成 servers。
        // 仅当句子不以测试/删除/添加/切换/代理 开头时才认作「列表」。
        if (TryKeyword(raw, "节点", "servers", out _, out var serversArg)
            && !raw.StartsWith("测试", StringComparison.Ordinal)
            && !raw.StartsWith("删除", StringComparison.Ordinal)
            && !raw.StartsWith("添加", StringComparison.Ordinal)
            && !raw.StartsWith("切换", StringComparison.Ordinal)
            && !raw.StartsWith("代理", StringComparison.Ordinal)
            && !raw.StartsWith("test", StringComparison.OrdinalIgnoreCase)
            && !raw.StartsWith("delete", StringComparison.OrdinalIgnoreCase))
            return new LocalCommand { Name = "servers", Args = BuildArgs("remarks", serversArg) };

        if (TryKeyword(raw, "切换", "select", out _, out var selectArg))
            return new LocalCommand { Name = "selectGroup", Args = BuildArgs("remarks", selectArg) };

        if (TryKeyword(raw, "删除", "delete", out _, out var deleteArg))
            return new LocalCommand { Name = "deleteNode", Args = BuildArgs("remarks", deleteArg) };

        if (TryKeyword(raw, "添加", "add", out _, out var addArg))
            return new LocalCommand { Name = "addNodes", Args = BuildLinksArg(addArg) };

        if (TryKeyword(raw, "测试", "test", out _, out var testArg))
            return new LocalCommand { Name = "testNode", Args = BuildLinksArg(testArg) };

        if (TryKeyword(raw, "代理", "proxy", out _, out var proxyMode))
            return new LocalCommand { Name = "systemProxy", Args = BuildArgs("mode", NormalizeProxyMode(proxyMode)) };

        return null;
    }

    private static bool ContainsAny(string raw, params string[] keywords)
        => keywords.Any(k => raw.Contains(k, StringComparison.OrdinalIgnoreCase));

    private static bool TryKeyword(string raw, string cn, string en, out string name, out string? arg)
    {
        name = en;
        arg = null;

        var lower = raw.ToLowerInvariant();
        var idx = lower.IndexOf(en, StringComparison.Ordinal);
        if (idx >= 0)
        {
            arg = raw[(idx + en.Length)..].Trim();
            return true;
        }

        idx = raw.IndexOf(cn, StringComparison.Ordinal);
        if (idx >= 0)
        {
            arg = raw[(idx + cn.Length)..].Trim();
            return true;
        }

        return false;
    }

    private static JsonElement BuildArgs(string key, string? value)
    {
        var dict = new Dictionary<string, JsonElement>();
        if (!string.IsNullOrWhiteSpace(value))
            dict[key] = JsonSerializer.SerializeToElement(value!);
        return JsonSerializer.SerializeToElement(dict);
    }

    private static JsonElement BuildLinksArg(string? input)
    {
        var dict = new Dictionary<string, JsonElement>();
        if (!string.IsNullOrWhiteSpace(input))
        {
            var links = input.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            dict["links"] = JsonSerializer.SerializeToElement(links);
        }
        return JsonSerializer.SerializeToElement(dict);
    }

    private static string NormalizeProxyMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return "set";
        var lower = mode.ToLowerInvariant();
        if (lower.Contains("pac") || lower.Contains("自动"))
            return "pac";
        if (lower.Contains("clear") || lower.Contains("off") || lower.Contains("关闭") || lower.Contains("关"))
            return "clear";
        return "set";
    }

    /// <summary>
    /// Execute a local command by looking it up in the registered AI capability
    /// registry. This reuses the same code path as the HTTP API so behaviour
    /// stays identical whether the AI is called over HTTP or from the chat box.
    /// </summary>
    private async Task ExecuteLocalCommandAsync(LocalCommand cmd, string rawInput)
    {
        // 自主采集闭环：不走 AiApiServer，直接调本 VM 的全自动搜索
        //（抓取固定来源 → 提取 → 真实延迟验证 → 只入库有效节点 → 报告）
        if (string.Equals(cmd.Name, "autoSearch", StringComparison.OrdinalIgnoreCase))
        {
            await AutoSearchAsync();
            return;
        }

        var capability = AiCapabilityRegistry.All.FirstOrDefault(c =>
            string.Equals(c.Descriptor.Name, cmd.Name, StringComparison.OrdinalIgnoreCase));

        if (capability is null)
        {
            AddMessage(AIChatRole.AI, $"[失败] 未找到命令 `/{cmd.Name}`。");
            return;
        }

        AddMessage(AIChatRole.AI, $"🧠 执行 `/{cmd.Name}`...");

        var result = await capability.InvokeAsync(cmd.Args, CancellationToken.None);

        if (!result.Ok)
        {
            AddMessage(AIChatRole.AI, $"[失败] 命令失败：{result.Message}");
            return;
        }

        var body = result.Data?.GetRawText() ?? "{}";
        var pretty = PrettyJson(body);
        var head = result.Message is { Length: > 0 } ? result.Message : "完成";
        var text = $"[成功] {head}\n\n{Truncate(pretty, 900)}";
        AddMessage(AIChatRole.AI, text);
    }

    private static string PrettyJson(string raw)
    {
        try
        {
            var doc = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return raw;
        }
    }

    private static string Truncate(string s, int max)
    {
        if (s.Length <= max)
            return s;
        return s[..max] + "\n... (已截断)";
    }

    #endregion

    #region Content Helpers

    private string ExtractProtocol(string nodeLink)
    {
        if (nodeLink.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
            return "VMess";
        if (nodeLink.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
            return "VLESS";
        if (nodeLink.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase))
            return "Trojan";
        if (nodeLink.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
            return "Shadowsocks";
        if (nodeLink.StartsWith("hy2://", StringComparison.OrdinalIgnoreCase) || nodeLink.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase))
            return "Hysteria2";
        if (nodeLink.StartsWith("tuic://", StringComparison.OrdinalIgnoreCase))
            return "TUIC";
        if (nodeLink.StartsWith("socks://", StringComparison.OrdinalIgnoreCase) || nodeLink.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase))
            return "SOCKS";
        if (nodeLink.StartsWith("wireguard://", StringComparison.OrdinalIgnoreCase))
            return "WireGuard";
        if (nodeLink.StartsWith("anytls://", StringComparison.OrdinalIgnoreCase))
            return "AnyTLS";
        if (nodeLink.StartsWith("naive://", StringComparison.OrdinalIgnoreCase) || nodeLink.StartsWith("naive+https://", StringComparison.OrdinalIgnoreCase) || nodeLink.StartsWith("naive+quic://", StringComparison.OrdinalIgnoreCase))
            return "Naive";
        return "Unknown";
    }

    private string ExtractAddress(string nodeLink)
    {
        try
        {
            if (nodeLink.Contains("@"))
            {
                var afterAt = nodeLink.Split('@').Last();
                return afterAt.Split(':').First().Split('?').First();
            }
            // Try URI parsing
            if (Uri.TryCreate(nodeLink, UriKind.Absolute, out var uri))
            {
                return uri.Host;
            }
        }
        catch { }
        return "unknown";
    }

    #endregion

    #region Backend Interface Calls

    /// <summary>Test all nodes in the current AI subscription group</summary>
    public async Task TestAllNodesAsync()
    {
        if (IsProcessing)
            return;

        IsProcessing = true;
        AddMessage(AIChatRole.User, "🔍 测试全部节点");

        try
        {
            AddMessage(AIChatRole.AI, "正在测试最近添加的节点...");

            // Test recent nodes from the last search
            var recentNodes = Messages
                .Where(m => m.NodeResults?.Count > 0)
                .SelectMany(m => m.NodeResults!)
                .Where(r => r.Status == AIChatNodeStatus.Passed)
                .Select(r => r.NodeLink)
                .Distinct()
                .Take(50)
                .ToList();

            if (recentNodes.Count == 0)
            {
                AddMessage(AIChatRole.AI, "[警告]️ 没有找到可测试的节点。\n\n你可以先通过AI搜索获取免费节点，然后再测试。");
                return;
            }

            AddMessage(AIChatRole.AI, $"📋 找到 **{recentNodes.Count}** 个节点，开始测试...");

            // 真实延迟验证（起临时内核逐节点发真实 HTTP 请求，不再做 TCP 端口探测）
            var results = new List<AIChatNodeResult>();
            var validSet = await ValidateRealAsync(recentNodes);
            var validNodes = recentNodes.Where(n => validSet.Contains(n)).ToList();

            foreach (var nodeLink in recentNodes)
            {
                var passed = validSet.Contains(nodeLink);
                results.Add(new AIChatNodeResult
                {
                    NodeLink = nodeLink,
                    DisplayName = ExtractNodeName(nodeLink),
                    Protocol = ExtractProtocol(nodeLink),
                    Address = ExtractAddress(nodeLink),
                    Status = passed ? AIChatNodeStatus.Passed : AIChatNodeStatus.Failed,
                    StatusText = passed ? "[成功] 可用" : "[失败] 失效"
                });
            }

            // Show results
            AddNodeResultMessage(results, validNodes.Count);

            if (validNodes.Count < recentNodes.Count)
            {
                var failedCount = recentNodes.Count - validNodes.Count;
                AddMessage(AIChatRole.AI, $"[警告]️ 测试完成：{validNodes.Count} 个可用，{failedCount} 个失效。\n\n💡 建议定期点击「测试全部」按钮检查节点状态。");
            }
            else
            {
                AddMessage(AIChatRole.AI, $"[成功] 测试完成：所有 **{recentNodes.Count}** 个节点均可用！");
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            AddMessage(AIChatRole.AI, $"[失败] 测试过程中出现错误：\n\n`{ex.Message}`");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private async Task<string> DownloadUrlContent(string url)
    {
        // 统一走 AiUrlFetchService（重定向 / 解压 / 重试 / 镜像 / 代理双通道）
        var res = await AiUrlFetchService.FetchAsync(url);
        return res.Success ? res.Content : string.Empty;
    }

    private async Task<List<string>?> ExtractNodesFromContent(AIConfigItem aiConfig, string content, string sourceUrl)
    {
        // 1) 强提取：全文本正则 + HTML 实体 + 多层 Base64，不依赖 AI，也比 AI 稳定
        var strong = AiUrlFetchService.ExtractNodes(content);
        if (strong.Count > 0)
        {
            return strong;
        }

        // 2) 行级提取兜底（兼容一些特殊排版）
        var directNodes = ParseNodesFromText(content);
        if (directNodes.Count > 0)
        {
            return directNodes;
        }

        // 3) 仍取不到才交给 AI。先剥掉 HTML 标签，省下的 token 用来看更多正文。
        //    AI 没配就直接返回 null——不要拿着空 API 地址去发请求，那是白等 90 秒超时。
        if (aiConfig == null || !aiConfig.Enabled || aiConfig.ApiUrl.IsNullOrEmpty())
        {
            return null;
        }

        var plain = AiUrlFetchService.StripHtml(content);
        if (plain.Trim().Length < 40)
        {
            plain = content;
        }

        var truncatedContent = plain.Length > 12000 ? plain[..12000] + "\n... (内容已截断)" : plain;
        var prompt = $"""
        请分析以下网页/文本内容，从中提取所有有效的VPN/代理节点链接。

        来源URL: {sourceUrl}

        内容:
        {truncatedContent}

        请执行以下步骤：
        1. 检查内容中是否包含 vmess://, vless://, trojan://, ss://, hy2://, hysteria2://, tuic:// 等节点链接
        2. 如果内容是Base64编码的订阅内容，先解码再提取
        3. 如果内容是HTML页面，提取页面中嵌入的节点信息
        4. 如果是GitHub页面，查找raw文件链接中的节点
        5. 只返回有效的节点链接，每行一个
        6. 如果没有找到节点，请回复 NO_NODES_FOUND

        返回格式：每行一个完整的节点链接，不要添加任何说明文字
        """;

        return await CallAIForNodeExtraction(aiConfig, prompt);
    }

    private async Task<List<string>?> SearchGitHubForFreeNodes(AIConfigItem aiConfig)
    {
        var prompt = """
        你是一个VPN节点搜索助手。请搜索以下GitHub仓库中最新的免费VPN/代理节点。

        重点搜索这些类型的仓库：
        1. 包含免费v2ray/vless/trojan/shadowsocks节点分享的仓库
        2. 免费VPN订阅链接聚合仓库
        3. 免费clash/sing-box代理节点仓库

        搜索关键词建议：
        - free v2ray nodes
        - free clash proxy
        - free vpn subscription
        - v2ray free share
        - free hysteria2
        - free trojan nodes

        对每个找到的节点：
        1. 验证格式是否正确（vmess://, vless://, trojan://, ss://, hy2://, tuic://）
        2. 提取完整的节点链接
        3. 只返回有效的节点链接，每行一个

        返回格式：每行一个完整的节点链接，不要添加任何说明文字
        """;

        return await CallAIForNodeExtraction(aiConfig, prompt);
    }

    private async Task<List<string>?> CallAIForNodeExtraction(AIConfigItem aiConfig, string prompt)
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(90);

        var requestBody = new
        {
            model = aiConfig.ModelId,
            messages = new[]
            {
                new { role = "system", content = "你是一个专业的VPN节点解析助手，专门从各种网页、仓库、订阅链接中提取VPN代理节点。你只返回节点链接，不添加任何多余文字。" },
                new { role = "user", content = prompt }
            },
            temperature = 0.2,
            max_tokens = 4000
        };

        var json = JsonSerializer.Serialize(requestBody);
        var httpContent = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

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
            return ParseNodesFromText(message ?? string.Empty);
        }
        return null;
    }

    /// <summary>
    /// 汇总软件实时状态，注入给模型——否则它对「现在连的哪个节点、延迟多少、
    /// 分组里有什么」一无所知，回答只能是空话，这是「AI 不聪明」的第一根因。
    /// </summary>
    private async Task<string> BuildAppStateSummaryAsync()
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            var coreUp = AppManager.Instance.IsRunningCore(ECoreType.Xray) ||
                         AppManager.Instance.IsRunningCore(ECoreType.sing_box);
            sb.AppendLine($"- 核心运行中：{(coreUp ? "是" : "否")}");
            sb.AppendLine($"- 系统代理模式：{_config.SystemProxyItem.SysProxyType}（ForcedChange=自动配置，ForcedClear=清除）");

 var exs = await ProfileExManager.Instance.GetProfileExs();
 var current = (await AppManager.Instance.ProfileItems("") ?? []).FirstOrDefault(t => t.IndexId == _config.IndexId);
 if (current != null)
 {
 var delay = exs.FirstOrDefault(t => t.IndexId == _config.IndexId)?.Delay ?? 0;
 // 延迟来源说明：列表测速值（ProfileExManager），与状态栏底部实时穿透探测可能略有差异——
 // 状态栏每 20s 跑一次真实 HTTP 请求，是最新值；列表值是上次测速时写入的。
 sb.AppendLine($"- 当前节点：{current.Remarks}（{(delay > 0 ? $"延迟 {delay} ms（列表测速值）" : "未测或不可达")}）");
 }

            var all = await AppManager.Instance.ProfileItems("") ?? [];
            var top = (from p in all
                       join e in exs on p.IndexId equals e.IndexId
                       where e.Delay > 0
                       orderby e.Delay
                       select new { p.Remarks, e.Delay }).Take(3).ToList();
            sb.AppendLine($"- 节点库：共 {all.Count} 个节点");
            if (top.Count > 0)
            {
                sb.AppendLine($"- 最快节点：{string.Join("；", top.Select(t => $"{t.Remarks} {t.Delay}ms"))}");
            }

            if (AISchedulerService.LastRunAtUtc != DateTime.MinValue)
            {
                sb.AppendLine($"- 上次自动采集：{AISchedulerService.LastRunAtUtc.ToLocalTime():MM-dd HH:mm}，{AISchedulerService.LastMessage ?? "无记录"}");
            }
            sb.AppendLine($"- 每日自动采集：{(AutoCrawlEnabled ? $"已开启（每天 {AutoCrawlIntervalMinutes:00}:00）" : "已关闭")}");
            return sb.ToString();
        }
        catch
        {
            return "- （状态读取失败）";
        }
    }

    /// <summary>
    /// 取最近的对话历史（跳过系统节点结果消息，合并连续同角色），
    /// 让模型看得到上下文——没有历史时「它」「刚才那个」之类的追问完全无法理解，
    /// 这是「AI 不聪明」的第二根因。
    /// </summary>
    private List<(string Role, string Content)> BuildChatHistory()
    {
        var history = new List<(string Role, string Content)>();
        foreach (var m in Messages.TakeLast(12))
        {
            if (m.Role == AIChatRole.System || m.Content.IsNullOrEmpty())
            {
                continue;
            }
            var role = m.Role == AIChatRole.User ? "user" : "assistant";
            var content = m.Content.Length > 600 ? m.Content[..600] + "..." : m.Content;
            if (history.Count > 0 && history[^1].Role == role)
            {
                history[^1] = (role, history[^1].Content + "\n" + content);
            }
            else
            {
                history.Add((role, content));
            }
        }

        // 最后一条应是刚加进 Messages 的当前用户消息；确保以 user 结尾
        if (history.Count > 0 && history[^1].Role != "user")
        {
            history.Add(("user", string.Empty));
        }
        if (history.Count == 0)
        {
            history.Add(("user", string.Empty));
        }
        return history;
    }

    /// <summary>
    /// Sends a free-form chat message to the Hermes API (OpenAI-compatible /chat/completions)
    /// and returns the assistant's reply.
    /// </summary>
    private async Task<string?> AskHermesAsync(AIConfigItem aiConfig, string userMessage)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(90);

            var stateSummary = await BuildAppStateSummaryAsync();
            var systemPrompt = $"{AiSystemPrompt}\n\n【软件实时状态（回答时以此为准，不要凭空编造）】\n{stateSummary}";

            var history = BuildChatHistory();
            // 历史最后一条就是刚加入 Messages 的当前用户消息；若被截断则用原文合并
            var messages = new List<object> { new { role = "system", content = systemPrompt } };
            for (var i = 0; i < history.Count; i++)
            {
                var (role, content) = history[i];
                if (i == history.Count - 1 && role == "user")
                {
                    var merged = content.IsNullOrEmpty()
                        ? userMessage
                        : (content.Contains(userMessage, StringComparison.Ordinal) ? content : $"{content}\n{userMessage}");
                    messages.Add(new { role = "user", content = merged });
                }
                else
                {
                    messages.Add(new { role, content });
                }
            }

            var requestBody = new
            {
                model = aiConfig.ModelId,
                messages,
                temperature = 0.7,
                max_tokens = 1500
            };

            TraceWire("AI-wire system-prompt", systemPrompt);
            TraceWire("AI-wire user-input", userMessage);
            var json = JsonSerializer.Serialize(requestBody);
            var httpContent = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            if (aiConfig.ApiKey.IsNotEmpty())
            {
                httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", aiConfig.ApiKey);
            }

            var response = await httpClient.PostAsync($"{aiConfig.ApiUrl}/chat/completions", httpContent);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Logging.SaveLog($"AskHermesAsync failed: {response.StatusCode} {responseJson}");
                var errBody = responseJson.Length > 300 ? responseJson[..300] + "..." : responseJson;
                return $"[失败] AI 服务返回错误：{response.StatusCode}\n\n`{errBody}`";
            }

            var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var reply = choices[0].GetProperty("message").GetProperty("content").GetString();
                TraceWire("AI-wire raw-reply", reply);
                if (!string.IsNullOrWhiteSpace(reply))
                    return reply;
            }

            return "（AI 未返回内容）";
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return $"[失败] 请求 AI 服务失败：`{ex.Message}`";
        }
    }

    /// <summary>
    ///     批量真实延迟验证：委托 AIFetchService 起一个临时内核，
    ///     给每个候选节点分配独立的本地 SOCKS5 入站，逐节点发真实 HTTP 请求测延迟。
    ///     只有能真正转发流量的节点才算通过——TCP 端口开着不算。
    /// </summary>
    private async Task<HashSet<string>> ValidateRealAsync(IEnumerable<string> nodes)
    {
        var list = nodes.Where(n => !n.IsNullOrEmpty())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (list.Count == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var fetch = new AIFetchService(_config, (_, _) => Task.CompletedTask);
        var valid = await fetch.ValidateNodesAsync(list, list.Count);
        return new HashSet<string>(valid, StringComparer.OrdinalIgnoreCase);
    }

    private List<string> ParseNodesFromText(string text)
    {
        // 先做「全文本扫描」：一行里挤了多个节点、或节点前面带说明文字
        // （比如"节点1：vmess://..."、"- vless://..."）时，
        // 老写法逐行 StartsWith 会整行漏掉——只在行首才认。
        var scanned = AiUrlFetchService.ExtractNodes(text);
        if (scanned.Count > 0)
        {
            return scanned;
        }

        var nodes = new List<string>();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim().TrimStart('-', '*', ' ', '•');
            if (string.IsNullOrEmpty(trimmed))
                continue;

            if (trimmed.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("vless://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("ss://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("hy2://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("tuic://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("socks://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("wireguard://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("anytls://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("naive://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("naive+https://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("naive+quic://", StringComparison.OrdinalIgnoreCase))
            {
                nodes.Add(trimmed);
            }
            else if (trimmed.Contains("://") && trimmed.Contains("@") && trimmed.Length > 20)
            {
                nodes.Add(trimmed);
            }
        }

        return nodes.Distinct().ToList();
    }

    private async Task<string> GetOrCreateAISubscriptionGroup(string remarks)
    {
        var subItems = await AppManager.Instance.SubItems();

        var existing = subItems?.FirstOrDefault(s =>
            string.Equals(s.Remarks ?? string.Empty, remarks ?? string.Empty, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            return existing.Id;
        }

        var newSub = new SubItem
        {
            Id = Utils.GetGuid(),
            Remarks = remarks,
            Url = "ai-auto-search://github.com",
            Enabled = true,
            AutoUpdateInterval = 0,
            Sort = 0,
            Memo = "由AI自动搜索获取的免费VPN节点"
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

    /// <summary>一次输入里可能包含多个地址，拆开处理；本身是节点链接时不拆。</summary>
    private static List<string> SplitUrls(string raw)
    {
        var list = new List<string>();
        if (raw.IsNullOrEmpty())
        {
            return list;
        }

        if (Array.Exists(NodeLinkPrefixes, p => raw.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            list.Add(raw);
            return list;
        }

        var parts = raw.Split(new[] { ' ', '\t', '\n', '\r', '，', '；', ';', '、' },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            var t = part.Trim();
            if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(t);
            }
        }

        return list.Distinct().ToList();
    }

    private static string ShortUrl(string url)
    {
        return url.Length <= 70 ? url : url[..40] + "..." + url[^20..];
    }

    /// <summary>
    /// 从任意文本里抽出 HTTP(S) 地址 —— 不管它在句子的什么位置。
    /// 兼容：Markdown 链接 [文字](url)、被括号/引号包裹、以及中文全角标点结尾。
    /// </summary>
    private static List<string> ExtractHttpUrls(string text)
    {
        var list = new List<string>();
        if (text.IsNullOrEmpty())
        {
            return list;
        }

        // 先把 Markdown 的 [文字](url) 还原成裸地址，否则右括号会被当成 URL 的一部分
        var withoutMd = Regex.Replace(
            text,
            @"\[[^\]]*\]\(\s*(https?://[^\s\)]+)\s*\)",
            "$1",
            RegexOptions.IgnoreCase);

        foreach (Match m in Regex.Matches(withoutMd,
                     @"https?://[^\s<>""'）)】\]，。；！？]+", RegexOptions.IgnoreCase))
        {
            var u = CleanUrlToken(m.Value);
            if (u.Length > 12 && !list.Contains(u))
            {
                list.Add(u);
            }
        }

        return list;
    }

    /// <summary>去掉粘在 URL 尾部的中英文标点、引号、右括号和 Markdown 残留。</summary>
    private static string CleanUrlToken(string u)
    {
        return u.Trim().TrimEnd(
            '.', ',', ';', ':', '!', '?', '"', '\'', ')', '}', '>', '`', '*',
            '。', '，', '；', '：', '！', '？', '、', '）', '】', '》', '」', '』');
    }

    /// <summary>用户回复的是「导入 / 确认 / y / ok」这类肯定词。</summary>
    private static bool IsConfirmWord(string? raw)
    {
        if (raw.IsNullOrEmpty())
        {
            return false;
        }

        var s = raw.Trim().Trim('。', '.', '！', '!');
        return s.Equals("导入", StringComparison.OrdinalIgnoreCase)
               || s.Equals("确认", StringComparison.OrdinalIgnoreCase)
               || s.Equals("是", StringComparison.OrdinalIgnoreCase)
               || s.Equals("y", StringComparison.OrdinalIgnoreCase)
               || s.Equals("yes", StringComparison.OrdinalIgnoreCase)
               || s.Equals("ok", StringComparison.OrdinalIgnoreCase)
               || s.Equals("import", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>导入上一轮未通过连通性测试、但格式合法的候选节点。</summary>
    private async Task ImportPendingAsync()
    {
        var list = _pendingImport ?? new List<string>();
        _pendingImport = null;

        if (list.Count == 0)
        {
            AddMessage(AIChatRole.AI, "没有待导入的节点。");
            return;
        }

        AddMessage(AIChatRole.AI, $"📦 正在导入 {list.Count} 个节点到「{TargetGroup}」...");
        try
        {
            var subId = await GetOrCreateAISubscriptionGroup(TargetGroup);
            var (added, dup) = await AIFetchService.AddNewNodesToGroupAsync(_config, subId, list);
            AddMessage(AIChatRole.AI,
                added > 0
                    ? $"🎉 已导入 **{added}** 个节点到「**{TargetGroup}**」（跳过已存在的 {dup} 个）。\n\n" +
                      "这些节点未经连通性验证，建议在主界面批量测速后筛掉不可用的。"
                    : $"ℹ️ 这 {list.Count} 个节点在「**{TargetGroup}**」里已全部存在，未做改动。");
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            AddMessage(AIChatRole.AI, $"[失败] 导入失败：`{ex.Message}`");
        }
    }

    #endregion
}

/// <summary>GitHub repository item for user selection</summary>
public partial class GitHubRepoItem : MyReactiveObject
{
    [Reactive] public partial string Owner { get; set; } = string.Empty;
    [Reactive] public partial string Repo { get; set; } = string.Empty;
    [Reactive] public partial string Description { get; set; } = string.Empty;
    [Reactive] public partial int Stars { get; set; }
    [Reactive] public partial bool IsSelected { get; set; } = true;
    [Reactive] public partial string Url { get; set; } = string.Empty;
}
