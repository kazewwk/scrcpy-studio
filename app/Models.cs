using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ScrcpyStudio;

internal abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raises a change for one property, or for every property when the name is empty.</summary>
    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed class DeviceItem
{
    public required string Serial { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public string State { get; init; } = "";
    public string Tone { get; init; } = "none";
    public Geometry? Icon { get; init; }

    public override string ToString() => Title;
}

/// <summary>One catalogue entry with its switch, value and expanded state.</summary>
internal sealed class OptionItem : Observable
{
    private static readonly HashSet<string> Pickers =
        ["record", "ADB", "SCRCPY_ICON_DIR", "SCRCPY_SERVER_PATH", "background-color"];

    private readonly Studio _studio;
    private string _text;
    private bool _expanded;
    private bool _showGroup;

    internal OptionItem(Studio studio, Option option)
    {
        _studio = studio;
        Option = option;
        _text = studio.Config.GetValueOrDefault(option.Key) ?? "";
    }

    internal Option Option { get; }
    public string Key => Option.Key;
    public string Title => Option.Title;
    public string Group => Option.Group;
    public string FlagText => Option.Alias is null ? Option.Flag : $"{Option.Flag}   {Option.Alias}";
    public string Syntax => Option.Syntax;
    public string Description => Option.Description;
    public string Hint => Option.Placeholder ?? Option switch
    {
        { Environment: true } => "变量值",
        { Choices.Length: > 0 } => "选择或输入",
        { Argument: "value" or null } => "输入值",
        _ => Option.Argument ?? "",
    };
    public string[] Choices => Option.Choices;
    public bool HasChoices => Option.Argument is not null && Option.Choices.Length > 0;
    public bool HasText => Option.Argument is not null && Option.Choices.Length == 0;
    public bool HasPicker => Pickers.Contains(Key);
    public bool IsColor => Key == "background-color";
    public bool IsQuery => Studio.IsQuery(Key);
    public bool IsLinux => Option.Platform is not null;
    public string SwitchTip => Option.Environment ? "覆盖此环境变量" : "启用此参数";

    public string? Note => Key switch
    {
        "pause-on-exit" => "会话暂停时，可点击底部的「结束暂停」继续退出。",
        _ when Option.Platform is not null => $"此选项需要 {Option.Platform} 客户端，在 Windows 上无法启动。",
        _ when Option.Optional => "参数值可以留空：留空时只传递开关本身。",
        _ => null,
    };

    public bool IsOn
    {
        get => _studio.Config.ContainsKey(Key);
        set
        {
            if (value != IsOn)
                Commit(value);
        }
    }

    public string Text
    {
        get => _text;
        set
        {
            value ??= "";
            if (_text == value)
                return;
            _text = value;
            // Typing a value is taken as the wish to use it.
            Commit(IsOn || value.Length > 0);
        }
    }

    /// <summary>True when a mandatory value is still blank, which scrcpy will reject for most options.</summary>
    public bool IsBlank => IsOn && Option.Argument is not null && !Option.Optional && _text.Length == 0;

    public bool IsExpanded
    {
        get => _expanded;
        set
        {
            if (_expanded == value)
                return;
            _expanded = value;
            Raise();
        }
    }

    public bool ShowGroup
    {
        get => _showGroup;
        set
        {
            if (_showGroup == value)
                return;
            _showGroup = value;
            Raise();
        }
    }

    public override string ToString() => Title;

    internal void Load(bool on, string? value = null)
    {
        if (value is not null)
            _text = value;
        Commit(on);
    }

    private void Commit(bool on)
    {
        if (on)
            _studio.Config[Key] = Option.Argument is null || (Option.Optional && _text.Length == 0) ? null : _text;
        else
            _studio.Config.Remove(Key);
        Raise("");
        _studio.OnConfigChanged(Key);
    }
}
