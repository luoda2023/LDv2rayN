namespace ServiceLib.Handler;

/// <summary>
/// Kill Switch handler — blocks all outbound network traffic when the proxy core
/// disconnects unexpectedly. Uses OS-level tools (Windows Firewall on Windows,
/// iptables on Linux/macOS) to enforce the block, then removes the rules when
/// the proxy is re-established or the user disables Kill Switch.
/// </summary>
public static class KillSwitchHandler
{
    private static readonly string _tag = "KillSwitchHandler";
    private static bool _isActive;
    private static readonly object _lock = new();

    // Windows Firewall rule names (for easy cleanup)
    private const string RulePrefix = "LDv2rayN_KillSwitch";
    private const string BlockRuleNameOut = RulePrefix + "_BlockAll_Out";
    private const string BlockRuleNameIn = RulePrefix + "_BlockAll_In";
    private const string AllowLoopbackOut = RulePrefix + "_AllowLoopback_Out";
    private const string AllowLoopbackIn = RulePrefix + "_AllowLoopback_In";
    private const string AllowProxyOut = RulePrefix + "_AllowProxy_Out";
    private const string AllowProxyIn = RulePrefix + "_AllowProxy_In";
    private const string AllowDnsOut = RulePrefix + "_AllowDNS_Out";
    private const string AllowDnsIn = RulePrefix + "_AllowDNS_In";
    private const string AllowAppOut = RulePrefix + "_AllowApp_Out";

    /// <summary>
    /// Activates the Kill Switch — blocks all outbound/inbound traffic except
    /// loopback. Call this when the core process exits unexpectedly while TUN
    /// is active and Kill Switch is enabled.
    /// </summary>
    public static async Task Activate(Config config)
    {
        lock (_lock)
        {
            if (_isActive)
            {
                return;
            }
        }

        if (!config.TunModeItem.EnableKillSwitch)
        {
            return;
        }

        try
        {
            if (Utils.IsWindows())
            {
                await ActivateWindowsFirewall();
            }
            else if (Utils.IsLinux())
            {
                await ActivateLinuxIptables();
            }
            else if (Utils.IsMacOS())
            {
                await ActivateMacOsPf();
            }

            lock (_lock)
            {
                _isActive = true;
            }
            Logging.SaveLog("Kill Switch ACTIVATED — all traffic blocked");
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    /// <summary>
    /// Deactivates the Kill Switch — removes all blocking rules and restores
    /// normal network access. Call this when the proxy is re-established or
    /// the user manually disables Kill Switch.
    /// </summary>
    public static async Task Deactivate()
    {
        lock (_lock)
        {
            if (!_isActive)
            {
                return;
            }
        }

        try
        {
            if (Utils.IsWindows())
            {
                await DeactivateWindowsFirewall();
            }
            else if (Utils.IsLinux())
            {
                await DeactivateLinuxIptables();
            }
            else if (Utils.IsMacOS())
            {
                await DeactivateMacOsPf();
            }

            lock (_lock)
            {
                _isActive = false;
            }
            Logging.SaveLog("Kill Switch DEACTIVATED — traffic restored");
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    /// <summary>
    /// Force-deactivates the Kill Switch regardless of state (used during app exit cleanup).
    /// </summary>
    public static async Task ForceDeactivate()
    {
        try
        {
            if (Utils.IsWindows())
            {
                await DeactivateWindowsFirewall();
            }
            else if (Utils.IsLinux())
            {
                await DeactivateLinuxIptables();
            }
            else if (Utils.IsMacOS())
            {
                await DeactivateMacOsPf();
            }

            lock (_lock)
            {
                _isActive = false;
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    /// <summary>
    /// Returns true if the Kill Switch is currently active (traffic blocked).
    /// </summary>
    public static bool IsActive => _isActive;

    #region Windows Firewall

    [SupportedOSPlatform("windows")]
    private static async Task ActivateWindowsFirewall()
    {
        // Remove any stale rules first
        await RemoveAllKillSwitchRules();

        // Allow loopback (so the app can still communicate internally)
        await RunNetsh($"advfirewall firewall add rule name=\"{AllowLoopbackOut}\" dir=out action=allow remoteip=127.0.0.1");
        await RunNetsh($"advfirewall firewall add rule name=\"{AllowLoopbackIn}\" dir=in action=allow remoteip=127.0.0.1");

        // Allow DNS to loopback (so DNS resolution still works for local apps)
        await RunNetsh($"advfirewall firewall add rule name=\"{AllowDnsOut}\" dir=out action=allow remoteip=127.0.0.1 protocol=udp remoteport=53");
        await RunNetsh($"advfirewall firewall add rule name=\"{AllowDnsIn}\" dir=in action=allow remoteip=127.0.0.1 protocol=udp localport=53");

        // Allow the LDv2rayN application itself (so it can reconnect)
        var appPath = Utils.GetExePath();
        if (appPath.IsNotEmpty() && File.Exists(appPath))
        {
            await RunNetsh($"advfirewall firewall add rule name=\"{AllowAppOut}\" dir=out action=allow program=\"{appPath}\"");
        }

        // Allow the proxy core processes (Xray, sing-box)
        await AllowCoreProcess("xray.exe");
        await AllowCoreProcess("sing-box.exe");

        // Block ALL other outbound traffic
        await RunNetsh($"advfirewall firewall add rule name=\"{BlockRuleNameOut}\" dir=out action=block");

        // Block ALL other inbound traffic
        await RunNetsh($"advfirewall firewall add rule name=\"{BlockRuleNameIn}\" dir=in action=block");
    }

    [SupportedOSPlatform("windows")]
    private static async Task AllowCoreProcess(string processName)
    {
        var binPath = Utils.GetBinPath(processName);
        if (binPath.IsNotEmpty() && File.Exists(binPath))
        {
            await RunNetsh($"advfirewall firewall add rule name=\"{RulePrefix}_Allow{processName}_Out\" dir=out action=allow program=\"{binPath}\"");
            await RunNetsh($"advfirewall firewall add rule name=\"{RulePrefix}_Allow{processName}_In\" dir=in action=allow program=\"{binPath}\"");
        }
    }

    [SupportedOSPlatform("windows")]
    private static async Task DeactivateWindowsFirewall()
    {
        await RemoveAllKillSwitchRules();
    }

    [SupportedOSPlatform("windows")]
    private static async Task RemoveAllKillSwitchRules()
    {
        // Remove all rules with our prefix
        await RunNetsh($"advfirewall firewall delete rule name=\"{RulePrefix}_BlockAll_Out\"");
        await RunNetsh($"advfirewall firewall delete rule name=\"{RulePrefix}_BlockAll_In\"");
        await RunNetsh($"advfirewall firewall delete rule name=\"{AllowLoopbackOut}\"");
        await RunNetsh($"advfirewall firewall delete rule name=\"{AllowLoopbackIn}\"");
        await RunNetsh($"advfirewall firewall delete rule name=\"{AllowDnsOut}\"");
        await RunNetsh($"advfirewall firewall delete rule name=\"{AllowDnsIn}\"");
        await RunNetsh($"advfirewall firewall delete rule name=\"{AllowProxyOut}\"");
        await RunNetsh($"advfirewall firewall delete rule name=\"{AllowProxyIn}\"");
        await RunNetsh($"advfirewall firewall delete rule name=\"{AllowAppOut}\"");
        await RunNetsh($"advfirewall firewall delete rule name=\"{RulePrefix}_Allowxray.exe_Out\"");
        await RunNetsh($"advfirewall firewall delete rule name=\"{RulePrefix}_Allowxray.exe_In\"");
        await RunNetsh($"advfirewall firewall delete rule name=\"{RulePrefix}_Allowsing-box.exe_Out\"");
        await RunNetsh($"advfirewall firewall delete rule name=\"{RulePrefix}_Allowsing-box.exe_In\"");
    }

    [SupportedOSPlatform("windows")]
    private static async Task RemoveWindowsFirewallRule(string ruleName)
    {
        await RunNetsh($"advfirewall firewall delete rule name=\"{ruleName}\"");
    }

    [SupportedOSPlatform("windows")]
    private static async Task RunNetsh(string args)
    {
        try
        {
            var netshPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "netsh.exe");
            await Utils.GetCliWrapOutput(netshPath, args);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    #endregion Windows Firewall

    #region Linux iptables

    private static async Task ActivateLinuxIptables()
    {
        // Allow loopback
        await RunBash("iptables -A OUTPUT -o lo -j ACCEPT");
        await RunBash("iptables -A INPUT -i lo -j ACCEPT");

        // Allow established connections (so existing TCP streams don't break mid-transfer)
        await RunBash("iptables -A OUTPUT -m state --state ESTABLISHED,RELATED -j ACCEPT");
        await RunBash("iptables -A INPUT -m state --state ESTABLISHED,RELATED -j ACCEPT");

        // Allow DNS to loopback
        await RunBash("iptables -A OUTPUT -d 127.0.0.1 -p udp --dport 53 -j ACCEPT");

        // Block everything else
        await RunBash("iptables -A OUTPUT -j DROP");
        await RunBash("iptables -A INPUT -j DROP");
    }

    private static async Task DeactivateLinuxIptables()
    {
        // Flush our rules (in reverse order)
        await RunBash("iptables -D INPUT -j DROP");
        await RunBash("iptables -D OUTPUT -j DROP");
        await RunBash("iptables -D OUTPUT -d 127.0.0.1 -p udp --dport 53 -j ACCEPT");
        await RunBash("iptables -D INPUT -m state --state ESTABLISHED,RELATED -j ACCEPT");
        await RunBash("iptables -D OUTPUT -m state --state ESTABLISHED,RELATED -j ACCEPT");
        await RunBash("iptables -D INPUT -i lo -j ACCEPT");
        await RunBash("iptables -D OUTPUT -o lo -j ACCEPT");
    }

    private static async Task RunBash(string command)
    {
        try
        {
            await Utils.GetCliWrapOutput(Global.LinuxBash, command);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    #endregion Linux iptables

    #region macOS pf

    private static async Task ActivateMacOsPf()
    {
        // On macOS, we use pf (packet filter) to block traffic.
        // Create a pf anchor rule that blocks all outbound except loopback.
        var pfRules = @"
# LDv2rayN Kill Switch rules
block out all
block in all
pass out on lo0 all
pass in on lo0 all
pass out proto udp to 127.0.0.1 port 53
";

        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, pfRules);
            await RunBash($"pfctl -a ldv2rayn_killswitch -f {tempFile}");
            await RunBash("pfctl -a ldv2rayn_killswitch -E");
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private static async Task DeactivateMacOsPf()
    {
        await RunBash("pfctl -a ldv2rayn_killswitch -d 2>/dev/null || true");
        await RunBash("pfctl -a ldv2rayn_killswitch -f /dev/null 2>/dev/null || true");
    }

    #endregion macOS pf
}
