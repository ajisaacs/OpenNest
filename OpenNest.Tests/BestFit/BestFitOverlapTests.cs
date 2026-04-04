using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using OpenNest.IO;
using Xunit.Abstractions;

namespace OpenNest.Tests.BestFit;

public class BestFitOverlapTests
{
    private const string DxfPath = @"C:\Users\AJ\Desktop\Templates\4526 A14 PT16.dxf";
    private readonly ITestOutputHelper _output;

    public BestFitOverlapTests(ITestOutputHelper output) => _output = output;

    private static Drawing MakeRoundedRect(double w = 7.25, double h = 3.31, double r = 0.5)
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w - r, 0)));
        pgm.Codes.Add(new ArcMove(new Vector(w, r), new Vector(w - r, r), RotationType.CW));
        pgm.Codes.Add(new LinearMove(new Vector(w, h - r)));
        pgm.Codes.Add(new ArcMove(new Vector(w - r, h), new Vector(w - r, h - r), RotationType.CW));
        pgm.Codes.Add(new LinearMove(new Vector(0, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Drawing("rounded-rect", pgm);
    }

    private static Drawing ImportDxf()
    {
        if (!File.Exists(DxfPath))
            return null;

        var importer = new DxfImporter();
        importer.GetGeometry(DxfPath, out var geometry);
        var pgm = ConvertGeometry.ToProgram(geometry);
        return new Drawing("PT16", pgm);
    }

    [Fact]
    public void KeptPairs_NoOverlap()
    {
        var drawing = ImportDxf() ?? MakeRoundedRect();
        var bbox = drawing.Program.BoundingBox();
        _output.WriteLine($"Drawing: {drawing.Name}, bbox={bbox.Length:F2} x {bbox.Width:F2}");

        var finder = new BestFitFinder(120, 60);
        var results = finder.FindBestFits(drawing);

        var kept = results.Where(r => r.Keep).ToList();
        _output.WriteLine($"Total results: {results.Count}, Kept: {kept.Count}");

        var overlapping = 0;

        foreach (var result in kept)
        {
            var parts = result.BuildParts(drawing);
            if (parts[0].Intersects(parts[1], out var pts))
            {
                overlapping++;
                _output.WriteLine($"  OVERLAP #{overlapping}: Test {result.Candidate.TestNumber} " +
                    $"Part2Rot={OpenNest.Math.Angle.ToDegrees(result.Candidate.Part2Rotation):F1}° " +
                    $"collision pts={pts.Count}");
            }
        }

        Assert.Equal(0, overlapping);
    }
}
