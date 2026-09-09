using System.IO;
using ServiceLib.Services;
using ServiceLib.Common;

namespace v2rayN.Views;

public partial class AILogWindow : Window
{
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private const string AiLogTag = "AISchedulerService";
    private const string AiFetchTag = "AIFetchService";

    public AILogWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
        _refreshTimer.Tick += (_, _) => Refresh();
        _refreshTimer.Start();
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        try
        {
            // Scheduler status
            txtState.Text = AISchedulerService.IsRunning ? "运行中" : "未启动";
            txtState.Foreground = AISchedulerService.IsRunning
                ? System.Windows.Media.Brushes.LightGreen
                : System.Windows.Media.Brushes.OrangeRed;

            txtLastRun.Text = FormatUtc(AISchedulerService.LastRunAtUtc);
            txtLastSuccess.Text = FormatUtc(AISchedulerService.LastSuccessAtUtc);
            txtLastAdded.Text = AISchedulerService.LastAddedNodes.ToString();
            txtFailures.Text = AISchedulerService.ConsecutiveFailures.ToString();
            txtLastMsg.Text = AISchedulerService.LastMessage ?? "-";

            // Last search run details
            if (AISearchTracker.HasRun)
            {
                txtReposFound.Text = AISearchTracker.ReposFound.ToString();
                txtFetchedUrls.Text = AISearchTracker.FetchedUrls.ToString();
                txtFetchErrors.Text = AISearchTracker.FetchErrors > 0
                    ? $"({AISearchTracker.FetchErrors} 失败)"
                    : "(全部成功)";
                txtFetchErrors.Foreground = AISearchTracker.FetchErrors > 0
                    ? System.Windows.Media.Brushes.OrangeRed
                    : System.Windows.Media.Brushes.LightGreen;
                txtMirrorFallbacks.Text = AISearchTracker.MirrorFallbacks.ToString();
                txtMirrorFallbacks.Foreground = AISearchTracker.MirrorFallbacks > 0
                    ? System.Windows.Media.Brushes.Khaki
                    : System.Windows.Media.Brushes.LightGray;
                txtCandidates.Text = AISearchTracker.CandidatesFound.ToString();
                txtCandidatesByProto.Text = FormatProtoMap(AISearchTracker.CandidatesByProtocol);
                txtPassed.Text = AISearchTracker.Passed.ToString();
                txtFailed.Text = AISearchTracker.Failed.ToString();
                txtPassedByProto.Text = FormatProtoMap(AISearchTracker.PassedByProtocol);
                txtImported.Text = AISearchTracker.Imported.ToString();

                timelineItems.ItemsSource = null;
                timelineItems.ItemsSource = AISearchTracker.Timeline;
            }
            else
            {
                txtReposFound.Text = "0";
                txtFetchedUrls.Text = "0";
                txtFetchErrors.Text = "";
                txtMirrorFallbacks.Text = "0";
                txtCandidates.Text = "0";
                txtCandidatesByProto.Text = "";
                txtPassed.Text = "0";
                txtFailed.Text = "0";
                txtPassedByProto.Text = "";
                txtImported.Text = "0";
                timelineItems.ItemsSource = null;
            }

            // Config summary
            try
            {
                var ai = AppManager.Instance.Config.AIConfigItem;
                if (ai != null)
                {
                    txtApiUrl.Text = ai.ApiUrl ?? "(未配置)";
                    txtModel.Text = ai.ModelId ?? "(未配置)";
                    txtInterval.Text = $"{Math.Max(ai.AutoCrawlIntervalMinutes, 5)} 分钟 / 次";
                    txtGroup.Text = ai.AiGroupRemarks ?? "(未配置)";
                }
            }
            catch { /* config may not be ready */ }
        }
        catch (Exception ex)
        {
            txtState.Text = $"读取失败: {ex.Message}";
            txtState.Foreground = System.Windows.Media.Brushes.Red;
        }

        // Recent AI log entries
        logItems.ItemsSource = null;
        logItems.ItemsSource = ReadRecentAiLogs();
    }

    private static string FormatProtoMap(Dictionary<string, int> map)
    {
        if (map.Count == 0) return string.Empty;
        return string.Join("  ", map.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"));
    }

    private static string FormatUtc(DateTime utc)
    {
        if (utc == DateTime.MinValue) return "从未";
        var local = utc.ToLocalTime();
        return local.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static List<string> ReadRecentAiLogs()
    {
        var result = new List<string>();
        try
        {
            var logDir = Utils.GetLogPath();
            if (!Directory.Exists(logDir)) return result;

            // Read today's log (LogCrypto writes one file per day)
            var files = Directory.GetFiles(logDir, "*.txt")
                .OrderByDescending(System.IO.Path.GetFileName)
                .Take(3);

            foreach (var file in files)
            {
                try
                {
                    var lines = LogCrypto.DecryptFile(file);
                    if (lines == null) continue;

                    var matches = lines
                        .Where(l => l.Contains(AiLogTag, StringComparison.OrdinalIgnoreCase)
                                 || l.Contains(AiFetchTag, StringComparison.OrdinalIgnoreCase))
                        .TakeLast(30);

                    foreach (var line in matches)
                    {
                        result.Add(line);
                    }
                }
                catch { /* single-file failure is fine */ }
            }
        }
        catch { /* log dir may not exist */ }

        return result.Count > 0 ? result : new List<string> { "(暂无 AI 相关日志)" };
    }
}
