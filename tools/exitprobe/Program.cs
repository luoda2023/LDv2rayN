using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Automation;
using System.Runtime.InteropServices;

// Approach: Launch EXE with LDV2RAYN_TEST_EXIT=1 (a test hook we add to MainWindow).
// The hook triggers ShutdownNow() 3s after startup, running AppExitAsync.
// We wait for exit and read exit_trace.log.

static class P
{
    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    static void Main(string[] args)
    {
        var exe = @"D:\LUODA\LDv2rayN\v2rayN\bin\Release\net10.0-windows10.0.19041.0\LDv2rayN.exe";
        var tracePath = @"D:\LUODA\LDv2rayN\exit_trace.log";
        var configPath = @"D:\LUODA\LDv2rayN\guiConfigs\guiNConfig.json";

        // Kill any previous instance
        foreach (var p in Process.GetProcessesByName("LDv2rayN"))
        {
            try { p.Kill(); } catch { }
        }
        Thread.Sleep(1000);

        // Delete trace
        try { File.Delete(tracePath); } catch { }

        // Start with test hook env var
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = false,
            EnvironmentVariables = { ["LDV2RAYN_TEST_EXIT"] = "1" }
        };
        psi.Environment["LDV2RAYN_TEST_EXIT"] = "1";

        Console.WriteLine($"[+] Starting LDv2rayN with LDV2RAYN_TEST_EXIT=1");
        var proc = Process.Start(psi);
        if (proc == null) { Console.WriteLine("FAIL to start"); return; }
        Console.WriteLine($"[+] PID={proc.Id}");

        // Poll for exit, up to 15s
        var sw = Stopwatch.StartNew();
        while (!proc.HasExited && sw.Elapsed < TimeSpan.FromSeconds(15))
        {
            Thread.Sleep(500);
            var traceExists = File.Exists(tracePath);
            var traceSize = traceExists ? new FileInfo(tracePath).Length : 0;
            Console.WriteLine($"  +{sw.Elapsed.TotalSeconds:F1}s: alive, traceSize={traceSize}");
        }

        if (proc.HasExited)
        {
            Console.WriteLine($"[+] Exited in {sw.Elapsed.TotalSeconds:F1}s (code={proc.ExitCode})");
        }
        else
        {
            Console.WriteLine($"[!] NOT exited after 15s, force killing");
            try { proc.Kill(); } catch { }
        }

        // Check trace
        if (File.Exists(tracePath))
        {
            Console.WriteLine($"\n=== exit_trace.log ({new FileInfo(tracePath).Length} bytes) ===");
            foreach (var line in File.ReadAllLines(tracePath))
            {
                Console.WriteLine($"  {line}");
            }
        }
        else
        {
            Console.WriteLine("\n[!] exit_trace.log NOT created");
        }

        // Check config
        if (File.Exists(configPath))
        {
            var size = new FileInfo(configPath).Length;
            var mtime = new FileInfo(configPath).LastWriteTime;
            Console.WriteLine($"\n=== config ===");
            Console.WriteLine($"  {configPath}");
            Console.WriteLine($"  size={size} bytes");
            Console.WriteLine($"  mtime={mtime}");
            // Validate JSON
            try
            {
                var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(configPath));
                Console.WriteLine($"  JSON valid, root fields: {json.RootElement.EnumerateObject().Count()}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  JSON INVALID: {ex.Message}");
            }
        }
    }
}
