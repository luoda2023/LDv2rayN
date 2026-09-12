import io

path = r"D:\LUODA\LDv2rayN\v2rayN\Views\StatusBarView.xaml.cs"

with io.open(path, "r", encoding="utf-8", newline="") as f:
    lines = f.readlines()

# 1-indexed line range of the current menuExit_Click method body: 119..145
start = 119  # "} private async void menuExit_Click..."
end = 145    # "}" closing the method

# sanity check the boundaries before replacing
assert "menuExit_Click" in lines[start-1], repr(lines[start-1])
assert lines[end-1].strip() == "}", repr(lines[end-1])

new_method = '''} private async void menuExit_Click(object sender, RoutedEventArgs e)
{
    // 第一时间设置强制退出标志：后续任何一步失败，Application.Current.Shutdown()
    // 都不会被 MainWindow_Closing 的 e.Cancel = true 拦下（那是"点退出却没退出"的根因）。
    ExitManager.ForceExit = true;

    // 释放托盘图标可能抛异常（图标释放中/正在响应右键菜单）。异常绝不能阻断退出流程。
    try { tbNotify.Dispose(); } catch { }

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var cleanup = Task.Run(async () =>
    {
        try { await AppManager.Instance.AppExitAsync(false); } catch { }
    });

    try
    {
        // 优雅退出最多等 15s；超时直接强杀，绝不让用户去任务管理器手动结束。
        if (await Task.WhenAny(cleanup, Task.Delay(TimeSpan.FromSeconds(15), cts.Token)) != cleanup)
        {
            Logging.SaveLog("tray exit: cleanup did not finish in 15s, force-killing process");
            Environment.Exit(0);
        }
    }
    catch
    {
        Environment.Exit(0);
    }

    // 强制退出标志已置位，Shutdown 不会被取消。再失败就强杀兜底。
    Application.Current.Dispatcher.Invoke(() =>
    {
        try { Application.Current.Shutdown(); } catch { Environment.Exit(0); }
    });
}
'''

new_lines = [new_method]
lines[start-1:end] = new_lines

out = "".join(lines)
with io.open(path, "w", encoding="utf-8", newline="") as f:
    f.write(out)

print("OK: replaced lines", start, "to", end, "->", new_method.count("\n"), "new lines")
