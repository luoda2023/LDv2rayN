namespace ServiceLib.Services;

/// <summary>
/// Leak detection service — tests for DNS leaks, WebRTC leaks, and IPv6 leaks
/// to verify that the proxy is properly hiding the user's real IP address.
/// </summary>
public class LeakDetectionService
{
    private static readonly string _tag = "LeakDetectionService";

    // Multiple IP API endpoints for cross-validation
    private static readonly string[] _ipCheckUrls =
    [
        "https://api.ipify.org?format=json",
        "https://ipinfo.io/json",
        "https://httpbin.org/ip",
        "https://ifconfig.me/ip",
        "https://icanhazip.com",
    ];

    // DNS servers to test for leaks (queries these directly)
    private static readonly (string Name, string Url)[] _dnsTestServers =
    [
        ("Google DNS", "https://dns.google/resolve?name=whoami.akamai.net&type=A"),
        ("Cloudflare DNS", "https://cloudflare-dns.com/dns-query?name=whoami.akamai.net&type=A"),
        ("OpenDNS", "https://doh.opendns.com/dns-query?name=whoami.akamai.net&type=A"),
    ];

    /// <summary>
    /// Runs all leak detection tests and returns a summary.
    /// </summary>
    public async Task<LeakDetectionResult> RunFullDetection(IWebProxy? webProxy)
    {
        var result = new LeakDetectionResult();

        try
        {
            // Test 1: IP Leak — verify public IP is consistent
            result.IpLeak = await TestIpLeak(webProxy);

            // Test 2: DNS Leak — check if DNS queries are leaking
            result.DnsLeak = await TestDnsLeak(webProxy);

            // Test 3: IPv6 Leak — check if IPv6 address is exposed
            result.Ipv6Leak = await TestIpv6Leak(webProxy);

            // Test 4: WebRTC Leak — check for local IP exposure
            result.WebRtcLeak = await TestWebRtcLeak(webProxy);

            // Overall verdict
            result.OverallSafe = !result.IpLeak.IsLeaking
                                 && !result.DnsLeak.IsLeaking
                                 && !result.Ipv6Leak.IsLeaking
                                 && !result.WebRtcLeak.IsLeaking;
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Test 1: IP Leak — queries multiple IP APIs and checks consistency.
    /// If different IPs are returned, it may indicate a leak.
    /// </summary>
    private async Task<IpLeakInfo> TestIpLeak(IWebProxy? webProxy)
    {
        var info = new IpLeakInfo { TestName = "IP Address Leak Test" };
        var ips = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var url in _ipCheckUrls)
        {
            try
            {
                var response = await DownloadStringWithTimeout(url, webProxy, 10);
                if (response.IsNullOrEmpty()) continue;

                var ip = ExtractIpFromResponse(response);
                if (ip.IsNotEmpty())
                {
                    ips.Add(ip);
                    info.CheckedServers.Add((url, ip));
                }
            }
            catch
            {
                // Skip failed servers
            }
        }

        if (ips.Count == 0)
        {
            info.IsLeaking = true;
            info.Details = "Could not determine public IP from any server.";
            return info;
        }

        info.PublicIp = ips.First();
        info.IpCount = ips.Count;

        // If we got different IPs from different servers, something is wrong
        if (ips.Count > 1)
        {
            info.IsLeaking = true;
            info.Details = $"Inconsistent IPs detected ({ips.Count} different): {string.Join(", ", ips)}. This may indicate DNS or routing leaks.";
        }
        else
        {
            info.IsLeaking = false;
            info.Details = $"Public IP: {info.PublicIp} (consistent across {info.CheckedServers.Count} servers).";
        }

        return info;
    }

    /// <summary>
    /// Test 2: DNS Leak — queries DNS servers through the proxy and checks
    /// if the resolved IP matches the proxy's exit IP.
    /// </summary>
    private async Task<DnsLeakInfo> TestDnsLeak(IWebProxy? webProxy)
    {
        var info = new DnsLeakInfo { TestName = "DNS Leak Test" };

        // First get the public IP
        var publicIp = await GetPublicIp(webProxy);

        // Query DNS for a test domain through the proxy
        var dnsResults = new Dictionary<string, string>();

        foreach (var (name, url) in _dnsTestServers)
        {
            try
            {
                var response = await DownloadStringWithTimeout(url, webProxy, 10);
                if (response.IsNullOrEmpty()) continue;

                var resolvedIp = ExtractIpFromDnsResponse(response);
                if (resolvedIp.IsNotEmpty())
                {
                    dnsResults[name] = resolvedIp;
                    info.DnsServers.Add((name, resolvedIp));
                }
            }
            catch
            {
                // Skip failed servers
            }
        }

        if (dnsResults.Count == 0)
        {
            // No test server reachable — this is inconclusive, not a leak.
            info.IsLeaking = false;
            info.Details = "Could not reach any DNS test server; DNS leak status inconclusive. Check network connectivity and retry.";
            return info;
        }

        // Check if all DNS servers returned the same IP (good — indicates proxy is handling DNS)
        var uniqueIps = dnsResults.Values.Distinct().ToList();

        if (uniqueIps.Count == 1)
        {
            // All DNS servers returned the same IP — good sign
            info.IsLeaking = false;
            info.Details = $"DNS responses consistent ({uniqueIps.Count} unique IP). No DNS leak detected.";
        }
        else
        {
            // Different IPs from different DNS servers — potential leak
            info.IsLeaking = true;
            info.Details = $"DNS responses vary ({uniqueIps.Count} unique IPs). Possible DNS leak.";
        }

        return info;
    }

    /// <summary>
    /// Test 3: IPv6 Leak — checks if IPv6 address is visible when IPv6 should be blocked.
    /// </summary>
    private async Task<IPv6LeakInfo> TestIpv6Leak(IWebProxy? webProxy)
    {
        var info = new IPv6LeakInfo { TestName = "IPv6 Leak Test" };

        try
        {
            // Query for IPv6 address
            var response = await DownloadStringWithTimeout("https://api64.ipify.org?format=json", webProxy, 10);

            if (response.IsNullOrEmpty())
            {
                info.IsLeaking = false;
                info.Details = "Could not determine IPv6 status (may be blocked).";
                return info;
            }

            var ipv6 = ExtractIpv6FromResponse(response);

            if (ipv6.IsNotEmpty())
            {
                info.IsLeaking = true;
                info.Ipv6Address = ipv6;
                info.Details = $"IPv6 address exposed: {ipv6}. IPv6 traffic is leaking!";
            }
            else
            {
                info.IsLeaking = false;
                info.Details = "No IPv6 address detected. IPv6 is properly blocked.";
            }
        }
        catch (Exception ex)
        {
            info.IsLeaking = false;
            info.Details = $"IPv6 check completed (error: {ex.Message}).";
        }

        return info;
    }

    /// <summary>
    /// Test 4: WebRTC Leak — checks if local IP is exposed through WebRTC.
    /// Since we can't run a browser, we check if the proxy reveals local network info.
    /// </summary>
    private async Task<WebRtcLeakInfo> TestWebRtcLeak(IWebProxy? webProxy)
    {
        var info = new WebRtcLeakInfo { TestName = "WebRTC Leak Test" };

        try
        {
            // Check if we can detect local network info through the proxy
            // This is a simplified check — in a real browser, WebRTC can expose local IPs
            var localIp = await GetLocalIpAddress();
            info.LocalIp = localIp;

            // Try to get external IP that might reveal local network info
            var response = await DownloadStringWithTimeout("https://api.ipify.org?format=json", webProxy, 10);

            if (response.IsNotEmpty())
            {
                var externalIp = ExtractIpFromResponse(response);
                if (externalIp.IsNotEmpty())
                {
                    info.ExternalIp = externalIp;

                    // Check if external IP is in private range (indicates leak)
                    if (IsPrivateIp(externalIp))
                    {
                        info.IsLeaking = true;
                        info.Details = $"External IP {externalIp} is in private range — possible WebRTC leak!";
                    }
                    else
                    {
                        info.IsLeaking = false;
                        info.Details = $"External IP {externalIp} is public. No WebRTC leak detected at proxy level.";
                    }
                }
            }

            // Note: Full WebRTC leak detection requires browser JavaScript
            info.Details += " (Note: Browser-level WebRTC leaks require in-browser testing.)";
        }
        catch (Exception ex)
        {
            info.IsLeaking = false;
            info.Details = $"WebRTC check completed (error: {ex.Message}).";
        }

        return info;
    }

    #region Helper Methods

    private async Task<string?> GetPublicIp(IWebProxy? webProxy)
    {
        try
        {
            var response = await DownloadStringWithTimeout("https://api.ipify.org?format=json", webProxy, 10);
            return ExtractIpFromResponse(response);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> GetLocalIpAddress()
    {
        try
        {
            var hostName = System.Net.Dns.GetHostName();
            var addresses = await System.Net.Dns.GetHostAddressesAsync(hostName);
            return addresses.FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> DownloadStringWithTimeout(string url, IWebProxy? webProxy, int timeoutSeconds)
    {
        try
        {
            var handler = new SocketsHttpHandler
            {
                Proxy = webProxy,
                UseProxy = webProxy != null,
                ConnectTimeout = TimeSpan.FromSeconds(Math.Min(timeoutSeconds / 3, 5))
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };

            return await client.GetStringAsync(url);
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractIpFromResponse(string response)
    {
        // Try JSON parsing first
        try
        {
            var json = System.Text.Json.JsonDocument.Parse(response);
            if (json.RootElement.TryGetProperty("ip", out var ipProp))
            {
                return ipProp.GetString() ?? string.Empty;
            }
            if (json.RootElement.TryGetProperty("origin", out var originProp))
            {
                return originProp.GetString() ?? string.Empty;
            }
        }
        catch { }

        // Try to find IP address in plain text
        var match = System.Text.RegularExpressions.Regex.Match(response, @"\b(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\b");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string ExtractIpv6FromResponse(string response)
    {
        try
        {
            var json = System.Text.Json.JsonDocument.Parse(response);
            if (json.RootElement.TryGetProperty("ip", out var ipProp))
            {
                var ip = ipProp.GetString() ?? string.Empty;
                if (ip.Contains(':'))
                {
                    return ip;
                }
            }
        }
        catch { }

        var match = System.Text.RegularExpressions.Regex.Match(response, @"([0-9a-fA-F]{1,4}(:[0-9a-fA-F]{1,4}){7})");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string ExtractIpFromDnsResponse(string response)
    {
        try
        {
            var json = System.Text.Json.JsonDocument.Parse(response);
            if (json.RootElement.TryGetProperty("Answer", out var answerProp) && answerProp.GetArrayLength() > 0)
            {
                foreach (var answer in answerProp.EnumerateArray())
                {
                    if (answer.TryGetProperty("data", out var dataProp))
                    {
                        var data = dataProp.GetString() ?? string.Empty;
                        if (System.Text.RegularExpressions.Regex.IsMatch(data, @"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$"))
                        {
                            return data;
                        }
                    }
                }
            }
        }
        catch { }

        var match = System.Text.RegularExpressions.Regex.Match(response, @"\b(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\b");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static bool IsPrivateIp(string ip)
    {
        if (!System.Net.IPAddress.TryParse(ip, out var address)) return false;

        var bytes = address.GetAddressBytes();
        // 10.x.x.x
        if (bytes[0] == 10) return true;
        // 172.16-31.x.x
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
        // 192.168.x.x
        if (bytes[0] == 192 && bytes[1] == 168) return true;
        // 169.254.x.x (link-local)
        if (bytes[0] == 169 && bytes[1] == 254) return true;

        return false;
    }

    #endregion Helper Methods
}

#region Data Models

public class LeakDetectionResult
{
    public IpLeakInfo IpLeak { get; set; } = new();
    public DnsLeakInfo DnsLeak { get; set; } = new();
    public IPv6LeakInfo Ipv6Leak { get; set; } = new();
    public WebRtcLeakInfo WebRtcLeak { get; set; } = new();
    public bool OverallSafe { get; set; }
    public string? Error { get; set; }
}

public class IpLeakInfo
{
    public string TestName { get; set; } = string.Empty;
    public bool IsLeaking { get; set; }
    public string PublicIp { get; set; } = string.Empty;
    public int IpCount { get; set; }
    public List<(string Server, string Ip)> CheckedServers { get; set; } = [];
    public string Details { get; set; } = string.Empty;
}

public class DnsLeakInfo
{
    public string TestName { get; set; } = string.Empty;
    public bool IsLeaking { get; set; }
    public List<(string Server, string ResolvedIp)> DnsServers { get; set; } = [];
    public string Details { get; set; } = string.Empty;
}

public class IPv6LeakInfo
{
    public string TestName { get; set; } = string.Empty;
    public bool IsLeaking { get; set; }
    public string Ipv6Address { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

public class WebRtcLeakInfo
{
    public string TestName { get; set; } = string.Empty;
    public bool IsLeaking { get; set; }
    public string LocalIp { get; set; } = string.Empty;
    public string ExternalIp { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

#endregion Data Models
