using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ServiceLib.ViewModels;

namespace v2rayN.Desktop.Views;

public partial class AIChatWindow : Window
{
    public AIChatViewModel ViewModel { get; }

    public AIChatWindow()
    {
        InitializeComponent();
        ViewModel = new AIChatViewModel();
        DataContext = ViewModel;

        // Auto-scroll when new messages are added
        ViewModel.Messages.CollectionChanged += Messages_CollectionChanged;

        Loaded += AIChatWindow_Loaded;
    }

    private void AIChatWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        txtTargetGroup.Text = ViewModel.TargetGroup;
        txtMaxNodes.Text = ViewModel.MaxNodes.ToString();
        chkAutoTest.IsChecked = ViewModel.AutoTest;
        chkAutoCrawl.IsChecked = ViewModel.AutoCrawlEnabled;
        txtCrawlInterval.Text = ViewModel.AutoCrawlIntervalMinutes.ToString();

        chkAutoCrawl.IsCheckedChanged += (_, _) => ApplyAutoCrawl();

        // Update UI when processing state changes
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AIChatViewModel.IsProcessing))
            {
                btnAnalyzeUrl.IsEnabled = !ViewModel.IsProcessing;
                btnAutoSearch.IsEnabled = !ViewModel.IsProcessing;
                btnAnalyzeUrl.Content = ViewModel.IsProcessing ? "⏳ 处理中..." : "🔍 分析链接";
                btnAutoSearch.Content = ViewModel.IsProcessing ? "⏳ 搜索中..." : "🤖 自动搜索";
            }
        };

        // Focus input
        txtInput.Focus();
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            scrollChat.ScrollToEnd();
        }
    }

    private void TxtInput_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !ViewModel.IsProcessing)
        {
            ViewModel.ChatInput = txtInput.Text ?? string.Empty;
            _ = ViewModel.AnalyzeUrlAsync();
            txtInput.Text = string.Empty;
            e.Handled = true;
        }
    }

    private async void BtnAnalyzeUrl_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.IsProcessing) return;

        ViewModel.ChatInput = txtInput.Text ?? string.Empty;
        ViewModel.TargetGroup = txtTargetGroup.Text ?? "AI自动获取";
        if (int.TryParse(txtMaxNodes.Text, out var maxNodes))
        {
            ViewModel.MaxNodes = maxNodes;
        }
        ViewModel.AutoTest = chkAutoTest.IsChecked == true;

        await ViewModel.AnalyzeUrlAsync();
        txtInput.Text = string.Empty;
        txtInput.Focus();
    }

    private async void BtnAutoSearch_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel.IsProcessing) return;

        ViewModel.TargetGroup = txtTargetGroup.Text ?? "AI自动获取";
        if (int.TryParse(txtMaxNodes.Text, out var maxNodes))
        {
            ViewModel.MaxNodes = maxNodes;
        }
        ViewModel.AutoTest = chkAutoTest.IsChecked == true;

        await ViewModel.AutoSearchAsync();
        txtInput.Focus();
    }

    private void ApplyAutoCrawl()
    {
        ViewModel.AutoCrawlEnabled = chkAutoCrawl.IsChecked == true;
        if (int.TryParse(txtCrawlInterval.Text, out var interval))
        {
            ViewModel.AutoCrawlIntervalMinutes = interval;
        }
        ViewModel.ToggleAutoCrawl();
    }
}
