using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace ScrcpyStudio;

/// <summary>Swaps the colour palette and remembers the user's choice.</summary>
internal static class Theme
{
    private const string Personalize = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static string Store => Path.Combine(RuntimeFiles.Home, "theme.txt");

    internal static bool IsDark { get; private set; } = true;

    /// <summary>The saved choice, or the Windows app mode when nothing was saved.</summary>
    internal static bool Preferred()
    {
        try
        {
            if (File.Exists(Store))
                return File.ReadAllText(Store).Trim() != "light";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fall through to the system setting.
        }
        return Registry.GetValue(Personalize, "AppsUseLightTheme", 1) is 0;
    }

    internal static void Apply(bool dark, bool remember = false)
    {
        IsDark = dark;
        Application.Current.Resources.MergedDictionaries[0] = new ResourceDictionary
        {
            Source = new Uri($"/ScrcpyStudio;component/Theme/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative),
        };
        if (!remember)
            return;
        try
        {
            Directory.CreateDirectory(RuntimeFiles.Home);
            File.WriteAllText(Store, dark ? "dark" : "light");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The choice still applies to this run.
        }
    }
}
