using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Threading;

namespace ScrcpyStudio;

/// <summary>Application state and the scrcpy / ADB operations behind the window.</summary>
internal sealed class Studio : Observable
{
    private const int LogLimit = 4000;

    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly List<DeviceItem> _found = [];
    private readonly List<string> _log = [];
    private readonly Dictionary<string, OptionItem> _byKey;
    private readonly bool _volatile;

    private string _status = "正在准备内置组件…";
    private string _tone = "busy";
    private string _adbNote = "";
    private string? _backend;
    private DeviceItem? _selected;
    private BackendSession? _session;
    private Task<int>? _runTask;
    private bool _interactive;
    private bool _actionsReady;
    private bool _stopping;
    private bool _forceAvailable;
    private bool _pauseOffered;
    private bool _closing;
    private bool _adbBusy;
    private bool _syncing;
    private bool _demoLive;

    internal Studio(bool demo = false)
    {
        _volatile = demo;
        try
        {
            Config = demo ? new() : RuntimeFiles.LoadSettings();
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            Config = new();
            SettingsError = ex.Message;
        }

        // Optional values are now blank or filled; older settings stored a blank optional value as "".
        string[] blank = Config.Where(x => x.Value == "" && Catalog.ByKey[x.Key].Optional).Select(x => x.Key).ToArray();
        foreach (string key in blank)
            Config[key] = null;

        Options = Catalog.Options.Select(option => new OptionItem(this, option)).ToArray();
        _byKey = Options.ToDictionary(item => item.Key);
        Groups = Catalog.Options.Select(option => option.Group).Distinct().ToArray();
        RebuildDevices();
    }

    internal Dictionary<string, string?> Config { get; }
    internal string? SettingsError { get; }
    internal string[] Groups { get; }
    internal bool Closing => _closing;
    internal bool HasSession => _session is not null;
    internal string LogText => string.Join(Environment.NewLine, _log);

    internal event Action<string>? LogAppended;
    internal event Action? LogCleared;
    internal event Action<string, string?>? Navigate;
    internal event Action? SessionFailed;
    internal event Action? ConfigChanged;

    public IReadOnlyList<OptionItem> Options { get; }
    public ObservableCollection<DeviceItem> Devices { get; } = [];

    public string Status => _status;
    public string Tone => _tone;
    public string Command => Plan().PowerShell();
    public bool CanStart => _backend is not null && _session is null && !_closing && !_demoLive;
    public bool CanStop => _demoLive || (_session is not null && (!_stopping || _forceAvailable));
    public string StopText => _forceAvailable ? "强制结束" : "停止";
    public bool CanResume => _session is not null && _pauseOffered;
    public bool AdbReady => _backend is not null && !_adbBusy && !_closing;
    public bool CanQuery => _backend is not null && _session is null && !_closing;
    public bool CanControl => _demoLive || (_interactive && _actionsReady && _session is not null && !_stopping);
    public string AdbNote => _adbNote;

    public string DeviceTitle => _backend is null && _tone == "busy" ? "正在准备内置组件…"
        : _found.Count == 0 ? "等待设备连接"
        : $"已发现 {_found.Count} 台设备";

    public string DeviceHint
    {
        get
        {
            string[] selectors = LaunchPlan.Selectors.Where(Config.ContainsKey)
                .Select(key => new LaunchPlan("", new Dictionary<string, string?> { [key] = Config[key] }).Arguments[0])
                .ToArray();
            if (selectors.Length > 0)
                return $"当前连接参数  {string.Join("  ", selectors)}";
            if (_found.Count == 0)
                return "USB 投屏需开启 USB 调试，并在手机上确认电脑的调试授权。";
            return _found.Any(device => device.Tone == "warn")
                ? "有设备等待授权，请在手机上确认这台电脑的调试授权。"
                : "点选下方的设备卡片，然后开始投屏。";
        }
    }

    public DeviceItem? SelectedDevice
    {
        get => _selected;
        set
        {
            if (value is null || ReferenceEquals(value, _selected) || _syncing)
                return;
            _selected = value;
            _syncing = true;
            try
            {
                foreach (string selector in LaunchPlan.Selectors)
                    _byKey[selector].Load(false);
                if (value.Serial.Length > 0)
                    _byKey["serial"].Load(true, value.Serial);
            }
            finally
            {
                _syncing = false;
            }
            // The list is rebuilt after the selection event so a stale saved entry can drop out.
            _ui.BeginInvoke(RebuildDevices);
        }
    }

    internal static bool IsQuery(string key) =>
        key is "help" or "version" || key.StartsWith("list-", StringComparison.Ordinal);

    internal void Go(string section, string? focus = null) => Navigate?.Invoke(section, focus);

    internal void OnConfigChanged(string key)
    {
        Raise(nameof(Command));
        if (!_syncing && LaunchPlan.Selectors.Contains(key))
            RebuildDevices();
        ConfigChanged?.Invoke();
    }

    private LaunchPlan Plan(Dictionary<string, string?>? config = null) => new(_backend ?? "", config ?? Config);

    private void RefreshChrome() => Raise("");

    private void SetStatus(string text, string tone)
    {
        _status = text;
        _tone = tone;
        Raise(nameof(Status));
        Raise(nameof(Tone));
        Raise(nameof(DeviceTitle));
    }

    internal void Report(string text, string? detail = null, string tone = "warn")
    {
        SetStatus(text, tone);
        AppendLog(detail is null ? text : $"{text}。{detail}");
    }

    internal void AppendLog(string line)
    {
        if (!_ui.CheckAccess())
        {
            _ui.BeginInvoke(() => AppendLog(line));
            return;
        }

        if (line == "SCRCPY_STUDIO_READY" && _session is not null)
        {
            _actionsReady = true;
            Raise(nameof(CanControl));
            return;
        }

        foreach (string part in line.ReplaceLineEndings("\n").Split('\n'))
        {
            _log.Add(part);
            LogAppended?.Invoke(part);
        }
        if (_log.Count > LogLimit)
            _log.RemoveRange(0, LogLimit / 4);

        if (_session is not null && line.Contains("Press Enter to continue...", StringComparison.Ordinal))
        {
            _pauseOffered = true;
            Raise(nameof(CanResume));
            if (_stopping)
                _session.ContinueExit();
        }
    }

    internal void ClearLog()
    {
        _log.Clear();
        LogCleared?.Invoke();
    }

    internal void ContinueExit()
    {
        _session?.ContinueExit();
        _pauseOffered = false;
        Raise(nameof(CanResume));
    }

    internal async Task PrepareAsync()
    {
        if (SettingsError is not null)
            AppendLog($"读取上次设置失败。{SettingsError}");
        try
        {
            _backend = await Task.Run(RuntimeFiles.Extract);
            SetStatus("已就绪", "idle");
            RefreshChrome();
            if (!Config.ContainsKey("otg"))
                await RefreshDevicesAsync();
        }
        catch (Exception ex)
        {
            Report("无法准备内置组件", ex.Message, "bad");
        }
    }

    internal void SaveOptions()
    {
        if (_volatile)
            return;
        try
        {
            RuntimeFiles.SaveSettings(Config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Report("设置保存失败", ex.Message, "bad");
        }
    }

    // Devices and ADB

    private void RebuildDevices()
    {
        string current = Config.GetValueOrDefault("serial") ?? "";
        _syncing = true;
        try
        {
            Devices.Clear();
            Devices.Add(new DeviceItem
            {
                Serial = "", Title = "自动选择", Detail = "按启动参数决定设备", Icon = Ui.Glyph("Sparkles"),
            });
            foreach (DeviceItem device in _found)
                Devices.Add(device);
            if (current.Length > 0 && _found.All(device => device.Serial != current))
            {
                Devices.Add(new DeviceItem
                {
                    Serial = current, Title = current, Detail = "来自已保存的设置", State = "已配置", Tone = "accent",
                    Icon = Ui.Glyph(IsNetwork(current) ? "Wifi" : "Usb"),
                });
            }
            _selected = Devices.FirstOrDefault(device => device.Serial == current) ?? Devices[0];
        }
        finally
        {
            _syncing = false;
        }
        Raise(nameof(SelectedDevice));
        Raise(nameof(DeviceTitle));
        Raise(nameof(DeviceHint));
    }

    private static bool IsNetwork(string serial) =>
        serial.Contains(':') || serial.Contains("._adb-tls", StringComparison.Ordinal);

    internal async Task RefreshDevicesAsync()
    {
        if (_backend is null || _adbBusy)
            return;
        _adbBusy = true;
        Raise(nameof(AdbReady));
        try
        {
            await ReadDevicesAsync();
        }
        catch (Exception ex)
        {
            Report("读取 ADB 设备失败", ex.Message, "bad");
        }
        finally
        {
            _adbBusy = false;
            Raise(nameof(AdbReady));
        }
    }

    private async Task ReadDevicesAsync()
    {
        (int exit, string output, string error) = await Plan().RunAdbAsync("devices", "-l");

        _found.Clear();
        foreach (string line in output.Split('\n'))
        {
            string[] parts = line.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0] == "List" || parts[0] == "*" || exit != 0)
                continue;
            (string state, string tone) = parts[1] switch
            {
                "device" => ("已授权", "good"),
                "unauthorized" => ("待授权", "warn"),
                "offline" => ("离线", "muted"),
                _ => (parts[1], "muted"),
            };
            string? model = parts.FirstOrDefault(x => x.StartsWith("model:", StringComparison.Ordinal))?[6..];
            _found.Add(new DeviceItem
            {
                Serial = parts[0], Title = model?.Replace('_', ' ') ?? parts[0], Detail = parts[0],
                State = state, Tone = tone, Icon = Ui.Glyph(IsNetwork(parts[0]) ? "Wifi" : "Usb"),
            });
        }

        RebuildDevices();
        AppendLog("adb devices -l");
        AppendLog(output);
        if (error.Length > 0)
            AppendLog(error);
        if (exit != 0)
            Report("ADB 返回错误，详情见运行日志", tone: "bad");
    }

    internal void SetAdbNote(string text)
    {
        _adbNote = text;
        Raise(nameof(AdbNote));
    }

    internal async Task RunAdbOperationAsync(params string[] arguments)
    {
        if (_backend is null || _adbBusy)
            return;
        _adbBusy = true;
        Raise(nameof(AdbReady));
        try
        {
            SetAdbNote("正在等待 ADB…");
            (int exit, string stdout, string stderr) = await Plan().RunAdbAsync(arguments);
            string output = string.Join(Environment.NewLine, new[] { stdout, stderr }.Where(s => s.Length > 0));
            SetAdbNote(output.Length > 0 ? output.ReplaceLineEndings(" ") : $"ADB 退出代码 {exit}");
            AppendLog($"adb {arguments[0]} {arguments[1]}");
            AppendLog(output);
            await ReadDevicesAsync();
        }
        catch (Exception ex)
        {
            Report("无线调试命令失败", ex.Message, "bad");
            SetAdbNote(ex.Message);
        }
        finally
        {
            _adbBusy = false;
            Raise(nameof(AdbReady));
        }
    }

    // Sessions

    internal async Task LaunchAsync(string? query = null)
    {
        if (_backend is null || _session is not null || _closing)
            return;

        var config = query is null ? Config.ToDictionary() : LaunchPlan.ForQuery(Config, query);
        if (config.Keys.FirstOrDefault(key => Catalog.ByKey[key].Platform == "Linux") is { } linux)
        {
            Report($"{Catalog.ByKey[linux].Flag} 需要 Linux 客户端");
            Navigate?.Invoke(Catalog.ByKey[linux].Group, linux);
            return;
        }

        LaunchPlan plan = Plan(config);
        string? queryFlag = Catalog.Options.FirstOrDefault(o => config.ContainsKey(o.Key) && IsQuery(o.Key))?.Key;
        if (queryFlag is not null)
            Navigate?.Invoke("运行日志", null);

        SaveOptions();
        BackendSession? session = null;
        try
        {
            session = new BackendSession(plan, AppendLog);
            _session = session;
            _interactive = queryFlag is null && !config.ContainsKey("otg") && !config.ContainsKey("no-window");
            _actionsReady = false;
            _stopping = false;
            _forceAvailable = false;
            _pauseOffered = false;
            _status = queryFlag is null ? "scrcpy 会话运行中" : "正在执行查询…";
            _tone = queryFlag is null ? "live" : "busy";
            AppendLog("");
            AppendLog(plan.PowerShell());
            RefreshChrome();

            _runTask = session.RunAsync();
            int exit = await _runTask;
            (string result, string tone) = exit switch
            {
                0 => ("scrcpy 已退出", "idle"),
                2 => ("设备已断开", "warn"),
                _ => ($"scrcpy 退出代码 {exit}，详情见运行日志", "bad"),
            };
            Report(result, tone: tone);
            if (!_closing && exit != 0)
                SessionFailed?.Invoke();
        }
        catch (Exception ex)
        {
            Report("scrcpy 启动或运行失败", ex.Message, "bad");
            if (!_closing)
                Navigate?.Invoke("运行日志", null);
        }
        finally
        {
            _session = null;
            _runTask = null;
            _pauseOffered = false;
            _forceAvailable = false;
            session?.Dispose();
            RefreshChrome();
        }
    }

    internal async Task<bool> SendAsync(char code)
    {
        if (_session is not { } session)
            return false;
        if (await session.SendAsync((byte)code) || _session != session)
            return true;
        Report("会话控制暂不可用。请确认投屏窗口已经打开。");
        return false;
    }

    internal async Task StopAsync(bool allowForce = false)
    {
        if (_session is not { } session || _runTask is not { } run)
            return;
        if (_forceAvailable)
        {
            if (allowForce)
            {
                try
                {
                    session.ForceStop();
                }
                catch (Win32Exception ex)
                {
                    Report("强制结束失败", ex.Message, "bad");
                }
            }
            return;
        }

        _stopping = true;
        _status = "正在结束会话并等待文件写入…";
        _tone = "busy";
        RefreshChrome();
        session.ContinueExit();
        await session.SendAsync(0);

        if (await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(8))) == run || _session != session)
            return;
        _forceAvailable = true;
        Report("scrcpy 尚未退出，强制结束可能影响正在写入的录制");
        RefreshChrome();
    }

    /// <summary>Stops the running session before the window closes. Returns false when it is still alive.</summary>
    internal async Task<bool> CloseAsync()
    {
        _closing = true;
        Task<int>? run = _runTask;
        await StopAsync();
        if (run is { IsCompleted: false })
        {
            _closing = false;
            RefreshChrome();
            return false;
        }
        if (run is not null)
        {
            try
            {
                await run;
            }
            catch (Exception ex)
            {
                AppendLog(ex.Message);
            }
        }
        return true;
    }

    /// <summary>Fills the window with sample state for documentation screenshots and render checks.</summary>
    internal void LoadDemo()
    {
        _backend = @"C:\Users\Studio\AppData\Local\ScrcpyStudio\runtime";
        _found.Add(new DeviceItem
        {
            Serial = "3A151FDJG000XX", Title = "Pixel 8 Pro", Detail = "3A151FDJG000XX",
            State = "已授权", Tone = "good", Icon = Ui.Glyph("Usb"),
        });
        _found.Add(new DeviceItem
        {
            Serial = "192.168.1.23:5555", Title = "Galaxy Tab S9", Detail = "192.168.1.23:5555",
            State = "待授权", Tone = "warn", Icon = Ui.Glyph("Wifi"),
        });
        _byKey["serial"].Load(true, "3A151FDJG000XX");
        _byKey["max-size"].Load(true, "1920");
        _byKey["max-fps"].Load(true, "60");
        _byKey["video-codec"].Load(true, "h265");
        _byKey["stay-awake"].Load(true);
        _byKey["turn-screen-off"].Load(true);
        SetStatus("已就绪", "idle");
        RefreshChrome();

        string[] lines =
        [
            Command,
            "scrcpy 5.0.1 <https://github.com/Genymobile/scrcpy>",
            "INFO: ADB device found:",
            "INFO:     -->   (usb)  3A151FDJG000XX                  device  Pixel_8_Pro",
            "/data/local/tmp/scrcpy-server.jar: 1 file pushed, 0 skipped. 86.3 MB/s (96903 bytes in 0.001s)",
            "[server] INFO: Device: [Google] google Pixel 8 Pro (Android 15)",
            "[server] DEBUG: Using video encoder: 'c2.exynos.hevc.encoder'",
            "INFO: Renderer: direct3d11",
            "INFO: Texture: 1920x864",
            "WARN: Demuxer 'audio': stream explicitly disabled by the device",
            "ERROR: 示例错误行，用于展示日志配色",
            "INFO: Recording complete to mp4 file: demo.mp4",
        ];
        foreach (string line in lines)
            AppendLog(line);
    }

    /// <summary>Shows the sample state as a running session, without starting a process.</summary>
    internal void SetDemoLive(bool live)
    {
        _demoLive = live;
        _status = live ? "scrcpy 会话运行中" : "已就绪";
        _tone = live ? "live" : "idle";
        RefreshChrome();
    }
}
