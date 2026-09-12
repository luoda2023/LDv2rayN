namespace ServiceLib.Models.Configs;

[Serializable]
public class CoreBasicItem
{
    public bool LogEnabled { get; set; }

    public string Loglevel { get; set; }

    public string DefFingerprint { get; set; }

    public string DefUserAgent { get; set; }

    public string? SendThrough { get; set; }

    public string? BindInterface { get; set; }

    public bool EnableFragment { get; set; }

    public bool EnableFinalFragment { get; set; }

    public bool EnableCacheFile4Sbox { get; set; } = true;

    /// <summary>
    /// 自动故障转移：当前节点连续连通性探测失败时，自动切换到已知延迟最低的
    /// 可用节点并重连。默认开启——用户要的是「能连上」，不是「忠实守着一个死节点」。
    /// </summary>
    public bool AutoFailoverEnabled { get; set; } = true;
}

[Serializable]
public class InItem
{
    public int LocalPort { get; set; }
    public string Protocol { get; set; }
    public bool UdpEnabled { get; set; }
    public bool SniffingEnabled { get; set; } = true;
    public List<string>? DestOverride { get; set; } = ["http", "tls"];
    public bool RouteOnly { get; set; }
    // Keep local proxy listeners loopback-only unless the user explicitly opts into LAN access.
    public bool AllowLANConn { get; set; } = false;
    public bool NewPort4LAN { get; set; } = false;
    public string User { get; set; }
    public string Pass { get; set; }
    public bool SecondLocalPortEnabled { get; set; }
}

[Serializable]
public class KcpItem
{
    public int Mtu { get; set; }

    public int Tti { get; set; }

    public int UplinkCapacity { get; set; }

    public int DownlinkCapacity { get; set; }

    public int CwndMultiplier { get; set; }

    public int MaxSendingWindow { get; set; }
}

[Serializable]
public class GrpcItem
{
    public int? IdleTimeout { get; set; }
    public int? HealthCheckTimeout { get; set; }
    public bool? PermitWithoutStream { get; set; }
    public int? InitialWindowsSize { get; set; }
}

[Serializable]
public class GUIItem
{
    public string AppWebsite { get; set; } = Global.Website;
    public bool AutoRun { get; set; }
    public bool EnableStatistics { get; set; }
    public bool DisplayRealTimeSpeed { get; set; }
    public bool KeepOlderDedupl { get; set; }
    public int AutoUpdateInterval { get; set; }
    public int TrayMenuServersLimit { get; set; } = 20;
    public bool EnableHWA { get; set; } = false;
    public bool EnableLog { get; set; } = true;
    public string? RootCertProvider { get; set; }
}

[Serializable]
public class MsgUIItem
{
    public string? MainMsgFilter { get; set; }
    public bool? AutoRefresh { get; set; }
}

[Serializable]
public class UIItem
{
    public bool EnableAutoAdjustMainLvColWidth { get; set; }
    public int MainGirdHeight1 { get; set; }
    public int MainGirdHeight2 { get; set; }
    public EGirdOrientation MainGirdOrientation { get; set; } = EGirdOrientation.Vertical;
    public string? ColorPrimaryName { get; set; }
    public string? CurrentTheme { get; set; }
    public string CurrentLanguage { get; set; }
    public string CurrentFontFamily { get; set; }
    public int CurrentFontSize { get; set; }
    public bool EnableDragDropSort { get; set; }
    public bool DoubleClick2Activate { get; set; }
    public bool AutoHideStartup { get; set; }
    public bool Hide2TrayWhenClose { get; set; }
    public bool MacOSShowInDock { get; set; }
    public List<ColumnItem> MainColumnItem { get; set; }
    public List<WindowSizeItem> WindowSizeItem { get; set; }
    public bool HideColumnIpInfo { get; set; }
}

[Serializable]
public class ConstItem
{
    public string? SubConvertUrl { get; set; }
    public string? GeoSourceUrl { get; set; }
    public string? SrsSourceUrl { get; set; }
    public string? RouteRulesTemplateSourceUrl { get; set; }
}

[Serializable]
public class KeyEventItem
{
    public EGlobalHotkey EGlobalHotkey { get; set; }

    public bool Alt { get; set; }

    public bool Control { get; set; }

    public bool Shift { get; set; }

    public int? KeyCode { get; set; }
}

[Serializable]
public class CoreTypeItem
{
    public EConfigType ConfigType { get; set; }

    public ECoreType CoreType { get; set; }
}

[Serializable]
public class TunModeItem
{
    public bool EnableTun { get; set; }
    public bool AutoRoute { get; set; } = true;
    public bool StrictRoute { get; set; } = true;
    public string Stack { get; set; }
    public int Mtu { get; set; }
    // IPv6 remains opt-in so a host cannot accidentally bypass an IPv4-only tunnel.
    public bool EnableIPv6Address { get; set; } = false;
    public string IcmpRouting { get; set; }
    public bool EnableLegacyProtect { get; set; } = true;
    public bool EnableKillSwitch { get; set; } = true;
    public List<string>? RouteExcludeAddress { get; set; }
    public string IPv4Address { get; set; }
    public string IPv6Address { get; set; }
}

[Serializable]
public class SpeedTestItem
{
    public int SpeedTestTimeout { get; set; }
    public string SpeedTestUrl { get; set; }
    public string SpeedPingTestUrl { get; set; }
    public int MixedConcurrencyCount { get; set; }
    public string IPAPIUrl { get; set; }
    public string UdpTestTarget { get; set; }
    public int? SpeedTestPageSize { get; set; }
    public int? SpeedTestDelayInterval { get; set; }
}

[Serializable]
public class RoutingBasicItem
{
    public string DomainStrategy { get; set; }
    public string DomainStrategy4Singbox { get; set; }
    public string RoutingIndexId { get; set; }
}

[Serializable]
public class ColumnItem
{
    public string Name { get; set; }
    public int Width { get; set; }
    public int Index { get; set; }
}

[Serializable]
public class Mux4RayItem
{
    public int? Concurrency { get; set; }
    public int? XudpConcurrency { get; set; }
    public string? XudpProxyUDP443 { get; set; }
}

[Serializable]
public class Mux4SboxItem
{
    public string Protocol { get; set; }
    public int MaxConnections { get; set; }
    public bool? Padding { get; set; }
}

[Serializable]
public class HysteriaItem
{
    public int UpMbps { get; set; }
    public int DownMbps { get; set; }
    public int HopInterval { get; set; } = Global.Hysteria2DefaultHopInt;
}

[Serializable]
public class ClashUIItem
{
    public bool EnableIPv6 { get; set; }
    public bool EnableMixinContent { get; set; }
    public int ProxiesSorting { get; set; }
    public bool ProxiesAutoRefresh { get; set; }
    public int ProxiesAutoDelayTestInterval { get; set; } = 10;
    public bool ConnectionsAutoRefresh { get; set; }
    public int ConnectionsRefreshInterval { get; set; } = 2;
    public List<ColumnItem> ConnectionsColumnItem { get; set; }
}

[Serializable]
public class SystemProxyItem
{
    // 默认必须是「自动配置」。枚举零值是 ForcedClear（清除系统代理），
    // 不显式赋默认值的话，新装/重置配置后软件每次启动都会把系统代理清空——
    // 浏览器流量根本不经过本程序，而软件内测速走的本地端口照常出数字，
    // 表现为「测延迟测速度都正常，就是连不上」。
    public ESysProxyType SysProxyType { get; set; } = ESysProxyType.ForcedChange;

    /// <summary>
    /// 一次性迁移标记：老配置里零值 ForcedClear 是枚举默认值泄漏而非用户选择，
    /// 升级到显式默认值时只翻这一次，之后的用户选择不再干预。
    /// </summary>
    public bool SysProxyDefaultMigrated { get; set; }
    public string SystemProxyExceptions { get; set; }
    public bool NotProxyLocalAddress { get; set; } = true;
    public string SystemProxyAdvancedProtocol { get; set; }
    public string? CustomSystemProxyPacPath { get; set; }
    public string? CustomSystemProxyScriptPath { get; set; }
}

[Serializable]
public class WebDavItem
{
    public string? Url { get; set; }
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? DirName { get; set; }
}

[Serializable]
public class Fragment4RayItem
{
    public string? Packets { get; set; }
    public List<string>? Lengths { get; set; }
    public List<string>? Delays { get; set; }
    public string? MaxSplit { get; set; }

    // For migration from old version, remove those properties in the future
    public string? Length { get; set; }

    public string? Interval { get; set; }
    // migration end
}

[Serializable]
public class WindowSizeItem
{
    public string TypeName { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

[Serializable]
public class SimpleDNSItem
{
    public bool? UseSystemHosts { get; set; }
    public bool? AddCommonHosts { get; set; }
    public bool? FakeIP { get; set; }
    public bool? GlobalFakeIp { get; set; }
    public string? FakeIPRange { get; set; }
    public bool? BlockBindingQuery { get; set; }
    public bool? BlockAAAAQuery { get; set; }
    public string? DirectDNS { get; set; }
    public string? RemoteDNS { get; set; }
    public string? BootstrapDNS { get; set; }
    public string? Strategy4Freedom { get; set; }
    public string? Strategy4Proxy { get; set; }
    public string? Strategy4ProxyDial { get; set; }
    public bool? ServeStale { get; set; }
    public bool? ParallelQuery { get; set; }
    public string? Hosts { get; set; }
    public string? DirectExpectedIPs { get; set; }
    public bool? EnableHappyEyeballs { get; set; }
    public bool? ForceDnsThroughProxy { get; set; }
}

[Serializable]
public class AIConfigItem
{
    public string? ApiUrl { get; set; } = string.Empty;
    public string? ApiKey { get; set; } = string.Empty;
    public string? ModelId { get; set; } = "hermesAPI";
    public bool Enabled { get; set; } = true;
    public int SearchIntervalMinutes { get; set; } = 60;
    public string? AiGroupRemarks { get; set; } = "AI自动获取";
        public int MaxNodesPerSearch { get; set; } = 100;
        public bool AutoCrawlEnabled { get; set; } = true;
    public int AutoCrawlIntervalMinutes { get; set; } = 3;

    /// <summary>
    /// 上次自动采集的日期（本地 yyyy-MM-dd）。
    /// 用于保证「每天只采集一遍」：软件一天内反复启动时不会每次都重新抓一轮。
    /// </summary>
    public string? LastAutoCrawlDate { get; set; }

    /// 
    public AIExternalApiConfigItem ExternalApi { get; set; } = new();
}

[Serializable]
public class AIExternalApiConfigItem
{
    public bool Enabled { get; set; } = false;
    public string Host { get; set; } = Global.Loopback;
    public int Port { get; set; } = 26066;
    public string? Token { get; set; }
}

[Serializable]
public class HappyEyeballs4RayItem
{
    public int? TryDelayMs { get; set; }
    public bool? PrioritizeIPv6 { get; set; }
    public int? Interleave { get; set; }
    public int? MaxConcurrentTry { get; set; }
}
