using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace v2rayN.Views;

public partial class MarkdownText : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(MarkdownText),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public MarkdownText()
    {
        InitializeComponent();
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownText ctrl)
        {
            ctrl.Render((string?)e.NewValue ?? string.Empty);
        }
    }

    private void Render(string raw)
    {
        var collection = textBlock.Inlines;
        collection.Clear();
        if (string.IsNullOrEmpty(raw)) return;

        foreach (var line in raw.Split('\n'))
        {
            AddLine(collection, line);
            collection.Add(new LineBreak());
        }

        // InlineCollection implements IList (legacy). Remove trailing LineBreak.
        var list = (System.Collections.IList)collection;
        if (list.Count > 0 && list[list.Count - 1] is LineBreak)
        {
            list.RemoveAt(list.Count - 1);
        }
    }

    private void AddLine(InlineCollection target, string line)
    {
        int i = 0;
        while (i < line.Length)
        {
            if (i + 1 < line.Length && line[i] == '*' && line[i + 1] == '*')
            {
                var end = line.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end > 0)
                {
                    var content = line.Substring(i + 2, end - i - 2);
                    target.Add(new Run(content) { FontWeight = FontWeights.Bold });
                    i = end + 2;
                    continue;
                }
            }

            if (line[i] == '`')
            {
                var end = line.IndexOf('`', i + 1);
                if (end > 0)
                {
                    var content = line.Substring(i + 1, end - i - 1);
                    // Run has no Padding in WPF; simulate with a theme-following background.
                    target.Add(new Run(" " + content + " ")
                    {
                        FontFamily = new FontFamily("Consolas"),
                        Background = (Brush)FindResource("MaterialDesignDivider"),
                        Foreground = (Brush)FindResource("MaterialDesignBody")
                    });
                    i = end + 1;
                    continue;
                }
            }

            var start = i;
            while (i < line.Length)
            {
                if (i + 1 < line.Length && line[i] == '*' && line[i + 1] == '*') break;
                if (line[i] == '`') break;
                i++;
            }
            if (i > start)
            {
                target.Add(new Run(line.Substring(start, i - start)));
            }
        }
    }
}
