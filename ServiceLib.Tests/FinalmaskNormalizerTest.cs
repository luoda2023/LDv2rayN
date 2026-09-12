using System.Text.Json.Nodes;
using ServiceLib.Common;

namespace ServiceLib.Tests;

/// <summary>
/// finalmask（fm 参数）规范化回归测试。
///
/// 背景：xray 的 fragment mask 只认单值 length 字段算 LengthMin，
/// 算出 &lt;= 0 就拒绝整份配置文件。节点 URI 里常见的 fm 只带 lengths 数组、
/// 没有 length，一条这种节点混进批量测速 / 主配置，整批全部报废
/// （实测日志：LengthMin can't be 0 → 0/50 nodes reachable → 死节点清不掉、
/// 新候选全部回退 TCP 兜底入库）。
/// </summary>
public class FinalmaskNormalizerTest
{
    /// <summary>线上实际出现过的毒载荷：只有 lengths 数组，没有 length。</summary>
    private const string PoisonFromLog =
        """{"tcp":[{"type":"fragment","settings":{"packets":"tlshello","lengths":["5","94","1"],"delays":["0"],"maxSplit":"0"}}]}""";

    [Test]
    public async Task FragmentWithoutLength_IsRepaired()
    {
        var normalized = FinalmaskNormalizer.Normalize(PoisonFromLog);
        await normalized.Should().NotBeNull();

        var settings = normalized!["tcp"]![0]!["settings"]!.AsObject();
        var length = settings["length"]?.ToString() ?? string.Empty;
        var lowerBoundPart = length.Split('-').FirstOrDefault() ?? string.Empty;
        var parsed = int.TryParse(lowerBoundPart, out var min) ? min : 0;
        await (parsed > 0).Should().BeTrue();
    }

    [Test]
    public async Task FragmentWithZeroLowerBound_IsDropped()
    {
        var json = """{"tcp":[{"type":"fragment","settings":{"packets":"tlshello","lengths":["0","0-10"]}}]}""";
        var normalized = FinalmaskNormalizer.Normalize(json);

        // 补不出合法 length → 这一段 mask 必须被丢弃，而不是原样放行毒配置
        var kept = normalized?["tcp"] is JsonArray arr && arr.Count > 0;
        await kept.Should().BeFalse();
    }

    [Test]
    public async Task UnrepairableMasks_RemoveEmptyContainers()
    {
        var json = """{"tcp":[{"type":"fragment","settings":{"lengths":["0"]}}],"udp":[{"type":"fragment","settings":{"lengths":["0"]}}]}""";
        var normalized = FinalmaskNormalizer.Normalize(json);
        await normalized.Should().BeNull();
    }

    [Test]
    public async Task NonFragmentMasks_ArePreserved()
    {
        var json = """{"udp":[{"type":"noise","settings":{"lengths":["0"]}}]}""";
        var normalized = FinalmaskNormalizer.Normalize(json);
        await normalized.Should().NotBeNull();
        await normalized!["udp"]![0]!["type"]!.ToString().Should().BeEqualTo("noise");
    }

    [Test]
    public async Task InvalidJson_ReturnsNull()
    {
        await FinalmaskNormalizer.Normalize("not-json").Should().BeNull();
        await FinalmaskNormalizer.Normalize("").Should().BeNull();
        await FinalmaskNormalizer.Normalize(null).Should().BeNull();
    }

    [Test]
    public async Task HasUsableRange_MatchesXrayLengthMinRule()
    {
        await FinalmaskNormalizer.HasUsableRange("50-100").Should().BeTrue();
        await FinalmaskNormalizer.HasUsableRange("5").Should().BeTrue();
        await FinalmaskNormalizer.HasUsableRange("0-100").Should().BeFalse();
        await FinalmaskNormalizer.HasUsableRange("").Should().BeFalse();
        await FinalmaskNormalizer.HasUsableRange(null).Should().BeFalse();
    }
}
