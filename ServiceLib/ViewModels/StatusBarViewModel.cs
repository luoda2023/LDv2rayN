namespace ServiceLib.ViewModels;

public partial class StatusBarViewModel : MyReactiveObject
{
    public Interaction<string, RxVoid> SetClipboardDataInteraction { get; } = new();
    public Interaction<RxVoid, string?> PasswordInputInteraction { get; } = new();
    public Interaction<RxVoid, RxVoid> DispatcherRefreshIconInteraction { get; } = new();
    public EventChannel<bool> SubscriptionsUpdateRequested { get; } = new();
    public EventChannel<bool?> ShowHideWindowRequested { get; } = new();
    public EventChannel<RxVoid> OpenAiChatRequested { get; } = new(); private static readonly Lazy<StatusBarViewModel> _instance = new(() => new());
    public static StatusBarViewModel Instance => _instance.Value;

    // Fires every 20 s so the bottom connection / latency line stays current.
    private System.Threading.Timer? _latencyTimer;

    public EventChannel<string> SetDefaultServerRequested { get; } = new();
    public EventChannel<RxVoid> ReloadRequested { get; } = new();
    public EventChannel<RxVoid> AddServerViaScanRequested { get; } = new();
    public EventChannel<RxVoid> AddServerViaClipboardRequested { get; } = new();

    #region ObservableCollection

    public BulkObservableCollection<RoutingItem> RoutingItems { get; } = [];

    public BulkObservableCollection<ComboItem> Servers { get; } = [];

    [Reactive]
    public partial RoutingItem SelectedRouting { get; set; }

    [Reactive]
    public partial ComboItem SelectedServer { get; set; }

    [Reactive]
    public partial bool BlServers { get; set; }

    #endregion ObservableCollection

    public ReactiveCommand<RxVoid, RxVoid> AddServerViaClipboardCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddServerViaScanCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SubUpdateCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SubUpdateViaProxyCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> CopyProxyCmdToClipboardCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> NotifyLeftClickCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> ShowWindowCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> HideWindowCmd { get; }

    #region System Proxy

    [Reactive]
    public partial bool BlSystemProxyClear { get; set; }

    [Reactive]
    public partial bool BlSystemProxySet { get; set; }

    [Reactive]
    public partial bool BlSystemProxyNothing { get; set; }

    [Reactive]
    public partial bool BlSystemProxyPac { get; set; }

    public ReactiveCommand<RxVoid, RxVoid> SystemProxyClearCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SystemProxySetCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SystemProxyNothingCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> SystemProxyPacCmd { get; }

    [Reactive]
    public partial bool BlRouting { get; set; }

    [Reactive]
    public partial int SystemProxySelected { get; set; }

    [Reactive]
    public partial bool BlSystemProxyPacVisible { get; set; }

    #endregion System Proxy

    #region UI

    [Reactive]
    public partial string InboundDisplay { get; set; }

    [Reactive]
    public partial string InboundLanDisplay { get; set; }

    [Reactive]
    public partial string RunningServerDisplay { get; set; }

    [Reactive]
    public partial string RunningServerToolTipText { get; set; }

    [Reactive]
    public partial string RunningInfoDisplay { get; set; }

    [Reactive]
    public partial string SpeedProxyDisplay { get; set; }
    [Reactive]
    public partial string SpeedDirectDisplay { get; set; }

    // Bottom bar: connection status, latency, and short speed line.
    [Reactive]
    public partial string ConnectionDisplay { get; set; }

    [Reactive]
    public partial bool IsCoreConnected { get; set; }

    [Reactive]
    public partial string LatencyDisplay { get; set; }

    [Reactive]
    public partial string StatusSpeedDisplay { get; set; }

    [Reactive]
    public partial bool EnableTun { get; set; }

    [Reactive]
    public partial bool BlIsNonWindows { get; set; }

    #endregion UI

    /// <summary>
    /// 系统代理组合框的就绪守卫：视图 Loaded 之前，绑定初始化推入的值一律不生效
    /// （否则会把配置改写成 ForcedClear 并清掉系统代理，见 DoSystemProxySelected 注释）。
    /// </summary>
    private bool _sysProxyReady;

    /// <summary>连续真实探测失败的次数；连续 2 次失败才把状态栏判为「连接失败」。</summary>
    private int _availabilityConsecutiveFailures;

    /// <summary>真实探测的在途守卫：一次探测最坏 ~19 秒，可能跨过 20s 定时周期。</summary>
    private int _availabilityInFlight;

    /// <summary>上次自动故障转移的时间；3 分钟冷却，防止全组失效时来回切换抖动。</summary>
    private DateTime _lastAutoSwitchAt = DateTime.MinValue;

    public StatusBarViewModel()
    {
        _config = AppManager.Instance.Config;
        SelectedRouting = new();
        SelectedServer = new();
        RunningServerToolTipText = GetRunningServerToolTipText("-");
        BlSystemProxyPacVisible = Utils.IsWindows();
        BlIsNonWindows = Utils.IsNonWindows();

        if (_config.TunModeItem.EnableTun && AllowEnableTun())
        {
            EnableTun = true;
        }
        else
        {
            _config.TunModeItem.EnableTun = EnableTun = false;
        }

        #region WhenAnyValue && ReactiveCommand

        this.WhenAnyValue(x => x.SelectedRouting)
            .Where(y => y != null && !y.Remarks.IsNullOrEmpty())
            .SubscribeAsync(async _ => await RoutingSelectedChangedAsync());

        this.WhenAnyValue(x => x.SelectedServer)
            .Where(y => y != null && !y.Text.IsNullOrEmpty())
            .Subscribe(_ => ServerSelectedChanged());

        SystemProxySelected = (int)_config.SystemProxyItem.SysProxyType;
        this.WhenAnyValue(x => x.SystemProxySelected)
            .Where(y => y >= 0)
            .SubscribeAsync(async _ => await DoSystemProxySelected());

        this.WhenAnyValue(x => x.EnableTun)
            .SubscribeAsync(async _ => await DoEnableTun());

        CopyProxyCmdToClipboardCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await CopyProxyCmdToClipboard();
        });
        NotifyLeftClickCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            // Tray left-click: bring the main window forward AND open the AI
            // assistant dialog — this is what users expect from the tray icon.
            ShowHideWindowRequested.Publish(true);
            OpenAiChatRequested.Publish(RxVoid.Default);
            await Task.CompletedTask;
        });
        ShowWindowCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            ShowHideWindowRequested.Publish(true);
            await Task.CompletedTask;
        });
        HideWindowCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            ShowHideWindowRequested.Publish(false);
            await Task.CompletedTask;
        });

        AddServerViaClipboardCmd = ReactiveCommand.CreateFromTask(async () =>
            {
                await AddServerViaClipboard();
            });
        AddServerViaScanCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerViaScan();
        });
        SubUpdateCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess(false);
        });
        SubUpdateViaProxyCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess(true);
        });

        //System proxy
        SystemProxyClearCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetListenerType(ESysProxyType.ForcedClear);
        });
        SystemProxySetCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetListenerType(ESysProxyType.ForcedChange);
        });
        SystemProxyNothingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetListenerType(ESysProxyType.Unchanged);
        });
        SystemProxyPacCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetListenerType(ESysProxyType.Pac);
        });

        #endregion WhenAnyValue && ReactiveCommand

        #region AppEvents

        AppEvents.DispatcherStatisticsRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async result => await UpdateStatistics(result));
        AppEvents.SysProxyChangeRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .SubscribeAsync(async result => await SetListenerType(result));

        #endregion AppEvents

 // Refresh the connection status / latency line every 20s so the bottom
 // bar always shows the most recent state, even when no test is run.
 // 启动时先显示「连接中…」而不是上次会话残留的「连接成功」——
 // 用户反馈「还没连上就显示已连接」就是因为旧值没被清掉。
 ConnectionDisplay = "连接中…";
 IsCoreConnected = false;
 _latencyTimer = new System.Threading.Timer(async _ =>
 {
 try
 {
 RxSchedulers.MainThreadScheduler.Schedule(async () => await RefreshConnectionDisplay());
 }
 catch { }
 }, null, 2000, 20000);

        _ = Init();
    }

    private async Task Init()
    {
        await ConfigHandler.InitBuiltinRouting(_config);
        await RefreshRoutingsMenu();
        await InboundDisplayStatus();
        await ChangeSystemProxyAsync(_config.SystemProxyItem.SysProxyType, true);

        BlRouting = true;
    }

    private async Task CopyProxyCmdToClipboard()
    {
        var cmd = Utils.IsWindows() ? "set" : "export";
        var address = $"{Global.Loopback}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}";

        var sb = new StringBuilder();
        sb.AppendLine($"{cmd} http_proxy={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} https_proxy={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} all_proxy={Global.Socks5Protocol}{address}");
        sb.AppendLine("");
        sb.AppendLine($"{cmd} HTTP_PROXY={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} HTTPS_PROXY={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} ALL_PROXY={Global.Socks5Protocol}{address}");

        await SetClipboardDataInteraction.HandleSafe(sb.ToString());
    }

    private async Task AddServerViaClipboard()
    {
        AddServerViaClipboardRequested.Publish();
        await Task.Delay(1000);
    }

    private async Task AddServerViaScan()
    {
        AddServerViaScanRequested.Publish();
        await Task.Delay(1000);
    }

    private async Task UpdateSubscriptionProcess(bool blProxy)
    {
        SubscriptionsUpdateRequested.Publish(blProxy);
        await Task.Delay(1000);
    }
    public async Task RefreshServersBiz()
    {
        await RefreshServersMenu();

        //display running server
        var running = await ConfigHandler.GetDefaultServer(_config);
        if (running != null)
        {
            RunningServerDisplay = running.GetSummary();
            RunningServerToolTipText = GetRunningServerToolTipText(RunningServerDisplay);
        }
        else
        {
            RunningServerDisplay = ResUI.CheckServerSettings;
            RunningServerToolTipText = GetRunningServerToolTipText(RunningServerDisplay);
        }
    }

    private string GetRunningServerToolTipText(string serverInfo)
    {
        return Utils.IsLinux() ? Global.AppName : serverInfo;
    }

    private async Task RefreshServersMenu()
    {
        var lstModel = await AppManager.Instance.ProfileModels(_config.SubIndexId, "");

        if (lstModel?.Count > _config.GuiItem.TrayMenuServersLimit)
        {
            BlServers = false;
            return;
        }

        var models = lstModel.Select(it => new ComboItem { ID = it.IndexId, Text = it.GetSummary() }).ToList();

        BlServers = true;
        Servers.ReplaceRange(models);

        // Update the ItemsSource before SelectedItem so a collection reset does not clear the tray selection.
        SelectedServer = models.FirstOrDefault(it => it.ID == _config.IndexId) ?? new();
    }

    private void ServerSelectedChanged()
    {
        if (SelectedServer == null)
        {
            return;
        }
        if (SelectedServer.ID.IsNullOrEmpty())
        {
            return;
        }
        SetDefaultServerRequested.Publish(SelectedServer.ID);
    }
    public async Task<AvailabilityCheckResult?> TestServerAvailability()
    {
        var item = await ConfigHandler.GetDefaultServer(_config);
        if (item == null)
        {
            await TestServerAvailabilitySub("❌ 未选择节点，无法测试");
            return null;
        }

        // 核心未运行时，代理端口没有监听，测出来必然是 -1。先给明确提示。
        if (item.CoreType is { } coreType && !AppManager.Instance.IsRunningCore(coreType))
        {
            var msg0 = $"⚠️ 核心未运行，无法测延迟\n当前节点: {item.Remarks}";
            NoticeManager.Instance.SendMessageEx(msg0);
            await TestServerAvailabilitySub(msg0);
            return null;
        }

        await TestServerAvailabilitySub(ResUI.Speedtesting);

        var result = await Task.Run(ConnectionHandler.RunAvailabilityCheck);

        var ip = result.GetValidIp();
        if (ip.IsNotEmpty())
        {
            ProfileExManager.Instance.SetTestIpInfo(item.IndexId, ip);
        }

        string msg;
        if (result.Time > 0)
        {
            ProfileExManager.Instance.SetTestDelay(item.IndexId, result.Time);
            msg = string.Format(ResUI.TestMeOutput, result.Time, result.Ip);
        }
        else
        {
            // 核心在跑但延迟测不出来 = 当前节点连不通或代理链路断了。
            // 同步把列表里的延迟标为 -1：否则列表还挂着上一次的旧正数，
            // 用户看到「有延迟」的节点接上去却连不上——检测与实际不一致。
            ProfileExManager.Instance.SetTestDelay(item.IndexId, -1);
            msg = $"❌ 节点不可达（{result.Time} ms）\n当前节点: {item.Remarks}\n请尝试切换到其他节点后重测";
        }

        NoticeManager.Instance.SendMessageEx(msg);
        await TestServerAvailabilitySub(msg);
        return result;
    }

    private async Task TestServerAvailabilitySub(string msg)
    {
        RxSchedulers.MainThreadScheduler.Schedule(() =>
        {
            _ = TestServerAvailabilityResult(msg);
        });
        await Task.CompletedTask;
    }

    /// <summary>
    /// 自动故障转移：当前节点连续探测失败时，切到「已知延迟 > 0」的最优节点并重连。
    /// 3 分钟冷却防止全组失效时来回抖动；用户可用配置关闭（CoreBasicItem.AutoFailoverEnabled）。
    /// </summary>
    private async Task TryAutoSwitchOnFailure()
    {
        try
        {
            if (_config.CoreBasicItem.AutoFailoverEnabled == false)
            {
                return;
            }
            if ((DateTime.UtcNow - _lastAutoSwitchAt).TotalMinutes < 3)
            {
                return;
            }

            var allProfiles = await AppManager.Instance.ProfileItems("");
            var profileExs = await ProfileExManager.Instance.GetProfileExs();
            var best = (from p in allProfiles ?? []
                        where p.IndexId != _config.IndexId && !p.ConfigType.IsComplexType()
                        join e in profileExs on p.IndexId equals e.IndexId
                        where e.Delay > 0
                        orderby e.Delay
                        select new { Profile = p, e.Delay }).FirstOrDefault();

            if (best == null)
            {
                NoticeManager.Instance.SendMessageEx("⚠️ 当前节点连接失败，且没有其他已知可用的节点可切换。建议：运行「测试真连接延迟」刷新数据，或输入「采集」获取新节点。");
                return;
            }

            _lastAutoSwitchAt = DateTime.UtcNow;
            Interlocked.Exchange(ref _availabilityConsecutiveFailures, 0);
            NoticeManager.Instance.SendMessageEx($"🔄 当前节点连接失败，已自动切换到「{best.Profile.Remarks}」（延迟 {best.Delay} ms），正在重连...");

            await ConfigHandler.SetDefaultServerIndex(_config, best.Profile.IndexId);
            ReloadRequested.Publish();
        }
        catch (Exception ex)
        {
            Logging.SaveLog("AutoSwitchOnFailure failed", ex);
        }
    }

    public async Task TestServerAvailabilityResult(string msg)
    {
        RunningInfoDisplay = msg;
        await Task.CompletedTask;
    }

    #region System proxy and Routings

    private async Task SetListenerType(ESysProxyType type)
    {
        if (_config.SystemProxyItem.SysProxyType == type)
        {
            return;
        }
        _config.SystemProxyItem.SysProxyType = type;
        await ChangeSystemProxyAsync(type, true);
        NoticeManager.Instance.SendMessageEx($"{ResUI.TipChangeSystemProxy} - {_config.SystemProxyItem.SysProxyType}");

        SystemProxySelected = (int)_config.SystemProxyItem.SysProxyType;
        await ConfigHandler.SaveConfig(_config);
    }

    public async Task ChangeSystemProxyAsync(ESysProxyType type, bool blChange)
    {
        await SysProxyHandler.UpdateSysProxy(_config, false);

        BlSystemProxyClear = type == ESysProxyType.ForcedClear;
        BlSystemProxySet = type == ESysProxyType.ForcedChange;
        BlSystemProxyNothing = type == ESysProxyType.Unchanged;
        BlSystemProxyPac = type == ESysProxyType.Pac;

        if (blChange)
        {
            await DispatcherRefreshIconInteraction.HandleSafe(RxVoid.Default);
        }
    }

    public async Task RefreshRoutingsMenu()
    {
        var routings = await AppManager.Instance.RoutingItems();

        RoutingItems.ReplaceRange(routings);

        SelectedRouting = routings.FirstOrDefault(t => t.IsActive == true);
    }

    private async Task RoutingSelectedChangedAsync()
    {
        if (SelectedRouting == null)
        {
            return;
        }

        var item = await AppManager.Instance.GetRoutingItem(SelectedRouting?.Id);
        if (item is null)
        {
            return;
        }

        if (await ConfigHandler.SetDefaultRouting(_config, item) == 0)
        {
            NoticeManager.Instance.SendMessageEx(ResUI.TipChangeRouting);
            ReloadRequested.Publish();
            await DispatcherRefreshIconInteraction.HandleSafe(RxVoid.Default);
        }
    }

    private async Task DoSystemProxySelected()
    {
        // 视图初始化期间，cmbSystemProxy 的双向绑定会把 SelectedIndex=0（清除系统代理）
        // 推回 VM，触发本方法把配置改写成 ForcedClear 并保存、顺带清掉系统代理——
        // 这正是「每次启动系统代理都被悄悄关掉、测速正常却连不上」的根因。
        // Loaded 之前忽略一切组合框值变化，Loaded 后由 MarkSystemProxyReady 以配置为准重新同步。
        if (!_sysProxyReady)
        {
            return;
        }
        if (_config.SystemProxyItem.SysProxyType == (ESysProxyType)SystemProxySelected)
        {
            return;
        }
        await SetListenerType((ESysProxyType)SystemProxySelected);
    }

    /// <summary>
    /// 视图 Loaded 后调用：系统代理组合框已完成初始化，此后用户的选择才被采纳；
    /// 并以当前配置为准回写一次组合框，纠正初始化期间可能被推入的错误值。
    /// </summary>
    public void MarkSystemProxyReady()
    {
        _sysProxyReady = true;
        var configType = (int)_config.SystemProxyItem.SysProxyType;
        if (SystemProxySelected != configType)
        {
            SystemProxySelected = configType;
        }
    }

    private async Task DoEnableTun()
    {
        if (_config.TunModeItem.EnableTun == EnableTun)
        {
            return;
        }

        _config.TunModeItem.EnableTun = EnableTun;

        if (EnableTun && AllowEnableTun() == false)
        {
            // When running as a non-administrator, reboot to administrator mode
            if (Utils.IsWindows())
            {
                _config.TunModeItem.EnableTun = false;
                await AppManager.Instance.RebootAsAdmin();
                return;
            }
            else
            {
                var password = await PasswordInputInteraction.HandleSafe(RxVoid.Default);
                if (password.IsNullOrEmpty())
                {
                    _config.TunModeItem.EnableTun = false;
                    return;
                }
            }
        }

        await ConfigHandler.SaveConfig(_config);
        ReloadRequested.Publish();
    }

    private bool AllowEnableTun()
    {
        if (Utils.IsWindows())
        {
            return Utils.IsAdministrator();
        }
        else if (Utils.IsLinux())
        {
            return AppManager.Instance.LinuxSudoPwd.IsNotEmpty();
        }
        else if (Utils.IsMacOS())
        {
            return AppManager.Instance.LinuxSudoPwd.IsNotEmpty();
        }
        return false;
    }

    #endregion System proxy and Routings

    #region UI

    public async Task InboundDisplayStatus()
    {
        StringBuilder sb = new();
        sb.Append($"[{EInboundProtocol.mixed}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}");
        if (_config.Inbound.First().SecondLocalPortEnabled)
        {
            sb.Append($",{AppManager.Instance.GetLocalPort(EInboundProtocol.socks2)}");
        }
        sb.Append(']');
        InboundDisplay = $"{ResUI.LabLocal}:{sb}";

        if (_config.Inbound.First().AllowLANConn)
        {
            var lan = _config.Inbound.First().NewPort4LAN
                ? $"[{EInboundProtocol.mixed}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks3)}]"
                : $"[{EInboundProtocol.mixed}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}]";
            InboundLanDisplay = $"{ResUI.LabLAN}:{lan}";
        }
        else
        {
            InboundLanDisplay = $"{ResUI.LabLAN}:{Global.None}";
        }
        await Task.CompletedTask;
    }        // Recompute Connection/Latency/Speed displays from the current default server.
    private async Task RefreshConnectionDisplay()
    {
        try
        {
            // A core is running when either xray or sing-box is up.
            var coreUp = AppManager.Instance.IsRunningCore(ECoreType.Xray) ||
                         AppManager.Instance.IsRunningCore(ECoreType.sing_box);

            if (!coreUp)
            {
                Interlocked.Exchange(ref _availabilityConsecutiveFailures, 0);
                IsCoreConnected = false;
                ConnectionDisplay = "未连接";
                LatencyDisplay = string.Empty;
                StatusSpeedDisplay = string.Empty;
                return;
            }

            // 真实穿透探测：通过本地 SOCKS 入站发一条实际 HTTP 请求。
            // 老实现只看「核心进程活着」+ 上一次存的延迟数字——节点闲置后死掉
            // 也会一直显示「连接成功 延时0.9秒」，状态栏与实际连接不一致。
            // 现在显示的每一个「连接成功」都对应一次刚刚穿透成功的真实请求。
            if (Interlocked.CompareExchange(ref _availabilityInFlight, 1, 0) != 0)
            {
                return; // 上一轮探测还没结束（最坏 ~19 秒，可能跨过 20s 周期）
            }
            try
            {
                var result = await Task.Run(ConnectionHandler.RunAvailabilityCheck);

                if (result.Time > 0)
                {
                    Interlocked.Exchange(ref _availabilityConsecutiveFailures, 0);
                    IsCoreConnected = true;
                    ProfileExManager.Instance.SetTestDelay(_config.IndexId, result.Time);
                    var ip = result.GetValidIp();
                    if (ip.IsNotEmpty())
                    {
                        ProfileExManager.Instance.SetTestIpInfo(_config.IndexId, ip);
                    }
                    ConnectionDisplay = result.Time < 1000
                        ? $"连接成功   延时 {result.Time} ms"
                        : $"连接成功   延时 {result.Time / 1000.0:0.#}秒";
                }
                else
                {
                    // 连续两次失败才判「连接失败」：单次超时可能是瞬时网络抖动
                    var failures = Interlocked.Increment(ref _availabilityConsecutiveFailures);
                    if (failures >= 2)
                    {
                        IsCoreConnected = false;
                        // 列表里的延迟同步标为失败，保持检测与实际一致
                        ProfileExManager.Instance.SetTestDelay(_config.IndexId, -1);
                        ConnectionDisplay = "连接失败   当前节点不可用，请切换节点";
                        // 自动故障转移：切到已知延迟最低的可用节点并重连
                        await TryAutoSwitchOnFailure();
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _availabilityInFlight, 0);
            }

            LatencyDisplay = string.Empty;

            // Compact the speed string already computed by UpdateStatistics
            // ("[x]: ↑..s ↓..s" → "↑..s ↓..s") so the bottom bar stays tight.
            StatusSpeedDisplay = CompactSpeed(SpeedProxyDisplay);
        }
        catch { }
    }

    // Reduce "[x]: ↑2 MB/s ↓1 MB/s" down to "↑2 MB/s ↓1 MB/s" for a tight bottom bar.
    private static string CompactSpeed(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;
        var up = System.Text.RegularExpressions.Regex.Match(raw, @"↑([^↓↑]+)");
        var down = System.Text.RegularExpressions.Regex.Match(raw, @"↓([^↓↑]+)");
        if (up.Success && down.Success)
        {
            return $"↑{up.Groups[1].Value.Trim()} ↓{down.Groups[1].Value.Trim()}";
        }
        return raw;
    }

    public async Task UpdateStatistics(ServerSpeedItem update)
    {
        if (!_config.GuiItem.DisplayRealTimeSpeed)
        {
            return;
        }

        try
        {
            if (AppManager.Instance.IsRunningCore(ECoreType.sing_box))
            {
                SpeedProxyDisplay = string.Format(ResUI.SpeedDisplayText, EInboundProtocol.mixed, Utils.HumanFy(update.ProxyUp), Utils.HumanFy(update.ProxyDown));
                SpeedDirectDisplay = string.Empty;
            }
            else
            {
                SpeedProxyDisplay = string.Format(ResUI.SpeedDisplayText, Global.ProxyTag, Utils.HumanFy(update.ProxyUp), Utils.HumanFy(update.ProxyDown));
                SpeedDirectDisplay = string.Format(ResUI.SpeedDisplayText, Global.DirectTag, Utils.HumanFy(update.DirectUp), Utils.HumanFy(update.DirectDown));
            }
        }
        catch
        {
        }

        // 统计每秒来一次，顺手把底部「连接成功/未连接 + 延时」也刷新，
        // 这样内核启动/停止后状态栏能立刻变色，不必等 20s 定时器。
        await RefreshConnectionDisplay();
    }

    #endregion UI
}
