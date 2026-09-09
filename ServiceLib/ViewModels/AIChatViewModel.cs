using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using ServiceLib.Models;
using ServiceLib.Services.AiApi;

namespace ServiceLib.ViewModels;

public partial class AIChatViewModel : MyReactiveObject, ICloseable
{
    private static readonly string _tag = "AIChatViewModel";
    public event EventHandler? RequestClose;

    [Reactive] public partial string ChatInput { get; set; } = string.Empty;
    [Reactive] public partial bool IsProcessing { get; set; }
    [Reactive] public partial string TargetGroup { get; set; } = "AI自动获取";
    [Reactive] public partial int MaxNodes { get; set; } = 50;
    [Reactive] public partial bool AutoTest { get; set; } = true;
    [Reactive] public partial bool AutoCrawlEnabled { get; set; }
    [Reactive] public partial int AutoCrawlIntervalMinutes { get; set; } = 120;
    [Reactive] public partial bool IsRepoListVisible { get; set; }
    private Timer? _autoCrawlTimer;
    private CancellationTokenSource? _autoCrawlCts;

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

🔍 **GitHub 自动搜索** — 点击「自动搜索」或输入 `搜索`，我会在 GitHub 上搜索最新免费节点仓库，下载订阅、解析节点、测活后把通过的导入分组。

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
            StartAutoCrawlTimer();
            AddMessage(AIChatRole.System, $"⏰ 已开启自动爬取，每 {AutoCrawlIntervalMinutes} 分钟自动搜索免费节点");
        }
        else
        {
            StopAutoCrawlTimer();
            AddMessage(AIChatRole.System, "⏰ 已关闭自动爬取");
        }
    }

    private void StartAutoCrawlTimer()
    {
        StopAutoCrawlTimer();
        _autoCrawlCts = new CancellationTokenSource();
        var interval = TimeSpan.FromMinutes(Math.Max(AutoCrawlIntervalMinutes, 5));
        _autoCrawlTimer = new Timer(async _ =>
        {
            if (IsProcessing) return;
            try
            {
                AddMessage(AIChatRole.System, "⏰ 定时触发：开始自动搜索免费节点...");
                await AutoSearchAsync();
            }
            catch (Exception ex)
            {
                Logging.SaveLog(_tag, ex);
            }
        }, null, interval, interval);
    }

    private void StopAutoCrawlTimer()
    {
        _autoCrawlCts?.Cancel();
        _autoCrawlCts = null;
        _autoCrawlTimer?.Dispose();
        _autoCrawlTimer = null;
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
                    if (repos.Count >= 20) return;
                    var url = $"https://api.github.com/search/repositories?q={Uri.EscapeDataString(query)}&sort=updated&order=desc&per_page=5";
                    var resp = await searchClient.GetAsync(url);
                    if (!resp.IsSuccessStatusCode) return;
                    var json = await resp.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    if (!doc.RootElement.TryGetProperty("items", out var items)) return;
                    foreach (var item in items.EnumerateArray())
                    {
                        if (!item.TryGetProperty("full_name", out var fn)) continue;
                        var full = fn.GetString();
                        if (full is null) continue;
                        var parts = full.Split('/');
                        if (parts.Length != 2) continue;

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
                AddMessage(AIChatRole.AI, "⚠️ 未找到免费节点仓库。\n\n可能原因：\n- GitHub API限制\n- 网络连接问题\n\n建议稍后重试。");
                IsRepoListVisible = false;
            }
            else
            {
                AddMessage(AIChatRole.AI, $"✅ 找到 **{GitHubRepos.Count}** 个仓库\n\n请在下方列表中勾选要导入的仓库，然后点击「导入所选」按钮。");
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            AddMessage(AIChatRole.AI, $"❌ 搜索过程中出现错误：\n\n`{ex.Message}`");
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
            AddMessage(AIChatRole.AI, "⚠️ 请先勾选要导入的仓库。");
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
                AddMessage(AIChatRole.AI, "❌ **AI功能未启用**\n\n请先在「设置 → AI智能获取设置」中配置API地址和密钥。");
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
                        AddMessage(AIChatRole.AI, $"✅ 从 {repo.Owner}/{repo.Repo} 获取到 {nodes.Count} 个节点");
                    }
                    else
                    {
                        AddMessage(AIChatRole.AI, $"⚠️ 从 {repo.Owner}/{repo.Repo} 未获取到节点");
                    }
                }
                catch (Exception ex)
                {
                    Logging.SaveLog($"{_tag}: fetch from {repo.Owner}/{repo.Repo} failed: {ex.Message}");
                    AddMessage(AIChatRole.AI, $"❌ 从 {repo.Owner}/{repo.Repo} 获取失败：{ex.Message}");
                }
                finally
                {
                    fetchSemaphore.Release();
                }
            });
            await Task.WhenAll(fetchTasks);

            if (allNodes.Count == 0)
            {
                AddMessage(AIChatRole.AI, "⚠️ 未从任何仓库获取到节点。\n\n建议尝试其他仓库或稍后重试。");
                return;
            }

            // Deduplicate
            allNodes = allNodes.Distinct().ToList();
            AddMessage(AIChatRole.AI, $"🔍 共获取到 **{allNodes.Count}** 个不重复节点，开始验证...");

            // Test nodes concurrently
            var validNodes = new List<string>();
            var resultLock = new object();

            using var testSemaphore = new SemaphoreSlim(20);
            var testTasks = allNodes.Select(async nodeLink =>
            {
                await testSemaphore.WaitAsync();
                try
                {
                    if (await TestNode(nodeLink))
                    {
                        lock (resultLock)
                        {
                            validNodes.Add(nodeLink);
                        }
                    }
                }
                finally
                {
                    testSemaphore.Release();
                }
            });
            await Task.WhenAll(testTasks);

            // Cap at MaxNodes
            if (validNodes.Count > MaxNodes)
            {
                validNodes = validNodes.Take(MaxNodes).ToList();
            }

            if (validNodes.Count == 0)
            {
                AddMessage(AIChatRole.AI, "⚠️ 所有节点验证均失败。\n\n建议稍后重试或尝试其他仓库。");
                return;
            }

            // Add valid nodes to target group
            AddMessage(AIChatRole.AI, $"📦 正在将 {validNodes.Count} 个有效节点添加到「{TargetGroup}」分组...");

            var subId = await GetOrCreateAISubscriptionGroup(TargetGroup);
            var addedCount = await ConfigHandler.AddBatchServers(_config, string.Join("\n", validNodes), subId, true);

            AddMessage(AIChatRole.AI, $"🎉 **完成！** 成功添加 **{addedCount}** 个有效节点到「**{TargetGroup}**」分组。\n\n你可以在主界面的分组列表中查看和使用这些节点。");
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            AddMessage(AIChatRole.AI, $"❌ 导入过程中出现错误：\n\n`{ex.Message}`");
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
                    if (nodes.Count >= 100) break; // Stop after enough
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
    public async Task AnalyzeUrlAsync()
    {
        if (IsProcessing || string.IsNullOrWhiteSpace(ChatInput))
        {
            return;
        }

        var raw = ChatInput.Trim();
        ChatInput = string.Empty;
        IsProcessing = true;

        // Fast path 1: local command router
        var cmd = TryMatchLocalCommand(raw);
        if (cmd is not null)
        {
            AddMessage(AIChatRole.User, $"💬 {raw}");
            try { await ExecuteLocalCommandAsync(cmd, raw); }
            finally { IsProcessing = false; }
            return;
        }

        var url = raw;

        // Fast path 2: direct node link or HTTP URL
        var isDirectNodeLink = Array.Exists(NodeLinkPrefixes, p => raw.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        var isHttpUrl = raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        // Fast path 3: free-form question — always reset IsProcessing so a
        // follow-up message is never swallowed by a stale busy flag.
        if (!isDirectNodeLink && !isHttpUrl)
        {
            try
            {
                AddMessage(AIChatRole.User, $"💬 {raw}");
                var aiConfig = _config.AIConfigItem ?? new AIConfigItem();
                if (!aiConfig.Enabled || aiConfig.ApiUrl.IsNullOrEmpty())
                {
                    AddMessage(AIChatRole.AI, "❌ **AI功能未启用**\n\n请先在「设置 → AI智能获取设置」中配置API地址和密钥。");
                    return;
                }
                var answer = await AskHermesAsync(aiConfig, raw);
                AddMessage(AIChatRole.AI, answer ?? "❌ AI 未返回任何内容。");
            }
            finally
            {
                IsProcessing = false;
            }
            return;
        }

        AddMessage(AIChatRole.User, $"🔗 分析这个内容：{(url.Length > 200 ? url[..200] + "..." : url)}");

        try
        {
            var aiConfig = _config.AIConfigItem ?? new AIConfigItem();
            if (!aiConfig.Enabled || aiConfig.ApiUrl.IsNullOrEmpty())
            {
                AddMessage(AIChatRole.AI, "❌ **AI功能未启用**\n\n请先在「设置 → AI智能获取设置」中配置API地址和密钥，然后再使用AI助手。");
                return;
            }

            // Fast path: user pasted direct node links
            var directNodes = ParseNodesFromText(url);
            // Also try Base64 decode
            if (directNodes.Count == 0 && Utils.IsBase64String(url))
            {
                directNodes = ParseNodesFromText(Utils.Base64Decode(url));
            }

            List<string>? nodes;
            if (directNodes.Count > 0)
            {
                nodes = directNodes;
                AddMessage(AIChatRole.AI, $"📋 检测到 **{nodes.Count}** 个直接粘贴的节点链接，跳过下载步骤，直接验证...");
            }
            else
            {
                AddMessage(AIChatRole.AI, "🔍 正在下载并分析链接内容...");

                // Step 1: Download URL content
                var content = await DownloadUrlContent(url);
                if (content.IsNullOrEmpty())
                {
                    AddMessage(AIChatRole.AI, $"❌ 无法下载链接内容，也无法识别为节点链接。\n\n请检查：\n- URL是否正确、网络是否正常\n- 或者直接粘贴节点链接（vmess://、vless://等）\n\n`{(url.Length > 100 ? url[..100] + "..." : url)}`");
                    return;
                }

                AddMessage(AIChatRole.AI, $"📥 已下载内容（{content.Length:N0} 字符），正在让AI分析提取节点...");

                // Step 2: Use AI to extract nodes from content
                nodes = await ExtractNodesFromContent(aiConfig, content, url);
            }

            if (nodes == null || nodes.Count == 0)
            {
                AddMessage(AIChatRole.AI, $"⚠️ 未能从链接中提取到有效的VPN节点。\n\n可能原因：\n- 页面内容不包含节点链接\n- 节点格式无法识别\n- 需要登录才能查看内容\n\n**来源URL**: `{url}`");
                return;
            }

            // Cap the candidate pool
            var pool = nodes.Count > MaxNodes * 3
                ? nodes.Take(MaxNodes * 3).ToList()
                : nodes;
            if (pool.Count > 150) pool = pool.Take(150).ToList();
            AddMessage(AIChatRole.AI, $"🔍 从内容中提取到 **{nodes.Count}** 个候选节点，先验证前 **{pool.Count}** 个...");

            // Step 3: Test nodes concurrently
            var results = new List<AIChatNodeResult>();
            var validNodes = new List<string>();
            var resultLock = new object();

            using var semaphore = new SemaphoreSlim(20);
            var testTasks = pool.Select(async nodeLink =>
            {
                var result = new AIChatNodeResult
                {
                    NodeLink = nodeLink,
                    DisplayName = ExtractNodeName(nodeLink),
                    Protocol = ExtractProtocol(nodeLink),
                    Address = ExtractAddress(nodeLink),
                    Status = AIChatNodeStatus.Testing,
                    StatusText = "正在验证..."
                };
                lock (resultLock)
                {
                    results.Add(result);
                }

                await semaphore.WaitAsync();
                bool passed;
                try
                {
                    passed = await TestNode(nodeLink);
                }
                finally
                {
                    semaphore.Release();
                }

                if (passed)
                {
                    result.Status = AIChatNodeStatus.Passed;
                    result.StatusText = "✅ 验证通过";
                    lock (resultLock)
                    {
                        validNodes.Add(nodeLink);
                    }
                }
                else
                {
                    result.Status = AIChatNodeStatus.Failed;
                    result.StatusText = "❌ 验证失败";
                }
            }).ToList();
            await Task.WhenAll(testTasks);

            // Restore original order
            var orderMap = new Dictionary<string, int>(pool.Count);
            for (int i = 0; i < pool.Count; i++) orderMap[pool[i]] = i;
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
                AddMessage(AIChatRole.AI, $"⚠️ 所有节点验证均失败。建议稍后重试或尝试其他链接。");
                return;
            }

            // Step 4: Add valid nodes to target group
            AddMessage(AIChatRole.AI, $"📦 正在将 {validNodes.Count} 个有效节点添加到「{TargetGroup}」分组...");

            var subId = await GetOrCreateAISubscriptionGroup(TargetGroup);
            var addedCount = await ConfigHandler.AddBatchServers(_config, string.Join("\n", validNodes), subId, true);

            AddMessage(AIChatRole.AI, $"🎉 **完成！** 成功添加 **{addedCount}** 个有效节点到「**{TargetGroup}**」分组。\n\n你可以在主界面的分组列表中查看和使用这些节点。");
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            AddMessage(AIChatRole.AI, $"❌ 分析过程中出现错误：\n\n`{ex.Message}`");
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
                AddMessage(AIChatRole.AI, "❌ **AI功能未启用**\n\n请先在「设置 → AI智能获取设置」中配置API地址和密钥。");
                return;
            }

            // Override settings
            aiConfig.AiGroupRemarks = TargetGroup;
            aiConfig.MaxNodesPerSearch = MaxNodes;
            _config.AIConfigItem = aiConfig;

            AddMessage(AIChatRole.AI, "🔍 正在搜索GitHub上的免费VPN节点...\n\n我会搜索以下关键词：\n- `free v2ray nodes`\n- `free clash proxy`\n- `free vpn subscription github`\n- `v2ray free share`\n- `free hysteria2 nodes`");

            var aiService = new AIFetchService(_config, async (success, msg) =>
            {
                AddMessage(AIChatRole.AI, msg);
                await Task.CompletedTask;
            });

            var result = await aiService.FetchAndAddNodesAsync();
            if (result > 0)
            {
                AddMessage(AIChatRole.AI, $"✅ **自动搜索完成！**\n\n共添加 **{result}** 个有效节点到「**{TargetGroup}**」分组。\n\n💡 你可以点击「自动搜索」按钮定期刷新，获取最新节点。");
            }
            else
            {
                AddMessage(AIChatRole.AI, "⚠️ 本次搜索未找到有效节点。\n\n可能原因：\n- GitHub上暂时没有新的免费节点分享\n- 网络连接问题\n- AI API配置问题\n\n建议稍后重试。");
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            AddMessage(AIChatRole.AI, $"❌ 搜索过程中出现错误：\n\n`{ex.Message}`");
        }
        finally
        {
            IsProcessing = false;
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

        // Chinese + English command vocabulary
        var lsName = "servers";
        string? lsArg = null;
        if (TryKeyword(raw, "列表", "list", out lsName, out lsArg))
            return new LocalCommand { Name = lsName, Args = BuildArgs("remarks", lsArg) };

        if (TryKeyword(raw, "分组", "groups", out _, out _))
            return new LocalCommand { Name = "groups" };

        if (TryKeyword(raw, "状态", "status", out _, out _))
            return new LocalCommand { Name = "status" };

        if (TryKeyword(raw, "节点", "servers", out _, out var serversArg))
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
        if (string.IsNullOrWhiteSpace(mode)) return "set";
        var lower = mode.ToLowerInvariant();
        if (lower.Contains("pac") || lower.Contains("自动")) return "pac";
        if (lower.Contains("clear") || lower.Contains("off") || lower.Contains("关闭") || lower.Contains("关")) return "clear";
        return "set";
    }

    /// <summary>
    /// Execute a local command by looking it up in the registered AI capability
    /// registry. This reuses the same code path as the HTTP API so behaviour
    /// stays identical whether the AI is called over HTTP or from the chat box.
    /// </summary>
    private async Task ExecuteLocalCommandAsync(LocalCommand cmd, string rawInput)
    {
        var capability = AiCapabilityRegistry.All.FirstOrDefault(c =>
            string.Equals(c.Descriptor.Name, cmd.Name, StringComparison.OrdinalIgnoreCase));

        if (capability is null)
        {
            AddMessage(AIChatRole.AI, $"❌ 未找到命令 `/{cmd.Name}`。");
            return;
        }

        AddMessage(AIChatRole.AI, $"🧠 执行 `/{cmd.Name}`...");

        var result = await capability.InvokeAsync(cmd.Args, CancellationToken.None);

        if (!result.Ok)
        {
            AddMessage(AIChatRole.AI, $"❌ 命令失败：{result.Message}");
            return;
        }

        var body = result.Data?.GetRawText() ?? "{}";
        var pretty = PrettyJson(body);
        var head = result.Message is { Length: > 0 } ? result.Message : "完成";
        var text = $"✅ {head}\n\n{Truncate(pretty, 900)}";
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
        if (s.Length <= max) return s;
        return s[..max] + "\n... (已截断)";
    }

    #endregion

    #region Content Helpers

    private string ExtractProtocol(string nodeLink)
    {
        if (nodeLink.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase)) return "VMess";
        if (nodeLink.StartsWith("vless://", StringComparison.OrdinalIgnoreCase)) return "VLESS";
        if (nodeLink.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase)) return "Trojan";
        if (nodeLink.StartsWith("ss://", StringComparison.OrdinalIgnoreCase)) return "Shadowsocks";
        if (nodeLink.StartsWith("hy2://", StringComparison.OrdinalIgnoreCase) || nodeLink.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase)) return "Hysteria2";
        if (nodeLink.StartsWith("tuic://", StringComparison.OrdinalIgnoreCase)) return "TUIC";
        if (nodeLink.StartsWith("socks://", StringComparison.OrdinalIgnoreCase) || nodeLink.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase)) return "SOCKS";
        if (nodeLink.StartsWith("wireguard://", StringComparison.OrdinalIgnoreCase)) return "WireGuard";
        if (nodeLink.StartsWith("anytls://", StringComparison.OrdinalIgnoreCase)) return "AnyTLS";
        if (nodeLink.StartsWith("naive://", StringComparison.OrdinalIgnoreCase) || nodeLink.StartsWith("naive+https://", StringComparison.OrdinalIgnoreCase) || nodeLink.StartsWith("naive+quic://", StringComparison.OrdinalIgnoreCase)) return "Naive";
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
        if (IsProcessing) return;

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
                AddMessage(AIChatRole.AI, "⚠️ 没有找到可测试的节点。\n\n你可以先通过AI搜索获取免费节点，然后再测试。");
                return;
            }

            AddMessage(AIChatRole.AI, $"📋 找到 **{recentNodes.Count}** 个节点，开始测试...");

            // Test nodes concurrently
            var results = new List<AIChatNodeResult>();
            var validNodes = new List<string>();
            var resultLock = new object();

            using var semaphore = new SemaphoreSlim(20);
            var testTasks = recentNodes.Select(async nodeLink =>
            {
                var result = new AIChatNodeResult
                {
                    NodeLink = nodeLink,
                    DisplayName = ExtractNodeName(nodeLink),
                    Protocol = ExtractProtocol(nodeLink),
                    Address = ExtractAddress(nodeLink),
                    Status = AIChatNodeStatus.Testing,
                    StatusText = "正在验证..."
                };

                lock (resultLock)
                {
                    results.Add(result);
                }

                await semaphore.WaitAsync();
                bool passed;
                try
                {
                    passed = await TestNode(nodeLink);
                }
                finally
                {
                    semaphore.Release();
                }

                if (passed)
                {
                    result.Status = AIChatNodeStatus.Passed;
                    result.StatusText = "✅ 可用";
                    lock (resultLock)
                    {
                        validNodes.Add(nodeLink);
                    }
                }
                else
                {
                    result.Status = AIChatNodeStatus.Failed;
                    result.StatusText = "❌ 失效";
                }
            }).ToList();

            await Task.WhenAll(testTasks);

            // Show results
            AddNodeResultMessage(results, validNodes.Count);

            if (validNodes.Count < recentNodes.Count)
            {
                var failedCount = recentNodes.Count - validNodes.Count;
                AddMessage(AIChatRole.AI, $"⚠️ 测试完成：{validNodes.Count} 个可用，{failedCount} 个失效。\n\n💡 建议定期点击「测试全部」按钮检查节点状态。");
            }
            else
            {
                AddMessage(AIChatRole.AI, $"✅ 测试完成：所有 **{recentNodes.Count}** 个节点均可用！");
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            AddMessage(AIChatRole.AI, $"❌ 测试过程中出现错误：\n\n`{ex.Message}`");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private async Task<string> DownloadUrlContent(string url)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            return await httpClient.GetStringAsync(url);
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"DownloadUrlContent failed: {ex.Message}");
            return string.Empty;
        }
    }

    private async Task<List<string>?> ExtractNodesFromContent(AIConfigItem aiConfig, string content, string sourceUrl)
    {
        // Fast path: try direct regex extraction
        var directNodes = ParseNodesFromText(content);
        if (directNodes.Count > 0)
        {
            return directNodes;
        }

        // Slow path: ask AI to analyze the content
        var truncatedContent = content.Length > 8000 ? content[..8000] + "\n... (内容已截断)" : content;
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
    /// Sends a free-form chat message to the Hermes API (OpenAI-compatible /chat/completions)
    /// and returns the assistant's reply.
    /// </summary>
    private async Task<string?> AskHermesAsync(AIConfigItem aiConfig, string userMessage)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(90);

            var requestBody = new
            {
                model = aiConfig.ModelId,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content =
                        @"你是嵌入在 LDv2rayN 客户端中的 AI 助手（模型 hermesAPI）。当前用户在 Windows 桌面软件里通过聊天窗口跟你对话。

背景：
- LDv2rayN 是一个代理/VPN 客户端，用于绕过中国大陆的网络访问限制，可以管理 vless/vmess/trojan/shadowsocks/hysteria2/tuic 等节点。
- 用户可以在此对话框直接输入节点链接（vmess:// vless:// trojan:// ss:// hy2:// tuic:// 等）或 HTTP URL，程序会自动分析、测活、加入分组。
- 用户也可以输入简短的中文/英文命令让程序执行操作，例如：「列表」查节点、「分组」查分组、「状态」查应用状态、「切换 xxx」切换分组、「删除 xxx」删除节点、「添加 <链接>」添加节点、「测试 <链接>」测活、「代理 clear」关闭系统代理。

回答要求：
1. 用中文回复，简洁但有帮助。
2. 当用户的问题可以通过上述命令完成时，主动告诉用户「你可以在对话框输入 XXX」。
3. 当用户询问与 VPN/代理/网络相关的知识时，尽量给出实用建议（例如如何选择节点、如何检测节点可用性、如何配置绕过限制）。
4. 当用户粘贴了一个 URL 或节点链接，程序会自动处理，你不要重复处理，只需要确认。
5. 支持 Markdown 格式（**粗体**、`code`、列表）。"
                    },
                    new { role = "user", content = userMessage }
                },
                temperature = 0.7,
                max_tokens = 1500
            };

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
                return $"❌ AI 服务返回错误：{response.StatusCode}\n\n`{errBody}`";
            }

            var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var reply = choices[0].GetProperty("message").GetProperty("content").GetString();
                if (!string.IsNullOrWhiteSpace(reply))
                    return reply;
            }

            return "（AI 未返回内容）";
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            return $"❌ 请求 AI 服务失败：`{ex.Message}`";
        }
    }

    private async Task<bool> TestNode(string nodeLink)
    {
        try
        {
            var profile = FmtHandler.ResolveConfig(nodeLink, out _);
            if (profile == null || !profile.IsValid())
            {
                return false;
            }

            var address = profile.Address;
            var port = profile.Port;

            if (address.IsNullOrEmpty() || address.Equals("127.0.0.1") || port <= 0)
            {
                return true;
            }

            // TCP connect test with DNS fallback
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(address, port, cts.Token);
                return client.Connected;
            }
            catch
            {
                try
                {
                    var hostEntry = await System.Net.Dns.GetHostEntryAsync(address);
                    return hostEntry.AddressList.Length > 0;
                }
                catch
                {
                    return false;
                }
            }
        }
        catch
        {
            return false;
        }
    }

    private List<string> ParseNodesFromText(string text)
    {
        var nodes = new List<string>();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim().TrimStart('-', '*', ' ', '•');
            if (string.IsNullOrEmpty(trimmed)) continue;

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
            s.Remarks.Equals(remarks, StringComparison.OrdinalIgnoreCase));

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
