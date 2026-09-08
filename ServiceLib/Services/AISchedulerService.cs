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

            try
            {
                Logging.SaveLog($"{_tag}: Auto-crawl triggered");

                var aiService = new AIFetchService(config, async (success, msg) =>
                {
                    Logging.SaveLog($"{_tag}: {msg}");
                    await Task.CompletedTask;
                });

                var result = await aiService.FetchAndAddNodesAsync();
                Logging.SaveLog($"{_tag}: Auto-crawl completed, added {result} nodes");
            }
            catch (Exception ex)
            {
                Logging.SaveLog(_tag, ex);
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
