using ReactiveUI;
using ServiceLib.ViewModels;

namespace v2rayN.Desktop.Views;

public partial class LeakDetectionWindow : ReactiveWindow<LeakDetectionViewModel>
{
    public LeakDetectionWindow()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            this.BindCommand(ViewModel, vm => vm.RunDetectionCmd, v => v.btnRunDetection).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.CloseCmd, v => v.btnClose).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.OverallStatus, v => v.txtOverallStatus.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.StatusMessage, v => v.txtStatusMessage.Text).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.IpLeakStatus, v => v.txtIpLeakStatus.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.IpLeakDetails, v => v.txtIpLeakDetails.Text).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.DnsLeakStatus, v => v.txtDnsLeakStatus.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.DnsLeakDetails, v => v.txtDnsLeakDetails.Text).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.Ipv6LeakStatus, v => v.txtIpv6LeakStatus.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.Ipv6LeakDetails, v => v.txtIpv6LeakDetails.Text).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.WebRtcLeakStatus, v => v.txtWebRtcLeakStatus.Text).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.WebRtcLeakDetails, v => v.txtWebRtcLeakDetails.Text).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.IsRunning, v => v.btnRunDetection.IsEnabled).DisposeWith(disposables);
        });
    }
}
