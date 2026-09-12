using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Automation;

static class P
{
    class ListResult
    {
        public List<string> Items { get; } = new();
        public int PassCount { get; set; }
        public int FailCount { get; set; }
        public void Pass(string s) { Items.Add("[PASS] " + s); PassCount++; }
        public void Fail(string s) { Items.Add("[FAIL] " + s); FailCount++; }
    }

    static int Main()
    {
        var procs = Process.GetProcessesByName("LDv2rayN");
        if (procs.Length == 0) { Console.WriteLine("FAIL: no LDv2rayN"); return 1; }
        var pid = procs[0].Id;
        Console.WriteLine($"pid={pid}");

        var root = AutomationElement.RootElement;
        var pc = new PropertyCondition(AutomationElement.ProcessIdProperty, pid);
        var all = root.FindAll(TreeScope.Descendants, pc);
        Console.WriteLine($"elements: {all.Count}");

        var res = new ListResult();

        var byId = new Dictionary<string, AutomationElement>();
        foreach (AutomationElement e in all)
        {
            try
            {
                var id = e.Current.AutomationId;
                if (!string.IsNullOrEmpty(id) && !byId.ContainsKey(id)) byId[id] = e;
            }
            catch { }
        }

        // 1) emoji menu items (codepoint surrogate pairs)
        CheckEmoji(res, all, "\uD83E\uDD16", "menuAISetting emoji (U+1F916)");
        CheckEmoji(res, all, "\uD83D\uDCCB", "menuAILog emoji (U+1F4CB)");
        CheckEmoji(res, all, "\uD83D\uDCAC", "menuAIChat emoji (U+1F4AC)");

        // 2) menuAiProductivity
        if (byId.TryGetValue("menuAiProductivity", out var prod))
            res.Pass("menuAiProductivity found (Name='" + Trunc(prod.Current.Name ?? "") + "')");
        else res.Fail("menuAiProductivity NOT FOUND");

        // 3) btnAiAssistant position
        if (byId.TryGetValue("btnAiAssistant", out var btnAi))
        {
            var r = btnAi.Current.BoundingRectangle;
            res.Pass($"btnAiAssistant found at x={r.X},y={r.Y},w={r.Width},h={r.Height}");
        }
        else res.Fail("btnAiAssistant NOT FOUND");

        // 4) AI dialog: invoke menuAIChat, look for new window
        if (byId.TryGetValue("menuAIChat", out var menuAIChat))
        {
            try
            {
                if (menuAIChat.TryGetCurrentPattern(InvokePattern.Pattern, out var inv))
                {
                    ((InvokePattern)inv).Invoke();
                    res.Pass("invoked menuAIChat");
                    Thread.Sleep(3000);

                    var newAll = root.FindAll(TreeScope.Descendants, pc);
                    AutomationElement? chatWin = null;
                    foreach (AutomationElement e in newAll)
                    {
                        try
                        {
                            var name = e.Current.Name ?? "";
                            if (e.Current.ControlType == ControlType.Window &&
                                (name.Contains("AI") || name.Contains("Chat") || name.Contains("Assistant")))
                            { chatWin = e; break; }
                        }
                        catch { }
                    }
                    if (chatWin != null)
                        res.Pass($"AI dialog window found: name='{chatWin.Current.Name}'");
                    else res.Fail("AI dialog window NOT found after menuAIChat invoke");
                }
                else res.Fail("menuAIChat has no InvokePattern");
            }
            catch (Exception ex) { res.Fail("menuAIChat invoke error: " + ex.Message); }
        }
        else res.Fail("menuAIChat NOT FOUND");

        // 5) Screenshot main window
        try
        {
            foreach (AutomationElement e in all)
            {
                try
                {
                    var cls = e.Current.ClassName;
                    var name = e.Current.Name ?? "";
                    if (cls == "MainWindow" || name.Contains("LDv2rayN"))
                    {
                        var rect = e.Current.BoundingRectangle;
                        if (rect.Width > 100 && rect.Height > 100)
                        {
                            using var bmp = new Bitmap((int)rect.Width, (int)rect.Height);
                            using var g = Graphics.FromImage(bmp);
                            g.CopyFromScreen((int)rect.X, (int)rect.Y, 0, 0, bmp.Size);
                            var dir = "E:\\Temp";
                            Directory.CreateDirectory(dir);
                            var path = Path.Combine(dir, "ldv2rayn_verify.png");
                            bmp.Save(path, ImageFormat.Png);
                            res.Pass($"screenshot: {path}");
                            break;
                        }
                    }
                }
                catch { }
            }
        }
        catch (Exception ex) { res.Fail("screenshot failed: " + ex.Message); }

        Console.WriteLine("\n=== RESULTS ===");
        foreach (var r in res.Items) Console.WriteLine(r);
        Console.WriteLine($"\nTotal: {res.PassCount} pass, {res.FailCount} fail");
        return res.FailCount == 0 ? 0 : 1;
    }

    static void CheckEmoji(ListResult r, AutomationElementCollection all, string emoji, string label)
    {
        foreach (AutomationElement e in all)
        {
            try
            {
                var nm = e.Current.Name ?? "";
                if (nm.Contains(emoji))
                {
                    r.Pass($"{label} in Name='{Trunc(nm)}'");
                    return;
                }
            }
            catch { }
        }
        r.Fail($"{label} NOT found");
    }

    static string Trunc(string s) { if (s == null) return ""; return s.Length > 60 ? s[..60] + "..." : s; }
}
