$ErrorActionPreference = 'Stop'
$p = 'D:\LUODA\LDv2rayN\v2rayN\Views\StatusBarView.xaml.cs'
$start = 119
$end = 145
$lines = [System.Collections.Generic.List[string]]::new([System.IO.File]::ReadAllLines($p))
if ($lines[$start-1] -notlike '*menuExit_Click*') { throw ('not menuExit at ' + $start) }
if ($lines[$end-1].Trim() -ne '}') { throw ('not brace at ' + $end) }
$n = @(
'} private async void menuExit_Click(object sender, RoutedEventArgs e)',
'{',
'    ExitManager.ForceExit = true;',
'',
'    try { tbNotify.Dispose(); } catch { }',
'',
'    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));',
'    var cleanup = Task.Run(async () =>',
'    {',
'        try { await AppManager.Instance.AppExitAsync(false); } catch { }',
'    });',
'',
'    try',
'    {',
'        if (await Task.WhenAny(cleanup, Task.Delay(TimeSpan.FromSeconds(15), cts.Token)) != cleanup)',
'        {',
'            Logging.SaveLog("tray exit: cleanup did not finish in 15s, force-killing process");',
'            Environment.Exit(0);',
'        }',
'    }',
'    catch { Environment.Exit(0); }',
'',
'    Application.Current.Dispatcher.Invoke(() =>',
'    {',
'        try { Application.Current.Shutdown(); } catch { Environment.Exit(0); }',
'    });',
'}'
)
$lines.RemoveRange($start - 1, $end - $start + 1)
for ($i = $n.Length - 1; $i -ge 0; $i--) { $lines.Insert($start - 1, $n[$i]) }
[System.IO.File]::WriteAllLines($p, $lines)
Write-Host ('DONE replaced ' + $start + '-' + $end + ' with ' + $n.Length)
