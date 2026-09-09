using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using v2rayN.Base;
using v2rayN.Manager;

namespace v2rayN.Views;

public partial class MainWindow
{
    private static Config _config;
    private readonly SingleReplaceableDisposable _layoutBindingsDisposable = new();
    private BackupAndRestoreView? _backupAndRestoreView;
    private AIChatWindow? _aiChatWindow;

    public MainWindow()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;
        ThreadPool.RegisterWaitForSingleObject(App.ProgramStarted, OnProgramStarted, null, -1, false);

        App.Current.SessionEnding += Current_SessionEnding;
        Closing += MainWindow_Closing;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        menuSettingsSetUWP.Click += MenuSettingsSetUWP_Click;
        menuClose.Click += MenuClose_Click;
        menuBackupAndRestore.Click += MenuBackupAndRestore_Click;

        pbTheme.Content ??= new ThemeSettingView();

        this.WhenActivated(disposables =>
        {
            //servers
            this.BindCommand(ViewModel, vm => vm.AddVmessServerCmd, v => v.menuAddVmessServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddVlessServerCmd, v => v.menuAddVlessServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddShadowsocksServerCmd, v => v.menuAddShadowsocksServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddSocksServerCmd, v => v.menuAddSocksServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddHttpServerCmd, v => v.menuAddHttpServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddTrojanServerCmd, v => v.menuAddTrojanServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddHysteria2ServerCmd, v => v.menuAddHysteria2Server).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddTuicServerCmd, v => v.menuAddTuicServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddWireguardServerCmd, v => v.menuAddWireguardServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddAnytlsServerCmd, v => v.menuAddAnytlsServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddNaiveServerCmd, v => v.menuAddNaiveServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddCustomServerCmd, v => v.menuAddCustomServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddCustomOutboundServerCmd, v => v.menuAddCustomOutboundServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddPolicyGroupServerCmd, v => v.menuAddPolicyGroupServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddProxyChainServerCmd, v => v.menuAddProxyChainServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaClipboardCmd, v => v.menuAddServerViaClipboard).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaScanCmd, v => v.menuAddServerViaScan).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaImageCmd, v => v.menuAddServerViaImage).DisposeWith(disposables);

            //sub
            this.BindCommand(ViewModel, vm => vm.SubSettingCmd, v => v.menuSubSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateCmd, v => v.menuSubUpdate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateViaProxyCmd, v => v.menuSubUpdateViaProxy).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubGroupUpdateCmd, v => v.menuSubGroupUpdate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubGroupUpdateViaProxyCmd, v => v.menuSubGroupUpdateViaProxy).DisposeWith(disposables);

            //setting
            this.BindCommand(ViewModel, vm => vm.OptionSettingCmd, v => v.menuOptionSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RoutingSettingCmd, v => v.menuRoutingSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.DNSSettingCmd, v => v.menuDNSSetting).DisposeWith(disposables);        // 智能获取：API 有效时后台启动调度器，不再弹设置窗口；否则打开设置 menuAISetting.Click += (s, e) =>
 {
 // 设置入口：无论配置是否有效，都打开设置窗口，让用户能看到/修改
 // API 地址、密钥、模型、分组、间隔等。之前改成"API 有效就不弹窗"
 // 导致用户找不到配置入口。
 try
 {
 var vm = new ServiceLib.ViewModels.AISettingViewModel();
 var dialog = new AISettingWindow { DataContext = vm };
 dialog.ShowDialog(this);

 // 从设置窗口回来后，如果配置有效则后台启动调度器
 var config = AppManager.Instance.Config;
 var ai = config.AIConfigItem;
 if (ai is { Enabled: true }
 && !string.IsNullOrWhiteSpace(ai.ApiUrl)
 && !string.IsNullOrWhiteSpace(ai.ApiKey)
 && !string.IsNullOrWhiteSpace(ai.ModelId))
 {
 _ = ServiceLib.Handler.ConfigHandler.SaveConfig(config);
 ServiceLib.Services.AISchedulerService.Restart(config);
 }
 }
 catch (Exception ex)
 {
                System.Diagnostics.Debug.WriteLine($"menuAISetting failed: {ex}");            MessageBox.Show(
                $"AI 启动失败：{ex.Message}",
                "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    };
    // 一键安全配置：把 TUN / Kill Switch / IPv6 防泄露 / DNS 走代理 / 全局代理
    // 打包成预设一次性应用，避免用户在多处设置里漏配导致真实 IP 泄露。
    menuSecurityPreset.Click += async (s, e) =>
    {
        try
        {
            var result = MessageBox.Show(
                "将应用以下安全预设：\n\n" +
                "• 开启 TUN 模式（全流量走代理）\n" +
                "• 开启 Kill Switch（断线即阻断全部流量）\n" +
                "• 开启严格路由\n" +
                "• 关闭 IPv6（防止 IPv6 泄露真实 IP）\n" +
                "• 系统代理设为全局代理\n\n" +
                "应用后会重启核心服务，确定继续？",
                "🛡️ 一键安全配置", MessageBoxButton.OKCancel, MessageBoxImage.Information);
            if (result != MessageBoxResult.OK) return;

            var config = AppManager.Instance.Config;

            // TUN + Kill Switch + StrictRoute + IPv6 off（IPv6 泄露是真实 IP 泄露的常见途径）
            config.TunModeItem.EnableTun = true;
            config.TunModeItem.AutoRoute = true;
            config.TunModeItem.StrictRoute = true;
            config.TunModeItem.EnableKillSwitch = true;
            config.TunModeItem.EnableIPv6Address = false;

            // 全局代理（ForcedChange = 系统代理指向本地代理端口）
            config.SystemProxyItem.SysProxyType = ServiceLib.Enums.ESysProxyType.ForcedChange;
            config.SystemProxyItem.NotProxyLocalAddress = true;

            _ = await ServiceLib.Handler.ConfigHandler.SaveConfig(config);

            // 重启核心让 TUN/KillSwitch 生效
            try
            {
                await CoreManager.Instance.CoreStop();
                await CoreManager.Instance.LoadCore(null, null);
            }
            catch (Exception ex)
            {
                Logging.SaveLog($"SecurityPreset restart core failed: {ex.Message}");
            }

            MessageBox.Show(
                "✅ 安全预设已应用！\n\n" +
                "TUN 模式 + Kill Switch + 严格路由已开启，IPv6 已关闭，系统代理已设为全局。\n\n" +
                "建议到「设置 → Leak Detection」跑一次泄露检测确认真实 IP 已隐藏。",
                "🛡️ 一键安全配置", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"SecurityPreset failed: {ex}");
            MessageBox.Show($"安全配置失败：{ex.Message}", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    };
    menuAILog.Click += (s, e) =>
    {
        var dialog = new AILogWindow();
        dialog.ShowDialog(this);
    };
    // Floating panel: Show() instead of ShowDialog() so the main window stays usable.
    // Shared between the menu item, the floating bottom-right button, and the tray
    // icon left-click — only one instance.
    var toggleAi = ToggleAiChatPanel;
    menuAIChat.Click += (s, e) => toggleAi();
    btnAiAssistant.Click += (s, e) => toggleAi();

    // Tray icon left-click opens the AI assistant (plus shows the main window).
    ViewModel.StatusBarViewModel?.OpenAiChatRequested.AsObservable()
        .Subscribe(_ => toggleAi())
        .DisposeWith(disposables);
    menuLeakDetection.Click += (s, e) =>
    {
        var vm = new ServiceLib.ViewModels.LeakDetectionViewModel();
        var dialog = new LeakDetectionWindow { DataContext = vm };
        dialog.ShowDialog(this);
    };
            menuPromotion.Click += (s, e) =>
            {
                ProcUtils.ProcessStart(Global.Website);
            };
            this.BindCommand(ViewModel, vm => vm.FullConfigTemplateCmd, v => v.menuFullConfigTemplate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.GlobalHotkeySettingCmd, v => v.menuGlobalHotkeySetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RebootAsAdminCmd, v => v.menuRebootAsAdmin).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ClearServerStatisticsCmd, v => v.menuClearServerStatistics).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.OpenTheFileLocationCmd, v => v.menuOpenTheFileLocation).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetDefaultCmd, v => v.menuRegionalPresetsDefault).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetRussiaCmd, v => v.menuRegionalPresetsRussia).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetIranCmd, v => v.menuRegionalPresetsIran).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.ReloadCmd, v => v.menuReload).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlReloadEnabled, v => v.menuReload.IsEnabled).DisposeWith(disposables);

            _layoutBindingsDisposable.DisposeWith(disposables);

            this.WhenAnyValue(v => v.ViewModel.MainGirdOrientation)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(UpdateLayout)
                .DisposeWith(disposables);

            this.WhenAnyValue(v => v.ViewModel.StatusBarViewModel)
                .Subscribe(vm => ViewHost.Show(contentStatusBarView, vm))
                .DisposeWith(disposables);

            ViewModel.ReadTextFromClipboardInteraction.RegisterHandler(interaction =>
            {
                var clipboardData = WindowsUtils.GetClipboardData();
                interaction.SetOutput(clipboardData);
            }).DisposeWith(disposables);

            ViewModel.ScanScreenInteraction.RegisterHandler(interaction =>
            {
                ShowHideWindow(false);
                if (Application.Current?.MainWindow is { } window)
                {
                    var bytes = QRCodeWindowsUtils.CaptureScreen(window);
                    interaction.SetOutput(bytes);
                }
                ShowHideWindow(true);
            }).DisposeWith(disposables);

            ViewModel.BrowseImageFileInteraction.RegisterHandler(interaction =>
            {
                if (UI.OpenFileDialog(out var fileName, "PNG|*.png|All|*.*") != true)
                {
                    interaction.SetOutput(null);
                    return;
                }
                interaction.SetOutput(fileName);
            }).DisposeWith(disposables);

            ViewModel.ShowHideWindowInteraction.RegisterHandler(interaction =>
            {
                ShowHideWindow(interaction.Input);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            AppEvents.SendSnackMsgRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(async content => await DelegateSnackMsg(content))
              .DisposeWith(disposables);

            AppEvents.AppExitRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(_ => StorageUI())
              .DisposeWith(disposables);

            AppEvents.ShutdownRequested
             .AsObservable()
             .ObserveOn(RxSchedulers.MainThreadScheduler)
             .Subscribe(Shutdown)
             .DisposeWith(disposables);
        });

        Title = $"LDv2rayN | {Global.Website} | {(Utils.IsAdministrator() ? ResUI.RunAsAdmin : ResUI.NotRunAsAdmin)}";
        if (_config.UiItem.AutoHideStartup)
        {
            WindowState = WindowState.Minimized;
        }

        if (!_config.GuiItem.EnableHWA)
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        }

        AddHelpMenuItem();
        WindowsManager.Instance.RegisterGlobalHotkey(_config, OnHotkeyHandler, null);
    }

    #region Event

    private void OnProgramStarted(object state, bool timeout)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            ShowHideWindow(true);
        });
    }

    private async Task DelegateSnackMsg(string content)
    {
        MainSnackbar.MessageQueue?.Enqueue(content);
        await Task.CompletedTask;
    }

    private void OnHotkeyHandler(EGlobalHotkey e)
    {
        switch (e)
        {
            case EGlobalHotkey.ShowForm:
                ShowHideWindow(null);
                break;

            case EGlobalHotkey.SystemProxyClear:
            case EGlobalHotkey.SystemProxySet:
            case EGlobalHotkey.SystemProxyUnchanged:
            case EGlobalHotkey.SystemProxyPac:
                AppEvents.SysProxyChangeRequested.Publish((ESysProxyType)((int)e - 1));
                break;
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        ShowHideWindow(false);
    }

    private async void Current_SessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        Logging.SaveLog("Current_SessionEnding");
        StorageUI();
        await AppManager.Instance.AppExitAsync(false);
    }

    private void Shutdown(bool obj)
    {
        Application.Current.Shutdown();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
        {
            switch (e.Key)
            {
                case Key.V:
                    if (Keyboard.FocusedElement is TextBox)
                    {
                        return;
                    }
                    AddServerViaClipboardAsync().ContinueWith(_ => { });

                    break;

                case Key.S:
                    ScanScreenTaskAsync().ContinueWith(_ => { });
                    break;
            }
        }
        else
        {
            if (e.Key == Key.F5)
            {
                ViewModel?.Reload();
            }
        }
    }

    private void MenuClose_Click(object sender, RoutedEventArgs e)
    {
        StorageUI();
        ShowHideWindow(false);
    }

    private void MenuSettingsSetUWP_Click(object sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart(Utils.GetBinPath("EnableLoopback.exe"));
    }

    public async Task AddServerViaClipboardAsync()
    {
        var clipboardData = WindowsUtils.GetClipboardData();
        if (clipboardData.IsNotEmpty() && ViewModel != null)
        {
            await ViewModel.AddServerViaClipboardAsync(clipboardData);
        }
    }

    private async Task ScanScreenTaskAsync()
    {
        ShowHideWindow(false);

        if (Application.Current?.MainWindow is Window window)
        {
            var bytes = QRCodeWindowsUtils.CaptureScreen(window);
            await ViewModel?.ScanScreenResult(bytes);
        }

        ShowHideWindow(true);
    }

    private void MenuBackupAndRestore_Click(object sender, RoutedEventArgs e)
    {
        _backupAndRestoreView ??= new BackupAndRestoreView();
        _backupAndRestoreView.ViewModel = ViewModel?.BackupAndRestoreViewModel;
        DialogHost.Show(_backupAndRestoreView, "RootDialog");
    }

    /// <summary>
    /// Toggle the floating AI assistant chat panel. Keeps a single instance alive
    /// so reopening it doesn't recreate the ViewModel (and lose chat history).
    /// </summary>
    private void ToggleAiChatPanel()
    {
        if (_aiChatWindow is { IsVisible: true })
        {
            _aiChatWindow.Hide();
            return;
        }

        if (_aiChatWindow == null)
        {
            try
            {
                _aiChatWindow = new AIChatWindow { Owner = this };
                _aiChatWindow.Closed += (_, _) => _aiChatWindow = null;
            }
            catch (Exception ex)
            {
                Logging.SaveLog($"AIChatWindow creation failed: {ex}");
                MessageBox.Show(
                    $"AI 对话框创建失败：{ex.Message}",
                    "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        // Re-anchor to the bottom-right of the main window every time.
        double anchorRight = Left + Width - _aiChatWindow.Width - 16;
        double anchorBottom = Top + Height - _aiChatWindow.Height - 16;
        _aiChatWindow.Left = Math.Max(anchorRight, 0);
        _aiChatWindow.Top = Math.Max(anchorBottom, 0);

        _aiChatWindow.Show();
        _aiChatWindow.Activate();
    }

    #endregion Event

    #region UI

    public void ShowHideWindow(bool? blShow)
    {
        var bl = blShow ?? !AppManager.Instance.ShowInTaskbar;
        if (bl)
        {
            this?.Show();
            if (this?.WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            this?.Activate();
            this?.Focus();
        }
        else
        {
            this?.Hide();
        }
        AppManager.Instance.ShowInTaskbar = bl;
    } protected override void OnLoaded(object? sender, RoutedEventArgs e)
 {
 base.OnLoaded(sender, e);
 if (_config.UiItem.AutoHideStartup)
 {
 ShowHideWindow(false);
 }
 RestoreUI();

 // 预创建 AI 对话框（延迟 1.5s，等主窗口稳定），用户点击时秒开。
 // WPF 窗口首次创建要加载 XAML + MaterialDesign 样式，是最慢的一步。
 Dispatcher.BeginInvoke(new Action(() =>
 {
 if (_aiChatWindow != null) return;
 try
 {
 _aiChatWindow = new AIChatWindow { Owner = this };
 _aiChatWindow.Closed += (_, _) => _aiChatWindow = null;
 }
 catch (Exception ex)
 {
 Logging.SaveLog($"AIChatWindow prewarm failed: {ex}");
 }
 }), System.Windows.Threading.DispatcherPriority.Background);
 }

    private void RestoreUI()
    {
        if (_config.UiItem.MainGirdHeight1 > 0 && _config.UiItem.MainGirdHeight2 > 0)
        {
            if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Horizontal)
            {
                gridMain.ColumnDefinitions[0].Width = new GridLength(_config.UiItem.MainGirdHeight1, GridUnitType.Star);
                gridMain.ColumnDefinitions[2].Width = new GridLength(_config.UiItem.MainGirdHeight2, GridUnitType.Star);
            }
            else if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Vertical)
            {
                gridMain1.RowDefinitions[0].Height = new GridLength(_config.UiItem.MainGirdHeight1, GridUnitType.Star);
                gridMain1.RowDefinitions[2].Height = new GridLength(_config.UiItem.MainGirdHeight2, GridUnitType.Star);
            }
        }
    }

    private void StorageUI()
    {
        ConfigHandler.SaveWindowSizeItem(_config, GetType().Name, Width, Height);

        if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Horizontal)
        {
            ConfigHandler.SaveMainGirdHeight(_config, gridMain.ColumnDefinitions[0].ActualWidth, gridMain.ColumnDefinitions[2].ActualWidth);
        }
        else if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Vertical)
        {
            ConfigHandler.SaveMainGirdHeight(_config, gridMain1.RowDefinitions[0].ActualHeight, gridMain1.RowDefinitions[2].ActualHeight);
        }
    }

    private void UpdateLayout(EGirdOrientation orientation)
    {
        var currentLayoutDisposables = new MultipleDisposable();
        _layoutBindingsDisposable.Create(currentLayoutDisposables);

        gridMain.Visibility = orientation == EGirdOrientation.Horizontal ? Visibility.Visible : Visibility.Collapsed;
        gridMain1.Visibility = orientation == EGirdOrientation.Vertical ? Visibility.Visible : Visibility.Collapsed;
        gridMain2.Visibility = orientation == EGirdOrientation.Tab ? Visibility.Visible : Visibility.Collapsed;

        switch (orientation)
        {
            case EGirdOrientation.Horizontal:
                this.WhenAnyValue(v => v.ViewModel.ProfilesViewModel)
                    .Subscribe(vm => ViewHost.Show(tabProfiles, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.WhenAnyValue(v => v.ViewModel.MsgViewModel)
                    .Subscribe(vm => ViewHost.Show(tabMsgView, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.WhenAnyValue(v => v.ViewModel.ClashProxiesViewModel)
                    .Subscribe(vm => ViewHost.Show(tabClashProxies, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.WhenAnyValue(v => v.ViewModel.ClashConnectionsViewModel)
                    .Subscribe(vm => ViewHost.Show(tabClashConnections, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabMsgView.Visibility).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashProxies.Visibility).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashConnections.Visibility).DisposeWith(currentLayoutDisposables);
                this.Bind(ViewModel, vm => vm.TabMainSelectedIndex, v => v.tabMain.SelectedIndex).DisposeWith(currentLayoutDisposables);
                break;

            case EGirdOrientation.Vertical:
                this.WhenAnyValue(v => v.ViewModel.ProfilesViewModel)
                    .Subscribe(vm => ViewHost.Show(tabProfiles1, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.WhenAnyValue(v => v.ViewModel.MsgViewModel)
                    .Subscribe(vm => ViewHost.Show(tabMsgView1, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.WhenAnyValue(v => v.ViewModel.ClashProxiesViewModel)
                    .Subscribe(vm => ViewHost.Show(tabClashProxies1, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.WhenAnyValue(v => v.ViewModel.ClashConnectionsViewModel)
                    .Subscribe(vm => ViewHost.Show(tabClashConnections1, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabMsgView1.Visibility).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashProxies1.Visibility).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashConnections1.Visibility).DisposeWith(currentLayoutDisposables);
                this.Bind(ViewModel, vm => vm.TabMainSelectedIndex, v => v.tabMain1.SelectedIndex).DisposeWith(currentLayoutDisposables);
                break;

            case EGirdOrientation.Tab:
            default:
                this.WhenAnyValue(v => v.ViewModel.ProfilesViewModel)
                    .Subscribe(vm => ViewHost.Show(tabProfiles2, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.WhenAnyValue(v => v.ViewModel.MsgViewModel)
                    .Subscribe(vm => ViewHost.Show(tabMsgView2, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.WhenAnyValue(v => v.ViewModel.ClashProxiesViewModel)
                    .Subscribe(vm => ViewHost.Show(tabClashProxies2, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.WhenAnyValue(v => v.ViewModel.ClashConnectionsViewModel)
                    .Subscribe(vm => ViewHost.Show(tabClashConnections2, vm))
                    .DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashProxies2.Visibility).DisposeWith(currentLayoutDisposables);
                this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashConnections2.Visibility).DisposeWith(currentLayoutDisposables);
                this.Bind(ViewModel, vm => vm.TabMainSelectedIndex, v => v.tabMain2.SelectedIndex).DisposeWith(currentLayoutDisposables);
                break;
        }

        RestoreUI();
    }

    private void AddHelpMenuItem()
    {
        var coreInfo = CoreInfoManager.Instance.GetCoreInfo();
        foreach (var it in coreInfo
            .Where(t => t.CoreType is not ECoreType.v2fly
                        and not ECoreType.hysteria))
        {
            var item = new MenuItem()
            {
                Tag = it.Url.Replace(@"/releases", ""),
                Header = string.Format(ResUI.menuWebsiteItem, it.CoreType.ToString().Replace("_", " ")).UpperFirstChar()
            };
            item.Click += MenuItem_Click;
            menuHelp.Items.Add(item);
        }
    }

    private void MenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item)
        {
            ProcUtils.ProcessStart(item.Tag.ToString());
        }
    }

    #endregion UI
}
