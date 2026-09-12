namespace ServiceLib.Services.CoreConfig;

public partial class CoreConfigV2rayService(CoreConfigContext context)
{
    private static readonly string _tag = "CoreConfigV2rayService";
    private readonly Config _config = context.AppConfig;
    private readonly ProfileItem _node = context.Node;

    private V2rayConfig _coreConfig = new();

    #region public gen function

    public RetResult GenerateClientConfigContent()
    {
        var ret = new RetResult();
        try
        {
            if (_node == null
                || !_node.IsValid())
            {
                ret.Msg = ResUI.CheckServerSettings;
                return ret;
            }

            if (_node.GetNetwork() is nameof(ETransport.quic))
            {
                ret.Msg = ResUI.Incorrectconfiguration + $" - {_node.GetNetwork()}";
                return ret;
            }

            ret.Msg = ResUI.InitialConfiguration;

            var result = EmbedUtils.GetEmbedText(Global.V2raySampleClient);
            if (result.IsNullOrEmpty())
            {
                ret.Msg = ResUI.FailedGetDefaultConfiguration;
                return ret;
            }

            _coreConfig = JsonUtils.Deserialize<V2rayConfig>(result);
            if (_coreConfig == null)
            {
                ret.Msg = ResUI.FailedGenDefaultConfiguration;
                return ret;
            }

            GenLog();

            GenInbounds();

            GenOutbounds();

            GenRouting();

            GenDns();

            GenStatistic();

            if (_config.CoreBasicItem.EnableFragment)
            {
                ApplyOutboundFragment();
            }
            if (_config.CoreBasicItem.EnableFinalFragment)
            {
                ApplyFinalFragment();
            }
            ApplyOutboundBindInterface();
            ApplyOutboundSendThrough();
            ApplyOutboundKeepAlive();

            var finalRule = BuildFinalRule();
            if (!string.IsNullOrEmpty(finalRule?.balancerTag))
            {
                _coreConfig.routing.rules.Add(finalRule);
            }

            ret.Msg = string.Format(ResUI.SuccessfulConfiguration, "");
            ret.Success = true;
            ret.Data = ApplyFinalConfigModifiers();
            return ret;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            ret.Msg = ResUI.FailedGenDefaultConfiguration;
            return ret;
        }
    }

    public RetResult GenerateClientSpeedtestConfig(List<ServerTestItem> selecteds)
    {
        var ret = new RetResult();
        try
        {
            ret.Msg = ResUI.InitialConfiguration;

            var result = EmbedUtils.GetEmbedText(Global.V2raySampleClient);
            var txtOutbound = EmbedUtils.GetEmbedText(Global.V2raySampleOutbound);
            if (result.IsNullOrEmpty() || txtOutbound.IsNullOrEmpty())
            {
                ret.Msg = ResUI.FailedGetDefaultConfiguration;
                return ret;
            }

            _coreConfig = JsonUtils.Deserialize<V2rayConfig>(result);
            if (_coreConfig == null)
            {
                ret.Msg = ResUI.FailedGenDefaultConfiguration;
                return ret;
            }

            var (lstIpEndPoints, lstTcpConns) = Utils.GetActiveNetworkInfo();

            GenLog();
            _coreConfig.inbounds.Clear();
            _coreConfig.outbounds.Clear();
            _coreConfig.routing.rules.Clear();

            var initPort = AppManager.Instance.GetLocalPort(EInboundProtocol.speedtest);

            foreach (var it in selecteds)
            {
                if (!(Global.XraySupportConfigType.Contains(it.ConfigType) || it.ConfigType.IsGroupType() || it.ConfigType is EConfigType.Outbound))
                {
                    continue;
                }
                if (!it.ConfigType.IsComplexType() && it.Port <= 0)
                {
                    continue;
                }

                // 单个节点生成失败（畸形 finalmask、非法传输参数等）只跳过该节点，
                // 绝不能让异常冒出去把整份测速配置废掉——否则整批节点的真实延迟
                // 验证全部落空，清理步骤还会把「全测不通」误判成本地断网。
                try
                {
                    AppendSpeedtestNode(it, lstIpEndPoints, lstTcpConns, ref initPort);
                }
                catch (Exception ex)
                {
                    Logging.SaveLog($"{_tag}: skip speedtest node {it.IndexId}", ex);
                }
            }

            if (_config.CoreBasicItem.EnableFragment)
            {
                ApplyOutboundFragment();
            }
            if (_config.CoreBasicItem.EnableFinalFragment)
            {
                ApplyFinalFragment();
            }
            ApplyOutboundBindInterface();
            ApplyOutboundSendThrough();
            ApplyOutboundKeepAlive();
            //ret.Msg =string.Format(ResUI.SuccessfulConfiguration"), node.getSummary());
            ret.Success = true;
            ret.Data = ApplyCustomOutboundReplace();
            return ret;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            ret.Msg = ResUI.FailedGenDefaultConfiguration;
            return ret;
        }
    }

    /// <summary>
    /// 为一个测速节点追加独立入站 + 出站 + 路由规则。
    /// 从 <see cref="GenerateClientSpeedtestConfig(List{ServerTestItem})"/> 拆出来，
    /// 让调用方可以按节点兜住异常——这里抛错只影响该节点自己的入库，
    /// 不再连坐整份配置。
    /// </summary>
    private void AppendSpeedtestNode(ServerTestItem it, List<IPEndPoint>? lstIpEndPoints,
        List<TcpConnectionInformation>? lstTcpConns, ref int initPort)
    {
        var actIndexId = context.ServerTestItemMap.GetValueOrDefault(it.IndexId, it.IndexId);
        var item = context.AllProxiesMap.GetValueOrDefault(actIndexId);
        if (item is null || item.ConfigType is EConfigType.Custom || !item.IsValid())
        {
            return;
        }

        //find unused port
        var port = initPort;
        for (var k = initPort; k < Global.MaxPort; k++)
        {
            if (lstIpEndPoints?.FindIndex(_it => _it.Port == k) >= 0)
            {
                continue;
            }
            if (lstTcpConns?.FindIndex(_it => _it.LocalEndPoint.Port == k) >= 0)
            {
                continue;
            }
            //found
            port = k;
            initPort = port + 1;
            break;
        }

        //Port In Used
        if (lstIpEndPoints?.FindIndex(_it => _it.Port == port) >= 0)
        {
            return;
        }
        it.Port = port;
        it.AllowTest = true;

        //inbound
        Inbounds4Ray inbound = new()
        {
            listen = Global.Loopback,
            port = port,
            protocol = nameof(EInboundProtocol.mixed),
            settings = new Inboundsettings4Ray()
            {
                udp = true,
                auth = "noauth"
            },
        };
        inbound.tag = inbound.protocol + inbound.port.ToString();
        _coreConfig.inbounds.Add(inbound);

        var tag = Global.ProxyTag + inbound.port.ToString();
        var isBalancer = false;
        //outbound
        var proxyOutbounds =
            new CoreConfigV2rayService(context with { Node = item }).BuildAllProxyOutbounds(tag);
        _coreConfig.outbounds.AddRange(proxyOutbounds);
        if (proxyOutbounds.Count(n => n.tag.StartsWith(tag)) > 1)
        {
            isBalancer = true;
            var multipleLoad = item.GetProtocolExtra().MultipleLoad ?? EMultipleLoad.LeastPing;
            GenObservatory(multipleLoad, tag);
            GenBalancer(multipleLoad, tag);
        }

        //rule
        RulesItem4Ray rule = new()
        {
            inboundTag = [inbound.tag],
            outboundTag = tag,
            type = "field"
        };
        if (isBalancer)
        {
            rule.balancerTag = tag + Global.BalancerTagSuffix;
            rule.outboundTag = null;
        }
        _coreConfig.routing.rules.Add(rule);
    }

    public RetResult GenerateClientSpeedtestConfig(int port)
    {
        var ret = new RetResult();
        try
        {
            if (_node == null
                || !_node.IsValid())
            {
                ret.Msg = ResUI.CheckServerSettings;
                return ret;
            }

            if (_node.GetNetwork() is nameof(ETransport.quic))
            {
                ret.Msg = ResUI.Incorrectconfiguration + $" - {_node.GetNetwork()}";
                return ret;
            }

            var result = EmbedUtils.GetEmbedText(Global.V2raySampleClient);
            if (result.IsNullOrEmpty())
            {
                ret.Msg = ResUI.FailedGetDefaultConfiguration;
                return ret;
            }

            _coreConfig = JsonUtils.Deserialize<V2rayConfig>(result);
            if (_coreConfig == null)
            {
                ret.Msg = ResUI.FailedGenDefaultConfiguration;
                return ret;
            }

            GenLog();
            GenOutbounds();

            _coreConfig.routing.domainStrategy = Global.AsIs;
            _coreConfig.routing.rules.Clear();
            _coreConfig.inbounds.Clear();
            _coreConfig.inbounds.Add(new()
            {
                tag = $"{EInboundProtocol.socks}{port}",
                listen = Global.Loopback,
                port = port,
                protocol = nameof(EInboundProtocol.mixed),
                settings = new Inboundsettings4Ray()
                {
                    udp = true,
                    auth = "noauth"
                },
            });

            _coreConfig.routing.rules.Add(BuildFinalRule());

            if (_config.CoreBasicItem.EnableFragment)
            {
                ApplyOutboundFragment();
            }
            if (_config.CoreBasicItem.EnableFinalFragment)
            {
                ApplyFinalFragment();
            }
            ApplyOutboundBindInterface();
            ApplyOutboundKeepAlive();

            ret.Msg = string.Format(ResUI.SuccessfulConfiguration, "");
            ret.Success = true;
            ret.Data = ApplyCustomOutboundReplace();
            return ret;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            ret.Msg = ResUI.FailedGenDefaultConfiguration;
            return ret;
        }
    }

    #endregion public gen function
}
