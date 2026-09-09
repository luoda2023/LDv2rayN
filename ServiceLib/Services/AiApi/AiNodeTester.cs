using System.Net.Sockets;
using ServiceLib.Handler;

namespace ServiceLib.Services.AiApi;

/// <summary>
/// Shared TCP-connect / DNS-fallback test for VPN node links. Used by
/// <see cref="TestNodeCapability"/> and <see cref="AnalyzeUrlCapability"/>.
/// </summary>
internal static class AiNodeTester
{
    public static async Task<bool> TestAsync(string nodeLink)
    {
        try
        {
            var profile = FmtHandler.ResolveConfig(nodeLink, out _);
            if (profile is null || !profile.IsValid()) return false;
            if (string.IsNullOrEmpty(profile.Address) || profile.Address == "127.0.0.1" || profile.Port <= 0)
                return true;

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(profile.Address, profile.Port, cts.Token);
                return client.Connected;
            }
            catch
            {
                // Fall back to DNS resolution if TCP connect fails.
                try { return (await System.Net.Dns.GetHostEntryAsync(profile.Address)).AddressList.Length > 0; }
                catch { return false; }
            }
        }
        catch { return false; }
    }
}
