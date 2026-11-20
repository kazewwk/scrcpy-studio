using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScrcpyStudio;

internal sealed record SessionAction(char Code, string Text, string Hint, Geometry? Icon)
{
    public override string ToString() => Text;
}

internal sealed partial class ControlPage : UserControl
{
    private readonly Studio _studio;

    internal ControlPage(Studio studio)
    {
        InitializeComponent();
        DataContext = _studio = studio;

        DeviceActions.ItemsSource = new SessionAction[]
        {
            new('h', "主页", "MOD+h", Ui.Glyph("House")),
            new('b', "返回", "MOD+b", Ui.Glyph("ArrowLeft")),
            new('s', "最近任务", "MOD+s", Ui.Glyph("Layers")),
            new('m', "菜单", "MOD+m", Ui.Glyph("Menu")),
            new('p', "电源键", "MOD+p", Ui.Glyph("Power")),
            new('o', "手机熄屏", "MOD+o", Ui.Glyph("EyeOff")),
            new('O', "点亮手机", "MOD+Shift+o", Ui.Glyph("Sun")),
            new('n', "通知栏", "MOD+n", Ui.Glyph("Bell")),
            new('j', "快捷设置", "MOD+n 两次", Ui.Glyph("SlidersHorizontal")),
            new('N', "收起面板", "MOD+Shift+n", Ui.Glyph("ChevronsUp")),
            new('r', "旋转设备", "MOD+r", Ui.Glyph("RotateCw")),
            new('+', "音量＋ / 变焦", "MOD+↑", Ui.Glyph("Volume2")),
            new('-', "音量－ / 缩焦", "MOD+↓", Ui.Glyph("Volume1")),
            new('k', "实体键盘设置", "MOD+k", Ui.Glyph("Keyboard")),
        };
        DisplayActions.ItemsSource = new SessionAction[]
        {
            new('f', "切换全屏", "F11 / MOD+f", Ui.Glyph("Maximize")),
            new('[', "左转 90°", "MOD+←", Ui.Glyph("RotateCcw")),
            new(']', "右转 90°", "MOD+→", Ui.Glyph("RotateCw")),
            new('{', "水平翻转", "MOD+Shift+←", Ui.Glyph("CaretsToAxisDashedVertical")),
            new('}', "垂直翻转", "MOD+Shift+↓", Ui.Glyph("CaretsToAxisDashedHorizontal")),
            new('z', "暂停画面", "MOD+z", Ui.Glyph("Pause")),
            new('Z', "恢复画面", "MOD+Shift+z", Ui.Glyph("Play")),
            new('R', "重置视频", "MOD+Shift+r", Ui.Glyph("RefreshCw")),
            new('g', "1∶1 像素", "MOD+g", Ui.Glyph("Scan")),
            new('w', "去除黑边", "MOD+w", Ui.Glyph("Crop")),
            new('i', "帧率统计", "MOD+i", Ui.Glyph("Gauge")),
        };
        ExtraActions.ItemsSource = new SessionAction[]
        {
            new('c', "复制设备文本", "MOD+c", Ui.Glyph("Copy")),
            new('x', "剪切设备文本", "MOD+x", Ui.Glyph("Scissors")),
            new('v', "同步并粘贴", "MOD+v", Ui.Glyph("ClipboardPaste")),
            new('V', "注入电脑文本", "MOD+Shift+v", Ui.Glyph("TextCursorInput")),
            new('t', "打开手电筒", "MOD+t · 相机", Ui.Glyph("Flashlight")),
            new('T', "关闭手电筒", "MOD+Shift+t · 相机", Ui.Glyph("FlashlightOff")),
        };
    }

    private async void OnAction(object sender, RoutedEventArgs e)
    {
        var action = (SessionAction)((FrameworkElement)sender).DataContext;
        await _studio.SendAsync(action.Code);
    }
}
