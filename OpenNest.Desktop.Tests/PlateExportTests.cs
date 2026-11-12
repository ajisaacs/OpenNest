using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Math;

namespace OpenNest.Desktop.Tests;

public sealed class PlateExportTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), $"opennest-desktop-export-{Guid.NewGuid():N}");

    public PlateExportTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, true);

    [Theory]
    [InlineData("job 1", 3, "job 1-P3")]
    [InlineData("A/B:C*?", 1, "A_B_C__-P1")]
    [InlineData("a<b>c\"d|e\\f", 2, "a_b_c_d_e_f-P2")]
    [InlineData("  ", 2, "Nest-P2")]
    [InlineData(null, 4, "Nest-P4")]
    public void SuggestedFileName_IsUsableOnEveryPlatform(string nestName, int plate, string expected)
    {
        Assert.Equal(expected, PlateExport.SuggestedFileName(nestName, plate));
        Assert.Equal(expected + ".dxf", PlateExport.ExternalCadFileName(nestName, plate));
    }

    [Fact]
    public void ExternalCadPath_IsTheDxfInTheTempFolder() =>
        Assert.Equal(Path.Combine(Path.GetTempPath(), "job-P2.dxf"), PlateExport.ExternalCadPath("job", 2));

    [Theory]
    [InlineData(1, PlateExportFormat.Dxf)]
    [InlineData(2, PlateExportFormat.Image)]
    [InlineData(3, PlateExportFormat.PlacementText)]
    [InlineData(4, PlateExportFormat.Svg)]
    public void FromFilterIndex_FollowsTheSaveDialogOrder(int index, PlateExportFormat expected) =>
        Assert.Equal(expected, PlateExport.FromFilterIndex(index));

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(99)]
    public void FromFilterIndex_RefusesOtherIndexes(int index) =>
        Assert.Equal("filterIndex", Assert.Throws<ArgumentException>(() => PlateExport.FromFilterIndex(index)).ParamName);

    [Theory]
    [InlineData("a.dxf", PlateExportFormat.Dxf)]
    [InlineData("a.DXF", PlateExportFormat.Dxf)]
    [InlineData("a.png", PlateExportFormat.Image)]
    [InlineData("a.JPG", PlateExportFormat.Image)]
    [InlineData("a.jpeg", PlateExportFormat.Image)]
    [InlineData("a.bmp", PlateExportFormat.Image)]
    [InlineData("dir.v2/a.txt", PlateExportFormat.PlacementText)]
    [InlineData("a.svg", PlateExportFormat.Svg)]
    public void FromPath_ReadsTheExtension(string path, PlateExportFormat expected) =>
        Assert.Equal(expected, PlateExport.FromPath(path));

    [Theory]
    [InlineData("a")]
    [InlineData("a.pdf")]
    [InlineData("a.dxf.bak")]
    [InlineData("")]
    [InlineData(null)]
    public void FromPath_IsNullForOtherFiles(string path) => Assert.Null(PlateExport.FromPath(path));

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    public void PlacementText_GivesEachPartsSourceOriginAndDegrees(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var plate = CreateJob().Plates[0];
            var path = Path.Combine(folder, "plate.txt");

            PlateExport.Write(plate, Units.Inches, path, PlateExportFormat.PlacementText, null);

            var expected = plate.Parts.Select(part =>
            {
                var pt = part.BaseDrawing.Source.Offset.Rotate(part.Rotation);
                return string.Format("{0}|{1},{2}|{3}", part.BaseDrawing.Source.Path,
                    System.Math.Round(part.Location.X - pt.X, 8),
                    System.Math.Round(part.Location.Y - pt.Y, 8),
                    Angle.ToDegrees(part.Rotation));
            });
            Assert.Equal(expected, File.ReadAllLines(path));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void PlacementText_LessTheRotatedSourceOffset()
    {
        var drawing = new Drawing("d", Square(2)) { Source = { Path = "d.dxf", Offset = new Vector(1, 0) } };
        var part = new Part(drawing, new Vector(10, 20));
        part.Rotate(Angle.ToRadians(90));
        var plate = new Plate(48, 96);
        plate.Parts.Add(part);
        var writer = new StringWriter { NewLine = "\n" };

        PlateExport.WritePlacements(plate, writer);

        // The source origin is the location less (1, 0) turned 90 degrees, (0, 1).
        var fields = writer.ToString().TrimEnd('\n').Split('|');
        Assert.Equal("d.dxf", fields[0]);
        var xy = fields[1].Split(',').Select(double.Parse).ToArray();
        Assert.Equal(part.Location.X, xy[0], 6);
        Assert.Equal(part.Location.Y - 1, xy[1], 6);
        Assert.Equal(90, double.Parse(fields[2]), 6);
    }

    [Fact]
    public void Dxf_ReadsBackThroughTheCadImporter()
    {
        var plate = CreateJob().Plates[0];
        var path = Path.Combine(folder, "plate.dxf");

        PlateExport.Write(plate, Units.Inches, path, PlateExportFormat.Dxf, null);

        var imported = CadImporter.Import(path);
        var box = imported.Entities.Cast<IBoundable>().GetBoundingBox();
        // The sheet outline and both parts come back.
        Assert.True(box.Width >= plate.Size.Width - 1e-6 && box.Length >= plate.Size.Length - 1e-6, $"{box}");
        Assert.True(imported.Entities.Count >= 4 + 2 * 4, $"only {imported.Entities.Count} entities");
    }

    [Fact]
    public void Svg_IsWellFormedSvgOfThePlate()
    {
        var plate = CreateJob().Plates[0];
        var path = Path.Combine(folder, "plate.svg");

        PlateExport.Write(plate, Units.Millimeters, path, PlateExportFormat.Svg, null);

        var root = XDocument.Load(path).Root!;
        Assert.Equal("svg", root.Name.LocalName);
        Assert.EndsWith("mm", root.Attribute("width")!.Value);
        Assert.NotEmpty(root.Descendants().Where(e => e.Name.LocalName == "path"));
    }

    [Fact]
    public void Image_IsDrawnByTheFrontEnd_AtTheChosenPath()
    {
        var path = Path.Combine(folder, "view.png");
        var drawn = new List<string>();

        PlateExport.Write(CreateJob().Plates[0], Units.Inches, path, PlateExportFormat.Image, drawn.Add);

        Assert.Equal(new[] { path }, drawn);
    }

    [Fact]
    public void Image_WithoutARenderer_IsRefusedAndWritesNothing()
    {
        var path = Path.Combine(folder, "view.png");

        Assert.Throws<NotSupportedException>(() =>
            PlateExport.Write(CreateJob().Plates[0], Units.Inches, path, PlateExportFormat.Image, null));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Write_RefusesAnUnknownFormat_WithoutCreatingAFile()
    {
        var path = Path.Combine(folder, "plate");

        Assert.Throws<ArgumentException>(() =>
            PlateExport.Write(CreateJob().Plates[0], Units.Inches, path, (PlateExportFormat)42, null));
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData(PlateExportFormat.Dxf)]
    [InlineData(PlateExportFormat.PlacementText)]
    [InlineData(PlateExportFormat.Svg)]
    public void Write_LeavesTheNestUntouched(PlateExportFormat format)
    {
        var nest = CreateJob();
        var plate = nest.Plates[0];
        var poses = plate.Parts.Select(p => (p.Location, p.Rotation)).ToList();
        var nested = nest.Drawings.Select(d => d.Quantity.Nested).ToList();

        PlateExport.Write(plate, nest.Units, Path.Combine(folder, "plate.out"), format, null);

        Assert.Equal(poses, plate.Parts.Select(p => (p.Location, p.Rotation)).ToList());
        Assert.Equal(nested, nest.Drawings.Select(d => d.Quantity.Nested).ToList());
    }

    [Fact]
    public void Write_ToAMissingFolder_Throws()
    {
        var path = Path.Combine(folder, "missing", "plate.txt");

        Assert.ThrowsAny<IOException>(() =>
            PlateExport.Write(CreateJob().Plates[0], Units.Inches, path, PlateExportFormat.PlacementText, null));
    }

    [Fact]
    public async Task ExportAll_ExportsEveryPlateInOrder()
    {
        var asked = new List<int>();

        var count = await PlateExport.ExportAllAsync(3, i => { asked.Add(i); return Task.FromResult(true); });

        Assert.Equal(3, count);
        Assert.Equal(new[] { 0, 1, 2 }, asked);
    }

    [Fact]
    public async Task ExportAll_StopsAtTheFirstCancelOrFailure()
    {
        var asked = new List<int>();

        var count = await PlateExport.ExportAllAsync(4, i => { asked.Add(i); return Task.FromResult(i != 1); });

        Assert.Equal(1, count);
        Assert.Equal(new[] { 0, 1 }, asked);
    }

    [Fact]
    public void Launcher_OpensThroughTheShellOnWindows()
    {
        var info = ExternalFileLauncher.StartInfo(@"C:\temp\job-P1.dxf", OSPlatform.Windows);

        Assert.True(info.UseShellExecute);
        Assert.Equal(@"C:\temp\job-P1.dxf", info.FileName);
    }

    [Theory]
    [InlineData("LINUX", "xdg-open")]
    [InlineData("FREEBSD", "xdg-open")]
    [InlineData("OSX", "open")]
    public void Launcher_OpensWithTheDesktopOpenerElsewhere(string platform, string opener)
    {
        const string path = "/tmp/my job's \"plate\"-P1.dxf";

        var info = ExternalFileLauncher.StartInfo(path, OSPlatform.Create(platform));

        Assert.False(info.UseShellExecute);
        Assert.Equal(opener, info.FileName);
        // The path is one argument, never parsed by a shell.
        Assert.Equal(new[] { path }, info.ArgumentList);
        Assert.Equal("", info.Arguments);
    }

    [Fact]
    public void Launcher_RefusesABlankPath() =>
        Assert.ThrowsAny<ArgumentException>(() => ExternalFileLauncher.StartInfo(" ", OSPlatform.Linux));

    internal static Program Square(double size)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(new Vector(0, 0)));
        program.Codes.Add(new LinearMove(new Vector(size, 0)));
        program.Codes.Add(new LinearMove(new Vector(size, size)));
        program.Codes.Add(new LinearMove(new Vector(0, size)));
        program.Codes.Add(new LinearMove(new Vector(0, 0)));
        return program;
    }

    internal static Nest CreateJob()
    {
        var nest = new Nest("export job") { Units = Units.Inches };
        var drawing = new Drawing("rect", Square(4));
        drawing.Source.Path = "/drawings/rect.dxf";
        drawing.Source.Offset = new Vector(1.234567891, -0.987654321);
        drawing.Quantity.Required = 4;
        nest.Drawings.Add(drawing);
        var plate = new Plate(24, 48) { Quantity = 1 };
        var part = new Part(drawing, new Vector(6.123456789, 8.987654321));
        part.Rotate(Angle.ToRadians(15.25));
        plate.Parts.Add(part);
        plate.Parts.Add(new Part(drawing, new Vector(14.5, 16.25)));
        nest.Plates.Add(plate);
        return nest;
    }
}
