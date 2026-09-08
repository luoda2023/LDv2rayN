namespace ServiceLib.ViewModels;

public partial class AISettingViewModel : MyReactiveObject, ICloseable
{
    public event EventHandler? RequestClose;

    [Reactive] public partial string ApiUrl { get; set; }
    [Reactive] public partial string ApiKey { get; set; }
    [Reactive] public partial string ModelId { get; set; }
    [Reactive] public partial bool AIEnabled { get; set; }
    [Reactive] public partial int SearchIntervalMinutes { get; set; }
    [Reactive] public partial string AiGroupRemarks { get; set; }
    [Reactive] public partial int MaxNodesPerSearch { get; set; }
    [Reactive] public partial string StatusMessage { get; set; }
    [Reactive] public partial bool IsSearching { get; set; }

    public ReactiveCommand<RxVoid, RxVoid> SaveCmd { get; }
    public ReactiveCommand<RxVoid, RxVoid> TestFetchCmd { get; }

    public AISettingViewModel()
    {
        _config = AppManager.Instance.Config;

        SaveCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SaveSettingAsync();
        });

        TestFetchCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await TestFetchAsync();
        });

        _ = Init();
    }

    private async Task Init()
    {
        var aiConfig = _config.AIConfigItem ?? new AIConfigItem();

        ApiUrl = aiConfig.ApiUrl ?? string.Empty;
        ApiKey = aiConfig.ApiKey ?? string.Empty;
        ModelId = aiConfig.ModelId ?? string.Empty;
        AIEnabled = aiConfig.Enabled;
        SearchIntervalMinutes = aiConfig.SearchIntervalMinutes;
        AiGroupRemarks = aiConfig.AiGroupRemarks ?? "AI自动获取";
        MaxNodesPerSearch = aiConfig.MaxNodesPerSearch;
        StatusMessage = "就绪";

        await Task.CompletedTask;
    }

    private async Task SaveSettingAsync()
    {
        _config.AIConfigItem ??= new AIConfigItem();
        _config.AIConfigItem.ApiUrl = ApiUrl.TrimEx();
        _config.AIConfigItem.ApiKey = ApiKey.TrimEx();
        _config.AIConfigItem.ModelId = ModelId.TrimEx();
        _config.AIConfigItem.Enabled = AIEnabled;
        _config.AIConfigItem.SearchIntervalMinutes = SearchIntervalMinutes;
        _config.AIConfigItem.AiGroupRemarks = AiGroupRemarks.TrimEx();
        _config.AIConfigItem.MaxNodesPerSearch = MaxNodesPerSearch;

        if (await ConfigHandler.SaveConfig(_config) == 0)
        {
            NoticeManager.Instance.Enqueue(ResUI.OperationSuccess);
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            NoticeManager.Instance.Enqueue(ResUI.OperationFailed);
        }

        await Task.CompletedTask;
    }

    private async Task TestFetchAsync()
    {
        if (IsSearching)
        {
            return;
        }

        IsSearching = true;
        StatusMessage = "正在测试AI连接...";

        try
        {
            var aiService = new AIFetchService(_config, async (success, msg) =>
            {
                StatusMessage = msg;
                await Task.CompletedTask;
            });

            var result = await aiService.FetchAndAddNodesAsync();
            StatusMessage = result > 0
                ? $"✅ 测试完成，成功添加 {result} 个节点"
                : "⚠️ 测试完成，未找到有效节点";
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ 测试失败: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }
}
