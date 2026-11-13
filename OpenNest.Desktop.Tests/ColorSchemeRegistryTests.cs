using System.Drawing;
using OpenNest.Data;

namespace OpenNest.Desktop.Tests;

// Applying a scheme changes ColorScheme.Default and the drawing palette for the whole process.
[Collection(ColorSchemeState.Collection)]
public class ColorSchemeRegistryTests : IDisposable
{
    private static readonly string[] BuiltInNames =
        ["Classic", "Pastel", "Dark", "Workshop", "Halloween", "Christmas", "4th of July"];

    private readonly ColorSchemeState saved = new();
    private readonly string directory = Path.Combine(Path.GetTempPath(), "opennest-schemes-" + Guid.NewGuid().ToString("N"));

    public ColorSchemeRegistryTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        saved.Restore();
        Directory.Delete(directory, true);
    }

    [Fact]
    public void BuiltInSchemesAreListedInOrder()
    {
        ColorSchemeRegistry.SchemesDirectory = directory;

        Assert.Equal(BuiltInNames, ColorSchemeRegistry.AllSchemes.Select(s => s.Name));
    }

    [Theory]
    [InlineData("Classic")]
    [InlineData("Pastel")]
    [InlineData("Dark")]
    [InlineData("Workshop")]
    [InlineData("Halloween")]
    [InlineData("Christmas")]
    [InlineData("4th of July")]
    public void EveryBuiltInSchemeRoundTripsUnchanged(string name)
    {
        var scheme = ColorSchemeRegistry.Get(name);

        var json = ColorSchemeSerializer.Serialize(scheme);
        var restored = ColorSchemeSerializer.Deserialize(json);

        Assert.Equal(Snapshot(scheme), Snapshot(restored));
        Assert.Equal(json, ColorSchemeSerializer.Serialize(restored));
    }

    [Fact]
    public void ASchemeFileInTheSchemesFolderLoads()
    {
        File.WriteAllText(Path.Combine(directory, "shop.json"), """
            {
              "name": "Shop Floor",
              "backgroundColor": "#102030",
              "layoutOutlineColor": "#405060",
              "layoutFillColor": "#F0F0E0",
              "boundingBoxColor": "#8080FF",
              "rapidColor": "#1E90FF",
              "originColor": "#808080",
              "edgeSpacingColor": "#B4B4B4",
              "previewPartColor": "#FF8C00",
              "partOutlineColor": "#202020",
              "partColors": ["#AA0000", "#00AA00"]
            }
            """);
        ColorSchemeRegistry.SchemesDirectory = directory;

        var scheme = ColorSchemeRegistry.Get("shop floor");

        Assert.Equal("Shop Floor", scheme.Name);
        Assert.Equal(Color.FromArgb(16, 32, 48), scheme.BackgroundColor);
        Assert.Equal(Color.FromArgb(32, 32, 32), scheme.PartOutlineColor);
        Assert.Equal([Color.FromArgb(170, 0, 0), Color.FromArgb(0, 170, 0)], scheme.PartColors);
        // Missing entries keep their defaults, as WinForms reads them.
        Assert.Equal(Color.Lime, scheme.EtchColor);
        Assert.True(scheme.SelectedPartColor.IsEmpty);
        Assert.Equal(new ColorScheme().LibraryFillColor, scheme.LibraryFillColor);
        Assert.Equal(BuiltInNames.Append("Shop Floor"), ColorSchemeRegistry.AllSchemes.Select(s => s.Name));
    }

    [Fact]
    public void BadSchemeFilesAreSkippedAndTheirNamesFallBackToClassic()
    {
        File.WriteAllText(Path.Combine(directory, "broken.json"), "{ \"name\": \"Broken\", ");
        File.WriteAllText(Path.Combine(directory, "badhex.json"), "{ \"name\": \"Bad Hex\", \"backgroundColor\": \"#GG0000\" }");
        File.WriteAllText(Path.Combine(directory, "null.json"), "null");
        File.WriteAllText(Path.Combine(directory, "classic.json"), "{ \"name\": \"classic\", \"backgroundColor\": \"#FF0000\" }");
        File.WriteAllText(Path.Combine(directory, "notes.txt"), "{ \"name\": \"Text\" }");
        ColorSchemeRegistry.SchemesDirectory = directory;

        Assert.Equal(BuiltInNames, ColorSchemeRegistry.AllSchemes.Select(s => s.Name));
        Assert.Equal("Classic", ColorSchemeRegistry.Get("Broken").Name);
        Assert.Equal("Classic", ColorSchemeRegistry.Get("Bad Hex").Name);
        // A file cannot replace a built-in scheme.
        Assert.Equal(Color.DarkGray, ColorSchemeRegistry.Get("Classic").BackgroundColor);
        Assert.Equal("Classic", ColorSchemeRegistry.Get(null).Name);
        Assert.Equal("Classic", ColorSchemeRegistry.Get("  ").Name);
    }

    [Fact]
    public void AMissingSchemesFolderListsOnlyTheBuiltIns()
    {
        ColorSchemeRegistry.SchemesDirectory = Path.Combine(directory, "missing");

        Assert.Equal(BuiltInNames, ColorSchemeRegistry.AllSchemes.Select(s => s.Name));
    }

    [Fact]
    public void TheSavedSchemeNameResolvesFromTheSharedUserSettings()
    {
        var path = Path.Combine(directory, "user-settings.json");
        UserSettingsFile.Write(path, new Dictionary<string, string>
        {
            ["MainWindowState"] = "Normal",
            [ColorSchemeRegistry.SettingName] = "Halloween",
        });

        var applied = ColorSchemeRegistry.ApplySaved(UserSettingsFile.Read(path));

        Assert.Equal("ActiveColorScheme", ColorSchemeRegistry.SettingName);
        Assert.Equal("Halloween", applied.Name);
        Assert.Equal("Halloween", ColorScheme.Default.Name);
        Assert.Equal(applied.BackgroundColor, ColorScheme.Default.BackgroundColor);
        Assert.Equal(applied.PartColors, PartColorPalette.Current.Colors);
    }

    [Fact]
    public void EditingASchemesColorsAfterApplyingDoesNotRecolorDrawings()
    {
        var scheme = new ColorScheme
        {
            Name = "Mutable",
            PartColors = [Color.FromArgb(10, 20, 30), Color.FromArgb(40, 50, 60)],
        };
        ColorSchemeRegistry.Apply(scheme);
        var palette = PartColorPalette.Current;

        scheme.PartColors[0] = Color.Black;
        scheme.UseGoldenAngleColors = true;

        Assert.Same(palette, PartColorPalette.Current);
        Assert.Equal(Color.FromArgb(10, 20, 30), Drawing.GetPartColor(0));
        Assert.Equal(Color.FromArgb(40, 50, 60), Drawing.GetPartColor(3));
    }

    [Fact]
    public void ANewSchemeOwnsItsOwnCopyOfTheClassicColors()
    {
        var first = new ColorScheme();
        var second = new ColorScheme();

        first.PartColors[0] = Color.Black;

        Assert.Equal(PartPalette.Classic.Colors, second.PartColors);
        Assert.NotEqual(Color.Black, PartPalette.Classic.ColorAt(0));
    }

    [Theory]
    [InlineData(null, "Classic")]
    [InlineData("", "Classic")]
    [InlineData("dark", "Dark")]
    [InlineData("Gone Scheme", "Classic")]
    public void TheSavedSchemeFallsBackToClassic(string? savedName, string expected)
    {
        var settings = new Dictionary<string, string>();
        if (savedName != null)
            settings[ColorSchemeRegistry.SettingName] = savedName;

        Assert.Equal(savedName ?? "Classic", ColorSchemeRegistry.SavedSchemeName(settings));
        Assert.Equal(expected, ColorSchemeRegistry.ApplySaved(settings).Name);
        Assert.Equal(expected, ColorScheme.Default.Name);
    }

    [Fact]
    public void ApplyingCopiesTheSchemeIntoTheDefaultAndRaisesApplied()
    {
        var workshop = ColorSchemeRegistry.Get("Workshop");
        var defaultVersion = ColorScheme.Default.Version;
        ColorScheme? raised = null;
        EventHandler<ColorScheme> handler = (_, scheme) => raised = scheme;
        ColorSchemeRegistry.Applied += handler;
        try
        {
            ColorSchemeRegistry.Apply(workshop);
        }
        finally
        {
            ColorSchemeRegistry.Applied -= handler;
        }

        Assert.Same(workshop, raised);
        Assert.Equal(Snapshot(workshop), Snapshot(ColorScheme.Default));
        Assert.NotEqual(defaultVersion, ColorScheme.Default.Version);
        Assert.True(PartColorPalette.Current.UseGoldenAngle);
        Assert.Equal(PartColorPalette.GoldenAngle(30), Drawing.GetPartColor(30));
    }

    [Fact]
    public void RecolorKeepsCustomColorsInTheirSlotsAndSkipsCutoffs()
    {
        ColorSchemeRegistry.Apply(ColorSchemeRegistry.Get("Classic"));
        var nest = new Nest("Recolor");
        var first = new Drawing("First") { Color = Color.Coral };
        var custom = new Drawing("Custom") { Color = Color.Red, HasCustomColor = true };
        var cutoff = new Drawing("Cutoff") { IsCutOff = true, Color = Color.Gray };
        var third = new Drawing("Third") { Color = Color.Coral };
        nest.Drawings.Add(first);
        nest.Drawings.Add(custom);
        nest.Drawings.Add(cutoff);
        nest.Drawings.Add(third);

        ColorSchemeRegistry.RecolorDrawings(nest);

        var classic = ColorSchemeRegistry.Get("Classic").PartColors;
        Assert.Equal(classic[0], first.Color);
        Assert.Equal(Color.Red, custom.Color);
        Assert.Equal(Color.Gray, cutoff.Color);
        Assert.Equal(classic[2], third.Color);
    }

    [Fact]
    public void RecolorFollowsTheActiveSchemesPalette()
    {
        var nest = new Nest("Recolor");
        var drawings = Enumerable.Range(0, 14).Select(i => new Drawing($"D{i}") { Color = Color.Coral }).ToList();
        foreach (var drawing in drawings)
            nest.Drawings.Add(drawing);

        ColorSchemeRegistry.Apply(ColorSchemeRegistry.Get("Dark"));
        ColorSchemeRegistry.RecolorDrawings(nest);
        var dark = ColorSchemeRegistry.Get("Dark").PartColors;
        Assert.Equal(Enumerable.Range(0, 14).Select(i => dark[i % dark.Length]), drawings.Select(d => d.Color));

        ColorSchemeRegistry.Apply(ColorSchemeRegistry.Get("Workshop"));
        ColorSchemeRegistry.RecolorDrawings(nest);
        Assert.Equal(Enumerable.Range(0, 14).Select(PartColorPalette.GoldenAngle), drawings.Select(d => d.Color));
    }

    private static string Snapshot(ColorScheme s) => string.Join(";",
        s.Name, s.BackgroundColor.ToArgb(), s.LayoutOutlineColor.ToArgb(), s.LayoutFillColor.ToArgb(),
        s.BoundingBoxColor.ToArgb(), s.RapidColor.ToArgb(), s.OriginColor.ToArgb(), s.EdgeSpacingColor.ToArgb(),
        s.PreviewPartColor.ToArgb(), s.PartOutlineColor.IsEmpty ? "empty" : s.PartOutlineColor.ToArgb().ToString(),
        s.SelectedPartColor.IsEmpty ? "empty" : s.SelectedPartColor.ToArgb().ToString(), s.EtchColor.ToArgb(),
        s.LibraryBackgroundColor.ToArgb(), s.LibraryFillColor.ToArgb(), s.LibraryOutlineColor.ToArgb(),
        s.LibraryEtchColor.ToArgb(), s.LibraryBendColor.ToArgb(), s.UseGoldenAngleColors,
        string.Join(",", s.PartColors.Select(c => c.ToArgb())));
}

[CollectionDefinition(ColorSchemeState.Collection, DisableParallelization = true)]
public sealed class ColorSchemeStateCollection
{
}

/// <summary>The process-wide scheme state a test changes, restored afterwards.</summary>
public sealed class ColorSchemeState
{
    public const string Collection = "Colour scheme state";

    private readonly ColorScheme previous =
        ColorSchemeSerializer.Deserialize(ColorSchemeSerializer.Serialize(ColorScheme.Default));
    private readonly PartPalette previousPalette = PartColorPalette.Current;
    private readonly string previousDirectory = ColorSchemeRegistry.SchemesDirectory;

    public void Restore()
    {
        ColorSchemeRegistry.Apply(previous);
        PartColorPalette.Use(previousPalette);
        ColorSchemeRegistry.SchemesDirectory = previousDirectory;
    }
}
