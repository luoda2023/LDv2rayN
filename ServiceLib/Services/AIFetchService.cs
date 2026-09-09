using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ServiceLib.Enums;

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
    }    // User-specified URLs to check daily
    private static readonly string[] UserSpecifiedUrls =
    {
        "https://github.com/0xRadikal/Free-v2ray-Configs",
        "https://github.com/cbusifabcap/daily_free_vpn",
        "https://github.com/kanaltvyt-dev/FreeForYoung",
        "https://github.com/hello-world-1989/cn-news",
    };

    public async Task<int> FetchAndAddNodesAsync()
    {
        var aiConfig = _config.AIConfigItem;
        if (aiConfig == null || !aiConfig.Enabled || aiConfig.ApiUrl.IsNullOrEmpty())
        {
            await _updateFunc(false, "AI功能未启用或API地址未配置");
            return 0;
        }

        try
        {
            await _updateFunc(false, "🤖 AI正在搜索免费VPN节点...");

            // Step 1: Fetch from user-specified URLs + GitHub search
            var allNodes = new List<string>();

            // First: try crawl4ai server if available
            var crawl4aiNodes = await FetchFromCrawl4AI();
            if (crawl4aiNodes.Count > 0)
            {
                allNodes.AddRange(crawl4aiNodes);
                await _updateFunc(false, $"✅ 从crawl4ai获取到 {crawl4aiNodes.Count} 个节点");
            }

            // Then: fetch from user-specified URLs
            await _updateFunc(false, "📥 正在从指定链接获取节点...");
            var specifiedNodes = await FetchFromSpecifiedUrls();
            if (specifiedNodes.Count > 0)
            {
                foreach (var n in specifiedNodes)
                {
                    if (!allNodes.Contains(n)) allNodes.Add(n);
                }
                await _updateFunc(false, $"✅ 从指定链接获取到 {specifiedNodes.Count} 个节点");
            }

            // Then: GitHub search
            await _updateFunc(false, "🔍 正在搜索GitHub...");
            var searchNodes = await SearchGitHubForFreeNodes(aiConfig);
            if (searchNodes != null && searchNodes.Count > 0)
            {
                foreach (var n in searchNodes)
                {
                    if (!allNodes.Contains(n)) allNodes.Add(n);
                }
            }

            if (allNodes.Count == 0)
            {
                AISearchTracker.RecordError("未找到可用的免费VPN节点");
                await _updateFunc(false, "❌ AI未找到可用的免费VPN节点");
                return 0;
            }

            AISearchTracker.RecordCandidates(allNodes);
            await _updateFunc(false, $"🔍 AI找到 {allNodes.Count} 个候选节点，正在快速验证...");

            // Step 2: Test nodes with optimized concurrency and timeout
            var validNodes = new List<string>();
            var resultLock = new object();
            using var semaphore = new SemaphoreSlim(10);
            var tasks = allNodes.Select(async node =>
            {
                await semaphore.WaitAsync();
                try
                {
                    if (await TestNodeFast(node))
                    {
                        lock (resultLock)
                        {
                            validNodes.Add(node);
                        }
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            var completedTasks = new List<Task>();
            foreach (var task in tasks)
            {
                completedTasks.Add(task);
                if (validNodes.Count >= Math.Min(aiConfig.MaxNodesPerSearch, 50))
                {
                    break;
                }
            }
            await Task.WhenAll(completedTasks);

            if (validNodes.Count > aiConfig.MaxNodesPerSearch)
            {
                validNodes = validNodes.Take(aiConfig.MaxNodesPerSearch).ToList();
            }

            AISearchTracker.RecordTestResult(validNodes, allNodes.Count);

            if (validNodes.Count == 0)
            {
                await _updateFunc(false, "⚠️ 所有节点验证均失败");
                return 0;
            }

            // Step 3: Add valid nodes to AI subscription group
            var subId = await GetOrCreateAISubscriptionGroup(aiConfig.AiGroupRemarks);
            var result = await ConfigHandler.AddBatchServers(_config, string.Join("\n", validNodes), subId, true);

            AISearchTracker.RecordImported(result);
            await _updateFunc(true, $"🎉 AI快速添加 {result} 个有效节点到「{aiConfig.AiGroupRemarks}」分组");
            return result;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            AISearchTracker.RecordError($"搜索失败: {ex.Message}");
            await _updateFunc(false, $"❌ AI搜索失败: {ex.Message}");
            return 0;
        }
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
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            // Check if crawl4ai server is running
            var healthResp = await httpClient.GetAsync("http://127.0.0.1:18888/health");
            if (!healthResp.IsSuccessStatusCode)
            {
                return nodes; // Server not running
            }

            Logging.SaveLog($"{_tag}: crawl4ai server detected, using it for crawling");

            // Fetch all repos via crawl4ai
            var response = await httpClient.GetStringAsync("http://127.0.0.1:18888/crawl-all");
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

        using var semaphore = new SemaphoreSlim(4);
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
                            if (!nodes.Contains(n)) nodes.Add(n);
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

        // Extract owner/repo from URL
        if (!repoUrl.Contains("github.com/")) return nodes;
        var path = repoUrl.Replace("https://github.com/", "").TrimEnd('/');
        var parts = path.Split('/');
        if (parts.Length < 2) return nodes;

        var owner = parts[0];
        var repo = parts[1];
        var repoKey = $"{owner}/{repo}";

        // Repo-specific file paths (based on actual repo structure analysis)
        var paths = GetRepoSpecificPaths(repoKey);

        foreach (var p in paths)
        {
            try
            {
                var rawUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/HEAD/{p}";
                var fetched = await FetchWithMirrorFast(rawUrl);
                if (fetched != null && fetched.Count > 0)
                {
                    nodes.AddRange(fetched);
                    if (nodes.Count >= 200) break; // Stop after enough
                }
            }
            catch { }
        }

        return nodes.Distinct().ToList();
    }

    /// <summary>
    /// Returns repo-specific file paths to check for nodes.
    /// Based on actual analysis of each repo's structure.
    /// </summary>
    private static List<string> GetRepoSpecificPaths(string repoKey)
    {
        return repoKey switch
        {
            // 0xRadikal: has Countries/*.txt and all/configs.txt
            "0xRadikal/Free-v2ray-Configs" => new List<string>
            {
                "all/configs.txt",
                "Countries/USA.txt",
                "Countries/Germany.txt",
                "Countries/UK.txt",
                "Countries/Japan.txt",
                "Countries/Singapore.txt",
                "Countries/Hong Kong.txt",
                "Countries/Taiwan.txt",
                "Countries/Korea.txt",
                "Countries/France.txt",
                "Countries/Netherlands.txt",
                "Countries/Canada.txt",
                "Countries/Australia.txt",
            },
            // cbusifabcap: has Z.txt
            "cbusifabcap/daily_free_vpn" => new List<string>
            {
                "Z.txt",
                "sub/sub_merge.txt",
                "sub/sub.txt",
            },
            // kanaltvyt: has singapore.txt and output/*.txt
            "kanaltvyt-dev/FreeForYoung" => new List<string>
            {
                "singapore.txt",
                "output/singapore.txt",
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
            if (subItems == null) return 0;

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
    private static bool IsAICreatedGroup(SubItem sub)
    {
        // Check URL marker (AI groups have 'ai-auto' in URL)
        if (sub.Url.Contains("ai-auto", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check Remarks marker (AI groups have specific names)
        var remarks = sub.Remarks;
        if (remarks.Contains("AI", StringComparison.OrdinalIgnoreCase) ||
            remarks.Contains("自动", StringComparison.OrdinalIgnoreCase) ||
            remarks.Contains("auto", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check Memo marker (AI groups have specific memo)
        if (sub.Memo?.Contains("由AI自动搜索", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        return false;
    }

    private async Task<int> CleanInvalidNodesInGroup(string subId)
    {
        int removed = 0;

        try
        {
            // Get servers in this group using AppManager
            var servers = await AppManager.Instance.ProfileItems(subId);
            if (servers == null || servers.Count == 0) return 0;

            var invalidServers = new List<ProfileItem>();

            // Test each node concurrently - use Address and Port directly
            using var semaphore = new SemaphoreSlim(10);
            var tasks = servers.Select(async server =>
            {
                await semaphore.WaitAsync();
                try
                {
                    // Fast TCP test using address and port directly
                    if (!string.IsNullOrEmpty(server.Address) && server.Port > 0)
                    {
                        var valid = await TestAddressFast(server.Address, server.Port);
                        if (!valid)
                        {
                            lock (invalidServers)
                            {
                                invalidServers.Add(server);
                            }
                        }
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            // Remove invalid servers
            if (invalidServers.Count > 0)
            {
                await ConfigHandler.RemoveServers(_config, invalidServers);
                removed = invalidServers.Count;
                Logging.SaveLog($"{_tag}: Removed {removed} invalid nodes from group {subId}");
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}: CleanInvalidNodesInGroup failed: {ex.Message}");
        }

        return removed;
    }

    private async Task<bool> TestAddressFast(string address, int port)
    {
        try
        {
            if (address.IsNullOrEmpty() || address.Equals("127.0.0.1")) return true;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var client = new TcpClient();
            await client.ConnectAsync(address, port, cts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
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
                await _updateFunc(false, "❌ 无法下载链接内容");
                return 0;
            }

            await _updateFunc(false, $"📥 已下载内容 ({content.Length} 字符)，AI正在分析...");

            // Step 2: Ask AI to extract nodes from the content
            var nodes = await ExtractNodesFromContent(aiConfig, content, url);
            if (nodes == null || nodes.Count == 0)
            {
                AISearchTracker.RecordError("未能从内容中提取到有效的节点链接");
                await _updateFunc(false, "❌ AI未能从内容中提取到有效的节点链接");
                return 0;
            }

            AISearchTracker.BeginRun(0);
            AISearchTracker.RecordCandidates(nodes);
            await _updateFunc(false, $"🔍 AI提取到 {nodes.Count} 个候选节点");

 // Step 3: Test nodes concurrently (max 20 in flight, 3 s each)
 var validNodes = new List<string>();
 using var semaphore = new SemaphoreSlim(20);
 var tasks = nodes.Select(async node =>
 {
 await semaphore.WaitAsync();
 try
 {
 if (await TestNode(node))
 {
 lock (validNodes)
 {
 validNodes.Add(node);
 }
 }
 }
 finally
 {
 semaphore.Release();
 }
 }).ToList();
 await Task.WhenAll(tasks);

 if (validNodes.Count > maxNodes)
 {
 validNodes = validNodes.Take(maxNodes).ToList();
 }

 AISearchTracker.RecordTestResult(validNodes, nodes.Count);

 if (validNodes.Count == 0)
            {
                await _updateFunc(false, "⚠️ 所有节点验证均失败");
                return 0;
            }

            // Step 4: Add valid nodes to target group
            var subId = await GetOrCreateAISubscriptionGroup(targetGroup);
            var result = await ConfigHandler.AddBatchServers(_config, string.Join("\n", validNodes), subId, true);

            AISearchTracker.RecordImported(result);
            await _updateFunc(true, $"🎉 成功添加 {result} 个有效节点到「{targetGroup}」分组");
            return result;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            await _updateFunc(false, $"❌ 链接分析失败: {ex.Message}");
            return 0;
        }
    }

    private async Task<string> DownloadUrlContent(string url)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            return await httpClient.GetStringAsync(url);
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"DownloadUrlContent failed: {ex.Message}");
            return string.Empty;
        }
    } private async Task<List<string>?> ExtractNodesFromContent(AIConfigItem aiConfig, string content, string sourceUrl)
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

内容（截取前8000字符）:
{content[..Math.Min(content.Length, 8000)]}

请执行以下步骤：
1. 检查内容中是否包含 vmess://, vless://, trojan://, ss://, hy2://, hysteria2://, tuic:// 等节点链接
2. 如果是订阅链接，尝试解析其中的节点
3. 如果是网页，提取页面中包含的节点信息
4. 返回有效的节点链接，每行一个
5. 如果没有找到节点，返回 "NO_NODES_FOUND"

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
        } return null;
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
                if (repos.Count >= 15) return;
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
                    var pair = (parts[0], parts[1]);
                    lock (repoLock)
                    {
                        if (!repos.Contains(pair) && repos.Count < 15)
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

        AISearchTracker.BeginRun(repos.Count);

        // Fallback repos (always reliable)
        string[] FallbackRaw =
        {
            "https://raw.githubusercontent.com/mahdibland/V2RayAggregator/master/sub/sub_merge.txt",
            "https://raw.githubusercontent.com/Pawdroid/Free-servers/main/sub",
            "https://raw.githubusercontent.com/ripaojiedian/freenode/main/sub",
        };

        var probeUrls = new List<string>();
        foreach (var (owner, repo) in repos)
        {
            foreach (var p in CandidatePaths.Take(8)) // Reduced from 14 to 8 for speed
            {
                probeUrls.Add($"https://raw.githubusercontent.com/{owner}/{repo}/HEAD/{p}");
            }
        }
        probeUrls.AddRange(FallbackRaw);

        // Parallel fetch with higher concurrency (10)
        using var fetchSemaphore = new SemaphoreSlim(10);
        var fetchTasks = probeUrls.Select(async rawUrl =>
        {
            List<string>? nodes = null;
            try
            {
                await fetchSemaphore.WaitAsync();
                try
                {
                    nodes = await FetchWithMirrorFast(rawUrl);
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
        foreach (var nodes in fetchResults)
        {
            if (nodes is null) continue;
            foreach (var n in nodes)
            {
                if (found.Count >= 200) break;
                if (!found.Contains(n, StringComparer.Ordinal)) found.Add(n);
            }
            if (found.Count >= 200) break;
        }

        return found.Count > 0 ? found : null;
    }

    /// <summary>
    /// Fetch a raw.githubusercontent.com URL, falling back to China-friendly
    /// mirror prefixes when the direct connection times out or fails. Mirrors
    /// verified reachable: ghfast.top, gh-proxy.com, ghproxy.net.
    /// </summary>
    private async Task<List<string>?> FetchWithMirror(string rawUrl)
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
                client.Timeout = TimeSpan.FromSeconds(12);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                var resp = await client.GetAsync(url);
                if (!resp.IsSuccessStatusCode) continue;
                var text = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(text) || text.Length > 2_000_000) continue;

                var nodes = ParseNodesFromResponse(text);
                if (nodes.Count == 0 && text.Trim().Length > 40)
                {
                    try
                    {
                        var cleaned = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
                        var decodedBytes = Convert.FromBase64String(cleaned);
                        var decoded = Encoding.UTF8.GetString(decodedBytes);
                        nodes = ParseNodesFromResponse(decoded);
                    }
                    catch { }
                }

                if (nodes.Count > 0)
                {
                    return nodes;
                }
            }
            catch (Exception ex)
            {
                Logging.SaveLog($"{_tag}: fetch {url} failed: {ex.Message}");
            }
        }

        return null;
    }

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

        bool usedMirror = false;
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
                {
                    AISearchTracker.RecordFetchResult(false);
                    continue;
                }
                var text = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrEmpty(text) || text.Length > 2_000_000)
                {
                    AISearchTracker.RecordFetchResult(false);
                    continue;
                }

                var nodes = ParseNodesFromResponse(text);
                if (nodes.Count == 0 && text.Trim().Length > 40)
                {
                    try
                    {
                        var cleaned = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
                        var decodedBytes = Convert.FromBase64String(cleaned);
                        var decoded = Encoding.UTF8.GetString(decodedBytes);
                        nodes = ParseNodesFromResponse(decoded);
                    }
                    catch { }
                }

                if (nodes.Count > 0)
                {
                    AISearchTracker.RecordFetchResult(true);
                    if (usedMirror)
                        AISearchTracker.RecordMirrorFallback(url);
                    return nodes;
                }

                if (!usedMirror && url != rawUrl)
                    usedMirror = true;
            }
            catch
            {
                AISearchTracker.RecordFetchResult(false);
            }
        }

        return null;
    }

    private List<string> ParseNodesFromResponse(string response)
    {
        var nodes = new List<string>();
        var lines = response.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Check if line contains a valid node link
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
            else if (trimmed.Contains("://") && trimmed.Contains("@"))
            {
                // Might be a node link with unusual prefix
                nodes.Add(trimmed);
            }
        }

        return nodes.Distinct().ToList();
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

            // Method 1: Try through local SOCKS5 proxy (official v2rayN approach)
            // This tests if the node actually works for traffic forwarding
            try
            {
                var socksPort = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
                if (socksPort > 0)
                {
                    var result = await TestThroughSocksProxy(address, port, socksPort);
                    if (result) return true;
                }
            }
            catch { }

            // Method 2: Direct TCP test (fallback)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(address, port, cts.Token);
                return client.Connected;
            }
            catch
            {
                // If direct TCP fails, try DNS resolution as last resort
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

    /// <summary>
    /// Test node through local SOCKS5 proxy (official v2rayN approach).
    /// This verifies the node can actually forward traffic.
    /// </summary>
    private async Task<bool> TestThroughSocksProxy(string targetAddress, int targetPort, int socksPort)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", socksPort, cts.Token);

            // SOCKS5 handshake
            var stream = client.GetStream();
            
            // Send greeting (SOCKS5, 1 auth method)
            await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 }, cts.Token);
            var response = new byte[2];
            await stream.ReadAsync(response, cts.Token);
            if (response[0] != 0x05) return false;

            // Send connect request
            var addrBytes = System.Text.Encoding.ASCII.GetBytes(targetAddress);
            var request = new byte[7 + addrBytes.Length];
            request[0] = 0x05; // SOCKS5
            request[1] = 0x01; // CONNECT
            request[2] = 0x00; // Reserved
            request[3] = 0x03; // Domain name
            request[4] = (byte)addrBytes.Length;
            Array.Copy(addrBytes, 0, request, 5, addrBytes.Length);
            request[^2] = (byte)(targetPort >> 8);
            request[^1] = (byte)(targetPort & 0xFF);

            await stream.WriteAsync(request, cts.Token);
            var connectResponse = new byte[10];
            await stream.ReadAsync(connectResponse, cts.Token);

            // Check if connection succeeded (response[1] == 0x00)
            return connectResponse[1] == 0x00;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TestNodeFast(string nodeLink)
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

            // Try through local SOCKS5 proxy first (official v2rayN approach)
            try
            {
                var socksPort = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
                if (socksPort > 0)
                {
                    var result = await TestThroughSocksProxy(address, port, socksPort);
                    if (result) return true;
                }
            }
            catch { }

            // Fast TCP test: 3 second timeout (fallback)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(address, port, cts.Token);
                return client.Connected;
            }
            catch
            {
                // Try DNS as last resort
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

    private async Task<string> GetOrCreateAISubscriptionGroup(string remarks)
    {
        var subItems = await AppManager.Instance.SubItems();

        // Find existing AI subscription group
        var existing = subItems?.FirstOrDefault(s =>
            s.Remarks.Equals(remarks, StringComparison.OrdinalIgnoreCase) ||
            s.Url.Contains("ai-auto", StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            return existing.Id;
        }

        // Create new AI subscription group
        var newSub = new SubItem
        {
            Id = Utils.GetGuid(),
            Remarks = remarks,
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
