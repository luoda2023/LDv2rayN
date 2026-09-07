using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

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
    }

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

            // Step 1: Ask AI to search GitHub for free VPN nodes
            var nodes = await SearchGitHubForFreeNodes(aiConfig);
            if (nodes == null || nodes.Count == 0)
            {
                await _updateFunc(false, "❌ AI未找到可用的免费VPN节点");
                return 0;
            }

            await _updateFunc(false, $"🔍 AI找到 {nodes.Count} 个候选节点，正在验证...");

            // Step 2: Test each node
            var validNodes = new List<string>();
            foreach (var node in nodes)
            {
                if (await TestNode(node))
                {
                    validNodes.Add(node);
                    await _updateFunc(false, $"✅ 节点验证通过: {ExtractNodeName(node)}");
                }
                else
                {
                    await _updateFunc(false, $"❌ 节点验证失败: {ExtractNodeName(node)}");
                }

                if (validNodes.Count >= aiConfig.MaxNodesPerSearch)
                {
                    break;
                }
            }

            if (validNodes.Count == 0)
            {
                await _updateFunc(false, "⚠️ 所有节点验证均失败");
                return 0;
            }

            // Step 3: Add valid nodes to AI subscription group
            var subId = await GetOrCreateAISubscriptionGroup(aiConfig.AiGroupRemarks);
            var result = await ConfigHandler.AddBatchServers(_config, string.Join("\n", validNodes), subId, true);

            await _updateFunc(true, $"🎉 AI成功添加 {result} 个有效节点到「{aiConfig.AiGroupRemarks}」分组");
            return result;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            await _updateFunc(false, $"❌ AI搜索失败: {ex.Message}");
            return 0;
        }
    }

    private async Task<List<string>?> SearchGitHubForFreeNodes(AIConfigItem aiConfig)
    {
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(60);

        var prompt = """
你是一个VPN节点搜索助手。请搜索GitHub上最新的免费VPN/代理节点订阅链接。

搜索关键词：
1. free vpn subscription github
2. free v2ray nodes github
3. free clash nodes github  
4. free proxy list github
5. v2ray free nodes
6. clash free proxy

请执行以下步骤：
1. 搜索GitHub上包含免费VPN节点的仓库
2. 找到最新的订阅链接或节点分享
3. 提取有效的v2ray/vmess/vless/trojan/shadowsocks/hysteria2链接
4. 只返回有效的节点链接，每行一个

返回格式：每行一个节点链接（vmess://, vless://, trojan://, ss://, hy2:// 等）
""";

        var requestBody = new
        {
            model = aiConfig.ModelId,
            messages = new[]
            {
                new { role = "system", content = "你是一个专业的VPN节点搜索助手，专门搜索GitHub上的免费VPN节点。" },
                new { role = "user", content = prompt }
            },
            temperature = 0.3,
            max_tokens = 4000
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        if (aiConfig.ApiKey.IsNotEmpty())
        {
            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", aiConfig.ApiKey);
        }

        var response = await httpClient.PostAsync($"{aiConfig.ApiUrl}/chat/completions", content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(responseJson);

        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var message = choices[0].GetProperty("message").GetProperty("content").GetString();
            return ParseNodesFromResponse(message ?? string.Empty);
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
                trimmed.StartsWith("tuic://", StringComparison.OrdinalIgnoreCase))
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
            // Parse the node and try to connect
            var profile = FmtHandler.ResolveConfig(nodeLink, out _);
            if (profile == null || !profile.IsValid())
            {
                return false;
            }

            // Try a quick connectivity test via the node
            // For now, we do a basic validation - check if the address resolves
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
