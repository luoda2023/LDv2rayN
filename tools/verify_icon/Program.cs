using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

// Verify:
// 1) MainWindow toolbar PackIcon elements exist (Kind=Server, BookClock, etc)
// 2) AI menu items visible (menuAiProductivity, menuAiChat, menuAISetting)
// 3) Then relaunch with --exit-test to verify exit path writes trace

static class P
{
    static int Pass = 0, Fail = 0;
    static void Ok(string s) { Console.WriteLine($"  [PASS] {s}"); Pass++; }
    static void No(string s) { Console.WriteLine($"  [FAIL] {s}"); Fail++; }
    static void Log(string s) { Console.WriteLine($"  {s}"); }

    static AutomationElement FindByName(AutomationElement root, string name)
    {
        try
        {
            var cond = new PropertyCondition(AutomationElement.NameProperty, name);
            return root.FindFirst(TreeScope.Descendants, cond);
        }
        catch { return null; }
    }

    static AutomationElement FindById(AutomationElement root, string id)
    {
        try
        {
            var cond = new PropertyCondition(AutomationElement.AutomationIdProperty, id);
            return root.FindFirst(TreeScope.Descendants, cond);
        }
        catch { return null; }
    }

    static void Main()
    {
        var exe = @"D:\LUODA\LDv2rayN\v2rayN\bin\Release\net10.0-windows10.0.19041.0\LDv2rayN.exe";
        var tracePath = @"D:\LUODA\LDv2rayN\exit_trace.log";

        // Kill previous
        foreach (var p in Process.GetProcessesByName("LDv2rayN"))
        {
            try { p.Kill(); } catch { }
        }
        Thread.Sleep(1500);

        // Start fresh
        Console.WriteLine("=== 1) Starting LDv2rayN.exe (normal mode) ===");
        var psi = new ProcessStartInfo { FileName = exe, UseShellExecute = false };
        var proc = Process.Start(psi);
        Thread.Sleep(4000); // wait for WPF window to load

        if (proc.HasExited)
        {
            Console.WriteLine($"[FAIL] LDv2rayN exited immediately (code={proc.ExitCode})");
            return;
        }
        Ok($"LDv2rayN started, PID={proc.Id}");

        // Enumerate main window
        var desktop = AutomationElement.RootElement;
        var mainCond = new PropertyCondition(AutomationElement.ProcessIdProperty, proc.Id);
        var mainWin = desktop.FindFirst(TreeScope.Children, mainCond);
        if (mainWin == null) { No("main window not found by PID"); return; }
        Ok($"main window: name='{mainWin.Current.Name}'");

        // 2) Check AI menu items exist (by AutomationId)
        Console.WriteLine("\n=== 2) AI menu items ===");
        string[] aiIds = { "menuAiProductivity", "menuAISetting", "menuAILog", "menuAIChat", "menuAIFetch", "menuLeakDetection" };
        foreach (var id in aiIds)
        {
            var el = FindById(desktop, id);
            if (el != null) Ok($"AutomationId={id} exists");
            else No($"AutomationId={id} NOT FOUND");
        }

        // 3) Check toolbar PackIcons - by Name (PackIcon kind string)
        Console.WriteLine("\n=== 3) Toolbar PackIcons (expected Kind=...) ===");
        // PackIcon in WPF: automation exposes Kind as part of Name, or we look for Pane/Image with specific name
        // Try enumerating all Pane elements with Kind= in name
        var paneCond = new PropertyCondition(ControlType.Pane.Property);
        var allPanes = desktop.FindAll(TreeScope.Descendants, paneCond);
        var iconKinds = new[] { "Server", "BookClock", "Settings", "Shield", "Robot", "File", "MessageText", "Lock", "HelpCircle", "Reload", "Minimize" };
        foreach (var kind in iconKinds)
        {
            bool found = false;
            foreach (AutomationElement pane in allPanes)
            {
                try
                {
                    var nm = pane.Current.Name ?? "";
                    if (nm.Contains(kind)) { found = true; break; }
                }
                catch { }
            }
            if (found) Ok($"Kind={kind} present in UIA tree");
            else No($"Kind={kind} NOT FOUND");
        }

        // 4) Verify tray icon existence (may fail — WPF NotifyIcon not always in UIA)
        Console.WriteLine("\n=== 4) Tray icon ===");
        try
        {
            // Try to find NotifyIcon area
            var trayWnd = desktop.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.NameProperty, "系统托盘"));
            if (trayWnd != null) Ok("system tray visible");
            else Log("(system tray may be hidden - normal)");
        }
        catch { Log("(tray check skipped)"); }

        // Kill current
        try { proc.Kill(); } catch { }
        Thread.Sleep(1000);

        // 5) Verify exit path via --exit-test
        Console.WriteLine("\n=== 5) Exit path via --exit-test ===");
        try { File.Delete(tracePath); } catch { }

        var psi2 = new ProcessStartInfo { FileName = exe, Arguments = "--exit-test", UseShellExecute = false };
        var proc2 = Process.Start(psi2);
        var sw = Stopwatch.StartNew();
        while (!proc2.HasExited && sw.Elapsed < TimeSpan.FromSeconds(15))
        {
            Thread.Sleep(500);
        }

        if (proc2.HasExited)
        {
            Ok($"LDv2rayN exited in {sw.Elapsed.TotalSeconds:F1}s (code={proc2.ExitCode})");
        }
        else
        {
            No($"LDv2rayN still alive after 15s");
            try { proc2.Kill(); } catch { }
        }

        // Read trace
        Console.WriteLine("\n=== 6) exit_trace.log ===");
        if (File.Exists(tracePath))
        {
            var size = new FileInfo(tracePath).Length;
            Ok($"exit_trace.log exists ({size} bytes)");
            var lines = File.ReadAllLines(tracePath);
            foreach (var line in lines.Take(30))
            {
                Console.WriteLine($"  {line}");
            }
        }
        else
        {
            No($"exit_trace.log NOT created - {tracePath}");
        }

        // 7) xray/sing-box residual check
        Console.WriteLine("\n=== 7) xray/sing-box residual ===");
        var xrayCount = Process.GetProcessesByName("xray").Length;
        var singCount = Process.GetProcessesByName("sing-box").Length;
        if (xrayCount == 0) Ok("xray: no residual"); else No($"xray: {xrayCount} residual");
        if (singCount == 0) Ok("sing-box: no residual"); else No($"sing-box: {singCount} residual");

        Console.WriteLine($"\n=== SUMMARY: PASS={Pass} FAIL={Fail} ===");
        Console.WriteLine("\nNOTE: 5 sub-window icons (DNS/Routing/etc) cannot be verified by UIA");
        Console.WriteLine("       because WPF MenuItem.Invoke only expands submenus, doesn't Click.");
        Console.WriteLine("       Open them manually: Help → Settings → DNS Setting / Routing / etc.");
        Console.WriteLine("       Look for Kind=\"Link\" icon (MaterialDesign, not 🔗 emoji).");
    }
}
