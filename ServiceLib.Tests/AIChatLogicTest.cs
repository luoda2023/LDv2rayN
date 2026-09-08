using ServiceLib.Models;
using ServiceLib.Models.Configs;

namespace ServiceLib.Tests;

/// <summary>
/// Unit tests for AI chat models, node parsing, config defaults.
/// </summary>
public class AIChatLogicTest
{
    [Test]
    public async Task AIConfigItem_DefaultsAreCorrect()
    {
        var config = new AIConfigItem();
        await config.Enabled.Should().BeTrue();
        await config.AiGroupRemarks.Should().BeEqualTo("AI自动获取");
        await config.MaxNodesPerSearch.Should().BeEqualTo(50);
        await config.SearchIntervalMinutes.Should().BeEqualTo(60);
        await config.AutoCrawlEnabled.Should().BeFalse();
        await config.AutoCrawlIntervalMinutes.Should().BeEqualTo(120);
        await config.ModelId.Should().BeEqualTo("hermesAPI");
    }

    [Test]
    public async Task AIChatMessage_IsUserProperty_WhenRoleIsUser()
    {
        var msg = new AIChatMessage
        {
            Role = AIChatRole.User,
            Content = "test",
            Timestamp = DateTime.Now
        };
        await msg.IsUser.Should().BeTrue();
        await msg.IsAI.Should().BeFalse();
        await msg.IsSystem.Should().BeFalse();
    }

    [Test]
    public async Task AIChatMessage_IsAIProperty_WhenRoleIsAI()
    {
        var msg = new AIChatMessage
        {
            Role = AIChatRole.AI,
            Content = "response",
            Timestamp = DateTime.Now
        };
        await msg.IsUser.Should().BeFalse();
        await msg.IsAI.Should().BeTrue();
        await msg.IsSystem.Should().BeFalse();
    }

    [Test]
    public async Task AIChatNodeResult_DefaultStatus()
    {
        var result = new AIChatNodeResult
        {
            NodeLink = "vmess://abc123",
            DisplayName = "test-node",
            Protocol = "VMess",
            Address = "1.2.3.4",
            Status = AIChatNodeStatus.Testing,
            StatusText = "testing..."
        };

        await result.Status.Should().BeEqualTo(AIChatNodeStatus.Testing);
        await result.Protocol.Should().BeEqualTo("VMess");
        await result.Address.Should().BeEqualTo("1.2.3.4");
    }

    [Test]
    public async Task NodeProtocol_VMess_Detected()
    {
        var detected = DetectProtocol("vmess://eyJhZGQiOiIxLjIuMy40In0=#TestNode");
        await detected.Should().BeEqualTo("VMess");
    }

    [Test]
    public async Task NodeProtocol_VLESS_Detected()
    {
        var detected = DetectProtocol("vless://uuid@1.2.3.4:443?security=tls#VlessNode");
        await detected.Should().BeEqualTo("VLESS");
    }

    [Test]
    public async Task NodeProtocol_Trojan_Detected()
    {
        var detected = DetectProtocol("trojan://password@1.2.3.4:443?security=tls#TrojanNode");
        await detected.Should().BeEqualTo("Trojan");
    }

    [Test]
    public async Task NodeProtocol_Shadowsocks_Detected()
    {
        var detected = DetectProtocol("ss://YWVzLTI1Ni1nY206cGFzc3dvcmQ=@1.2.3.4:8388#SSNode");
        await detected.Should().BeEqualTo("Shadowsocks");
    }

    [Test]
    public async Task NodeProtocol_Hysteria2_Detected()
    {
        var detected = DetectProtocol("hy2://password@1.2.3.4:443#Hysteria2");
        await detected.Should().BeEqualTo("Hysteria2");
    }

    [Test]
    public async Task NodeNameExtraction_FromFragment()
    {
        var name = ExtractNodeName("vmess://abc#MyCustomNode");
        await name.Should().BeEqualTo("MyCustomNode");
    }

    [Test]
    public async Task NodeNameExtraction_FromFragmentWithServer()
    {
        var name = ExtractNodeName("vless://uuid@1.2.3.4:443#Tokyo-Server");
        await name.Should().BeEqualTo("Tokyo-Server");
    }

    [Test]
    public async Task NodeAddressExtraction_FromAtSyntax()
    {
        var link = "vless://uuid@1.2.3.4:443?security=tls#Node";
        var afterAt = link.Split('@').Last();
        var address = afterAt.Split(':').First().Split('?').First();
        await address.Should().BeEqualTo("1.2.3.4");
    }

    [Test]
    public async Task TunModeItem_KillSwitch_Defaults()
    {
        var tunMode = new TunModeItem();
        await tunMode.EnableKillSwitch.Should().BeTrue();
        await tunMode.AutoRoute.Should().BeTrue();
        await tunMode.StrictRoute.Should().BeTrue();
        await tunMode.EnableIPv6Address.Should().BeFalse();
    }

    [Test]
    public async Task AIChatMessage_NodeResults_DefaultsToNull()
    {
        var msg = new AIChatMessage
        {
            Role = AIChatRole.System,
            Content = "results",
            Timestamp = DateTime.Now
        };
        await msg.NodeResults.Should().BeNull();
    }

    private static string DetectProtocol(string nodeLink)
    {
        if (nodeLink.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase)) return "VMess";
        if (nodeLink.StartsWith("vless://", StringComparison.OrdinalIgnoreCase)) return "VLESS";
        if (nodeLink.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase)) return "Trojan";
        if (nodeLink.StartsWith("ss://", StringComparison.OrdinalIgnoreCase)) return "Shadowsocks";
        if (nodeLink.StartsWith("hy2://", StringComparison.OrdinalIgnoreCase) || nodeLink.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase)) return "Hysteria2";
        if (nodeLink.StartsWith("tuic://", StringComparison.OrdinalIgnoreCase)) return "TUIC";
        return "Unknown";
    }

    private static string ExtractNodeName(string nodeLink)
    {
        try
        {
            if (nodeLink.Contains('#'))
            {
                var name = Uri.UnescapeDataString(nodeLink.Split('#').Last());
                return name.Length > 30 ? name[..30] + "..." : name;
            }
            if (nodeLink.Contains('@'))
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
