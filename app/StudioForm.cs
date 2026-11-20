using System.Runtime.InteropServices;

namespace ScrcpyStudio;

internal sealed partial class StudioForm : Form
{
    private static readonly Color Canvas = Color.FromArgb(242, 246, 252);
    private static readonly Color Ink = Color.FromArgb(25, 39, 57);
    private static readonly Color Muted = Color.FromArgb(76, 94, 117);
    private static readonly Color Rail = Color.FromArgb(22, 35, 52);
    private static readonly Color RailActive = Color.FromArgb(46, 68, 98);
    private static readonly Color Accent = Color.FromArgb(33, 96, 219);
    private static readonly Color Border = Color.FromArgb(209, 219, 232);
    private static readonly string UiFace = ChineseFont();

    private readonly Dictionary<string, string?> _config;
    private readonly Dictionary<string, Button> _nav = new();
    private readonly Panel _page = new() { Dock = DockStyle.Fill };
    private readonly Label _heading = Caption("", 22, bold: true);
    private readonly Label _state = Caption("正在准备内置组件…", 10, subtle: true);
    private readonly Button _start = ActionButton("开始投屏", 116, primary: true);
    private readonly Button _stop = ActionButton("停止", 78);
    private readonly Button _resumeExit = ActionButton("结束暂停", 96);
    private readonly TextBox _preview = new()
    {
        ReadOnly = true, Multiline = true, Dock = DockStyle.Fill, BackColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle, Font = new Font(UiFace, 9F), ScrollBars = ScrollBars.Vertical,
    };
    private readonly TextBox _log = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Both,
        WordWrap = false, BackColor = Color.FromArgb(24, 36, 53), ForeColor = Color.FromArgb(230, 238, 249),
        BorderStyle = BorderStyle.None, Font = new Font(UiFace, 10F),
    };
    private readonly ToolTip _tips = new() { AutoPopDelay = 10_000 };

    private Control _home = null!;
    private Control _options = null!;
    private Control _actions = null!;
    private Control _logPage = null!;
    private string _section = "连接";
    private string _status = "正在准备内置组件…";
    private string? _backend;
    private BackendSession? _session;
    private Task<int>? _runTask;
    private bool _interactiveSession;
    private bool _actionsReady;
    private bool _stopping;
    private bool _forceAvailable;
    private bool _closing;

    internal StudioForm()
    {
        Text = "scrcpy Studio";
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        Font = new Font(UiFace, 10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1130, 810);
        MinimumSize = new Size(930, 690);
        BackColor = Canvas;
        ForeColor = Ink;
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (!e.Control || e.KeyCode != Keys.F)
                return;
            if (_page.Controls.Count == 0 || _page.Controls[0] != _options)
                ShowSection("连接与隧道");
            _search.Focus();
            _search.SelectAll();
            e.SuppressKeyPress = true;
        };

        string? settingsError = null;
        try { _config = RuntimeFiles.LoadSettings(); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            _config = new();
            settingsError = ex.Message;
        }

        SuspendLayout();
        BuildShell();
        _home = BuildHome();
        _options = BuildOptions();
        _actions = BuildActions();
        _logPage = BuildLog();
        ShowSection("连接");
        RefreshChrome();
        if (settingsError is not null)
            AppendLog($"读取上次设置失败。{settingsError}");
        ResumeLayout();

        Shown += async (_, _) => await PrepareAsync();
        FormClosing += OnClosing;
    }

    private static string ChineseFont()
    {
        using var available = new System.Drawing.Text.InstalledFontCollection();
        var families = available.Families.Select(font => font.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] preferred = ["Microsoft YaHei UI", "Noto Sans CJK SC", "Microsoft YaHei", "SimSun"];
        return preferred.FirstOrDefault(families.Contains) ?? "Segoe UI";
    }

    private static Label Caption(string text, float size = 10F, bool bold = false, bool subtle = false)
    {
        return new Label
        {
            Text = text, AutoSize = true, Font = new Font(UiFace, size, bold ? FontStyle.Bold : FontStyle.Regular),
            ForeColor = subtle ? Muted : Ink, Margin = new Padding(0, 0, 0, 6),
        };
    }

    private static Button ActionButton(string text, int width = 98, bool primary = false)
    {
        var button = new Button
        {
            Text = text, Width = width, Height = 36, Margin = new Padding(0, 0, 8, 6),
            FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false,
            BackColor = primary ? Accent : Color.White,
            ForeColor = primary ? Color.White : Ink,
            Font = new Font(UiFace, 9F, primary ? FontStyle.Bold : FontStyle.Regular),
            Cursor = Cursors.Hand,
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(28, 83, 194) : Canvas;
        return button;
    }

    private static FlowLayoutPanel Row() => new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
        Margin = Padding.Empty, Padding = Padding.Empty,
    };

    private static TableLayoutPanel Table(int columns, int rows) => new()
    {
        ColumnCount = columns, RowCount = rows, Dock = DockStyle.Fill,
        Margin = Padding.Empty, Padding = Padding.Empty,
    };

    private static void FixedRow(TableLayoutPanel table, int px) =>
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, px));

    private static void FillRow(TableLayoutPanel table) =>
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

    private static void FixedColumn(TableLayoutPanel table, int px) =>
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, px));

    private static void FillColumn(TableLayoutPanel table, int percent = 100) =>
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, percent));

    private void BuildShell()
    {
        var root = Table(2, 1);
        FixedColumn(root, 210);
        FillColumn(root);
        FillRow(root);
        Controls.Add(root);

        var side = Table(1, 3);
        side.BackColor = Rail;
        side.Padding = new Padding(16, 19, 16, 12);
        FixedRow(side, 72);
        FillRow(side);
        FixedRow(side, 34);
        root.Controls.Add(side, 0, 0);

        var brand = Row();
        var picture = new PictureBox
        {
            Image = Icon?.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom,
            Width = 34, Height = 34, Margin = new Padding(0, 4, 8, 0),
        };
        var brandText = Caption("scrcpy Studio", 14, bold: true);
        brandText.ForeColor = Color.White;
        brandText.Margin = new Padding(0, 8, 0, 0);
        brand.Controls.Add(picture);
        brand.Controls.Add(brandText);
        side.Controls.Add(brand, 0, 0);

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Margin = Padding.Empty, AutoScroll = true,
        };
        side.Controls.Add(nav, 0, 1);
        foreach (string name in new[] { "连接" }.Concat(Catalog.Options.Select(o => o.Group).Distinct())
                     .Concat(["会话控制", "运行日志"]))
        {
            var button = ActionButton(name, 175);
            button.Height = 35;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.Padding = new Padding(9, 0, 0, 0);
            button.Margin = new Padding(0, 0, 0, 3);
            button.ForeColor = Color.FromArgb(226, 235, 247);
            button.BackColor = Rail;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = RailActive;
            button.Click += (_, _) => ShowSection(name);
            _nav[name] = button;
            nav.Controls.Add(button);
        }
        var version = Caption("scrcpy 5.0.1  ·  Windows x64", 8);
        version.ForeColor = Color.FromArgb(173, 194, 221);
        version.Margin = new Padding(5, 9, 0, 0);
        side.Controls.Add(version, 0, 2);

        var main = Table(1, 3);
        main.Padding = new Padding(24, 20, 24, 18);
        FixedRow(main, 81);
        FillRow(main);
        FixedRow(main, 80);
        root.Controls.Add(main, 1, 0);

        var header = Table(2, 2);
        FillColumn(header);
        FixedColumn(header, 329);
        FixedRow(header, 43);
        FillRow(header);
        header.Controls.Add(_heading, 0, 0);
        _state.AutoEllipsis = true;
        _state.AutoSize = false;
        _state.Dock = DockStyle.Fill;
        header.Controls.Add(_state, 0, 1);
        header.SetColumnSpan(_state, 2);
        var run = Row();
        run.FlowDirection = FlowDirection.RightToLeft;
        _start.Margin = new Padding(0, 0, 0, 6);
        _start.Click += async (_, _) => await LaunchAsync();
        _stop.Click += async (_, _) => await StopAsync(allowForce: true);
        _resumeExit.Visible = false;
        _resumeExit.Click += (_, _) =>
        {
            _session?.ContinueExit();
            _resumeExit.Visible = false;
        };
        run.Controls.AddRange([_start, _stop, _resumeExit]);
        header.Controls.Add(run, 1, 0);
        main.Controls.Add(header, 0, 0);
        main.Controls.Add(_page, 0, 1);

        var command = Table(2, 2);
        command.Padding = new Padding(0, 10, 0, 0);
        FillColumn(command);
        FixedColumn(command, 138);
        FixedRow(command, 22);
        FillRow(command);
        var title = Caption("PowerShell 命令预览", 9, bold: true, subtle: true);
        command.Controls.Add(title, 0, 0);
        command.Controls.Add(_preview, 0, 1);
        var copy = ActionButton("复制命令", 125);
        copy.Margin = new Padding(10, 1, 0, 0);
        copy.Click += (_, _) => Copy(_preview.Text);
        command.Controls.Add(copy, 1, 1);
        main.Controls.Add(command, 0, 2);
    }

    private Control BuildLog()
    {
        var contents = Table(1, 2);
        FixedRow(contents, 46);
        FillRow(contents);
        var row = Row();
        var copy = ActionButton("复制日志");
        copy.Click += (_, _) => Copy(_log.Text);
        var clear = ActionButton("清空日志");
        clear.Click += (_, _) => _log.Clear();
        row.Controls.AddRange([copy, clear]);
        contents.Controls.Add(row, 0, 0);
        var frame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = _log.BackColor };
        frame.Controls.Add(_log);
        contents.Controls.Add(frame, 0, 1);
        return contents;
    }

    private void Copy(string text)
    {
        if (text.Length == 0)
            return;

        try { Clipboard.SetText(text); }
        catch (ExternalException ex) { Report("剪贴板暂时不可用", ex.Message); }
    }

    private void ShowSection(string name, string? focus = null)
    {
        _section = name;
        _heading.Text = name;
        foreach (var (key, button) in _nav)
            button.BackColor = key == name ? RailActive : Rail;

        var content = name switch
        {
            "连接" => _home,
            "会话控制" => _actions,
            "运行日志" => _logPage,
            _ => _options,
        };
        if (_page.Controls.Count == 0 || _page.Controls[0] != content)
        {
            _page.Controls.Clear();
            _page.Controls.Add(content);
        }

        if (content == _options)
        {
            _search.Text = "";
            PopulateOptions(focus);
        }
        if (content == _home)
            ShowDevices();
    }

    private void PutOption(string key, bool enabled, string? value = null)
    {
        if (enabled)
            _config[key] = value;
        else
            _config.Remove(key);
        RefreshPreview();
    }

    private LaunchPlan Plan(Dictionary<string, string?>? config = null) =>
        new(_backend ?? "", config ?? _config);

    private void RefreshPreview()
    {
        _preview.Text = Plan().PowerShell();
    }

    private void RefreshChrome()
    {
        _state.Text = _status;
        _start.Enabled = _backend is not null && _session is null && !_closing;
        _stop.Enabled = _session is not null && (!_stopping || _forceAvailable);
        _stop.Text = _forceAvailable ? "强制结束" : "停止";
        _resumeExit.Visible = _session is not null && _resumeExit.Visible;
        UpdateActionState();
        UpdateAdbButtons();
        RefreshPreview();
    }

    private void Report(string text, string? detail = null)
    {
        _status = text;
        _state.Text = text;
        AppendLog(detail is null ? text : $"{text}。{detail}");
    }

    private void AppendLog(string line)
    {
        if (IsDisposed || Disposing)
            return;
        if (InvokeRequired)
        {
            if (IsHandleCreated)
            {
                try { BeginInvoke(() => AppendLog(line)); }
                catch (InvalidOperationException) { }
            }
            return;
        }

        if (_log.TextLength > 160_000)
        {
            _log.Select(0, 40_000);
            _log.SelectedText = "";
        }
        if (line == "SCRCPY_STUDIO_READY" && _session is not null)
        {
            _actionsReady = true;
            UpdateActionState();
            return;
        }
        _log.AppendText(line + Environment.NewLine);
        if (_session is not null && line.Contains("Press Enter to continue...", StringComparison.Ordinal))
        {
            _resumeExit.Visible = true;
            if (_stopping)
                _session.ContinueExit();
        }
    }

    private async Task PrepareAsync()
    {
        try
        {
            _backend = await Task.Run(RuntimeFiles.Extract);
            if (IsDisposed)
                return;
            _status = "已就绪";
            RefreshChrome();
            if (!_config.ContainsKey("otg"))
                await RefreshDevicesAsync();
        }
        catch (Exception ex)
        {
            Report("无法准备内置组件", ex.Message);
        }
    }

    private void SaveOptions()
    {
        try { RuntimeFiles.SaveSettings(_config); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report("设置保存失败", ex.Message);
        }
    }

    private async Task LaunchAsync(string? query = null)
    {
        if (_backend is null || _session is not null || _closing)
            return;

        var config = query is null ? _config.ToDictionary() : LaunchPlan.ForQuery(_config, query);
        if (config.Keys.FirstOrDefault(key => Catalog.ByKey[key].Platform == "Linux") is { } linux)
        {
            Report($"{Catalog.ByKey[linux].Flag} 需要 Linux 客户端");
            ShowSection(Catalog.ByKey[linux].Group, linux);
            return;
        }

        LaunchPlan plan = Plan(config);
        var queryFlag = Catalog.Options.FirstOrDefault(o =>
            config.ContainsKey(o.Key) && IsQuery(o.Key))?.Key;
        if (queryFlag is not null)
            ShowSection("运行日志");

        SaveOptions();
        BackendSession? session = null;
        try
        {
            session = new BackendSession(plan, AppendLog);
            _session = session;
            _interactiveSession = queryFlag is null && !config.ContainsKey("otg") && !config.ContainsKey("no-window");
            _actionsReady = false;
            _stopping = false;
            _forceAvailable = false;
            _status = queryFlag is null ? "scrcpy 会话运行中" : "正在执行查询…";
            AppendLog("");
            AppendLog(plan.PowerShell());
            RefreshChrome();

            _runTask = session.RunAsync();
            int exit = await _runTask;
            string result = exit switch
            {
                0 => "scrcpy 已退出",
                2 => "设备已断开",
                _ => $"scrcpy 退出代码 {exit}，详情见运行日志",
            };
            Report(result);
            if (IsHandleCreated && !_closing && exit != 0 && _section == "连接")
                ShowSection("运行日志");
        }
        catch (Exception ex)
        {
            Report("scrcpy 启动或运行失败", ex.Message);
            if (!_closing)
                ShowSection("运行日志");
        }
        finally
        {
            _session = null;
            _runTask = null;
            _resumeExit.Visible = false;
            _forceAvailable = false;
            session?.Dispose();
            if (!IsDisposed)
                RefreshChrome();
        }
    }

    private async Task StopAsync(bool allowForce = false)
    {
        if (_session is not { } session || _runTask is not { } run)
            return;
        if (_forceAvailable)
        {
            if (allowForce)
            {
                try { session.ForceStop(); }
                catch (System.ComponentModel.Win32Exception ex) { Report("强制结束失败", ex.Message); }
            }
            return;
        }

        _stopping = true;
        _status = "正在结束会话并等待文件写入…";
        RefreshChrome();
        session.ContinueExit();
        await session.SendAsync(0);

        if (await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(8))) == run || _session != session)
            return;
        _forceAvailable = true;
        Report("scrcpy 尚未退出，强制结束可能影响正在写入的录制");
        RefreshChrome();
    }

    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_session is null)
        {
            SaveOptions();
            return;
        }

        e.Cancel = true;
        if (_closing)
            return;

        _closing = true;
        var run = _runTask;
        await StopAsync();
        if (run is { IsCompleted: false })
        {
            _closing = false;
            RefreshChrome();
            return;
        }
        if (run is not null)
        {
            try { await run; }
            catch (Exception ex) { AppendLog(ex.Message); }
        }
        BeginInvoke(Close);
    }

    private static bool IsQuery(string key) => key is "help" or "version" || key.StartsWith("list-", StringComparison.Ordinal);
}
