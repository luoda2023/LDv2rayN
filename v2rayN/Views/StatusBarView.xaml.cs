using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using v2rayN.Manager;

namespace v2rayN.Views;

public partial class StatusBarView
{
    private static Config _config;

    public StatusBarView()
    {
        InitializeComponent();
        _config = AppManager.Instance.Config;

        menuExit.Click += menuExit_Click;
        txtRunningServerDisplay.PreviewMouseDown += txtRunningInfoDisplay_MouseDoubleClick;
        txtRunningInfoDisplay.PreviewMouseDown += txtRunningInfoDisplay_MouseDoubleClick;

        this.WhenActivated(disposables =>
        {
            //system proxy
            this.OneWayBind(ViewModel, vm => vm.BlSystemProxyClear, v => v.menuSystemProxyClear2.Visibility, conversionHint: BooleanToVisibilityHint.UseHidden, viewModelToViewConverterOverride: new BooleanToVisibilityTypeConverter()).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlSystemProxySet, v => v.menuSystemProxySet2.Visibility, conversionHint: BooleanToVisibilityHint.UseHidden, viewModelToViewConverterOverride: new BooleanToVisibilityTypeConverter()).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlSystemProxyNothing, v => v.menuSystemProxyNothing2.Visibility, conversionHint: BooleanToVisibilityHint.UseHidden, viewModelToViewConverterOverride: new BooleanToVisibilityTypeConverter()).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlSystemProxyPac, v => v.menuSystemProxyPac2.Visibility, conversionHint: BooleanToVisibilityHint.UseHidden, viewModelToViewConverterOverride: new BooleanToVisibilityTypeConverter()).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SystemProxyClearCmd, v => v.menuSystemProxyClear).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SystemProxySetCmd, v => v.menuSystemProxySet).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SystemProxyNothingCmd, v => v.menuSystemProxyNothing).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SystemProxyPacCmd, v => v.menuSystemProxyPac).DisposeWith(disposables);

            //routings and servers
            this.OneWayBind(ViewModel, vm => vm.RoutingItems, v => v.cmbRoutings.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedRouting, v => v.cmbRoutings.SelectedItem).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlRouting, v => v.menuRoutings.Visibility).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlRouting, v => v.sepRoutings.Visibility).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.Servers, v => v.cmbServers.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedServer, v => v.cmbServers.SelectedItem).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlServers, v => v.cmbServers.Visibility).DisposeWith(disposables);

            //tray menu
            this.BindCommand(ViewModel, vm => vm.AddServerViaClipboardCmd, v => v.menuAddServerViaClipboard2).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaScanCmd, v => v.menuAddServerViaScan2).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateCmd, v => v.menuSubUpdate2).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateViaProxyCmd, v => v.menuSubUpdateViaProxy2).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.CopyProxyCmdToClipboardCmd, v => v.menuCopyProxyCmdToClipboard).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.RunningServerToolTipText, v => v.tbNotify.ToolTipText).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.NotifyLeftClickCmd, v => v.tbNotify.LeftClickCommand).DisposeWith(disposables);

            //status bar
            this.OneWayBind(ViewModel, vm => vm.InboundDisplay, v => v.txtInboundDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.InboundLanDisplay, v => v.txtInboundLanDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.RunningServerDisplay, v => v.txtRunningServerDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.RunningInfoDisplay, v => v.txtRunningInfoDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.SpeedProxyDisplay, v => v.txtSpeedProxyDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.SpeedDirectDisplay, v => v.txtSpeedDirectDisplay.Text).DisposeWith(disposables);
            // New bottom bar (right of the routing combo): connection state + speed.
            this.OneWayBind(ViewModel, vm => vm.ConnectionDisplay, v => v.txtConnectionDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.StatusSpeedDisplay, v => v.txtStatusSpeedDisplay.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnableTun, v => v.togEnableTun.IsChecked).DisposeWith(disposables);

            // Switch tray icon based on connection state.
            // 未连接 = LDv2rayN.ico（源：仓库根 LDv2rayN.png）
            // 已连接 = LDv2rayN2.ico（源：仓库根 LDv2rayN2.png，红色）
            // 注意：这两个 ico 必须在 v2rayN.csproj 里登记为 Resource，否则这里加载会抛异常
            // （之前用的 NotifyIconRed.ico 就没登记，导致连上后图标根本不变）。
            ViewModel.WhenAnyValue(vm => vm.IsCoreConnected)
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(connected =>
            {
                // 本视图 DataContext 从未设为 ViewModel（全部绑定走 this.OneWayBind），
                // XAML 里的 DataTrigger {Binding IsCoreConnected} 因此永远不生效，
                // chip 会一直卡在默认红色。颜色必须在这里用代码设置，
                // 与文字（RefreshConnectionDisplay 里同时赋值）保持严格同步：
                //   已连接 = 绿 #4CAF50，未连接 = 红 #F44336
                bdConnectionStatus.Background = new System.Windows.Media.SolidColorBrush(
    connected
    ? System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50)
    : System.Windows.Media.Color.FromRgb(0xF4, 0x43, 0x36));

                try
                {
                    var path = connected ? WindowsManager.BrandIconConnectedPath : WindowsManager.BrandIconPath;
                    tbNotify.IconSource = new System.Windows.Media.ImageSourceConverter()
        .ConvertFromString($"pack://application:,,,{path}") as System.Windows.Media.ImageSource;
                }
                catch (Exception ex)
                {
                    Logging.SaveLog("tray icon switch failed", ex);
                }
            })
            .DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.SystemProxySelected, v => v.cmbSystemProxy.SelectedIndex).DisposeWith(disposables);
            // 组合框初始化会把 SelectedIndex=0（清除系统代理）推回 VM，启动时悄悄关掉系统代理。
            // Loaded 之前 VM 忽略该值，Loaded 后以配置为准重新同步一次。
            this.Loaded += (_, _) => ViewModel.MarkSystemProxyReady();
            this.OneWayBind(ViewModel, vm => vm.RoutingItems, v => v.cmbRoutings2.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.SelectedRouting, v => v.cmbRoutings2.SelectedItem).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlRouting, v => v.cmbRoutings2.Visibility).DisposeWith(disposables);

            ViewModel.SetClipboardDataInteraction.RegisterHandler(interaction =>
            {
                var strData = interaction.Input;
                WindowsUtils.SetClipboardData(strData);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);

            ViewModel.DispatcherRefreshIconInteraction.RegisterHandler(interaction =>
            {
                Application.Current?.Dispatcher.Invoke(RefreshIcon, DispatcherPriority.Normal);
                interaction.SetOutput(RxVoid.Default);
            }).DisposeWith(disposables);
        });

        RefreshIcon();

        // AI auto-crawl completion -> tray notification
        ServiceLib.Services.AISchedulerService.RunCompleted += (msg, added) =>
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                try
                {
                    tbNotify.ShowNotification("AI 自动搜索完成", msg, H.NotifyIcon.Core.NotificationIcon.Info);
                }
                catch { }
            });
        };
    }

    private void RefreshIcon()
    {
        Application.Current.MainWindow?.Icon = WindowsManager.Instance.GetAppIcon(_config);
    }
    private async void menuExit_Click(object sender, RoutedEventArgs e)
    {
        tbNotify.Dispose();

        // 告诉 MainWindow_Closing 别再把本次 close 拦截为“隐藏到托盘”。
        // 没有这个标志，Application.Current.Shutdown() 会被 e.Cancel = true 拦下，进程会永远残留。
        ExitManager.ForceExit = true;

        // 优雅退出跑在独立线程，不被 UI 线程阻塞。CoreStop 停核心进程可能需要 ~7s，
        // 给 15s 余量；超时后直接强杀进程，绝不让用户去任务管理器手动结束。
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var cleanup = Task.Run(async () =>
        {
            try
            { await AppManager.Instance.AppExitAsync(false); }
            catch { }
            Application.Current.Dispatcher.Invoke(() =>
     {
            try
            { Application.Current.Shutdown(); }
            catch { }
        });
        });
        if (await Task.WhenAny(cleanup, Task.Delay(TimeSpan.FromSeconds(15), cts.Token)) != cleanup)
        {
            Logging.SaveLog("tray exit: cleanup did not finish in 15s, force-killing process");
            Environment.Exit(0);
        }


    }
    private void txtRunningInfoDisplay_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.TestServerAvailability();
    }
}
