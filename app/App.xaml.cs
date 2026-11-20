using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ScrcpyStudio;

internal partial class App : Application
{
    private DateTime _lastFault;
    private int _faults;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args is ["--snapshot", var directory, ..])
        {
            // An optional third argument such as 960x600 overrides the 1200x800 layout size.
            double[] size = e.Args.Length > 2 ? Array.ConvertAll(e.Args[2].Split('x'), double.Parse) : [1200, 800];
            Shutdown(await RenderSnapshotsAsync(Path.GetFullPath(directory), size[0], size[1]));
            return;
        }

        DispatcherUnhandledException += OnFault;
        Theme.Apply(Theme.Preferred());
        var studio = new Studio();
        new MainWindow(studio).Show();
        await studio.PrepareAsync();
    }

    private void OnFault(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // One failed handler should not end the session, but a fault that keeps repeating must.
        _faults = DateTime.UtcNow - _lastFault < TimeSpan.FromSeconds(2) ? _faults + 1 : 1;
        if (_faults > 3)
            return;
        e.Handled = true;
        MessageBox.Show(e.Exception.Message, "scrcpy Studio", MessageBoxButton.OK, MessageBoxImage.Error);
        _lastFault = DateTime.UtcNow;
    }

    /// <summary>
    /// Renders every page with sample data in both themes. Used for the README pictures and by CI to
    /// prove that the published EXE can build its whole interface.
    /// </summary>
    private async Task<int> RenderSnapshotsAsync(string directory, double width, double height)
    {
        Directory.CreateDirectory(directory);
        string failure = Path.Combine(directory, "error.txt");
        DispatcherUnhandledException += (_, error) =>
        {
            File.WriteAllText(failure, error.Exception.ToString());
            error.Handled = true;
            Shutdown(1);
        };
        try
        {
            Theme.Apply(dark: true);
            var studio = new Studio(demo: true);
            var window = new MainWindow(studio);
            FrameworkElement root = window.PrepareSnapshot(width, height);
            window.Show();
            studio.LoadDemo();

            (string Name, bool Live, Action Open)[] pages =
            [
                ("home", false, () => window.ShowSection("连接")),
                ("video", false, () => window.ShowSection("视频", "max-size")),
                ("display", false, () => window.ShowSection("显示与窗口")),
                ("search", false, () => window.Find("fps")),
                ("control", true, () => window.ShowSection("会话控制")),
                ("log", true, () => window.ShowSection("运行日志")),
            ];
            foreach (bool dark in new[] { true, false })
            {
                Theme.Apply(dark);
                window.SyncTheme();
                foreach ((string name, bool live, Action open) in pages)
                {
                    studio.SetDemoLive(live);
                    open();
                    await Task.Delay(600);
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Save(root, window.Background, Path.Combine(directory, $"{name}-{(dark ? "dark" : "light")}.png"));
                }
            }
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(failure, ex.ToString());
            return 1;
        }
    }

    private static void Save(FrameworkElement root, Brush background, string path)
    {
        const int scale = 2;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * scale),
            (int)Math.Ceiling(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var backdrop = new DrawingVisual();
        using (DrawingContext drawing = backdrop.RenderOpen())
            drawing.DrawRectangle(background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bitmap.Render(backdrop);
        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream file = File.Create(path);
        encoder.Save(file);
    }
}
