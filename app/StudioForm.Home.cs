namespace ScrcpyStudio;

internal sealed partial class StudioForm
{
    private sealed record Device(string Serial, string Detail)
    {
        public override string ToString() => Detail;
    }

    private readonly ComboBox _devices = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill,
        Margin = new Padding(0, 3, 10, 0), AccessibleName = "Android 设备",
    };
    private readonly Button _refresh = ActionButton("刷新设备");
    private readonly Button _connect = ActionButton("连接", 74);
    private readonly Button _disconnect = ActionButton("断开", 74);
    private readonly Button _pair = ActionButton("配对", 74);
    private readonly Label _deviceHint = Caption("", 9, subtle: true);
    private readonly Label _adbNote = Caption("", 9, subtle: true);
    private readonly List<Device> _foundDevices = [];
    private readonly List<Button> _queryButtons = [];
    private bool _adbBusy;

    private static TextBox Input(string label, string placeholder) => new()
    {
        AccessibleName = label, PlaceholderText = placeholder, Dock = DockStyle.Fill,
        Margin = new Padding(0, 4, 10, 0), BorderStyle = BorderStyle.FixedSingle,
    };

    private static TableLayoutPanel Card(string heading, int nRows, int height)
    {
        var card = Table(1, nRows);
        card.Height = height;
        card.BackColor = Color.White;
        card.Padding = new Padding(16, 12, 16, 8);
        FixedRow(card, 31);
        card.Controls.Add(Caption(heading, 13, bold: true), 0, 0);
        return card;
    }

    private static Control Cards(params Control[] cards)
    {
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var stack = Table(1, cards.Length);
        stack.Dock = DockStyle.Top;
        stack.AutoSize = true;
        stack.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FillColumn(stack);
        for (int i = 0; i < cards.Length; i++)
        {
            FixedRow(stack, cards[i].Height + 10);
            cards[i].Margin = new Padding(0, 0, 0, 10);
            stack.Controls.Add(cards[i], 0, i);
        }
        scroll.Controls.Add(stack);
        return scroll;
    }

    private Control BuildHome()
    {
        var device = Card("Android 设备", 3, 135);
        FixedRow(device, 43);
        FillRow(device);
        var pick = Table(2, 1);
        FillColumn(pick);
        FixedColumn(pick, 99);
        FillRow(pick);
        pick.Controls.Add(_devices, 0, 0);
        pick.Controls.Add(_refresh, 1, 0);
        device.Controls.Add(pick, 0, 1);
        _deviceHint.Dock = DockStyle.Fill;
        _deviceHint.AutoSize = false;
        _deviceHint.AutoEllipsis = true;
        device.Controls.Add(_deviceHint, 0, 2);

        _devices.SelectionChangeCommitted += (_, _) =>
        {
            if (_devices.SelectedItem is not Device choice)
                return;
            foreach (string selector in LaunchPlan.Selectors)
                _config.Remove(selector);
            if (choice.Serial.Length > 0)
                _config["serial"] = choice.Serial;
            ShowDevices();
            RefreshPreview();
        };
        _refresh.Click += async (_, _) => await RefreshDevicesAsync();

        var network = Card("无线调试", 5, 200);
        FixedRow(network, 32);
        FixedRow(network, 43);
        FixedRow(network, 43);
        FillRow(network);
        var wirelessHelp = Caption("在手机的无线调试页面查看配对地址和连接地址。两者通常使用不同端口。", 9, subtle: true);
        wirelessHelp.MaximumSize = new Size(600, 0);
        network.Controls.Add(wirelessHelp, 0, 1);

        var pairRow = Table(3, 1);
        FillColumn(pairRow, 65);
        FillColumn(pairRow, 35);
        FixedColumn(pairRow, 75);
        FillRow(pairRow);
        var pairAddress = Input("配对地址", "配对地址 IP:端口");
        var pin = Input("配对码", "六位配对码");
        pin.UseSystemPasswordChar = true;
        pin.MaxLength = 6;
        pairRow.Controls.Add(pairAddress, 0, 0);
        pairRow.Controls.Add(pin, 1, 0);
        pairRow.Controls.Add(_pair, 2, 0);
        network.Controls.Add(pairRow, 0, 2);

        var connectRow = Table(3, 1);
        FillColumn(connectRow);
        FixedColumn(connectRow, 82);
        FixedColumn(connectRow, 75);
        FillRow(connectRow);
        var connectAddress = Input("连接地址", "连接地址 IP:端口");
        connectRow.Controls.Add(connectAddress, 0, 0);
        connectRow.Controls.Add(_connect, 1, 0);
        connectRow.Controls.Add(_disconnect, 2, 0);
        network.Controls.Add(connectRow, 0, 3);
        _adbNote.AutoSize = false;
        _adbNote.AutoEllipsis = true;
        _adbNote.Dock = DockStyle.Fill;
        network.Controls.Add(_adbNote, 0, 4);

        _pair.Click += async (_, _) =>
        {
            string address = pairAddress.Text.Trim();
            if (address.Length == 0 || pin.Text.Length != 6 || !pin.Text.All(char.IsAsciiDigit))
            {
                _adbNote.Text = "请输入配对地址和六位数字配对码。";
                return;
            }
            string code = pin.Text;
            pin.Clear();
            await RunAdbOperationAsync("pair", address, code);
        };
        _connect.Click += async (_, _) => await ConnectAction("connect");
        _disconnect.Click += async (_, _) => await ConnectAction("disconnect");

        async Task ConnectAction(string operation)
        {
            string address = connectAddress.Text.Trim();
            if (address.Length == 0)
            {
                _adbNote.Text = "请输入手机显示的无线连接地址。";
                connectAddress.Focus();
                return;
            }
            await RunAdbOperationAsync(operation, address);
        }

        var shortcuts = Card("常用设置", 2, 90);
        FillRow(shortcuts);
        var configure = Row();
        var links = new (string Name, string Key)[]
        {
            ("画质与帧率", "max-size"), ("相机投屏", "video-source"), ("虚拟显示", "new-display"),
            ("启用 OTG", "otg"), ("录制文件", "record"), ("USB 转无线", "tcpip"),
        };
        foreach ((string name, string key) in links)
        {
            var button = ActionButton(name, 91);
            button.Click += (_, _) => ShowSection(Catalog.ByKey[key].Group, key);
            configure.Controls.Add(button);
        }
        shortcuts.Controls.Add(configure, 0, 1);

        var queries = Card("查询设备能力", 2, 90);
        FillRow(queries);
        var queryRow = Row();
        var entries = new (string Name, string Key)[]
        {
            ("显示器", "list-displays"), ("已安装应用", "list-apps"), ("编码器", "list-encoders"),
            ("相机", "list-cameras"), ("相机分辨率", "list-camera-sizes"),
        };
        foreach (var entry in entries)
        {
            var button = ActionButton(entry.Name, 91);
            button.Click += async (_, _) => await LaunchAsync(entry.Key);
            _queryButtons.Add(button);
            queryRow.Controls.Add(button);
        }
        queries.Controls.Add(queryRow, 0, 1);
        return Cards(device, network, shortcuts, queries);
    }

    private void UpdateAdbButtons()
    {
        bool adb = _backend is not null && !_adbBusy && !_closing;
        _refresh.Enabled = adb;
        _connect.Enabled = adb;
        _disconnect.Enabled = adb;
        _pair.Enabled = adb;
        foreach (Button button in _queryButtons)
            button.Enabled = _backend is not null && _session is null && !_closing;
    }

    private void ShowDevices()
    {
        string current = _config.GetValueOrDefault("serial") ?? "";
        _devices.BeginUpdate();
        _devices.Items.Clear();
        _devices.Items.Add(new Device("", "按启动参数自动选择"));
        _devices.Items.AddRange(_foundDevices.ToArray());
        if (current.Length > 0 && !_foundDevices.Any(x => x.Serial == current))
            _devices.Items.Add(new Device(current, $"{current}  ·  已配置"));
        for (int i = 0; i < _devices.Items.Count; i++)
        {
            if (_devices.Items[i] is Device entry && entry.Serial == current)
            {
                _devices.SelectedIndex = i;
                break;
            }
        }
        _devices.EndUpdate();

        var selectors = LaunchPlan.Selectors.Where(_config.ContainsKey)
            .Select(k => new LaunchPlan("", new Dictionary<string, string?> { [k] = _config[k] }).Arguments[0])
            .ToArray();
        if (selectors.Length > 0)
            _deviceHint.Text = $"当前连接参数  {string.Join("  ", selectors)}";
        else if (_foundDevices.Count == 0)
            _deviceHint.Text = "USB 投屏需开启 USB 调试，并在手机上确认电脑的调试授权。";
        else
            _deviceHint.Text = $"ADB 已发现 {_foundDevices.Count} 台设备。请在手机上确认待授权的连接。";
    }

    private async Task RefreshDevicesAsync()
    {
        if (_backend is null || _adbBusy)
            return;
        _adbBusy = true;
        UpdateAdbButtons();
        try { await ReadDevicesAsync(); }
        catch (Exception ex) { Report("读取 ADB 设备失败", ex.Message); }
        finally
        {
            _adbBusy = false;
            if (!IsDisposed)
                UpdateAdbButtons();
        }
    }

    private async Task ReadDevicesAsync()
    {
        (int exit, string output, string error) = await Plan().RunAdbAsync("devices", "-l");
        if (IsDisposed)
            return;

        _foundDevices.Clear();
        foreach (string line in output.Split('\n'))
        {
            string[] parts = line.Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0] == "List" || parts[0] == "*" || exit != 0)
                continue;
            string state = parts[1] switch
            {
                "device" => "已授权",
                "unauthorized" => "待授权",
                "offline" => "离线",
                _ => parts[1],
            };
            string? model = parts.FirstOrDefault(x => x.StartsWith("model:", StringComparison.Ordinal))?[6..];
            string title = model is null ? parts[0] : $"{model.Replace('_', ' ')}  ·  {parts[0]}";
            _foundDevices.Add(new Device(parts[0], $"{title}  ·  {state}"));
        }

        ShowDevices();
        AppendLog("adb devices -l");
        AppendLog(output);
        if (error.Length > 0)
            AppendLog(error);
        if (exit != 0)
            Report("ADB 返回错误，详情见运行日志");
    }

    private async Task RunAdbOperationAsync(params string[] arguments)
    {
        if (_backend is null || _adbBusy)
            return;
        _adbBusy = true;
        UpdateAdbButtons();
        try
        {
            _adbNote.Text = "正在等待 ADB…";
            (int exit, string stdout, string stderr) = await Plan().RunAdbAsync(arguments);
            if (IsDisposed)
                return;
            string output = string.Join(Environment.NewLine, new[] { stdout, stderr }.Where(s => s.Length > 0));
            _adbNote.Text = output.Length > 0 ? output.ReplaceLineEndings(" ") : $"ADB 退出代码 {exit}";
            AppendLog($"adb {arguments[0]} {arguments[1]}");
            AppendLog(output);
            await ReadDevicesAsync();
        }
        catch (Exception ex)
        {
            Report("无线调试命令失败", ex.Message);
            if (!IsDisposed)
                _adbNote.Text = ex.Message;
        }
        finally
        {
            _adbBusy = false;
            if (!IsDisposed)
                UpdateAdbButtons();
        }
    }

    private readonly List<Button> _actionButtons = [];

    private Control BuildActions()
    {
        Control MakeActionCard(string name, int height, (char Code, string Text, string Hint)[] actions)
        {
            var card = Card(name, 2, height);
            FillRow(card);
            var buttons = Row();
            buttons.WrapContents = true;
            foreach ((char code, string text, string hint) in actions)
            {
                var button = ActionButton(text, 106);
                _tips.SetToolTip(button, hint);
                button.Click += async (_, _) =>
                {
                    if (_session is { } session && !await session.SendAsync((byte)code) && _session == session)
                        Report("会话控制暂不可用。请确认投屏窗口已经打开。");
                };
                buttons.Controls.Add(button);
                _actionButtons.Add(button);
            }
            card.Controls.Add(buttons, 0, 1);
            return card;
        }

        var device = MakeActionCard("手机控制", 176,
        [
            ('h', "主页", "MOD+h"), ('b', "返回", "MOD+b"), ('s', "最近任务", "MOD+s"),
            ('m', "菜单", "MOD+m"), ('p', "电源键", "MOD+p"), ('o', "手机熄屏", "MOD+o"),
            ('O', "点亮手机", "MOD+Shift+o"), ('n', "通知栏", "MOD+n"), ('j', "快捷设置", "MOD+n 两次"),
            ('N', "收起面板", "MOD+Shift+n"), ('r', "旋转设备", "MOD+r"),
            ('+', "音量＋ / 变焦", "MOD+↑"), ('-', "音量－ / 缩焦", "MOD+↓"),
            ('k', "实体键盘设置", "MOD+k"),
        ]);
        var display = MakeActionCard("投屏画面", 176,
        [
            ('f', "切换全屏", "F11 或 MOD+f"), ('[', "左转 90°", "MOD+←"), (']', "右转 90°", "MOD+→"),
            ('{', "水平翻转", "MOD+Shift+←"), ('}', "垂直翻转", "MOD+Shift+↓"), ('z', "暂停画面", "MOD+z"),
            ('Z', "恢复画面", "MOD+Shift+z"), ('R', "重置视频", "MOD+Shift+r"), ('g', "1∶1 像素", "MOD+g"),
            ('w', "去除黑边", "MOD+w"), ('i', "帧率统计", "MOD+i"),
        ]);
        var extras = MakeActionCard("剪贴板与相机", 134,
        [
            ('c', "复制设备文本", "MOD+c"), ('x', "剪切设备文本", "MOD+x"),
            ('v', "同步并粘贴", "MOD+v"), ('V', "注入电脑文本", "MOD+Shift+v"),
            ('t', "打开手电筒", "MOD+t，相机模式"), ('T', "关闭手电筒", "MOD+Shift+t，相机模式"),
        ]);
        var tips = Card("投屏窗口操作", 2, 119);
        FillRow(tips);
        var note = Caption("打开投屏后可使用上方按钮。将文件拖入投屏窗口可推送文件或安装 APK。\n按住 Ctrl 拖动可双指缩放和旋转，按住 Shift 拖动可模拟双指滑动。", 9, subtle: true);
        note.Dock = DockStyle.Fill;
        note.AutoSize = false;
        tips.Controls.Add(note, 0, 1);
        return Cards(device, display, extras, tips);
    }

    private void UpdateActionState()
    {
        foreach (Button button in _actionButtons)
            button.Enabled = _interactiveSession && _actionsReady && _session is not null && !_stopping;
    }
}
