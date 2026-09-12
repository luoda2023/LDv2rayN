using YamlDotNet.Serialization;

namespace ServiceLib.Common;

/// <summary>
/// 从 Clash / mihomo 订阅（YAML）的 <c>proxies:</c> 列表里还原出标准节点链接。
///
/// 为什么需要它：AI 采集会去 GitHub 上抓免费节点，其中相当一部分仓库提供的是
/// Clash YAML（<c>proxies</c> 数组）。那种格式里节点根本不是 URI 形态，
/// 「全量扫描 scheme://」的正则一条也抓不到 —— 以前碰到这类源就是「0 节点」，
/// 白跑一趟。本类把它翻译成 vmess:// / vless:// / ... 链接，交给后续统一的
/// 解析 / 去重 / 真实穿透验证流程。
///
/// 实现上刻意<b>不自己拼 URI</b>：只把 YAML 字段填进 ProfileItem，再交给
/// <see cref="FmtHandler.GetShareUri"/> 导出。这样生成的链接与解析器天然对称，
/// 不会出现「参数名写错 → 导出即失效」这种整批报废的问题。
/// 最后仍然回验一次，过不了的一律丢弃，绝不把半成品塞进候选池 ——
/// 候选名额是有限的，塞一条坏数据等于挤掉一个真节点。
/// </summary>
public static class ClashYamlNodeExtractor
{
    private static readonly string _tag = "ClashYamlNodeExtractor";

    /// <summary>单个订阅最多翻译多少条，防止超大 YAML 把 CPU 吃满。</summary>
    private const int MaxProxies = 800;

    /// <summary>
    /// 翻译一段文本里的 Clash 节点。不是 Clash 订阅、或一条都翻不出来时返回空列表。
    /// </summary>
    public static List<string> Extract(string? yamlText)
    {
        var result = new List<string>();
        if (yamlText.IsNullOrEmpty() || yamlText.Length > 4_000_000)
        {
            return result;
        }

        // 廉价前置判断：没有 proxies 段落就不是 Clash 订阅，别浪费一次 YAML 解析。
        if (!yamlText.Contains("proxies:", StringComparison.Ordinal))
        {
            return result;
        }

        List<object>? proxies = null;
        try
        {
            // 先过 PreprocessYaml：真实 Clash 订阅大量使用锚点与合并键（<<: *base），
            // 不展开的话每个条目只剩半个节点信息，翻译出来必然是坏的。
            var normalized = YamlUtils.PreprocessYaml(yamlText) ?? yamlText;
            var root = new DeserializerBuilder()
                .Build()
                .Deserialize<Dictionary<object, object>>(normalized);
            if (root != null
                && root.TryGetValue("proxies", out var raw)
                && raw is IEnumerable<object> list)
            {
                proxies = list.ToList();
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}: parse yaml failed", ex);
            return result;
        }

        if (proxies == null || proxies.Count == 0)
        {
            return result;
        }

        foreach (var proxy in proxies.Take(MaxProxies))
        {
            try
            {
                var link = ToLink(proxy as IDictionary<object, object>);
                if (link.IsNullOrEmpty())
                {
                    continue;
                }

                // 回验：导出的链接必须能被统一解析器认出来才算数。
                var check = FmtHandler.ResolveConfig(link, out _);
                if (check == null || !check.IsValid()
                    || check.Address.IsNullOrEmpty() || check.Port is <= 0 or >= 65536)
                {
                    continue;
                }

                result.Add(link);
            }
            catch
            {
                // 单条翻译失败是常态（不支持的协议、字段缺失），不该刷日志。
            }
        }

        return result;
    }

    private static string? ToLink(IDictionary<object, object>? p)
    {
        if (p == null)
        {
            return null;
        }

        var type = GetStr(p, "type");
        var server = GetStr(p, "server");
        var port = GetInt(p, "port");
        if (type.IsNullOrEmpty() || server.IsNullOrEmpty() || port is <= 0 or >= 65536)
        {
            return null;
        }

        // 服务器地址里混进空格 / 逗号的多半是 YAML 里被拆行的说明文字，不是真地址。
        if (server.Contains(' ') || server.Contains(',') || server.Contains('#'))
        {
            return null;
        }

        var name = GetStr(p, "name") ?? $"{server}:{port}";
        var item = type.ToLowerInvariant() switch
        {
            "vmess" => BuildVmess(p, server, port, name),
            "vless" => BuildVless(p, server, port, name),
            "trojan" => BuildTrojan(p, server, port, name),
            "ss" => BuildShadowsocks(p, server, port, name),
            "hysteria2" or "hy2" => BuildHysteria2(p, server, port, name),
            "tuic" => BuildTuic(p, server, port, name),
            "anytls" => BuildAnytls(p, server, port, name),
            "socks5" => BuildSocks5(p, server, port, name),
            _ => null,
        };

        return item == null ? null : FmtHandler.GetShareUri(item);
    }

    private static ProfileItem NewItem(EConfigType type, string server, int port, string name)
        => new()
        {
            ConfigType = type,
            Address = server,
            Port = port,
            Remarks = name,
        };

    /// <summary>把 Clash 的 tls / servername / skip-cert-verify / alpn / 指纹映射到公共 TLS 字段。</summary>
    private static void ApplyTls(ProfileItem item, IDictionary<object, object> p, bool forceTls = false)
    {
        if (forceTls || GetBool(p, "tls"))
        {
            item.StreamSecurity = Global.StreamSecurity;
        }

        // Clash 各协议对 sni 的叫法不统一：vmess/vless 用 servername，trojan/hy2/tuic 用 sni。
        var sni = GetStr(p, "servername") ?? GetStr(p, "sni");
        if (sni.IsNotEmpty())
        {
            item.Sni = sni;
        }

        if (GetBool(p, "skip-cert-verify"))
        {
            item.AllowInsecure = Global.StringTrue;
        }

        var alpn = GetStrList(p, "alpn");
        if (alpn.IsNotEmpty())
        {
            item.Alpn = alpn;
        }

        var fp = GetStr(p, "client-fingerprint");
        if (fp.IsNotEmpty())
        {
            item.Fingerprint = fp;
        }
    }

    /// <summary>
    /// 把 Clash 的 network / ws-opts / grpc-opts / h2-opts 映射到 TransportExtraItem。
    ///
    /// 只翻译内部模型真正支持往返的网络类型（raw / ws / httpupgrade / xhttp / grpc / kcp）。
    /// Clash 的 h2、quic、http 在内部模型里没有对应项，导出时会被静默降级成 raw，
    /// 生成一条「看着合法、实际连不上」的坏节点 —— 那种必须直接放弃，返回 false。
    /// </summary>
    private static bool ApplyTransport(ProfileItem item, IDictionary<object, object> p)
    {
        var network = (GetStr(p, "network") ?? Global.DefaultNetwork).ToLowerInvariant();
        if (network == Global.RawNetworkAlias)
        {
            network = nameof(ETransport.raw);
        }

        if (!Global.Networks.Contains(network))
        {
            return false;
        }

        var wsOpts = AsDict(GetObj(p, "ws-opts"));
        var grpcOpts = AsDict(GetObj(p, "grpc-opts"));

        var transport = new TransportExtraItem();
        switch (network)
        {
            case nameof(ETransport.ws):
            case nameof(ETransport.httpupgrade):
                transport = transport with
                {
                    Host = HeaderHost(wsOpts),
                    Path = GetStr(wsOpts, "path") ?? "/",
                };
                break;

            case nameof(ETransport.grpc):
                var serviceName = GetStr(grpcOpts, "grpc-service-name");
                if (serviceName.IsNullOrEmpty())
                {
                    return false;
                }
                transport = transport with
                {
                    GrpcServiceName = serviceName,
                    GrpcMode = Global.GrpcGunMode,
                };
                break;

            case nameof(ETransport.xhttp):
                transport = transport with
                {
                    Host = HeaderHost(wsOpts),
                    Path = GetStr(wsOpts, "path") ?? "/",
                    XhttpMode = Global.DefaultXhttpMode,
                };
                break;

            case nameof(ETransport.kcp):
                transport = transport with
                {
                    KcpHeaderType = GetStr(p, "header-type") ?? Global.None,
                    KcpSeed = GetStr(p, "seed"),
                };
                break;

            default:
                // 裸 TCP：Clash 用 network: tcp，可配 http 头伪装
                transport = transport with
                {
                    RawHeaderType = GetBool(p, "http-opts") ? Global.RawHeaderHttp : Global.None,
                };
                break;
        }

        item.Network = network;
        item.SetTransportExtra(transport);
        return true;
    }

    private static ProfileItem? BuildVmess(IDictionary<object, object> p, string server, int port, string name)
    {
        var uuid = GetStr(p, "uuid");
        if (uuid.IsNullOrEmpty())
        {
            return null;
        }

        var item = NewItem(EConfigType.VMess, server, port, name);
        item.Password = uuid;
        item.SetProtocolExtra(item.GetProtocolExtra() with
        {
            AlterId = GetInt(p, "alterId").ToString(),
            VmessSecurity = GetStr(p, "cipher") ?? "auto",
        });

        ApplyTls(item, p);
        return ApplyTransport(item, p) ? item : null;
    }

    private static ProfileItem? BuildVless(IDictionary<object, object> p, string server, int port, string name)
    {
        var uuid = GetStr(p, "uuid");
        if (uuid.IsNullOrEmpty())
        {
            return null;
        }

        var item = NewItem(EConfigType.VLESS, server, port, name);
        item.Password = uuid;

        // flow 只认 Global.Flows 里的取值；Clash 里偶有其它写法，认不出就置空，
        // 否则 IsValid 会直接判否，整条节点作废。
        var flow = GetStr(p, "flow");
        item.SetProtocolExtra(item.GetProtocolExtra() with
        {
            Flow = Global.Flows.Contains(flow ?? string.Empty) ? flow : null,
            VlessEncryption = Global.None,
        });

        var reality = AsDict(GetObj(p, "reality-opts"));
        ApplyTls(item, p, reality != null);
        if (reality != null)
        {
            // reality 要盖在 ApplyTls 之后：ApplyTls 会把 StreamSecurity 写成 tls。
            item.StreamSecurity = Global.StreamSecurityReality;
            item.PublicKey = GetStr(reality, "public-key");
            item.ShortId = GetStr(reality, "short-id");
            if (item.PublicKey.IsNullOrEmpty())
            {
                return null;
            }
        }

        return ApplyTransport(item, p) ? item : null;
    }

    private static ProfileItem? BuildTrojan(IDictionary<object, object> p, string server, int port, string name)
    {
        var password = GetStr(p, "password");
        if (password.IsNullOrEmpty())
        {
            return null;
        }

        var item = NewItem(EConfigType.Trojan, server, port, name);
        item.Password = password;

        // trojan 本身就是 TLS，Clash 里不写 tls 字段。
        ApplyTls(item, p, true);
        return ApplyTransport(item, p) ? item : null;
    }

    private static ProfileItem? BuildShadowsocks(IDictionary<object, object> p, string server, int port, string name)
    {
        var method = GetStr(p, "cipher");
        var password = GetStr(p, "password");
        if (method.IsNullOrEmpty() || password.IsNullOrEmpty())
        {
            return null;
        }

        var item = NewItem(EConfigType.Shadowsocks, server, port, name);
        item.Password = password;
        item.SetProtocolExtra(item.GetProtocolExtra() with { SsMethod = method });

        // Clash 的 plugin / plugin-opts 在内部模型里没有对等项，带插件的节点直接跳过，
        // 免得导出一条「没有插件」的链接，连不上还占验证名额。
        if (GetStr(p, "plugin").IsNotEmpty())
        {
            return null;
        }

        return item;
    }

    private static ProfileItem? BuildHysteria2(IDictionary<object, object> p, string server, int port, string name)
    {
        var item = NewItem(EConfigType.Hysteria2, server, port, name);
        item.Password = GetStr(p, "password") ?? GetStr(p, "auth") ?? string.Empty;

        // Clash 的 obfs-password 就是 hysteria2 的 salamander 密码；
        // 设上之后 ToUri 会自动补出 obfs=salamander & obfs-password=xxx。
        var obfsPassword = GetStr(p, "obfs-password");
        if (obfsPassword.IsNotEmpty())
        {
            item.SetProtocolExtra(item.GetProtocolExtra() with { SalamanderPass = obfsPassword });
        }

        ApplyTls(item, p, true);
        return item;
    }

    private static ProfileItem? BuildTuic(IDictionary<object, object> p, string server, int port, string name)
    {
        var item = NewItem(EConfigType.TUIC, server, port, name);
        item.Username = GetStr(p, "uuid") ?? string.Empty;
        item.Password = GetStr(p, "password") ?? string.Empty;
        if (item.Username.IsNullOrEmpty() && item.Password.IsNullOrEmpty())
        {
            return null;
        }

        item.SetProtocolExtra(item.GetProtocolExtra() with
        {
            CongestionControl = GetStr(p, "congestion-controller"),
        });

        ApplyTls(item, p, true);
        return item;
    }

    private static ProfileItem? BuildAnytls(IDictionary<object, object> p, string server, int port, string name)
    {
        var password = GetStr(p, "password");
        if (password.IsNullOrEmpty())
        {
            return null;
        }

        var item = NewItem(EConfigType.Anytls, server, port, name);
        item.Password = password;

        ApplyTls(item, p, true);
        return item;
    }

    private static ProfileItem? BuildSocks5(IDictionary<object, object> p, string server, int port, string name)
    {
        var item = NewItem(EConfigType.SOCKS, server, port, name);
        item.Username = GetStr(p, "username") ?? string.Empty;
        item.Password = GetStr(p, "password") ?? string.Empty;
        return item;
    }

    #region YAML 取值helper

    private static object? GetObj(IDictionary<object, object>? d, string key)
        => d != null && d.TryGetValue(key, out var v) ? v : null;

    private static IDictionary<object, object>? AsDict(object? o)
        => o as IDictionary<object, object>;

    private static string? GetStr(IDictionary<object, object>? d, string key)
    {
        var s = GetObj(d, key)?.ToString()?.Trim();
        return s.IsNullOrEmpty() ? null : s;
    }

    private static int GetInt(IDictionary<object, object>? d, string key)
        => int.TryParse(GetStr(d, key), out var v) ? v : 0;

    private static bool GetBool(IDictionary<object, object>? d, string key)
    {
        var s = GetStr(d, key);
        return s != null && (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1");
    }

    /// <summary>取可能是列表的值（如 alpn），统一成内部模型用的逗号分隔串。</summary>
    private static string? GetStrList(IDictionary<object, object> d, string key)
    {
        var o = GetObj(d, key);
        if (o is IEnumerable<object> list)
        {
            var parts = list.Select(x => x?.ToString()?.Trim())
                .Where(x => x.IsNotEmpty())
                .ToList();
            return parts.Count > 0 ? string.Join(",", parts) : null;
        }

        return GetStr(d, key);
    }

    /// <summary>取 ws-opts / h2-opts 里 headers.Host（大小写两种写法都认）。</summary>
    private static string? HeaderHost(IDictionary<object, object>? opts)
    {
        var headers = AsDict(GetObj(opts, "headers"));
        return GetStr(headers, "Host") ?? GetStr(headers, "host");
    }

    #endregion YAML 取值helper
}
