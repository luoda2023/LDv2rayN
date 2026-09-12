using DialogHostAvalonia;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public partial class StatusBarView : ReactiveUserControl<StatusBarViewModel>
{
    private static Config _config;

    public StatusBarView()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;

        txtRunningServerDisplay.Tapped += TxtRunningServerDisplay_Tapped;
        txtRunningInfoDisplay.Tapped += TxtRunningServerDisplay_Tapped;

        this.WhenActivated(disposables =>
        {
            //status bar
            this.OneWayBind(ViewModel, vm => vm.InboundDisplay, v => v.txtInboundDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.InboundLanDisplay, v => v.txtInboundLanDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.RunningServerDisplay, v => v.txtRunningServerDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.RunningInfoDisplay, v => v.txtRunningInfoDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.SpeedProxyDisplay, v => v.txtSpeedProxyDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.SpeedDirectDisplay, v => v.txtSpeedDirectDisplay.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnableTun, v => v.togEnableTun.IsChecked).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.SystemProxySelected, v => v.cmbSystemProxy.SelectedIndex).DisposeWith(disposables);
            // 组合框初始化会把 SelectedIndex=0（清除系统代理）推回 VM，启动时悄悄关掉系统代理。
            // Loaded 之前 VM 忽略该值，Loaded 后以配置为准重新同步一次。
            this.Loaded += (_, _) => ViewModel.MarkSystemProxyReady();
            this.Bind(ViewModel, vm => vm.SelectedRouting, v => v.cmbRoutings2.SelectedItem).DisposeWith(disposables);

            btnAIFetch.Click += async (s, e) =>
            {
                var config = AppManager.Instance.Config;
                var aiService = new ServiceLib.Services.AIFetchService(config, async (success, msg) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        NoticeManager.Instance.Enqueue(msg);
                    });
                    await Task.CompletedTask;
                });
                _ = Task.Run(async () =>
                {
                    await aiService.RunFullCycleAsync();
                });
            };

            ViewModel.SetClipboardDataInteraction.RegisterHandler(async interaction =>
            {
                var strData = interaction.Input;
                await AvaUtils.SetClipboardData(this, strData);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.PasswordInputInteraction.RegisterHandler(async interaction =>
            {
                var result = await PasswordInputAsync();
                interaction.SetOutput(result);
            }).DisposeWith(disposables);

            ViewModel.DispatcherRefreshIconInteraction.RegisterHandler(interaction =>
            {
                Dispatcher.UIThread.Post(RefreshIcon, DispatcherPriority.Default);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            // 连接状态变化 -> 托盘图标切换：
            // 未连接 = LDv2rayN.png，连接成功 = LDv2rayN2.png（红色）。跟 WPF 版行为一致。
            ViewModel.WhenAnyValue(vm => vm.IsCoreConnected)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(connected =>
                {
                    try
                    {
                        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                        {
                            desktop.MainWindow.Icon = AvaUtils.GetAppIcon(connected);
                            var iconslist = TrayIcon.GetIcons(Application.Current);
                            if (iconslist is { Count: > 0 })
                            {
                                iconslist[0].Icon = desktop.MainWindow.Icon;
                                TrayIcon.SetIcons(Application.Current, iconslist);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ServiceLib.Common.Logging.SaveLog("tray icon switch failed", ex);
                    }
                })
                .DisposeWith(disposables);
        });

        //spEnableTun.IsVisible = (Utils.IsWindows() || AppHandler.Instance.IsAdministrator);

        if (Utils.IsNonWindows() && cmbSystemProxy.Items.IsReadOnly == false)
        {
            cmbSystemProxy.Items.RemoveAt(cmbSystemProxy.Items.Count - 1);
        }

        // Because this view has not yet been initialized when DispatcherRefreshIconInteraction is first called.
        RefreshIcon();
    }

    private void RefreshIcon()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow.Icon = AvaUtils.GetAppIcon(ViewModel?.IsCoreConnected == true);
            var iconslist = TrayIcon.GetIcons(Application.Current);
            if (iconslist is { Count: > 0 })
            {
                iconslist[0].Icon = desktop.MainWindow.Icon;
                TrayIcon.SetIcons(Application.Current, iconslist);
            }
        }
    }

    private async Task<string?> PasswordInputAsync()
    {
        var dialog = new SudoPasswordInputView();
        var obj = await DialogHost.Show(dialog);

        var password = obj?.ToString();
        if (password.IsNullOrEmpty())
        {
            togEnableTun.IsChecked = false;
            return password;
        }

        AppManager.Instance.LinuxSudoPwd = password;
        return password;
    }

    private void TxtRunningServerDisplay_Tapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        ViewModel?.TestServerAvailability();
    }
}
