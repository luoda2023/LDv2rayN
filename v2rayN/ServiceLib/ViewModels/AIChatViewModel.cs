using System.Collections.ObjectModel;
using System.Net.Http.Headers;
using ServiceLib.Models;

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

    /// <summary>Observable collection of chat messages for the UI</summary>
    public ObservableCollection<AIChatMessage> Messages { get; } = new();

    public AIChatViewModel()
    {
        var aiConfig = _config.AIConfigItem ?? new AIConfigItem();
        TargetGroup = aiConfig.AiGroupRemarks ?? "AI自动获取";
        MaxNodes = aiConfig.MaxNodesPerSearch;

        // Show welcome message
        AddMessage(AIChatRole.AI, @"**欢迎使用 AI智能代理助手** 🤖

我可以帮你完成以下任务：

🔗 **分析链接** — 粘贴一个URL（GitHub仓库、订阅链接、节点分享页面等），我会自动下载内容、提取VPN节点、验证可用性，然后添加到你的分组中。

🔍 **自动搜索** — 我会自动在GitHub上搜索最新的免费VPN节点，逐个验证后添加到分组。

💡 **使用方法**：在下方输入框粘贴链接后按回车，或点击「分析链接」按钮。点击「自动搜索」开始全自动搜索。

支持的节点协议：`vmess://` `vless://` `trojan://` `ss://` `hy2://` `tuic://`");
    }

    /// <summary>Analyze a user-submitted URL: download → extract nodes → test → add to group</summary>
    public async Task AnalyzeUrlAsync()
    {
        if (IsProcessing || string.IsNullOrWhiteSpace(ChatInput))
        {
            return;
        }

        var url = ChatInput.Trim();
        ChatInput = string.Empty;
        IsProcessing = true;

        AddMessage(AIChatRole.User, $"🔗 分析这个链接：{url}");

        try
        {
            var aiConfig = _config.AIConfigItem ?? new AIConfigItem();
            if (!aiConfig.Enabled || aiConfig.ApiUrl.IsNullOrEmpty())
            {
                AddMessage(AIChatRole.AI, "❌ **AI功能未启用**\n\n请先在「设置 → AI智能获取设置」中配置API地址和密钥，然后再使用AI助手。");
                return;
            }

            AddMessage(AIChatRole.AI, "🔍 正在下载并分析链接内容...");

            // Step 1: Download URL content
            var content = await DownloadUrlContent(url);
            if (content.IsNullOrEmpty())
            {
                AddMessage(AIChatRole.AI, $"❌ 无法下载链接内容。请检查URL是否正确、网络是否正常。\n\n`{url}`");
                return;
            }

            AddMessage(AIChatRole.AI, $"📥 已下载内容（{content.Length:N0} 字符），正在让AI分析提取节点...");

            // Step 2: Use AI to extract nodes from content
            var nodes = await ExtractNodesFromContent(aiConfig, content, url);
            if (nodes == null || nodes.Count == 0)
            {
                AddMessage(AIChatRole.AI, $"⚠️ 未能从链接中提取到有效的VPN节点。\n\n可能原因：\n- 页面内容不包含节点链接\n- 节点格式无法识别\n- 需要登录才能查看内容\n\n**来源URL**: `{url}`");
                return;
            }

            AddMessage(AIChatRole.AI, $"🔍 从内容中提取到 **{nodes.Count}** 个候选节点，开始逐个验证...");

            // Step 3: Test each node
            var results = new List<AIChatNodeResult>();
            var validNodes = new List<string>();

            foreach (var nodeLink in nodes)
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
                results.Add(result);

                if (AutoTest)
                {
                    if (await TestNode(nodeLink))
                    {
                        result.Status = AIChatNodeStatus.Passed;
                        result.StatusText = "✅ 验证通过";
                        validNodes.Add(nodeLink);
                    }
                    else
                    {
                        result.Status = AIChatNodeStatus.Failed;
                        result.StatusText = "❌ 验证失败";
                    }
                }
                else
                {
                    result.Status = AIChatNodeStatus.Passed;
                    result.StatusText = "⏭️ 跳过验证";
                    validNodes.Add(nodeLink);
                }

                if (validNodes.Count >= MaxNodes)
                {
                    break;
                }
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
        Messages.Add(new AIChatMessage
        {
            Role = AIChatRole.System,
            Content = $"节点验证结果：{passedCount}/{results.Count} 通过",
            Timestamp = DateTime.Now,
            NodeResults = results
        });
    }

    #endregion

    #region Content Helpers

    private string ExtractProtocol(string nodeLink)
    {
        if (nodeLink.StartsWith("vmess://")) return "VMess";
        if (nodeLink.StartsWith("vless://")) return "VLESS";
        if (nodeLink.StartsWith("trojan://")) return "Trojan";
        if (nodeLink.StartsWith("ss://")) return "Shadowsocks";
        if (nodeLink.StartsWith("hy2://") || nodeLink.StartsWith("hysteria2://")) return "Hysteria2";
        if (nodeLink.StartsWith("tuic://")) return "TUIC";
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
            return ParseNodesFromText(message ?? string.Empty);
        }

        return null;
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

            // DNS resolution check
            if (profile.Address.IsNotEmpty() && !profile.Address.Equals("127.0.0.1"))
            {
                try
                {
                    var hostEntry = await System.Net.Dns.GetHostEntryAsync(profile.Address);
                    return hostEntry.AddressList.Length > 0;
                }
                catch
                {
                    return false;
                }
            }

            return true;
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
                trimmed.StartsWith("tuic://", StringComparison.OrdinalIgnoreCase))
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
