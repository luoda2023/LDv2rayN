using System.Diagnostics;
using System.Windows;
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
            // New bottom bar (right of the routing combo): connection state, latency, speed.
            this.OneWayBind(ViewModel, vm => vm.ConnectionDisplay, v => v.txtConnectionDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.LatencyDisplay, v => v.txtLatencyDisplay.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.StatusSpeedDisplay, v => v.txtStatusSpeedDisplay.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.EnableTun, v => v.togEnableTun.IsChecked).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.SystemProxySelected, v => v.cmbSystemProxy.SelectedIndex).DisposeWith(disposables);
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
    }        private async void menuExit_Click(object sender, RoutedEventArgs e)
        {
            tbNotify.Dispose();

            // 告诉 MainWindow_Closing 别再把本次 close 拦截为“隐藏到托盘”。
            // 没有这个标志，Application.Current.Shutdown() 会被 e.Cancel = true 拦下，进程会永远残留。
            ExitManager.ForceExit = true;

            // 5 秒看门狗：如果下面的清理链路 (ShutdownRequested -> Application.Current.Shutdown -> MainWindow Closing 拦截)
            // 因任何原因卡住，则强制结束进程。这是最后防线。
            var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            watchdog.Tick += (_, _) =>
            {
                try
                {
                    if (Process.GetCurrentProcess().HasExited) return;
                    Process.GetCurrentProcess().Kill();
                }
                catch { }
            };
            watchdog.Start();

            try
            {
                await AppManager.Instance.AppExitAsync(false);
            }
            catch { }

            // 直接触发 WPF Shutdown，而不是靠 AppExitAsync 内部发 ShutdownRequested 事件走 MainWindow 的订阅。
            // MainWindow_Closing 会 e.Cancel = true 拦截关窗（隐藏到托盘），Application.Current.Shutdown 会绕开它。
            try { Application.Current.Shutdown(); } catch { }

            watchdog.Stop();
        }

    private void txtRunningInfoDisplay_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.TestServerAvailability();
    }
}
