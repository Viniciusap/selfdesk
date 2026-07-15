using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace SelfDesk.Viewer.Settings;

public sealed record WindowSettings(
    [property: JsonPropertyName("windowLeft")] double Left,
    [property: JsonPropertyName("windowTop")] double Top,
    [property: JsonPropertyName("windowWidth")] double Width,
    [property: JsonPropertyName("windowHeight")] double Height,
    [property: JsonPropertyName("windowMaximized")] bool Maximized);

// Issue #16: persist viewer window position/size across sessions.
public static class WindowSettingsService
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SelfDesk", "settings.json");

    public static WindowSettings? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            return JsonSerializer.Deserialize<WindowSettings>(File.ReadAllText(FilePath));
        }
        catch
        {
            return null;
        }
    }

    public static void Save(WindowSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings));
        }
        catch
        {
            // Best-effort: a failed save should never crash the app on exit.
        }
    }

    // A saved position is unusable if the monitor it was on is gone (disconnected
    // second monitor, resolution change) — require the window to meaningfully
    // overlap the current virtual screen bounds, not just clip a corner into it.
    public static bool IsOnScreen(double left, double top, double width, double height)
    {
        const double minVisible = 50;
        var vLeft   = SystemParameters.VirtualScreenLeft;
        var vTop    = SystemParameters.VirtualScreenTop;
        var vRight  = vLeft + SystemParameters.VirtualScreenWidth;
        var vBottom = vTop + SystemParameters.VirtualScreenHeight;

        return left + minVisible <= vRight
            && left + width - minVisible >= vLeft
            && top + minVisible <= vBottom
            && top + height - minVisible >= vTop;
    }
}
