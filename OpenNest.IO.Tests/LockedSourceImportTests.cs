using ACadSharp;
using ACadSharp.IO;
using CSMath;
using CadLine = ACadSharp.Entities.Line;

namespace OpenNest.IO.Tests;

/// <summary>
/// AutoCAD keeps an open drawing locked for writing; importing it must still work.
/// </summary>
public class LockedSourceImportTests
{
    [Theory]
    [InlineData(".dxf")]
    [InlineData(".dwg")]
    public void ImportsCadFileHeldOpenForWritingByAnotherProgram(string extension)
    {
        WithFile(
            extension,
            path =>
            {
                var doc = Square();
                if (extension == ".dwg")
                    DwgWriter.Write(path, doc);
                else
                    DxfWriter.Write(path, doc, false);
            },
            path =>
            {
                var drawing = CadImporter.ImportDrawing(
                    path,
                    new CadImportOptions { DetectBends = false }
                );
                Assert.Equal(100, drawing.Area, 6);
                // The layer-filtered overload reads DXF only.
                if (extension == ".dxf")
                    Assert.Equal(4, Dxf.GetGeometry(path, _ => false).Count);
            }
        );
    }

    [Fact]
    public void ImportsSvgFileHeldOpenForWritingByAnotherProgram()
    {
        WithFile(
            ".svg",
            path =>
                File.WriteAllText(
                    path,
                    "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10in\" height=\"10in\" viewBox=\"0 0 10 10\">"
                        + "<rect width=\"10\" height=\"10\"/></svg>"
                ),
            path =>
            {
                var result = CadImporter.Import(path);
                Assert.NotEmpty(result.Entities);
            }
        );
    }

    private static CadDocument Square()
    {
        var doc = new CadDocument();
        var points = new[]
        {
            new XYZ(0, 0, 0),
            new XYZ(10, 0, 0),
            new XYZ(10, 10, 0),
            new XYZ(0, 10, 0),
        };
        for (var i = 0; i < points.Length; i++)
            doc.Entities.Add(new CadLine(points[i], points[(i + 1) % points.Length]));
        return doc;
    }

    private static void WithFile(string extension, Action<string> write, Action<string> import)
    {
        var path = Path.Combine(Path.GetTempPath(), $"opennest-locked-{Guid.NewGuid()}{extension}");
        try
        {
            write(path);
            // The lock AutoCAD holds on an open drawing: read/write access, readers allowed.
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                import(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
