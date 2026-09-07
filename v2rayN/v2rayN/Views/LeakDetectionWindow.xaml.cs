using ServiceLib.ViewModels;

namespace v2rayN.Views;

public partial class LeakDetectionWindow
{
    public LeakDetectionWindow()
    {
        InitializeComponent();

        var vm = new LeakDetectionViewModel();
        DataContext = vm;

        this.WhenActivated(disposables =>
        {
            this.BindCommand(vm, x => x.RunDetectionCmd, v => v.btnRunDetection).DisposeWith(disposables);
            this.BindCommand(vm, x => x.CloseCmd, v => v.btnClose).DisposeWith(disposables);

            this.OneWayBind(vm, x => x.OverallStatus, v => v.txtOverallStatus.Text).DisposeWith(disposables);
            this.OneWayBind(vm, x => x.StatusMessage, v => v.txtStatusMessage.Text).DisposeWith(disposables);

            this.OneWayBind(vm, x => x.IpLeakStatus, v => v.txtIpLeakStatus.Text).DisposeWith(disposables);
            this.OneWayBind(vm, x => x.IpLeakDetails, v => v.txtIpLeakDetails.Text).DisposeWith(disposables);

            this.OneWayBind(vm, x => x.DnsLeakStatus, v => v.txtDnsLeakStatus.Text).DisposeWith(disposables);
            this.OneWayBind(vm, x => x.DnsLeakDetails, v => v.txtDnsLeakDetails.Text).DisposeWith(disposables);

            this.OneWayBind(vm, x => x.Ipv6LeakStatus, v => v.txtIpv6LeakStatus.Text).DisposeWith(disposables);
            this.OneWayBind(vm, x => x.Ipv6LeakDetails, v => v.txtIpv6LeakDetails.Text).DisposeWith(disposables);

            this.OneWayBind(vm, x => x.WebRtcLeakStatus, v => v.txtWebRtcLeakStatus.Text).DisposeWith(disposables);
            this.OneWayBind(vm, x => x.WebRtcLeakDetails, v => v.txtWebRtcLeakDetails.Text).DisposeWith(disposables);

            this.OneWayBind(vm, x => x.IsRunning, v => v.btnRunDetection.IsEnabled).DisposeWith(disposables);
        });
        WindowsUtils.SetDarkBorder(this, AppManager.Instance.Config.UiItem.CurrentTheme);
    }
}
