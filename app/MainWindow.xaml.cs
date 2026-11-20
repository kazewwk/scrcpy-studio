using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ScrcpyStudio;

internal sealed class NavItem : Observable
{
    private int _count;

    public required string Name { get; init; }
    public Geometry? Icon { get; init; }
    public bool IsHeader { get; init; }

    public override string ToString() => Name;

    public int Count
    {
        get => _count;
        set
        {
            if (_count == value)
                return;
            _count = value;
            Raise();
        }
    }
}

internal sealed partial class MainWindow : Window
{
    private const string Home = "连接";
    private const string Control = "会话控制";
    private const string Log = "运行日志";

    private static readonly Dictionary<string, (string Icon, string Summary)> Sections = new()
    {
        [Home] = ("MonitorSmartphone", "选择设备，然后一键开始投屏"),
        ["连接与隧道"] = ("Cable", "设备选择、TCP/IP 与端口转发"),
        ["视频"] = ("Video", "分辨率、帧率、码率与编解码"),
        ["音频"] = ("Volume2", "音频来源、编码与缓冲"),
        ["相机"] = ("Camera", "把手机相机作为视频来源，需要 Android 12 或以上"),
        ["显示与窗口"] = ("AppWindow", "虚拟显示、画面方向与窗口外观"),
        ["键鼠与手柄"] = ("Gamepad2", "输入模式、OTG 与快捷键"),
        ["设备与电源"] = ("Power", "应用启动、屏幕与唤醒策略"),
        ["录制与投送"] = ("Film", "录制文件、会话时限与文件推送"),
        ["查询与诊断"] = ("Activity", "设备能力查询与日志级别"),
        ["运行环境"] = ("SquareTerminal", "环境变量与自定义组件路径"),
        [Control] = ("MousePointerClick", "向正在运行的投屏窗口发送按键与指令"),
        [Log] = ("ScrollText", "scrcpy 与 ADB 的输出"),
    };

    private readonly Studio _studio;
    private readonly HomePage _home;
    private readonly OptionsPage _options;
    private readonly ControlPage _control;
    private readonly LogPage _log;
    private readonly List<NavItem> _nav = [];
    private string _section = Home;
    private bool _syncing;
    private bool _closeConfirmed;
    private bool _systemBorder;
    private bool _snapshot;

    internal MainWindow(Studio studio)
    {
        InitializeComponent();
        DataContext = _studio = studio;

        // Keep the first window inside small or heavily scaled screens.
        Rect desk = SystemParameters.WorkArea;
        Width = Math.Min(Width, Math.Max(640, desk.Width - 24));
        Height = Math.Min(Height, Math.Max(480, desk.Height - 24));
        MinWidth = Math.Min(MinWidth, Width);
        MinHeight = Math.Min(MinHeight, Height);

        VersionText.Text = $"Studio {GetType().Assembly.GetName().Version?.ToString(3)}";

        _home = new HomePage(studio);
        _options = new OptionsPage(studio);
        _control = new ControlPage(studio);
        _log = new LogPage(studio);
        foreach (FrameworkElement page in new FrameworkElement[] { _home, _options, _control, _log })
        {
            page.Visibility = Visibility.Collapsed;
            PageHost.Children.Add(page);
        }

        AddHeader("开始");
        AddSection(Home);
        AddHeader("参数");
        foreach (string group in studio.Groups)
            AddSection(group);
        AddHeader("会话");
        AddSection(Control);
        AddSection(Log);
        Nav.ItemsSource = _nav;

        studio.Navigate += (section, focus) => ShowSection(section, focus);
        studio.SessionFailed += () =>
        {
            if (_section == Home && Search.Text.Length == 0)
                ShowSection(Log);
        };
        studio.ConfigChanged += UpdateCounts;
        _log.Copied += text => Copy(text, "日志已复制");

        StateChanged += (_, _) => SyncWindowState();
        PreviewKeyDown += OnPreviewKeyDown;
        UpdateCounts();
        SyncTheme();
        SyncWindowState();
        ShowSection(Home);
    }

    private void AddHeader(string name) => _nav.Add(new NavItem { Name = name, IsHeader = true });

    private void AddSection(string name) => _nav.Add(new NavItem
    {
        Name = name,
        Icon = Ui.Glyph(Sections.TryGetValue(name, out var known) ? known.Icon : "SlidersHorizontal"),
    });

    private void UpdateCounts()
    {
        foreach (NavItem item in _nav.Where(item => !item.IsHeader))
            item.Count = _studio.Options.Count(option => option.Group == item.Name && option.IsOn);
    }

    // Navigation

    internal void ShowSection(string name, string? focus = null)
    {
        _section = name;
        _syncing = true;
        Search.Text = "";
        Nav.SelectedItem = _nav.FirstOrDefault(item => !item.IsHeader && item.Name == name);
        _syncing = false;

        FrameworkElement page = name switch
        {
            Home => _home,
            Control => _control,
            Log => _log,
            _ => _options,
        };
        if (page == _options)
            _options.ShowGroup(name, focus);
        Heading.Text = name;
        Subheading.Text = Sections.TryGetValue(name, out var known) ? known.Summary : "";
        Present(page, animate: true);
    }

    internal void Find(string term) => Search.Text = term;

    private void ShowSearch(string term)
    {
        _syncing = true;
        Nav.SelectedItem = null;
        _syncing = false;
        int matches = _options.ShowSearch(term);
        Heading.Text = "参数搜索";
        Subheading.Text = $"“{term}”  ·  {matches} 项匹配";
        Present(_options, animate: _options.Visibility != Visibility.Visible);
    }

    private void Present(FrameworkElement page, bool animate)
    {
        foreach (UIElement child in PageHost.Children)
            child.Visibility = child == page ? Visibility.Visible : Visibility.Collapsed;
        if (!animate)
            return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        PageHost.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190)) { EasingFunction = ease });
        PageShift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
    }

    private void OnNavChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_syncing && Nav.SelectedItem is NavItem { IsHeader: false } item)
            ShowSection(item.Name);
    }

    private void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        bool empty = Search.Text.Length == 0;
        SearchKey.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        SearchClear.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        if (_syncing)
            return;

        string term = Search.Text.Trim();
        if (term.Length == 0)
            ShowSection(_section);
        else
            ShowSearch(term);
    }

    private void OnSearchClear(object sender, RoutedEventArgs e)
    {
        Search.Text = "";
        Search.Focus();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            Search.Focus();
            Search.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && Search.IsKeyboardFocusWithin && Search.Text.Length > 0)
        {
            Search.Text = "";
            e.Handled = true;
        }
    }

    // Launch bar

    private async void OnStart(object sender, RoutedEventArgs e) => await _studio.LaunchAsync();

    private async void OnStop(object sender, RoutedEventArgs e) => await _studio.StopAsync(allowForce: true);

    private void OnResume(object sender, RoutedEventArgs e) => _studio.ContinueExit();

    private void OnCopyCommand(object sender, RoutedEventArgs e) => Copy(_studio.Command, "命令已复制");

    private void Copy(string text, string done)
    {
        if (text.Length == 0)
            return;
        try
        {
            Clipboard.SetText(text);
            ShowToast(done);
        }
        catch (ExternalException ex)
        {
            _studio.Report("剪贴板暂时不可用", ex.Message);
        }
    }

    private void ShowToast(string text)
    {
        ToastText.Text = text;
        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1700))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2050))));
        Toast.BeginAnimation(OpacityProperty, fade);
    }

    // Window chrome and theme

    private void OnToggleTheme(object sender, RoutedEventArgs e)
    {
        Theme.Apply(!Theme.IsDark, remember: true);
        SyncTheme();
    }

    internal void SyncTheme()
    {
        Ui.SetIcon(ThemeButton, Ui.Glyph(Theme.IsDark ? "Sun" : "Moon"));
        SyncFrame();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        SyncFrame();
    }

    /// <summary>Matches the frame Windows draws around the window to the current palette.</summary>
    private void SyncFrame()
    {
        nint handle = new WindowInteropHelper(this).Handle;
        if (handle == 0)
            return;

        const int darkMode = 20, cornerPreference = 33, borderColour = 34, rounded = 2;
        int value = Theme.IsDark ? 1 : 0;
        DwmSetWindowAttribute(handle, darkMode, ref value, sizeof(int));
        value = rounded;
        DwmSetWindowAttribute(handle, cornerPreference, ref value, sizeof(int));

        // Windows 11 draws its own one-pixel border, which follows the rounded corners; Windows 10
        // rejects the attribute and the window keeps the border from its own layout.
        Color line = ((SolidColorBrush)FindResource("LineStrongBrush")).Color;
        value = line.R | line.G << 8 | line.B << 16;
        _systemBorder = DwmSetWindowAttribute(handle, borderColour, ref value, sizeof(int)) == 0;
        SyncWindowState();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void SyncWindowState()
    {
        bool maximized = WindowState == WindowState.Maximized;
        Ui.SetIcon(MaximizeButton, (Geometry)FindResource(maximized ? "Glyph.Restore" : "Glyph.Maximize"));
        MaximizeButton.ToolTip = maximized ? "向下还原" : "最大化";
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, (string)MaximizeButton.ToolTip);

        // A maximised window with custom chrome extends past the work area by its resize frame.
        Thickness frame = SystemParameters.WindowResizeBorderThickness;
        Shell.Margin = maximized
            ? new Thickness(frame.Left + 4, frame.Top + 4, frame.Right + 4, frame.Bottom + 4)
            : default;
        Shell.BorderThickness = new Thickness(maximized || (_systemBorder && !_snapshot) ? 0 : 1);
    }

    /// <summary>
    /// Lays the interface out at a fixed size inside a canvas, so a snapshot does not depend on how
    /// large a window the current desktop allows. Returns the element to render.
    /// </summary>
    internal FrameworkElement PrepareSnapshot(double width, double height)
    {
        _snapshot = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Content = null;
        Shell.Width = width;
        Shell.Height = height;
        Content = new System.Windows.Controls.Canvas { Children = { Shell } };
        return Shell;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!_studio.HasSession || _closeConfirmed)
        {
            _studio.SaveOptions();
            return;
        }

        e.Cancel = true;
        if (_studio.Closing)
            return;
        if (await _studio.CloseAsync())
        {
            _closeConfirmed = true;
            _ = Dispatcher.BeginInvoke(Close);
        }
    }
}
