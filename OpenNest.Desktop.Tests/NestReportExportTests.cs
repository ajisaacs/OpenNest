using OpenNest.Data;

namespace OpenNest.Desktop.Tests;

public sealed class NestReportExportTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 11, 11, 9, 30, 0, TimeSpan.Zero);
    private readonly string folder = Path.Combine(Path.GetTempPath(), $"opennest-desktop-report-{Guid.NewGuid():N}");

    public NestReportExportTests() => Directory.CreateDirectory(folder);

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, true);
    }

    [Fact]
    public void ReportFileName_IsTheNestNameDotReportPdf() =>
        Assert.Equal("export job.report.pdf", NestReportExport.ReportFileName(PlateExportTests.CreateJob()));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("a\0b")]
    public void ReportFileName_RefusesUnusableNames(string name)
    {
        var nest = PlateExportTests.CreateJob();
        nest.Name = name;

        var error = Assert.Throws<InvalidOperationException>(() => NestReportExport.ReportFileName(nest));
        Assert.Contains("Rename the nest", error.Message);
    }

    [Fact]
    public void ReportFileName_RefusesNoNest() =>
        Assert.Throws<InvalidOperationException>(() => NestReportExport.ReportFileName(null));

    [Fact]
    public void Write_WritesAPdf_AndLeavesTheNestUntouched()
    {
        var nest = PlateExportTests.CreateJob();
        var nested = nest.Drawings.Select(d => d.Quantity.Nested).ToList();
        var modified = nest.DateLastModified;
        var path = Path.Combine(folder, NestReportExport.ReportFileName(nest));

        var snapshot = NestReportExport.Capture(nest, At, Array.Empty<MachineConfig>(), new Dictionary<string, string>());
        NestReportExport.Write(snapshot, path);

        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(nested, nest.Drawings.Select(d => d.Quantity.Nested).ToList());
        Assert.Equal(modified, nest.DateLastModified);
        // Only the report's own file is left.
        Assert.Equal(new[] { path }, Directory.GetFiles(folder));
    }

    [Fact]
    public void Capture_TakesTheDensityFromTheNestsMachine()
    {
        var nest = PlateExportTests.CreateJob();
        nest.Thickness = 0.25;
        nest.Material = new Material("Steel", "A36");
        var other = Machine("Other", ("Steel", "A36", 0.5));
        var own = Machine("Own", ("Steel", "A36", 0.2836));
        nest.Machine = new NestMachine(own.Id, own.Name);

        var snapshot = NestReportExport.Capture(nest, At, new[] { other, own }, new Dictionary<string, string>());

        // A 4 x 4 square, 0.25 thick: 4 cubic inches at the nest machine's density.
        Assert.Equal(4 * 0.2836, snapshot.Drawings.Single().Weight!.Value, 6);
    }

    [Fact]
    public void Capture_FallsBackToAnyMachinesMaterial()
    {
        var nest = PlateExportTests.CreateJob();
        nest.Thickness = 0.25;
        nest.Material = new Material("Steel", "A36");

        var snapshot = NestReportExport.Capture(
            nest, At, new[] { Machine("Only", ("Steel", "A36", 0.3)) }, new Dictionary<string, string>());

        Assert.Equal(4 * 0.3, snapshot.Drawings.Single().Weight!.Value, 6);
    }

    [Fact]
    public void Capture_NamesTheThicknessFromTheNestsMachine()
    {
        var nest = PlateExportTests.CreateJob();
        nest.Thickness = 0.0598;
        nest.Material = new Material("Steel", "A36");
        var own = Machine("Own", ("Steel", "A36", 0.2836));
        own.Materials[0].Thicknesses.Add(new ThicknessConfig { Value = 0.0598, Name = " 16 ga " });
        nest.Machine = new NestMachine(own.Id, own.Name);

        var named = NestReportExport.Capture(nest, At, new[] { own }, new Dictionary<string, string>());
        nest.Thickness = 0.25;
        var unnamed = NestReportExport.Capture(nest, At, new[] { own }, new Dictionary<string, string>());

        Assert.Equal("16 ga", named.ThicknessName);
        Assert.Equal("", unnamed.ThicknessName);
    }

    [Fact]
    public void Capture_WithoutADensity_HasNoWeight()
    {
        var nest = PlateExportTests.CreateJob();
        nest.Thickness = 0.25;
        nest.Material = new Material("Unobtainium", "X");

        var snapshot = NestReportExport.Capture(
            nest, At, new[] { Machine("Only", ("Steel", "A36", 0.3)) }, new Dictionary<string, string>());

        Assert.Null(snapshot.Drawings.Single().Weight);
    }

    [Fact]
    public void Capture_EstimatesCutTimes_FromTheSavedCutTimeSettings()
    {
        var nest = PlateExportTests.CreateJob();
        var slow = new Dictionary<string, string>
        {
            ["LastFeedrate"] = "10",
            ["LastRapidFeedrate"] = "100",
            ["LastPierceTime"] = "1",
            ["CutTimeMachine"] = SavedCutTimeSettings.FullSpeed,
        };
        var fast = new Dictionary<string, string>(slow) { ["LastFeedrate"] = "1000" };

        var slowReport = NestReportExport.Capture(nest, At, Array.Empty<MachineConfig>(), slow);
        var fastReport = NestReportExport.Capture(nest, At, Array.Empty<MachineConfig>(), fast);

        Assert.True(slowReport.HasCutTimes);
        Assert.True(slowReport.Plates.Single().CutTime > fastReport.Plates.Single().CutTime);
    }

    [Fact]
    public void ViewPath_IsANewPrivateFolderPerReport()
    {
        var first = NestReportExport.CreateViewPath("job.report.pdf", folder);
        var second = NestReportExport.CreateViewPath("job.report.pdf", folder);

        Assert.NotEqual(first, second);
        Assert.Equal("job.report.pdf", Path.GetFileName(first));
        Assert.True(Directory.Exists(Path.GetDirectoryName(first)));
        Assert.Equal(folder, Path.GetDirectoryName(Path.GetDirectoryName(first)));

        NestReportExport.DeleteReportFolder(first);

        Assert.False(Directory.Exists(Path.GetDirectoryName(first)));
        Assert.True(Directory.Exists(Path.GetDirectoryName(second)));
    }

    [Fact]
    public void DeleteReportFolder_IgnoresAMissingFolder() =>
        NestReportExport.DeleteReportFolder(Path.Combine(folder, "gone", "job.report.pdf"));

    [Fact]
    public void DeleteViewedReportsBefore_RemovesOnlyOlderReports()
    {
        var old = NestReportExport.CreateViewPath("old.report.pdf", folder);
        File.WriteAllText(old, "%PDF");
        Directory.SetLastWriteTimeUtc(Path.GetDirectoryName(old)!, DateTime.UtcNow.AddDays(-2));
        var recent = NestReportExport.CreateViewPath("new.report.pdf", folder);
        File.WriteAllText(recent, "%PDF");

        var removed = NestReportExport.DeleteViewedReportsBefore(
            DateTime.UtcNow - NestReportExport.ViewedReportLifetime, folder);

        Assert.Equal(1, removed);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void DeleteViewedReportsBefore_WithoutTheFolder_RemovesNothing() =>
        Assert.Equal(0, NestReportExport.DeleteViewedReportsBefore(DateTime.UtcNow, Path.Combine(folder, "none")));

    private static MachineConfig Machine(string name, params (string Name, string Grade, double Density)[] materials) =>
        new()
        {
            Name = name,
            Materials = materials.Select(m => new MaterialConfig { Name = m.Name, Grade = m.Grade, Density = m.Density }).ToList(),
        };
}
