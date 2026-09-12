using System.Reflection;
using System.Text;
using ServiceLib.Common;
using ServiceLib.Handler.Fmt;
using ServiceLib.Models.Configs;
using ServiceLib.Services;

namespace ServiceLib.Tests;

/// <summary>
/// ClashYamlNodeExtractor 的回归测试。
///
/// 背景：AI 采集会去 GitHub 上抓免费节点，其中相当一部分仓库给的是 Clash YAML
/// （proxies 数组）。那种格式里节点不是 URI，全量正则一条都抓不到 —— 以前碰到
/// 这类源就是「0 节点」。这里锁住「翻译得出来、且翻译出来的能用」这两件事。
///
/// 每条翻译结果都必须能被 FmtHandler 重新解析（Extract 内部已回验），
/// 所以断言分两层：数量对得上 + 逐条能解析。
/// </summary>
public class ClashYamlExtractorTest
{
    /// <summary>覆盖全部受支持协议的 Clash 订阅样本。</summary>
    private const string SupportedYaml = """
        port: 7890
        proxies:
          - name: "hk-vmess-ws"
            type: vmess
            server: 1.2.3.4
            port: 443
            uuid: 11111111-2222-3333-4444-555555555555
            alterId: 0
            cipher: auto
            tls: true
            servername: hk.example.com
            network: ws
            ws-opts:
              path: "/ws"
              headers:
                Host: hk.example.com

          - name: "jp-vless-reality"
            type: vless
            server: 5.6.7.8
            port: 8443
            uuid: 11111111-2222-3333-4444-555555555555
            tls: true
            servername: www.microsoft.com
            flow: xtls-rprx-vision
            client-fingerprint: chrome
            network: tcp
            reality-opts:
              public-key: 0123456789abcdef0123456789abcdef0123456789ab
              short-id: 1a2b3c4d

          - name: "sg-trojan-grpc"
            type: trojan
            server: 9.10.11.12
            port: 443
            password: trojanpass
            sni: sg.example.com
            skip-cert-verify: true
            network: grpc
            grpc-opts:
              grpc-service-name: "gsvc"

          - name: "us-ss"
            type: ss
            server: 13.14.15.16
            port: 8388
            cipher: aes-256-gcm
            password: sspassword

          - name: "kr-hy2"
            type: hysteria2
            server: 17.18.19.20
            port: 443
            password: hy2pass
            sni: kr.example.com
            skip-cert-verify: true
            obfs: salamander
            obfs-password: obfspass

          - name: "de-tuic"
            type: tuic
            server: 21.22.23.24
            port: 443
            uuid: 11111111-2222-3333-4444-555555555555
            password: tuicpass
            sni: de.example.com
            alpn: [h3]
            congestion-controller: bbr

          - name: "fr-anytls"
            type: anytls
            server: 25.26.27.28
            port: 443
            password: anytlspass
            sni: fr.example.com
            skip-cert-verify: true
        """;

    private const int SupportedCount = 7;

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

    /// <summary>全部受支持协议都要翻译出来，一条不少。</summary>
    [Test]
    public async Task ClashYaml_TranslatesAllSupportedTypes()
    {
        var nodes = ClashYamlNodeExtractor.Extract(SupportedYaml);
        await nodes.Count.Should().BeEqualTo(SupportedCount);
    }

    /// <summary>翻译结果必须逐条能被统一解析器认出来（否则等于往候选池塞坏数据）。</summary>
    [Test]
    public async Task ClashYaml_ResultIsReparsable()
    {
        var failed = new List<string>();
        foreach (var link in ClashYamlNodeExtractor.Extract(SupportedYaml))
        {
            var profile = FmtHandler.ResolveConfig(link, out var msg);
            if (profile is null || !profile.IsValid())
            {
                failed.Add($"{link} -> {msg}");
            }
        }

        await failed.Should().BeEmpty();
    }

    /// <summary>关键字段不能在翻译过程中丢失。</summary>
    [Test]
    public async Task ClashYaml_KeepsKeyFields()
    {
        var nodes = ClashYamlNodeExtractor.Extract(SupportedYaml);
        var joined = string.Join("\n", nodes);

        var failed = new List<string>();

        // vmess 链接是 base64(JSON)，字段不在明文里，必须解开再看
        var vmess = nodes.FirstOrDefault(n => n.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase));
        if (vmess is null)
        {
            failed.Add("没有翻译出 vmess 节点");
        }
        else
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(vmess["vmess://".Length..]));
            if (!json.Contains("hk.example.com")) failed.Add("vmess 的 ws Host / servername 丢了");
            if (!json.Contains("/ws")) failed.Add("vmess 的 ws path 丢了");
        }

        if (!joined.Contains("www.microsoft.com")) failed.Add("vless 的 sni 丢了");
        if (!joined.Contains("gsvc")) failed.Add("trojan 的 grpc serviceName 丢了");
        if (!joined.Contains("sg.example.com")) failed.Add("trojan 的 sni 丢了");
        if (!joined.Contains("obfspass")) failed.Add("hysteria2 的 obfs 密码丢了");
        if (!joined.Contains("bbr")) failed.Add("tuic 的拥塞控制丢了");
        if (!joined.Contains("anytlspass")) failed.Add("anytls 的密码丢了");

        await failed.Should().BeEmpty();
    }

    /// <summary>不是 Clash 订阅的内容要快速返回空，不能把普通文本当 YAML 硬解。</summary>
    [Test]
    public async Task NonClashContent_ReturnsEmpty()
    {
        var nodes = ClashYamlNodeExtractor.Extract("trojan://pass@example.com:443#a\n随便一段文字");
        await nodes.Count.Should().BeEqualTo(0);
    }

    /// <summary>
    /// 内部模型不支持往返的网络类型（Clash 的 h2）必须整条跳过。
    /// 硬翻译的话会被静默降级成 raw，生成一条「看着合法、实际连不上」的坏节点。
    /// </summary>
    [Test]
    public async Task UnsupportedNetwork_IsSkipped()
    {
        var yaml = """
            proxies:
              - name: "h2-node"
                type: vmess
                server: 30.31.32.33
                port: 443
                uuid: 11111111-2222-3333-4444-555555555555
                alterId: 0
                tls: true
                network: h2
                h2-opts:
                  path: "/h2"
            """;

        var nodes = ClashYamlNodeExtractor.Extract(yaml);
        await nodes.Count.Should().BeEqualTo(0);
    }

    /// <summary>Clash 订阅里的锚点与合并键（&lt;&lt;: *base）必须先展开，否则条目只有半个节点。</summary>
    [Test]
    public async Task ClashYaml_WithAnchors_IsExpanded()
    {
        var yaml = """
            proxies:
              - &base
                name: "hk-1"
                type: vmess
                server: 1.2.3.4
                port: 443
                uuid: 11111111-2222-3333-4444-555555555555
                alterId: 0
                cipher: auto
                tls: true
                network: ws
                ws-opts:
                  path: "/ws"
              - <<: *base
                name: "hk-2"
                server: 1.2.3.5
            """;

        var nodes = ClashYamlNodeExtractor.Extract(yaml);
        await nodes.Count.Should().BeEqualTo(2);
    }

    /// <summary>端到端：Clash 订阅走 ParseNodesFromResponse 也要能出节点。</summary>
    [Test]
    public async Task ClashYaml_EndToEnd()
    {
        var nodes = Parse(SupportedYaml);
        await nodes.Count.Should().BeEqualTo(SupportedCount);
    }

    /// <summary>
    /// URL-safe Base64 订阅（+ → -、/ → _、省略 padding）。
    /// 旧实现只认标准 Base64 且要求长度是 4 的倍数，这类订阅整份解不出来。
    /// </summary>
    [Test]
    public async Task UrlSafeBase64Subscription_IsDecoded()
    {
        // '~' 的 UTF-8 是 0x7E，连续四个 '~' 的 base64 必然出现 '+'，
        // 正好用来构造 URL-safe 样本，并保证这个用例真的走到了替换分支。
        var raw = "trojan://pass123@example.com:443#hk~~~~";
        var std = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        await std.Contains('+').Should().BeTrue();

        var urlSafe = std.Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var nodes = Parse(urlSafe);
        await nodes.Count.Should().BeEqualTo(1);
    }

    /// <summary>网页里被 HTML 实体转义过的节点链接（&amp;amp;）要能还原。</summary>
    [Test]
    public async Task HtmlEscapedLinks_AreDecoded()
    {
        var content = """
            <p>vless://11111111-2222-3333-4444-555555555555@example.net:8443?encryption=none&amp;security=tls&amp;type=ws&amp;host=a.com&amp;path=%2Fws#jp</p>
            """;

        var nodes = Parse(content);
        await nodes.Count.Should().BeEqualTo(1);
        await nodes[0].Contains("&amp;").Should().BeFalse();
    }
}
