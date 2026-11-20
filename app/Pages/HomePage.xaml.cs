using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ScrcpyStudio;

internal sealed record Shortcut(string Text, string Key, Geometry? Icon)
{
    public override string ToString() => Text;
}

internal sealed partial class HomePage : UserControl
{
    private readonly Studio _studio;

    internal HomePage(Studio studio)
    {
        InitializeComponent();
        DataContext = _studio = studio;

        Shortcuts.ItemsSource = new Shortcut[]
        {
            new("画质与帧率", "max-size", Ui.Glyph("Gauge")),
            new("相机投屏", "video-source", Ui.Glyph("Camera")),
            new("虚拟显示", "new-display", Ui.Glyph("MonitorUp")),
            new("启用 OTG", "otg", Ui.Glyph("Usb")),
            new("录制文件", "record", Ui.Glyph("CircleDot")),
            new("USB 转无线", "tcpip", Ui.Glyph("Wifi")),
        };
        Queries.ItemsSource = new Shortcut[]
        {
            new("显示器", "list-displays", Ui.Glyph("Monitor")),
            new("已安装应用", "list-apps", Ui.Glyph("LayoutGrid")),
            new("编码器", "list-encoders", Ui.Glyph("Cpu")),
            new("相机", "list-cameras", Ui.Glyph("Camera")),
            new("相机分辨率", "list-camera-sizes", Ui.Glyph("Scan")),
        };
    }

    private static Shortcut Entry(object sender) => (Shortcut)((FrameworkElement)sender).DataContext;

    private async void OnRefresh(object sender, RoutedEventArgs e) => await _studio.RefreshDevicesAsync();

    private void OnShortcut(object sender, RoutedEventArgs e)
    {
        string key = Entry(sender).Key;
        _studio.Go(Catalog.ByKey[key].Group, key);
    }

    private async void OnQuery(object sender, RoutedEventArgs e) => await _studio.LaunchAsync(Entry(sender).Key);

    private async void OnPair(object sender, RoutedEventArgs e)
    {
        string address = PairAddress.Text.Trim();
        string code = PairCode.Password;
        if (address.Length == 0 || code.Length != 6 || !code.All(char.IsAsciiDigit))
        {
            _studio.SetAdbNote("请输入配对地址和六位数字配对码。");
            return;
        }
        PairCode.Clear();
        await _studio.RunAdbOperationAsync("pair", address, code);
    }

    private async void OnConnect(object sender, RoutedEventArgs e) => await ConnectAsync("connect");

    private async void OnDisconnect(object sender, RoutedEventArgs e) => await ConnectAsync("disconnect");

    private async Task ConnectAsync(string operation)
    {
        string address = ConnectAddress.Text.Trim();
        if (address.Length == 0)
        {
            _studio.SetAdbNote("请输入手机显示的无线连接地址。");
            ConnectAddress.Focus();
            return;
        }
        await _studio.RunAdbOperationAsync(operation, address);
    }
}
