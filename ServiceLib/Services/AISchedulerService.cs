namespace ServiceLib.Services;

/// <summary>
/// Daily scheduler for AI node crawling.
/// Fires once per day at a fixed time (default 03:00 local), crawls all
/// configured URLs, validates nodes via real latency test, and removes
/// invalid ones from AI groups. AutoCrawlIntervalMinutes is repurposed as
/// the hour-of-day to run (0-23); default is 3 (3 AM).
/// </summary>
public static class AISchedulerService
{
    private static readonly string _tag = "AISchedulerService";
    private static Timer? _timer;
    private static bool _isRunning;
    private static int _lastAddedNodes;
    private static int _lastCleanedNodes;
    private static DateTime _lastRunAtUtc = DateTime.MinValue;
    private static DateTime _lastSuccessAtUtc = DateTime.MinValue;
    private static int _consecutiveFailures;
    private static string? _lastMessage;

    public static bool IsRunning => _timer != null;
    public static bool IsBusy => _isRunning;
    public static int LastAddedNodes => _lastAddedNodes;
    public static int LastCleanedNodes => _lastCleanedNodes;
    public static DateTime LastRunAtUtc => _lastRunAtUtc;
    public static DateTime LastSuccessAtUtc => _lastSuccessAtUtc;
    public static int ConsecutiveFailures => _consecutiveFailures;
    public static string? LastMessage => _lastMessage;

    public static event Action<string, int>? RunCompleted;

    /// <summary>调度器自身任务的忙状态（来源固定为 "scheduler"）。</summary>
    public static event Action<bool>? IsBusyChanged;

    /// <summary>
    /// 带来源标识的忙状态。UI 用它按来源分别登记/注销，
    /// 这样聊天页的采集和定时任务的采集各自结束时，不会把对方的光晕一起灭掉。
    /// </summary>
    public static event Action<string, bool>? BusyScopeChanged;

    /// <summary>
    /// 手动入口（设置页、聊天页）直接调用 AIFetchService 时也会用到。
    /// 这些入口绕过了调度器，但 UI 的 AI 图标动画需要知道它们在忙，
    /// 所以这里带来源把忙状态转发给订阅者。不影响调度器自身的 _isRunning 守卫。
    /// </summary>
    public static void NotifyBusy(bool busy, string source = "manual")
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            source = "manual";
        }

        BusyScopeChanged?.Invoke(source, busy);
    }

    /// <summary>
    /// Start the daily scheduler. Fires once per day at the configured hour.
    /// </summary>
    public static void Start(Config config, bool immediateFirstRun = true)
    {
        Stop();

        var aiConfig = config.AIConfigItem;
        if (aiConfig == null || !aiConfig.Enabled || !aiConfig.AutoCrawlEnabled)
        {
            WriteDebug($"Start skipped: aiConfig={aiConfig != null}, Enabled={aiConfig?.Enabled}, AutoCrawl={aiConfig?.AutoCrawlEnabled}");
            return;
        }

        // AutoCrawlIntervalMinutes is repurposed: the hour of day to run (0-23).
        // Clamp to valid range; default to 3 (3 AM) if out of range.
        var runHour = aiConfig.AutoCrawlIntervalMinutes;
        if (runHour < 0 || runHour > 23)
            runHour = 3;

        ScheduleNextRun(config, runHour);
        Logging.SaveLog($"{_tag}: Daily scheduler started, will run at {runHour:00}:00 local time");
        WriteDebug($"Scheduler started, runHour={runHour}, immediateFirstRun={immediateFirstRun}, ApiUrl={aiConfig.ApiUrl}, Enabled={aiConfig.Enabled}");

        // 每天只主动采集一遍。
        // 之前是「每次启动都立刻抓一轮」——软件一天开 5 次就抓 5 次，跟「每天采集一遍」
        // 完全不是一回事，还拖慢启动。现在改成：今天还没采过才补采一次，
        // 已经采过就只排下一次定时任务。今天那个时间点软件没开着，也会在下次启动时补上。
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        if (immediateFirstRun && !string.Equals(aiConfig.LastAutoCrawlDate, today, StringComparison.Ordinal))
        {
            Logging.SaveLog($"{_tag}: First run of today triggered (last={aiConfig.LastAutoCrawlDate ?? "never"})");
            WriteDebug("Immediate first run triggered");
            // 不能立刻开跑：本方法是在 AppManager.InitApp() 末尾调用的，而
            // CoreManager 的 _config 要到主窗口 Init() 才赋值。抢跑的话清理步骤会在
            // LoadCoreConfigSpeedtest 里拿到空配置直接抛 NullReferenceException，
            // 采集永远走不到末尾的「标记今天已采」，于是每次启动都重试一遍
            // （实测日志里连续 24 次 CleanInvalidNodesInGroup failed）。
            var _ = Task.Run(async () =>
            {
                if (await WaitForCoreReadyAsync(TimeSpan.FromMinutes(3)))
                {
                    await ExecuteRunAsync(config);
                }
                else
                {
                    Logging.SaveLog($"{_tag}: Core not ready within 3 minutes, skipping today's immediate crawl");
                    WriteDebug("Immediate run skipped: core never became ready");
                }
            });
        }
        else if (immediateFirstRun)
        {
            Logging.SaveLog($"{_tag}: Already crawled today ({aiConfig.LastAutoCrawlDate}), skip immediate run");
            WriteDebug("Immediate run skipped: already crawled today");
        }
    }

    /// <summary>
    /// 等 CoreManager 初始化完成（最多等 timeout）。就绪返回 true，超时返回 false。
    /// </summary>
    private static async Task<bool> WaitForCoreReadyAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (CoreManager.Instance.IsInitialized)
                {
                    return true;
                }
            }
            catch
            {
                // 实例化异常不该让采集线程崩掉，继续等下一轮
            }
            await Task.Delay(500);
        }

        try
        {
            return CoreManager.Instance.IsInitialized;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Compute the delay until the next run and set a one-shot timer.
    /// </summary>
    private static void ScheduleNextRun(Config config, int runHour)
    {
        var now = DateTime.Now;
        var nextRun = new DateTime(now.Year, now.Month, now.Day, runHour, 0, 0);

        // If today's slot already passed, schedule for tomorrow
        if (nextRun <= now)
        {
            nextRun = nextRun.AddDays(1);
        }

        var delay = nextRun - now;
        Logging.SaveLog($"{_tag}: Next run at {nextRun:yyyy-MM-dd HH:mm}, in {delay.TotalMinutes:F0} min");

        _timer = new Timer(async _ => await ExecuteRunAsync(config), null, delay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Execute one crawl+clean cycle, then reschedule for tomorrow.
    /// </summary>
    private static async Task ExecuteRunAsync(Config config)
    {
        if (_isRunning)
        {
            // 上一轮还没跑完就直接 return，会把这一轮的定时任务彻底丢掉：
            // 定时器是一次性的，触发完就废了，不重排的话再也不会自动采。
            // 这里按配置重排下一次，宁可晚一点也不让自动采集悄悄停摆。
            Logging.SaveLog($"{_tag}: Previous run still busy, reschedule instead of dropping this cycle");
            WriteDebug("ExecuteRunAsync skipped: previous run still busy, rescheduled");
            var ai = config.AIConfigItem;
            var hour = ai?.AutoCrawlIntervalMinutes ?? 3;
            if (hour < 0 || hour > 23)
                hour = 3;
            ScheduleNextRun(config, hour);
            return;
        }
        _isRunning = true;
        _lastRunAtUtc = DateTime.UtcNow;
        IsBusyChanged?.Invoke(true);

        try
        {
            Logging.SaveLog($"{_tag}: Daily crawl triggered");
            WriteDebug("ExecuteRunAsync started");

            var aiService = new AIFetchService(config, async (success, msg) =>
            {
                Logging.SaveLog($"{_tag}: {msg}");
                _lastMessage = msg;
                await Task.CompletedTask;
            });

            // Step 1: 先清理组内已失效的节点。这一步会起临时内核做真实延迟测试，
            // 网络或内核异常时可能长时间卡住，所以同样加超时——否则后面的补充永远跑不到，
            // AI 图标也会一直亮着。
            var cleaned = 0;
            try
            {
                var cleanTask = aiService.CleanInvalidNodesInAIGroups();
                var cleanTimeout = Task.Delay(TimeSpan.FromMinutes(8));
                if (await Task.WhenAny(cleanTask, cleanTimeout) == cleanTask)
                {
                    cleaned = await cleanTask;
                }
                else
                {
                    Logging.SaveLog($"{_tag}: Clean step timed out after 8 minutes, skipped");
                    WriteDebug("CleanInvalidNodesInAIGroups TIMED OUT (8min)");
                }
            }
            catch (Exception ex)
            {
                Logging.SaveLog($"{_tag}: Clean step failed", ex);
                WriteDebug($"CleanInvalidNodesInAIGroups FAILED: {ex.Message}");
            }

            _lastCleanedNodes = cleaned;
            if (cleaned > 0)
            {
                Logging.SaveLog($"{_tag}: Cleaned {cleaned} invalid nodes from AI groups");
            }

            // Step 2: Fetch new nodes with a hard timeout. Without this, if any network
            // step hangs (GitHub search, API call), finally never runs and the AI glow
            // animation keeps pulsing even when nothing is actually happening.
            // 实测：光「抓 12 个源」这一步就要 80 秒左右（样本 4682 个候选），
            // 后面还要起内核做真实连通性验证。原来只给 2 分钟，
            // 等于每天这次自动采集几乎必然被超时掐断——日志里就是一次「AI抓取超时」然后什么都没进组。
            // 这里放宽到 12 分钟：采集在后台跑，宁可慢点也要跑完。
            WriteDebug("FetchAndAddNodesAsync starting");
            var fetchTask = aiService.FetchAndAddNodesAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromMinutes(12));
            var completed = await Task.WhenAny(fetchTask, timeoutTask);
            if (completed == timeoutTask)
            {
                WriteDebug("FetchAndAddNodesAsync TIMED OUT (12min), aborting");
                _lastMessage = "AI抓取超时";
                Logging.SaveLog($"{_tag}: Fetch timed out after 12 minutes");
            }
            else
            {
                var result = await fetchTask;
                _lastAddedNodes = result;
                _lastSuccessAtUtc = DateTime.UtcNow;
                _consecutiveFailures = 0;
                // 区分「新增了」和「没有新的」——后者的正确行为是保持原样，不是清空重建。
                _lastMessage = result > 0
                ? $"完成：新增 {result} 个有效节点，清理 {cleaned} 个失效节点"
                : $"完成：本次无新增节点（分组保持原样），清理 {cleaned} 个失效节点";
                Logging.SaveLog($"{_tag}: Daily crawl completed, added {result} new nodes, cleaned {cleaned}");
                WriteDebug($"FetchAndAddNodesAsync completed, added {result} nodes");
                RunCompleted?.Invoke(_lastMessage, result);
            }

            // 记录今天已经采过，避免软件重启后又抓一轮。
            // 异常路径不记录——那种情况多半是配置或网络问题，下次启动应该重试。
            await MarkCrawledToday(config);
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            _lastMessage = $"失败：{ex.Message}";
            Logging.SaveLog(_tag, ex);
            WriteDebug($"ExecuteRunAsync FAILED: {ex.Message}");
            RunCompleted?.Invoke(_lastMessage, 0);
        }
        finally
        {
            _isRunning = false;
            IsBusyChanged?.Invoke(false);
            WriteDebug("ExecuteRunAsync finished, IsBusyChanged(false) called");

            // Reschedule for tomorrow
            var aiConfig = config.AIConfigItem;
            if (aiConfig != null && aiConfig.Enabled && aiConfig.AutoCrawlEnabled)
            {
                var runHour = aiConfig.AutoCrawlIntervalMinutes;
                if (runHour < 0 || runHour > 23)
                    runHour = 3;
                ScheduleNextRun(config, runHour);
            }
        }
    }

    private static async Task MarkCrawledToday(Config config)
    {
        try
        {
            var aiConfig = config.AIConfigItem;
            if (aiConfig == null)
            {
                return;
            }

            aiConfig.LastAutoCrawlDate = DateTime.Now.ToString("yyyy-MM-dd");
            await ConfigHandler.SaveConfig(config);
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"{_tag}: MarkCrawledToday failed", ex);
        }
    }

    private static void WriteDebug(string msg)
    {
        try
        {
            var path = System.IO.Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "ai_debug.log");
            System.IO.File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}{Environment.NewLine}");
        }
        catch { }
    }
    /// <summary>
    /// Stop the scheduler and release the timer.
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
