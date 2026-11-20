using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ScrcpyStudio;

internal sealed partial class LogPage : UserControl
{
    private const int LineLimit = 4000;

    private static readonly Brush Plain = Frozen("#C9D4E3");
    private static readonly Brush Dim = Frozen("#7E8B9E");
    private static readonly Brush Command = Frozen("#86ABFF");
    private static readonly Brush Warn = Frozen("#F5B544");
    private static readonly Brush Bad = Frozen("#F47174");
    private static readonly Brush Good = Frozen("#3DD68C");

    private readonly Studio _studio;
    private readonly FlowDocument _document;
    private ScrollViewer? _scroller;

    internal LogPage(Studio studio)
    {
        InitializeComponent();
        DataContext = _studio = studio;
        _document = Box.Document;
        _document.FontFamily = Box.FontFamily;
        _document.FontSize = Box.FontSize;
        _document.LineHeight = 19;
        _document.Blocks.Clear();

        studio.LogAppended += Append;
        studio.LogCleared += Reset;
        Reset();
    }

    internal event Action<string>? Copied;

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static Brush Colour(string line)
    {
        if (line.StartsWith("& ", StringComparison.Ordinal) || line.StartsWith("$env:", StringComparison.Ordinal)
            || line.StartsWith("adb ", StringComparison.Ordinal))
            return Command;
        if (line.Contains("ERROR", StringComparison.Ordinal) || line.Contains("失败", StringComparison.Ordinal))
            return Bad;
        if (line.Contains("WARN", StringComparison.Ordinal))
            return Warn;
        if (line.Contains("DEBUG", StringComparison.Ordinal) || line.Contains("VERBOSE", StringComparison.Ordinal))
            return Dim;
        if (line.Contains("successfully", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("connected to", StringComparison.OrdinalIgnoreCase))
            return Good;
        return Plain;
    }

    private void Append(string line)
    {
        _scroller ??= Box.Template.FindName("PART_ContentHost", Box) as ScrollViewer;
        bool follow = _scroller is null || _scroller.VerticalOffset + _scroller.ViewportHeight >= _scroller.ExtentHeight - 24;

        var paragraph = new Paragraph(new Run(line) { Foreground = Colour(line) }) { Margin = new Thickness(0) };
        _document.Blocks.Add(paragraph);
        while (_document.Blocks.Count > LineLimit)
            _document.Blocks.Remove(_document.Blocks.FirstBlock);

        Empty.Visibility = Visibility.Collapsed;
        Counter.Text = $"{_document.Blocks.Count} 行";
        if (follow)
            Box.ScrollToEnd();
    }

    private void Reset()
    {
        _document.Blocks.Clear();
        Empty.Visibility = Visibility.Visible;
        Counter.Text = "0 行";
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (_studio.LogText.Length > 0)
            Copied?.Invoke(_studio.LogText);
    }

    private void OnClear(object sender, RoutedEventArgs e) => _studio.ClearLog();
}
