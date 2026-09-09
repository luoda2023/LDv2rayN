using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace v2rayN.Views;

/// <summary>
/// Floating AI chat panel. Borderless, top-most, positioned at the main window's bottom-right.
/// </summary>
public partial class AIChatWindow : Window
{
    /// <summary>
    /// Effective max width for chat bubbles — 3/4 of the actual window width.
    /// Updates on SizeChanged so bubbles resize as the window is resized.
    /// </summary>
    public static readonly DependencyProperty BubbleMaxWidthProperty =
        DependencyProperty.Register(
            nameof(BubbleMaxWidth),
            typeof(double),
            typeof(AIChatWindow),
            new PropertyMetadata(285.0));

    public double BubbleMaxWidth
    {
        get => (double)GetValue(BubbleMaxWidthProperty);
        set => SetValue(BubbleMaxWidthProperty, value);
    }

    public AIChatWindow()
    {
        InitializeComponent();

        _vm = new AIChatViewModel();
        DataContext = _vm;

        // Bind txtInput.Text to VM.ChatInput (TwoWay, delay so user can type freely).
        txtInput.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(AIChatViewModel.ChatInput))
        {
            Mode = System.Windows.Data.BindingMode.TwoWay,
            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
        });

        txtInput.KeyDown += TxtInput_KeyDown;
        SizeChanged += (s, e) =>
        {
            UpdateBubbleMaxWidth();
            ScrollToEnd();
        };
    }

    private AIChatViewModel _vm;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        PositionNearMainWindow();
        UpdateBubbleMaxWidth();
        ScrollToEnd();
    }

    private void UpdateBubbleMaxWidth() => BubbleMaxWidth = ActualWidth * 0.75;

    private void PositionNearMainWindow()
    {
        if (Owner is not null)
        {
            var margin = 20.0;
            Left = Owner.Left + Owner.Width - Width - margin;
            Top = Owner.Top + Owner.Height - Height - margin;
            return;
        }

        var main = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w is not AIChatWindow);
        if (main is not null)
        {
            var margin = 20.0;
            Left = main.Left + main.Width - Width - margin;
            Top = main.Top + main.Height - Height - margin;
        }
        else
        {
            Left = SystemParameters.PrimaryScreenWidth - Width - 40;
            Top = SystemParameters.PrimaryScreenHeight - Height - 120;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            Hide();
        }
        else if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

 private void BtnMinimize_Click(object sender, RoutedEventArgs e) => Hide();
 private void BtnClose_Click(object sender, RoutedEventArgs e) => Hide();

 private void BtnSettings_Click(object sender, RoutedEventArgs e)
 {
 // Open the same AISettingWindow the top menu uses. Bring this dialog
 // forward after the modal returns so the AI dialog stays on top.
 try
 {
 var vm = new ServiceLib.ViewModels.AISettingViewModel();
 var dialog = new AISettingWindow { DataContext = vm };
 dialog.ShowDialog();
 Activate();
 }
 catch (Exception ex)
 {
 Logging.SaveLog("AIChatWindow.BtnSettings", ex);
 }
 }

    private void TxtInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !txtInput.AcceptsReturn)
        {
            e.Handled = true;
            BtnSend_Click(this, e);
        }
    }

    private async void BtnSend_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsProcessing) return;
        var text = txtInput.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        await _vm.AnalyzeUrlAsync();
        ScrollToEnd();
    }

    private void ScrollToEnd()
    {
        if (chatItems?.Items is null) return;
        var count = chatItems.Items.Count;
        if (count == 0) return;
        scrollChat.ScrollToBottom();
    }
}
