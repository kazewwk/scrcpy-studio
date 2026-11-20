using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ScrcpyStudio;

internal sealed partial class OptionsPage : UserControl
{
    private static readonly string[] Swatches =
    [
        "#000000", "#111827", "#222222", "#374151", "#6b7280", "#d1d5db", "#ffffff",
        "#ef4444", "#f97316", "#f59e0b", "#84cc16", "#22c55e", "#14b8a6", "#06b6d4",
        "#3b82f6", "#6366f1", "#8b5cf6", "#d946ef", "#ec4899", "#f43f5e", "#78350f",
    ];

    private readonly Studio _studio;
    private OptionItem[] _shown = [];

    internal OptionsPage(Studio studio)
    {
        InitializeComponent();
        DataContext = _studio = studio;
    }

    internal int ShowGroup(string group, string? focus)
    {
        Show(_studio.Options.Where(item => item.Group == group).ToArray(), tagged: false, focus);
        return _shown.Length;
    }

    internal int ShowSearch(string term)
    {
        Show(_studio.Options.Where(item =>
        {
            Option o = item.Option;
            return $"{o.Title} {o.Flag} {o.Alias} {o.Description} {o.Group}"
                .Contains(term, StringComparison.OrdinalIgnoreCase);
        }).ToArray(), tagged: true, focus: null);
        return _shown.Length;
    }

    private void Show(OptionItem[] items, bool tagged, string? focus)
    {
        foreach (OptionItem item in _shown)
            item.IsExpanded = false;
        foreach (OptionItem item in items)
            item.ShowGroup = tagged;

        if (!items.SequenceEqual(_shown))
        {
            _shown = items;
            List.ItemsSource = items;
            ListCard.Visibility = items.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            Empty.Visibility = items.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        Scroller.ScrollToTop();

        if (focus is null || items.FirstOrDefault(item => item.Key == focus) is not { } target)
            return;
        target.IsExpanded = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (List.ItemContainerGenerator.ContainerFromItem(target) is FrameworkElement row)
                row.BringIntoView();
        });
    }

    private static OptionItem Entry(object sender) => (OptionItem)((FrameworkElement)sender).DataContext;

    private async void OnQuery(object sender, RoutedEventArgs e) => await _studio.LaunchAsync(Entry(sender).Key);

    private void OnPick(object sender, RoutedEventArgs e)
    {
        OptionItem item = Entry(sender);
        switch (item.Key)
        {
            case "background-color":
                PickColor(item, (FrameworkElement)sender);
                break;
            case "SCRCPY_ICON_DIR":
                var folder = new OpenFolderDialog { Title = item.Title };
                if (System.IO.Directory.Exists(item.Text))
                    folder.InitialDirectory = item.Text;
                if (folder.ShowDialog(Window.GetWindow(this)) == true)
                    item.Text = folder.FolderName;
                break;
            case "record":
                string format = _studio.Config.GetValueOrDefault("record-format") is { Length: > 0 } chosen ? chosen : "mp4";
                PickFile(item, new SaveFileDialog
                {
                    Title = item.Title, DefaultExt = format, AddExtension = true,
                    Filter = "媒体文件|*.mp4;*.mkv;*.m4a;*.mka;*.opus;*.aac;*.flac;*.wav|所有文件|*.*",
                });
                break;
            default:
                PickFile(item, new OpenFileDialog
                {
                    Title = item.Title,
                    Filter = item.Key == "ADB" ? "ADB 可执行文件|*.exe|所有文件|*.*" : "所有文件|*.*",
                });
                break;
        }
    }

    private void PickFile(OptionItem item, FileDialog dialog)
    {
        try
        {
            if (System.IO.Path.GetDirectoryName(item.Text) is { Length: > 0 } directory
                && System.IO.Directory.Exists(directory))
            {
                dialog.InitialDirectory = directory;
                dialog.FileName = System.IO.Path.GetFileName(item.Text);
            }
        }
        catch (ArgumentException)
        {
            // The current text is not a usable path; open the dialog at its default location.
        }
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            item.Text = dialog.FileName;
    }

    private void PickColor(OptionItem item, FrameworkElement anchor)
    {
        var grid = new UniformGrid { Columns = 7, Margin = new Thickness(10) };
        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false,
            AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, VerticalOffset = 4,
        };
        foreach (string hex in Swatches)
        {
            var swatch = new Button
            {
                Width = 26, Height = 26, Margin = new Thickness(3), Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = hex, Template = (ControlTemplate)FindResource("SwatchTemplate"),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
            };
            swatch.Click += (_, _) =>
            {
                item.Text = hex;
                popup.IsOpen = false;
            };
            grid.Children.Add(swatch);
        }

        var surface = new Border
        {
            Margin = new Thickness(10), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            Child = grid,
        };
        surface.SetResourceReference(Border.BackgroundProperty, "PopupBrush");
        surface.SetResourceReference(Border.BorderBrushProperty, "LineStrongBrush");
        surface.SetResourceReference(EffectProperty, "PopupShadow");

        // Placement ignores the margin kept for the shadow, so this lines up the right edges.
        surface.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        popup.HorizontalOffset = anchor.ActualWidth - (surface.DesiredSize.Width - 20);
        popup.Child = surface;
        popup.IsOpen = true;
    }
}
