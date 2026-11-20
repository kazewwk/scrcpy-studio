using System.Runtime.InteropServices;

namespace ScrcpyStudio;

internal static class Program
{
    private const nint PerMonitorV2 = -4;

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(nint value);

    [STAThread]
    private static int Main()
    {
        // The manifest already asks for per-monitor scaling; this covers hosts that drop the manifest.
        SetProcessDpiAwarenessContext(PerMonitorV2);

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
