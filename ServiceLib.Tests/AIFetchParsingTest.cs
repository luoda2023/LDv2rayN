using System.Reflection;
using System.Text;
using ServiceLib.Handler.Fmt;
using ServiceLib.Models.Configs;
using ServiceLib.Services;

namespace ServiceLib.Tests;

/// <summary>
/// AIFetchService.ParseNodesFromResponse 的解析回归测试。
///
/// 背景：旧实现要求节点链接「顶在行首」，并且有个兜底分支
/// 「含 :// 且含 @ 就当节点」。前者让 HTML / JSON 里夹带的节点全部漏掉，
/// 后者把邮箱、普通网页链接当成节点收下，挤占了后面的真实延迟验证名额。
/// 这里逐条锁住新行为。
/// </summary>
public class AIFetchParsingTest
{
    private static readonly string[] SampleLinks =
    [
        "trojan://pass123@example.com:443#hk-01",
        "vless://11111111-2222-3333-4444-555555555555@example.net:8443?encryption=none&security=tls&type=tcp#jp-02",
        "ss://YWVzLTI1Ni1nY206cGFzc3dvcmQ=@example.org:8388#sg-03",
    ];

    private static List<string> Parse(string content)
    {
        var svc = new AIFetchService(new Config(), (_, _) => Task.CompletedTask);
        var mi = typeof(AIFetchService).GetMethod(
            "ParseNodesFromResponse", BindingFlags.NonPublic | BindingFlags.Instance);
        if (mi is null)
        {
            throw new InvalidOperationException("ParseNodesFromResponse 不存在，签名可能被改过");
        }
        return (List<string>)mi.Invoke(svc, [content])!;
    }

    /// <summary>样本链接本身必须是合法节点，否则后面的断言全部失去意义。</summary>
    [Test]
    public async Task SampleLinks_AreValid()
    {
        var failed = new List<string>();
        foreach (var link in SampleLinks)
        {
            var profile = FmtHandler.ResolveConfig(link, out var msg);
            if (profile is null)
            {
                failed.Add($"{link} -> {msg}");
            }
        }

        // 断言为空列表：失败时输出里会直接列出是哪几条样本不合法
        await failed.Should().BeEmpty();
    }

    /// <summary>纯文本列表：每行一个节点，正常提取。</summary>
    [Test]
    public async Task PlainText_ParsesAll()
    {
        var content = string.Join("\n", SampleLinks);
        var nodes = Parse(content);
        await nodes.Count.Should().BeEqualTo(SampleLinks.Length);
    }

    /// <summary>
    /// HTML / JSON 里夹带的节点也要能捞出来。
    /// 旧实现只认行首，这种内容一个都取不到。
    /// </summary>
    [Test]
    public async Task NodesEmbeddedInHtml_AreExtracted()
    {
        var content = $$"""
            <html><body>
            <table><tr><td>香港</td><td><a href="{{SampleLinks[0]}}">链接</a></td></tr></table>
            <script>var cfg = {"server":"{{SampleLinks[1]}}","name":"jp"};</script>
            <p>备注：{{SampleLinks[2]}} 备用</p>
            </body></html>
            """;

        var nodes = Parse(content);
        await nodes.Count.Should().BeEqualTo(SampleLinks.Length);
    }

    /// <summary>
    /// 垃圾内容必须被丢掉：邮箱、普通网页、带凭据的 URL 都不是节点。
    /// 旧实现的「含 :// 且含 @」兜底会把它们全收下。
    /// </summary>
    [Test]
    public async Task JunkContent_IsRejected()
    {
        var content = """
            <a href="https://example.com/path">官网</a>
            <a href="https://user:pass@example.com/private">登录</a>
            联系邮箱 admin@example.com
            ftp://user@ftp.example.com/file
            """;

        var nodes = Parse(content);
        await nodes.Count.Should().BeEqualTo(0);
    }

    /// <summary>整段 Base64 的订阅（最常见的订阅形态）。</summary>
    [Test]
    public async Task WholeBodyBase64_IsDecoded()
    {
        var raw = string.Join("\n", SampleLinks);
        var content = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));

        var nodes = Parse(content);
        await nodes.Count.Should().BeEqualTo(SampleLinks.Length);
    }

    /// <summary>逐行 Base64：每行一个独立编码的节点。</summary>
    [Test]
    public async Task PerLineBase64_IsDecoded()
    {
        var content = string.Join("\n",
            SampleLinks.Select(l => Convert.ToBase64String(Encoding.UTF8.GetBytes(l))));

        var nodes = Parse(content);
        await nodes.Count.Should().BeEqualTo(SampleLinks.Length);
    }

    /// <summary>
    /// 同一个地址:端口换个备注名仍是同一台机器，必须去重。
    /// 否则同一台机器会在候选里出现几十次，反复占用验证名额。
    /// </summary>
    [Test]
    public async Task SameHostPort_DifferentRemark_IsDeduped()
    {
        var content = """
            trojan://pass123@example.com:443#hk-01
            trojan://pass123@example.com:443#hk-02
            trojan://pass123@example.com:443#香港节点
            """;

        var nodes = Parse(content);
        await nodes.Count.Should().BeEqualTo(1);
    }

    /// <summary>不同端口的同主机是不同节点，不能被误判成重复。</summary>
    [Test]
    public async Task SameHost_DifferentPort_AreKept()
    {
        var content = """
            trojan://pass123@example.com:443#a
            trojan://pass123@example.com:8443#b
            """;

        var nodes = Parse(content);
        await nodes.Count.Should().BeEqualTo(2);
    }

    /// <summary>不支持的协议（如 ssr）不该被当成节点收进来。</summary>
    [Test]
    public async Task UnsupportedScheme_IsDropped()
    {
        var content = "ssr://c2VydmVyOjEyMzQ6b3JpZ2luOmFlcy0yNTYtY2ZiOnBsYWluOmNHRnpjdw";
        var nodes = Parse(content);
        await nodes.Count.Should().BeEqualTo(0);
    }

    /// <summary>空内容、纯空白不该抛异常。</summary>
    [Test]
    public async Task EmptyContent_ReturnsEmpty()
    {
        await Parse("").Count.Should().BeEqualTo(0);
        await Parse("   \n\n  \t ").Count.Should().BeEqualTo(0);
    }
}
