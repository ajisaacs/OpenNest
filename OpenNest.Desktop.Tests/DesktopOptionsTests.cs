using OpenNest.Data;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Strategies;

namespace OpenNest.Desktop.Tests;

public class DesktopOptionsTests
{
    // As the WinForms app stores them in user-settings.json.
    private static Dictionary<string, string> WinFormsSaved() => new()
    {
        ["CreateNewNestOnOpen"] = "False",
        ["AutoSizePlateFactor"] = "0.25",
        ["CuttingPlanEffort"] = "2.5",
        ["ActiveColorScheme"] = "Dark",
        ["ClonePlacementOrder"] = "VerticalFirst",
        ["DisabledStrategies"] = "Pairs,Extents",
        ["UpdateIncludeDevBuilds"] = "True",
        ["UpdateFeedUrl"] = "http://feed.example/releases",
        ["LastFeedrate"] = "250",
    };

    [Fact]
    public void Read_TheWinFormsValues()
    {
        var options = DesktopOptions.Read(WinFormsSaved());

        Assert.False(options.CreateNewNestOnOpen);
        Assert.Equal(0.25, options.AutoSizePlateFactor);
        Assert.Equal(2.5, options.CuttingPlanEffort);
        Assert.Equal("Dark", options.ColorScheme);
        Assert.Equal(SettleOrder.VerticalFirst, options.ClonePlacementOrder);
        Assert.Equal(new[] { "Extents", "Pairs" }, options.DisabledStrategies.OrderBy(n => n));
        Assert.True(options.UpdateIncludeDevBuilds);
    }

    [Fact]
    public void Read_NothingSaved_IsTheWinFormsDefaults()
    {
        var options = DesktopOptions.Read(new Dictionary<string, string>());

        Assert.True(options.CreateNewNestOnOpen);
        Assert.Equal(0.5, options.AutoSizePlateFactor);
        Assert.Equal(1.0, options.CuttingPlanEffort);
        Assert.Equal("Classic", options.ColorScheme);
        Assert.Equal(SettleOrder.ClosestToCorner, options.ClonePlacementOrder);
        Assert.Empty(options.DisabledStrategies);
        Assert.False(options.UpdateIncludeDevBuilds);
    }

    [Fact]
    public void Read_UnreadableValues_AreTheWinFormsDefaults()
    {
        var options = DesktopOptions.Read(new Dictionary<string, string>
        {
            ["CreateNewNestOnOpen"] = "maybe",
            ["AutoSizePlateFactor"] = "half",
            ["CuttingPlanEffort"] = "",
            ["ClonePlacementOrder"] = "Sideways",
            ["DisabledStrategies"] = " , ,",
            ["UpdateIncludeDevBuilds"] = "1",
        });

        Assert.Equal(DesktopOptions.Defaults with { DisabledStrategies = options.DisabledStrategies }, options);
        Assert.Empty(options.DisabledStrategies);
    }

    [Fact]
    public void WriteTo_GivesBackTheWinFormsTextUnchanged_AndKeepsOtherSettings()
    {
        var stored = WinFormsSaved();

        var written = new Dictionary<string, string>(stored);
        DesktopOptions.Read(stored).WriteTo(written);

        // The set is written back in its own order; the rest is the stored text.
        Assert.Equal(
            FillStrategyOptions.ParseDisabled(stored["DisabledStrategies"]),
            FillStrategyOptions.ParseDisabled(written["DisabledStrategies"]));
        written["DisabledStrategies"] = stored["DisabledStrategies"];
        Assert.Equal(stored, written);
    }

    [Fact]
    public void WriteTo_TheWinFormsTextForms_AndNeverTheFeedUrl()
    {
        var written = new Dictionary<string, string>();

        new DesktopOptions
        {
            AutoSizePlateFactor = 0.1,
            CuttingPlanEffort = 3,
            ColorScheme = "",
            DisabledStrategies = new[] { "Row" },
        }.WriteTo(written);

        Assert.Equal("True", written["CreateNewNestOnOpen"]);
        Assert.Equal("False", written["UpdateIncludeDevBuilds"]);
        Assert.Equal("0.1", written["AutoSizePlateFactor"]);
        Assert.Equal("3", written["CuttingPlanEffort"]);
        Assert.Equal("Classic", written["ActiveColorScheme"]);
        Assert.Equal("ClosestToCorner", written["ClonePlacementOrder"]);
        Assert.Equal("Row", written["DisabledStrategies"]);
        Assert.False(written.ContainsKey("UpdateFeedUrl"));
    }

    [Theory]
    [InlineData(0.1, 0.5)]
    [InlineData(25.0, 10.0)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(4.0, 4.0)]
    public void CuttingPlanEffortForEditing_IsTheValuePlanCuttingUses(double saved, double shown)
    {
        Assert.Equal(shown, new DesktopOptions { CuttingPlanEffort = saved }.CuttingPlanEffortForEditing);
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(250.0, 100.0)]
    [InlineData(double.PositiveInfinity, 0.5)]
    [InlineData(0.375, 0.375)]
    public void AutoSizePlateFactorForEditing_IsWithinTheBoxRange(double saved, double shown)
    {
        Assert.Equal(shown, new DesktopOptions { AutoSizePlateFactor = saved }.AutoSizePlateFactorForEditing);
    }

    [Fact]
    public void ColorSchemeIndex_ExactName_ElseTheFirst()
    {
        var names = new[] { "Classic", "Pastel", "Dark" };

        Assert.Equal(2, new DesktopOptions { ColorScheme = "Dark" }.ColorSchemeIndex(names));
        Assert.Equal(0, new DesktopOptions { ColorScheme = "dark" }.ColorSchemeIndex(names));
        Assert.Equal(0, new DesktopOptions { ColorScheme = "Gone" }.ColorSchemeIndex(names));
    }

    [Fact]
    public void ClonePlacement_ChoicesInComboOrder()
    {
        Assert.Equal(
            new[] { SettleOrder.ClosestToCorner, SettleOrder.HorizontalFirst, SettleOrder.VerticalFirst },
            ClonePlacement.Choices.Select(c => c.Order));
        Assert.Equal(1, ClonePlacement.IndexOf(SettleOrder.HorizontalFirst));
        Assert.Equal(SettleOrder.VerticalFirst, ClonePlacement.At(2));
        Assert.Equal(SettleOrder.ClosestToCorner, ClonePlacement.At(-1));
    }

    [Theory]
    [InlineData("HorizontalFirst", SettleOrder.HorizontalFirst)]
    [InlineData("7", SettleOrder.ClosestToCorner)]
    [InlineData(null, SettleOrder.ClosestToCorner)]
    [InlineData("", SettleOrder.ClosestToCorner)]
    public void ClonePlacement_Parse_UnknownIsClosestToOrigin(string? stored, SettleOrder expected)
    {
        Assert.Equal(expected, ClonePlacement.Parse(stored!));
    }

    [Fact]
    public void FillStrategyOptions_ParseIgnoresCaseAndBlanks()
    {
        var names = FillStrategyOptions.ParseDisabled(" pairs , ,Row,ROW");

        Assert.Equal(2, names.Count);
        Assert.Contains("Pairs", names);
        Assert.Contains("row", names);
        Assert.Empty(FillStrategyOptions.ParseDisabled(null!));
    }

    [Fact]
    public void FillStrategyOptions_RowsListEveryStrategyInPipelineOrder()
    {
        Assert.Equal(
            FillStrategyRegistry.AllStrategies.Select(s => (s.Name, s.Phase, s.Order)),
            FillStrategyOptions.Rows.Select(r => (r.Name, r.Phase, r.Order)));
    }

    [Fact]
    public void UpdateFeed_SavesTheNormalizedUrl()
    {
        string? saved = null;

        var (notice, url) = UpdateFeedOption.Save("  https://feed.example/opennest/  ", u => saved = u);

        Assert.Equal("https://feed.example/opennest", url);
        Assert.Equal(url, saved);
        Assert.Equal(OptionNoticeKind.Information, notice.Kind);
        Assert.Contains("Restart OpenNest", notice.Text);
    }

    [Fact]
    public void UpdateFeed_BlankClearsIt()
    {
        string? saved = null;

        var (notice, url) = UpdateFeedOption.Save("   ", u => saved = u);

        Assert.Equal("", url);
        Assert.Equal("", saved);
        Assert.StartsWith("Update feed URL cleared.", notice.Text);
    }

    [Theory]
    [InlineData("ftp://feed.example")]
    [InlineData("not a url")]
    public void UpdateFeed_RefusesAnythingButHttp(string text)
    {
        var called = false;

        var (notice, url) = UpdateFeedOption.Save(text, _ => called = true);

        Assert.Null(url);
        Assert.False(called);
        Assert.Equal(OptionNoticeKind.Warning, notice.Kind);
    }

    [Fact]
    public void UpdateFeed_AFailedWriteIsReported()
    {
        var (notice, url) = UpdateFeedOption.Save("http://feed", _ => throw new IOException("disk full"));

        Assert.Null(url);
        Assert.Equal(OptionNoticeKind.Error, notice.Kind);
        Assert.Contains("disk full", notice.Text);
    }

    [Fact]
    public void UpdateFeed_ReadsTheSavedUrl()
    {
        Assert.Equal("http://feed.example/releases", UpdateFeedOption.Read(WinFormsSaved()));
        Assert.Equal("", UpdateFeedOption.Read(new Dictionary<string, string>()));
    }

    [Fact]
    public void ServerUrl_RefusesAnInvalidUrl()
    {
        var (notice, url) = NestServerUrlOption.Save("", _ => throw new InvalidOperationException());

        Assert.Null(url);
        Assert.Equal(OptionNoticeKind.Warning, notice.Kind);
        Assert.Equal("Enter a valid http or https server URL.", notice.Text);
    }

    [Fact]
    public void ServerUrl_AFailedWriteIsReported()
    {
        var (notice, url) = NestServerUrlOption.Save("http://nest:8080", _ => throw new IOException("locked"));

        Assert.Null(url);
        Assert.Equal(OptionNoticeKind.Error, notice.Kind);
        Assert.Equal("Could not save the server URL: locked", notice.Text);
    }

    [Fact]
    public void ServerUrl_SaveToFileKeepsTheStorageMode()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennest-options-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "storage.json");
        try
        {
            new NestStorageSettings { Mode = NestStorageMode.Database, ServerUrl = "http://old:8080" }.Save(path);

            var (notice, url) = NestServerUrlOption.Save(" http://nest.lan:8080/ ",
                u => NestServerUrlOption.SaveToFile(path, u));

            Assert.Equal(OptionNoticeKind.Information, notice.Kind);
            Assert.Equal("http://nest.lan:8080", url);
            var saved = NestStorageSettings.Load(path);
            Assert.Equal(NestStorageMode.Database, saved.Mode);
            Assert.Equal("http://nest.lan:8080", saved.ServerUrl);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ServerUrl_SaveToFileWithNoFileIsFileMode()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opennest-options-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "storage.json");
        try
        {
            var saved = NestServerUrlOption.SaveToFile(path, "http://nest:8080");

            Assert.Equal(NestStorageMode.File, saved.Mode);
            Assert.Equal("http://nest:8080", NestStorageSettings.Load(path).ServerUrl);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }
}

// Applying options changes the process-wide strategy set and colour scheme.
[Collection(ColorSchemeState.Collection)]
public class DesktopOptionsApplyTests : IDisposable
{
    private readonly ColorSchemeState saved = new();
    private readonly string[] disabledBefore = FillStrategyRegistry.DisabledNames.ToArray();

    public void Dispose()
    {
        FillStrategyOptions.Apply(disabledBefore);
        saved.Restore();
    }

    [Fact]
    public void Apply_DisablesExactlyTheSavedStrategies_AndUsesTheScheme()
    {
        var all = FillStrategyRegistry.AllStrategies.Select(s => s.Name).ToArray();
        FillStrategyRegistry.Disable(all[0]);

        new DesktopOptions
        {
            ColorScheme = "Dark",
            DisabledStrategies = new[] { all[1].ToUpperInvariant() },
        }.Apply();

        Assert.Equal(all.Where(n => n != all[1]), FillStrategyRegistry.Strategies.Select(s => s.Name));
        Assert.Equal("Dark", ColorScheme.Default.Name);
    }

    [Fact]
    public void ApplySaved_NoneSavedEnablesEveryStrategy()
    {
        FillStrategyRegistry.Disable(FillStrategyRegistry.AllStrategies[0].Name);

        FillStrategyOptions.ApplySaved(new Dictionary<string, string>());

        Assert.Equal(FillStrategyRegistry.AllStrategies, FillStrategyRegistry.Strategies);
    }
}
