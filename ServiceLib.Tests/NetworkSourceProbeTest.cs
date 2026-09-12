using System.Reflection;
using System.Text;
using ServiceLib.Models.Configs;
using ServiceLib.Services;

namespace ServiceLib.Tests;

/// <summary>
/// 联网采集源回归探针。
///
/// 这是一个**默认不执行**的可选测试：它真的会去访问外网、抓几十个订阅源，
/// 一轮下来约 2 分钟，放进日常测试集里只会拖慢反馈。只有显式设置环境变量
/// <c>LDV2RAYN_NET_PROBE=1</c> 时才会真正跑。
///
/// 用途：每次增删采集源 / 改镜像列表 / 动解析逻辑之后，用它确认
/// 「地址真的可达、真的能解析出节点」，而不是只看单元测试里喂进去的样本串。
///
/// 运行方式（本仓库 dotnet test 走不通，须直接 exec 测试 dll）：
/// <code>
/// set LDV2RAYN_NET_PROBE=1
/// dotnet exec ServiceLib.Tests/bin/Debug/net10.0/ServiceLib.Tests.dll --treenode-filter "/*/*/NetworkSourceProbeTest/*"
/// </code>
///
/// 结果会同时落到 <c>.workbuddy-ai/verify/fetch-probe-result.txt</c>，
/// 含各协议数量分布与样例链接，方便人工核对。
/// </summary>
public class NetworkSourceProbeTest
{
    private const string OptInEnvVar = "LDV2RAYN_NET_PROBE";

    private static readonly string OutFile =
        Path.Combine(@"D:\LUODA\LDv2rayN\.workbuddy-ai\verify", "fetch-probe-result.txt");

    private static readonly object _dumpLock = new();

    private static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable(OptInEnvVar), "1", StringComparison.Ordinal);

    static NetworkSourceProbeTest()
    {
        if (!Enabled)
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OutFile)!);
            File.Delete(OutFile);
        }
        catch
        {
            // 输出文件不可写不影响断言，忽略即可
        }
    }

    private static async Task<T> InvokeAsync<T>(string method, params object[] args)
    {
        var svc = new AIFetchService(new Config(), (_, _) => Task.CompletedTask);
        var mi = typeof(AIFetchService).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
                 ?? throw new InvalidOperationException($"{method} 不存在");
        var task = (Task<T>)mi.Invoke(svc, args)!;
        return await task;
    }

    private static void Dump(string title, IEnumerable<string> lines)
    {
        var sb = new StringBuilder();
        sb.AppendLine(title);
        foreach (var line in lines)
        {
            sb.AppendLine(line);
        }
        sb.AppendLine();
        lock (_dumpLock)
        {
            File.AppendAllText(OutFile, sb.ToString(), Encoding.UTF8);
        }
        Console.WriteLine(sb.ToString());
    }

    private static List<string> ProtocolBreakdown(IEnumerable<string> nodes)
        => nodes.Select(n => n.Split("://")[0])
            .GroupBy(s => s)
            .OrderByDescending(g => g.Count())
            .Select(g => $"{g.Key}={g.Count()}")
            .ToList();

    [Test]
    public async Task SpecifiedUrls_ShouldReturnRealNodes()
    {
        if (!Enabled)
        {
            Console.WriteLine($"[跳过] 未设置 {OptInEnvVar}=1，联网探针不执行。");
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var nodes = await InvokeAsync<List<string>>("FetchFromSpecifiedUrls");
        sw.Stop();

        Dump($"[指定URL采集] 耗时 {sw.Elapsed.TotalSeconds:F1}s，拿到 {nodes.Count} 个节点",
            ProtocolBreakdown(nodes));
        Dump("  样例：", nodes.Take(8).Select(n => "    " + n[..Math.Min(100, n.Length)]));

        await (nodes.Count > 0).Should().BeTrue();
    }

    [Test]
    public async Task GitHubSearch_ShouldReturnRealNodes()
    {
        if (!Enabled)
        {
            Console.WriteLine($"[跳过] 未设置 {OptInEnvVar}=1，联网探针不执行。");
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var nodes = await InvokeAsync<List<string>?>("SearchGitHubForFreeNodes", new AIConfigItem());
        sw.Stop();

        var list = nodes ?? [];
        Dump($"[GitHub搜索采集] 耗时 {sw.Elapsed.TotalSeconds:F1}s，拿到 {list.Count} 个节点",
            ProtocolBreakdown(list));
        Dump("  样例：", list.Take(8).Select(n => "    " + n[..Math.Min(100, n.Length)]));

        await (list.Count > 0).Should().BeTrue();
    }
}
