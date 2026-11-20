namespace ScrcpyStudio;

internal sealed partial class StudioForm
{
    private readonly TextBox _search = Input("搜索参数", "搜索全部选项，输入名称、参数或缩写");
    private readonly Label _matchCount = Caption("", 9, subtle: true);
    private readonly ListView _optionList = new()
    {
        View = View.Details, Dock = DockStyle.Fill, FullRowSelect = true, MultiSelect = false,
        HideSelection = false, HeaderStyle = ColumnHeaderStyle.Nonclickable,
        UseCompatibleStateImageBehavior = false, ShowItemToolTips = true,
        BorderStyle = BorderStyle.None, AccessibleName = "scrcpy 参数列表",
    };
    private readonly Panel _editor = new()
    {
        Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(14, 12, 14, 12),
    };
    private readonly Dictionary<string, ListViewItem> _shownOptions = new();

    private Control BuildOptions()
    {
        var layout = Table(1, 2);
        FixedRow(layout, 49);
        FillRow(layout);
        var searchRow = Table(2, 1);
        FillColumn(searchRow);
        FixedColumn(searchRow, 166);
        FillRow(searchRow);
        searchRow.Controls.Add(_search, 0, 0);
        _matchCount.Margin = new Padding(3, 8, 0, 0);
        searchRow.Controls.Add(_matchCount, 1, 0);
        layout.Controls.Add(searchRow, 0, 0);

        var split = Table(2, 1);
        FillColumn(split, 55);
        FillColumn(split, 45);
        FillRow(split);
        layout.Controls.Add(split, 0, 1);
        var listCard = new Panel
        {
            Dock = DockStyle.Fill, Padding = new Padding(8, 8, 8, 4),
            BackColor = Color.White, Margin = new Padding(0, 0, 10, 0),
        };
        _optionList.Columns.Add("功能", 177);
        _optionList.Columns.Add("参数", 185);
        _optionList.Columns.Add("当前值", 130);
        _optionList.ClientSizeChanged += (_, _) =>
        {
            int width = Math.Max(290, _optionList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 2);
            _optionList.Columns[0].Width = width * 38 / 100;
            _optionList.Columns[1].Width = width * 45 / 100;
            _optionList.Columns[2].Width = width * 17 / 100;
        };
        listCard.Controls.Add(_optionList);
        split.Controls.Add(listCard, 0, 0);
        _editor.Margin = Padding.Empty;
        split.Controls.Add(_editor, 1, 0);

        _search.TextChanged += (_, _) => PopulateOptions();
        _optionList.SelectedIndexChanged += (_, _) => ShowSelectedOption();
        return layout;
    }

    private void PopulateOptions(string? focus = null)
    {
        string term = _search.Text.Trim();
        _heading.Text = term.Length == 0 ? _section : "参数搜索";
        var matches = term.Length == 0
            ? Catalog.Options.Where(o => o.Group == _section)
            : Catalog.Options.Where(o =>
            {
                string text = $"{o.Title} {o.Flag} {o.Alias} {o.Description} {o.Group}";
                return text.Contains(term, StringComparison.OrdinalIgnoreCase);
            });

        _optionList.BeginUpdate();
        _optionList.Items.Clear();
        _shownOptions.Clear();
        foreach (Option option in matches)
        {
            string flagged = option.Alias is null ? option.Flag : $"{option.Flag}  {option.Alias}";
            var item = new ListViewItem([option.Title, flagged, FormatValue(option)])
            {
                Tag = option, ToolTipText = $"{option.Title}\n{option.Syntax}",
            };
            _optionList.Items.Add(item);
            _shownOptions.Add(option.Key, item);
        }

        if (focus is not null && _shownOptions.TryGetValue(focus, out ListViewItem? selected))
        {
            selected.Selected = true;
            selected.EnsureVisible();
        }
        else if (_optionList.Items.Count > 0)
        {
            _optionList.Items[0].Selected = true;
        }
        _optionList.EndUpdate();
        _matchCount.Text = $"{_optionList.Items.Count} 项";
        ShowSelectedOption();
    }

    private string FormatValue(Option option)
    {
        if (!_config.TryGetValue(option.Key, out string? value))
            return "默认";
        if (value is null)
            return "已启用";
        return value.Length == 0 ? "空字符串" : value;
    }

    private void SetOption(Option option, bool enabled, string? value)
    {
        PutOption(option.Key, enabled, value);
        if (_shownOptions.TryGetValue(option.Key, out ListViewItem? item))
            item.SubItems[2].Text = FormatValue(option);
    }

    private void ShowSelectedOption()
    {
        _editor.SuspendLayout();
        while (_editor.Controls.Count > 0)
            _editor.Controls[0].Dispose();

        if (_optionList.SelectedItems.Count == 0 || _optionList.SelectedItems[0].Tag is not Option option)
        {
            _editor.Controls.Add(Caption("没有匹配的选项", 12, bold: true));
            _editor.ResumeLayout();
            return;
        }

        var layout = Table(1, 8);
        FixedRow(layout, 47);
        FixedRow(layout, 41);
        FixedRow(layout, 30);
        FixedRow(layout, option.Optional ? 27 : 0);
        FixedRow(layout, option.Argument is null ? 0 : 41);
        FixedRow(layout, 35);
        FixedRow(layout, 24);
        FillRow(layout);
        _editor.Controls.Add(layout);

        var title = Caption(option.Title, 14, bold: true);
        title.Dock = DockStyle.Fill;
        title.AutoSize = false;
        title.AutoEllipsis = true;
        layout.Controls.Add(title, 0, 0);

        var syntax = new TextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, Margin = Padding.Empty,
            BackColor = Color.White, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9F),
            Text = option.Syntax + (option.Alias is null ? "" : $"  ({option.Alias})"),
        };
        layout.Controls.Add(syntax, 0, 1);

        bool active = _config.TryGetValue(option.Key, out string? current);
        var enabled = new CheckBox
        {
            Text = option.Environment ? "覆盖此变量" : "启用此参数",
            AutoSize = true, Margin = Padding.Empty, Checked = active,
        };
        layout.Controls.Add(enabled, 0, 2);
        var optionalValue = new CheckBox
        {
            Text = "提供参数值", AutoSize = true, Margin = Padding.Empty,
            Enabled = active, Checked = current is not null,
        };
        if (option.Optional)
            layout.Controls.Add(optionalValue, 0, 3);

        Control? entry = null;
        Button? browse = null;
        if (option.Argument is not null)
        {
            var line = Table(2, 1);
            FillColumn(line);
            bool hasPicker = option.Key is "record" or "ADB" or "SCRCPY_ICON_DIR" or "SCRCPY_SERVER_PATH" or "background-color";
            FixedColumn(line, hasPicker ? 80 : 0);
            FillRow(line);
            if (option.Choices.Length == 0)
            {
                entry = Input(option.Title, option.Placeholder ?? "");
            }
            else
            {
                var choices = new ComboBox
                {
                    Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown,
                    Margin = new Padding(0, 3, 8, 0), AccessibleName = option.Title,
                };
                choices.Items.AddRange(option.Choices);
                entry = choices;
            }
            entry.Text = current ?? "";
            line.Controls.Add(entry, 0, 0);
            if (hasPicker)
            {
                browse = ActionButton("选择…", 76);
                browse.Height = 31;
                browse.Click += (_, _) => PickValue(option, entry);
                line.Controls.Add(browse, 1, 0);
            }
            layout.Controls.Add(line, 0, 4);
        }

        void UpdateEntry()
        {
            optionalValue.Enabled = enabled.Checked;
            bool editable = enabled.Checked && (!option.Optional || optionalValue.Checked);
            if (entry is not null)
                entry.Enabled = editable;
            if (browse is not null)
                browse.Enabled = editable;
        }

        void Apply()
        {
            UpdateEntry();
            string? value = option.Optional && !optionalValue.Checked ? null : entry?.Text;
            SetOption(option, enabled.Checked, value);
        }

        enabled.CheckedChanged += (_, _) => Apply();
        optionalValue.CheckedChanged += (_, _) => Apply();
        if (entry is not null)
            entry.TextChanged += (_, _) => Apply();
        UpdateEntry();

        if (option.Platform is not null)
        {
            var platform = Caption($"此选项需要 {option.Platform} 客户端。", 9, bold: true);
            platform.ForeColor = Color.FromArgb(158, 77, 13);
            layout.Controls.Add(platform, 0, 5);
        }
        else if (IsQuery(option.Key))
        {
            var run = ActionButton("立即查询", 98);
            run.Height = 30;
            run.Enabled = _backend is not null && _session is null;
            run.Click += async (_, _) => await LaunchAsync(option.Key);
            layout.Controls.Add(run, 0, 5);
        }
        else if (option.Key == "pause-on-exit")
        {
            var hint = Caption("会话暂停时可点击顶部的「结束暂停」。", 8, subtle: true);
            hint.Dock = DockStyle.Fill;
            hint.AutoSize = false;
            layout.Controls.Add(hint, 0, 5);
        }

        layout.Controls.Add(Caption("上游参数说明", 9, bold: true, subtle: true), 0, 6);
        var documentation = new TextBox
        {
            Text = option.Description.ReplaceLineEndings(Environment.NewLine),
            Dock = DockStyle.Fill, Margin = Padding.Empty, ReadOnly = true, Multiline = true,
            BackColor = Canvas, ForeColor = Ink, ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle, Font = new Font(UiFace, 9F),
        };
        layout.Controls.Add(documentation, 0, 7);
        _editor.ResumeLayout();
    }

    private void PickValue(Option option, Control field)
    {
        if (option.Key == "background-color")
        {
            using var color = new ColorDialog { FullOpen = true };
            if (color.ShowDialog(this) == DialogResult.OK)
                field.Text = $"#{color.Color.R:x2}{color.Color.G:x2}{color.Color.B:x2}";
            return;
        }
        if (option.Key == "SCRCPY_ICON_DIR")
        {
            using var folder = new FolderBrowserDialog { SelectedPath = field.Text, Description = option.Title };
            if (folder.ShowDialog(this) == DialogResult.OK)
                field.Text = folder.SelectedPath;
            return;
        }
        if (option.Key == "record")
        {
            string format = _config.GetValueOrDefault("record-format") is { Length: > 0 } f ? f : "mp4";
            using var save = new SaveFileDialog
            {
                Title = option.Title, DefaultExt = format, AddExtension = true,
                Filter = "媒体文件|*.mp4;*.mkv;*.m4a;*.mka;*.opus;*.aac;*.flac;*.wav|所有文件|*.*",
                FileName = field.Text,
            };
            if (save.ShowDialog(this) == DialogResult.OK)
                field.Text = save.FileName;
            return;
        }

        using var open = new OpenFileDialog
        {
            Title = option.Title, FileName = field.Text,
            Filter = option.Key == "ADB" ? "ADB 可执行文件|*.exe|所有文件|*.*" : "所有文件|*.*",
        };
        if (open.ShowDialog(this) == DialogResult.OK)
            field.Text = open.FileName;
    }
}
