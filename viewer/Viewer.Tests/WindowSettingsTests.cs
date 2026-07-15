using System.Text.Json;
using SelfDesk.Viewer.Settings;
using Xunit;

namespace SelfDesk.Viewer.Tests;

public class WindowSettingsTests
{
    [Fact]
    public void SerializesWithDocumentedPropertyNames()
    {
        var settings = new WindowSettings(100, 50, 1280, 720, false);
        var json = JsonSerializer.Serialize(settings);

        Assert.Contains("\"windowLeft\":100", json);
        Assert.Contains("\"windowTop\":50", json);
        Assert.Contains("\"windowWidth\":1280", json);
        Assert.Contains("\"windowHeight\":720", json);
        Assert.Contains("\"windowMaximized\":false", json);
    }

    [Fact]
    public void RoundTripsThroughJson()
    {
        var original = new WindowSettings(10, 20, 800, 600, true);
        var json     = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<WindowSettings>(json);

        Assert.Equal(original, restored);
    }
}
