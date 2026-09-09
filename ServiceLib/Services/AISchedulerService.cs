namespace ServiceLib.Services;

/// <summary>
/// Runs periodic AI auto-crawl timer at app startup.
/// Checks AIConfigItem.AutoCrawlEnabled and starts a Timer that
/// periodically calls AIFetchService.FetchAndAddNodesAsync().
/// </summary>
public static class AISchedulerService
{
    private static readonly string _tag = "AISchedulerService";
    private static Timer? _timer;
    private static bool _isRunning;
    private static int _lastAddedNodes;
    private static DateTime _lastRunAtUtc = DateTime.MinValue;
    private static DateTime _lastSuccessAtUtc = DateTime.MinValue;
    private static int _consecutiveFailures;
    private static string? _lastMessage;

    public static bool IsRunning => _timer != null;
    public static bool IsBusy => _isRunning;
    public static int LastAddedNodes => _lastAddedNodes;
    public static DateTime LastRunAtUtc => _lastRunAtUtc;
    public static DateTime LastSuccessAtUtc => _lastSuccessAtUtc;
    public static int ConsecutiveFailures => _consecutiveFailures;
    public static string? LastMessage => _lastMessage;

    /// <summary>
    /// Raised after an auto-crawl run finishes (success or failure).
    /// Args: message, number of nodes added.
    /// </summary>
    public static event Action<string, int>? RunCompleted;

    /// <summary>
    /// Start the scheduler if AI auto-crawl is enabled in config.
    /// Call this from AppManager.InitApp() or after config is loaded.
    /// </summary>
    public static void Start(Config config)
    {
        Stop();

        var aiConfig = config.AIConfigItem;
        if (aiConfig == null || !aiConfig.Enabled || !aiConfig.AutoCrawlEnabled)
        {
            return;
        }

        var intervalMinutes = Math.Max(aiConfig.AutoCrawlIntervalMinutes, 5);
        var interval = TimeSpan.FromMinutes(intervalMinutes);

        Logging.SaveLog($"{_tag}: Starting auto-crawl scheduler, interval={intervalMinutes}min");

    _timer = new Timer(async _ =>
    {
        if (_isRunning) return;
        _isRunning = true;
        _lastRunAtUtc = DateTime.UtcNow;

        try
        {
            Logging.SaveLog($"{_tag}: Auto-crawl triggered");

            var aiService = new AIFetchService(config, async (success, msg) =>
            {
                Logging.SaveLog($"{_tag}: {msg}");
                _lastMessage = msg;
                await Task.CompletedTask;
            });

            var result = await aiService.FetchAndAddNodesAsync();
            _lastAddedNodes = result;
            _lastSuccessAtUtc = DateTime.UtcNow;
            _consecutiveFailures = 0;
            _lastMessage = $"完成：新增 {result} 个节点";
            Logging.SaveLog($"{_tag}: Auto-crawl completed, added {result} nodes");
            RunCompleted?.Invoke(_lastMessage, result);
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            _lastMessage = $"失败：{ex.Message}";
            Logging.SaveLog(_tag, ex);
            RunCompleted?.Invoke(_lastMessage, 0);
        }
        finally
        {
            _isRunning = false;
        }
    }, null, interval, interval);
    }

    /// <summary>
    /// Stop the scheduler.
    /// </summary>
    public static void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _isRunning = false;
    }

    /// <summary>
    /// Restart the scheduler with updated config.
    /// </summary>
    public static void Restart(Config config)
    {
        Stop();
        Start(config);
    }
}
