using System.Reflection;

namespace ServiceLib.Tests;

/// <summary>
/// AI 对话里「给个地址去采集节点」的入口识别。
///
/// 以前的判定是「整句话是否以 http 开头」，用户只要加一点中文
/// （「帮我采集 https://xxx」「采集这个：https://xxx」），
/// 整句就会被当成闲聊丢给 AI 模型，AI 回一段文字了事，节点一个都没采。
/// 这组用例锁住修复后的行为：句子里任意位置出现 URL 都要能认出来。
/// </summary>
public class AiChatUrlRoutingTest
{
    private static List<string> Extract(string input)
    {
        var type = typeof(ServiceLib.ViewModels.AIChatViewModel);
        var mi = type.GetMethod("ExtractHttpUrls", BindingFlags.NonPublic | BindingFlags.Static);
        if (mi == null)
        {
            throw new InvalidOperationException("ExtractHttpUrls not found — 方法被改名或删除了？");
        }

        return (List<string>)mi.Invoke(null, new object[] { input })!;
    }

    [Test]
    public async Task BareUrl_IsRecognised()
    {
        var r = Extract("https://sub.example.com/api/v1/client?sub=3");
        await r.Count.Should().BeEqualTo(1);
        await r[0].Should().BeEqualTo("https://sub.example.com/api/v1/client?sub=3");
    }

    [Test]
    [Arguments("帮我采集 https://sub.example.com/sub?token=abc")]
    [Arguments("采集这个：https://sub.example.com/sub?token=abc")]
    [Arguments("帮我去抓一下 https://sub.example.com/sub?token=abc 谢谢")]
    [Arguments("https://sub.example.com/sub?token=abc 这个帮我采")]
    public async Task Url_WithChinesePrefixOrSuffix_IsRecognised(string input)
    {
        var r = Extract(input);
        await r.Count.Should().BeEqualTo(1);
        await r[0].Should().BeEqualTo("https://sub.example.com/sub?token=abc");
    }

    [Test]
    public async Task MarkdownLink_IsUnwrapped()
    {
        var r = Extract("[我的订阅](https://sub.example.com/sub?token=abc)");
        await r.Count.Should().BeEqualTo(1);
        await r[0].Should().BeEqualTo("https://sub.example.com/sub?token=abc");
    }

    [Test]
    [Arguments("帮我采集 https://a.example.com/x。")]
    [Arguments("采集 https://a.example.com/x，谢谢")]
    [Arguments("（https://a.example.com/x）")]
    public async Task TrailingChinesePunctuation_IsStripped(string input)
    {
        var r = Extract(input);
        await r.Count.Should().BeEqualTo(1);
        await r[0].Should().BeEqualTo("https://a.example.com/x");
    }

    [Test]
    public async Task MultipleUrls_AreAllFound()
    {
        var r = Extract("https://a.example.com/x 和 https://b.example.com/y");
        await r.Count.Should().BeEqualTo(2);
    }

    [Test]
    [Arguments("vmess://eyJ2IjoiMiJ9")]
    [Arguments("今天天气怎么样")]
    [Arguments("帮我看看有哪些节点")]
    public async Task NonHttpInput_YieldsNoUrl(string input)
    {
        await Extract(input).Count.Should().BeEqualTo(0);
    }
}

/// <summary>
/// 自主找节点指令：用户一句话（「采集」「找节点」「搜索」等）就要触发
/// 全自动采集闭环，而不是被当成闲聊丢给 LLM 模型。
/// </summary>
public class AiChatAutoSearchCommandTest
{
    private static string? Match(string input)
    {
        var type = typeof(ServiceLib.ViewModels.AIChatViewModel);
        var mi = type.GetMethod("TryMatchLocalCommand", BindingFlags.NonPublic | BindingFlags.Static);
        if (mi == null)
        {
            throw new InvalidOperationException("TryMatchLocalCommand not found — 方法被改名或删除了？");
        }

        var cmd = mi.Invoke(null, new object[] { input });
        if (cmd is null)
        {
            return null;
        }
        var nameField = cmd.GetType().GetField("Name");
        return nameField?.GetValue(cmd) as string;
    }

    [Test]
    [Arguments("采集")]
    [Arguments("找节点")]
    [Arguments("帮我找几个节点")]
    [Arguments("抓节点")]
    [Arguments("自动搜索")]
    [Arguments("搜索")]
    [Arguments("autosearch")]
    public async Task AutoSearchKeywords_TriggerAutoSearchCommand(string input)
    {
        await Match(input).Should().BeEqualTo("autoSearch");
    }

    [Test]
    [Arguments("列表")]
    [Arguments("帮我看看有哪些节点")]
    [Arguments("今天天气怎么样")]
    public async Task NonCrawlPhrases_DoNotTriggerAutoSearch(string input)
    {
        var name = Match(input);
        await (name == "autoSearch").Should().BeFalse();
    }

    [Test]
    public async Task UrlInPhrase_StaysOnAnalysePath()
    {
        // 带链接的采集请求走分析管线（本地解析+验证），不触发全量搜索
        await Match("采集 https://sub.example.com/sub").Should().BeNull();
    }
}

/// <summary>
/// 问句保护：知识型提问（怎么/为什么/什么是）必须交给模型回答，
/// 不能被关键词路由劫持成指令——那会让 AI 显得「答非所问不聪明」。
/// </summary>
public class AiChatQuestionGuardTest
{
    private static string? Match(string input)
    {
        var type = typeof(ServiceLib.ViewModels.AIChatViewModel);
        var mi = type.GetMethod("TryMatchLocalCommand", BindingFlags.NonPublic | BindingFlags.Static);
        if (mi == null)
        {
            throw new InvalidOperationException("TryMatchLocalCommand not found");
        }
        var cmd = mi.Invoke(null, new object[] { input });
        return cmd is null ? null : cmd.GetType().GetField("Name")?.GetValue(cmd) as string;
    }

    [Test]
    [Arguments("怎么找节点")]
    [Arguments("为什么连不上")]
    [Arguments("什么是reality")]
    [Arguments("vless和vmess什么区别")]
    [Arguments("怎么搜索订阅")]
    public async Task KnowledgeQuestions_GoToLlm_NotCommands(string input)
    {
        await Match(input).Should().BeNull();
    }

    [Test]
    [Arguments("帮我找几个节点")]
    [Arguments("帮我采集一下")]
    public async Task ImperativeWithHelpWord_StillTriggersAutoSearch(string input)
    {
        await Match(input).Should().BeEqualTo("autoSearch");
    }
}

/// <summary>
/// 聊天命令名一致性：TryMatchLocalCommand 返回的 Name 必须能在
/// AiCapabilityRegistry 里找到，否则执行时报「未找到命令 /xxx」。
/// 实测事故：路由写了 "list"，注册表叫 "servers"——最基础的「列表」一直是坏的。
/// </summary>
public class AiChatCommandNameConsistencyTest
{
    private static string? Match(string input)
    {
        var type = typeof(ServiceLib.ViewModels.AIChatViewModel);
        var mi = type.GetMethod("TryMatchLocalCommand", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("TryMatchLocalCommand not found");
        var cmd = mi.Invoke(null, new object[] { input });
        return cmd is null ? null : cmd.GetType().GetField("Name")?.GetValue(cmd) as string;
    }

    [Test]
    [Arguments("列表", "servers")]
    [Arguments("分组", "groups")]
    [Arguments("状态", "status")]
    [Arguments("切换 测试组", "selectGroup")]
    [Arguments("删除 旧节点名", "deleteNode")]
    [Arguments("测试 这个节点", "testNode")]
    [Arguments("代理 clear", "systemProxy")]
    [Arguments("采集", "autoSearch")]
    public async Task ChatCommand_MapsToRegisteredCapability(string input, string expected)
    {
        var actual = Match(input);
        await actual.Should().BeEqualTo(expected, $"输入「{input}」期望 {expected}，实际 {actual ?? "null"}");
    }
}
