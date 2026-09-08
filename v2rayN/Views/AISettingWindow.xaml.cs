using System.Windows;
using ServiceLib;
using ServiceLib.Services;
using ServiceLib.ViewModels;

namespace v2rayN.Views;

public partial class AISettingWindow : Window
{
    private AISettingViewModel ViewModel { get; set; }

    public AISettingWindow()
    {
        InitializeComponent();
        ViewModel = new AISettingViewModel();
        DataContext = ViewModel;

        Loaded += AISettingWindow_Loaded;
    }

    private void AISettingWindow_Loaded(object sender, RoutedEventArgs e)
    {
        chkAIEnabled.IsChecked = ViewModel.AIEnabled;
        txtApiUrl.Text = ViewModel.ApiUrl;
        txtApiKey.Password = ViewModel.ApiKey;
        txtModelId.Text = ViewModel.ModelId;
        txtAiGroupRemarks.Text = ViewModel.AiGroupRemarks;
        txtSearchInterval.Text = ViewModel.SearchIntervalMinutes.ToString();
        txtMaxNodes.Text = ViewModel.MaxNodesPerSearch.ToString();
        txtStatus.Text = ViewModel.StatusMessage;

        btnSave.Click += BtnSave_Click;
        btnCancel.Click += BtnCancel_Click;
        btnTestFetch.Click += BtnTestFetch_Click;
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.AIEnabled = chkAIEnabled.IsChecked == true;
        ViewModel.ApiUrl = txtApiUrl.Text;
        ViewModel.ApiKey = txtApiKey.Password;
        ViewModel.ModelId = txtModelId.Text;
        ViewModel.AiGroupRemarks = txtAiGroupRemarks.Text;

        if (int.TryParse(txtSearchInterval.Text, out var interval))
        {
            ViewModel.SearchIntervalMinutes = interval;
        }

        if (int.TryParse(txtMaxNodes.Text, out var maxNodes))
        {
            ViewModel.MaxNodesPerSearch = maxNodes;
        }

        ViewModel.AIEnabled = chkAIEnabled.IsChecked == true;
        ViewModel.ApiUrl = txtApiUrl.Text;
        ViewModel.ApiKey = txtApiKey.Password;
        ViewModel.ModelId = txtModelId.Text;
        ViewModel.AiGroupRemarks = txtAiGroupRemarks.Text;

        if (int.TryParse(txtSearchInterval.Text, out var searchInterval))
        {
            ViewModel.SearchIntervalMinutes = searchInterval;
        }

        if (int.TryParse(txtMaxNodes.Text, out var maxNodesCount))
        {
            ViewModel.MaxNodesPerSearch = maxNodesCount;
        }

        var config = AppManager.Instance.Config;
        config.AIConfigItem ??= new AIConfigItem();
        config.AIConfigItem.ApiUrl = txtApiUrl.Text;
        config.AIConfigItem.ApiKey = txtApiKey.Password;
        config.AIConfigItem.ModelId = txtModelId.Text;
        config.AIConfigItem.AiGroupRemarks = txtAiGroupRemarks.Text;
        config.AIConfigItem.Enabled = chkAIEnabled.IsChecked == true;

        if (int.TryParse(txtSearchInterval.Text, out var interval2))
        {
            config.AIConfigItem.SearchIntervalMinutes = interval2;
        }

        if (int.TryParse(txtMaxNodes.Text, out var maxNodes2))
        {
            config.AIConfigItem.MaxNodesPerSearch = maxNodes2;
        }

        await ConfigHandler.SaveConfig(config);
        NoticeManager.Instance.Enqueue(ResUI.OperationSuccess);
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private async void BtnTestFetch_Click(object sender, RoutedEventArgs e)
    {
        btnTestFetch.IsEnabled = false;
        txtStatus.Text = "正在测试AI连接...";

        try
        {
            var config = AppManager.Instance.Config;
            config.AIConfigItem ??= new AIConfigItem();
            config.AIConfigItem.ApiUrl = txtApiUrl.Text;
            config.AIConfigItem.ApiKey = txtApiKey.Password;
            config.AIConfigItem.ModelId = txtModelId.Text;
            config.AIConfigItem.AiGroupRemarks = txtAiGroupRemarks.Text;

            var aiService = new AIFetchService(config, async (success, msg) =>
            {
                Dispatcher.Invoke(() => txtStatus.Text = msg);
                await Task.CompletedTask;
            });

            var result = await aiService.FetchAndAddNodesAsync();
            txtStatus.Text = result > 0
                ? $"✅ 测试完成，成功添加 {result} 个节点"
                : "⚠️ 测试完成，未找到有效节点";
        }
        catch (Exception ex)
        {
            txtStatus.Text = $"❌ 测试失败: {ex.Message}";
        }
        finally
        {
            btnTestFetch.IsEnabled = true;
        }
    }
}
