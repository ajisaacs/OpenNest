using System.Text.Json;
using OpenNest.Data;

namespace OpenNest.Tests.Data;

/// <summary>
/// Library settings carry priority (higher first) as <c>nestPriority</c> and keep the
/// lower-first legacy value under <c>priority</c>, which older clients read and send.
/// </summary>
public class DrawingNestingSettingsPriorityTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void WritesTheLegacyValueBeforeTheNewOne()
    {
        var json = JsonSerializer.Serialize(new DrawingNestingSettings { Priority = 4 }, Web);

        Assert.Contains("\"priority\":-4,\"nestPriority\":4", json);
    }

    [Fact]
    public void ReadsAnOlderClientsLegacyValue()
    {
        var settings = JsonSerializer.Deserialize<DrawingNestingSettings>("""{"cutFirst":true,"priority":2}""", Web)!;

        Assert.Equal(-2, settings.Priority);
    }

    [Fact]
    public void RoundTripsAndStaysEqual()
    {
        var settings = new DrawingNestingSettings { Priority = 7, CutFirst = true };

        var back = JsonSerializer.Deserialize<DrawingNestingSettings>(JsonSerializer.Serialize(settings, Web), Web);

        Assert.Equal(settings, back);
    }
}
