namespace ServiceLib.ViewModels;

public partial class LeakDetectionViewModel : MyReactiveObject, ICloseable
{
    public event EventHandler? RequestClose;

    private readonly LeakDetectionService _leakService = new();

    [Reactive] public partial bool IsRunning { get; set; }
    [Reactive] public partial string StatusMessage { get; set; } = string.Empty;
    [Reactive] public partial bool OverallSafe { get; set; }
    [Reactive] public partial string OverallStatus { get; set; } = string.Empty;

    // Test results
    [Reactive] public partial string IpLeakStatus { get; set; } = string.Empty;
    [Reactive] public partial string IpLeakDetails { get; set; } = string.Empty;
    [Reactive] public partial bool IpLeakIsLeaking { get; set; }

    [Reactive] public partial string DnsLeakStatus { get; set; } = string.Empty;
    [Reactive] public partial string DnsLeakDetails { get; set; } = string.Empty;
    [Reactive] public partial bool DnsLeakIsLeaking { get; set; }

    [Reactive] public partial string Ipv6LeakStatus { get; set; } = string.Empty;
    [Reactive] public partial string Ipv6LeakDetails { get; set; } = string.Empty;
    [Reactive] public partial bool Ipv6LeakIsLeaking { get; set; }

    [Reactive] public partial string WebRtcLeakStatus { get; set; } = string.Empty;
    [Reactive] public partial string WebRtcLeakDetails { get; set; } = string.Empty;
    [Reactive] public partial bool WebRtcLeakIsLeaking { get; set; }

    public ReactiveCommand<RxVoid, RxVoid> RunDetectionCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> CloseCmd { get; }

    public LeakDetectionViewModel()
    {
        RunDetectionCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await RunDetection();
        });

        CloseCmd = ReactiveCommand.Create(() =>
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
        });

        OverallStatus = "Ready — Click 'Run Detection' to start";
    }

    private async Task RunDetection()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        StatusMessage = "Running leak detection tests...";

        // Reset all results
        ResetResults();

        try
        {
            var webProxy = await GetWebProxy();
            var result = await _leakService.RunFullDetection(webProxy);

            // Update UI with results
            UpdateIpLeakResult(result.IpLeak);
            UpdateDnsLeakResult(result.DnsLeak);
            UpdateIpv6LeakResult(result.Ipv6Leak);
            UpdateWebRtcLeakResult(result.WebRtcLeak);

            // Overall verdict
            OverallSafe = result.OverallSafe;
            OverallStatus = result.OverallSafe
                ? "✅ ALL TESTS PASSED — Your IP is protected!"
                : "⚠️ LEAKS DETECTED — Review the test results below";

            if (result.Error.IsNotEmpty())
            {
                StatusMessage = $"Error: {result.Error}";
            }
            else
            {
                StatusMessage = "Detection complete.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            OverallSafe = false;
            OverallStatus = "❌ Detection failed";
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void ResetResults()
    {
        OverallSafe = false;
        OverallStatus = "Running tests...";

        IpLeakStatus = "⏳ Testing...";
        IpLeakDetails = string.Empty;
        IpLeakIsLeaking = false;

        DnsLeakStatus = "⏳ Testing...";
        DnsLeakDetails = string.Empty;
        DnsLeakIsLeaking = false;

        Ipv6LeakStatus = "⏳ Testing...";
        Ipv6LeakDetails = string.Empty;
        Ipv6LeakIsLeaking = false;

        WebRtcLeakStatus = "⏳ Testing...";
        WebRtcLeakDetails = string.Empty;
        WebRtcLeakIsLeaking = false;
    }

    private void UpdateIpLeakResult(IpLeakInfo info)
    {
        IpLeakIsLeaking = info.IsLeaking;
        IpLeakStatus = info.IsLeaking ? "❌ IP LEAK DETECTED" : "✅ No IP leak";
        IpLeakDetails = info.Details;
    }

    private void UpdateDnsLeakResult(DnsLeakInfo info)
    {
        DnsLeakIsLeaking = info.IsLeaking;
        DnsLeakStatus = info.IsLeaking ? "❌ DNS LEAK DETECTED" : "✅ No DNS leak";
        DnsLeakDetails = info.Details;
    }

    private void UpdateIpv6LeakResult(IPv6LeakInfo info)
    {
        Ipv6LeakIsLeaking = info.IsLeaking;
        Ipv6LeakStatus = info.IsLeaking ? "❌ IPv6 LEAK DETECTED" : "✅ No IPv6 leak";
        Ipv6LeakDetails = info.Details;
    }

    private void UpdateWebRtcLeakResult(WebRtcLeakInfo info)
    {
        WebRtcLeakIsLeaking = info.IsLeaking;
        WebRtcLeakStatus = info.IsLeaking ? "❌ WebRTC LEAK DETECTED" : "✅ No WebRTC leak";
        WebRtcLeakDetails = info.Details;
    }

    private static async Task<WebProxy?> GetWebProxy()
    {
        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.socks);
        if (port <= 0)
        {
            return null;
        }
        return new WebProxy($"socks5://{Global.Loopback}:{port}");
    }
}
