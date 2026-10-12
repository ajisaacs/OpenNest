using OpenNest.CNC;
using OpenNest.Data;
using OpenNest.Engine;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.Engine;

/// <summary>
/// The desktop clamp workflow end to end without the form: enabled machine settings give a
/// new nest its rail, whole-job Auto Nest (template stock, validated pipeline, commit) keeps
/// every part clear of the clamps on every sheet while still using the edge between them,
/// and save/reload preserves clamps and placements without writing the empty tail sheet.
/// </summary>
public class ClampRailWorkflowTests
{
    private const double EdgeSpacing = 0.25;
    private static readonly double[] Centers = { 3, 10.5, 18 };

    private static Drawing Block()
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(4, 0);
        program.LineTo(4, 2);
        program.LineTo(0, 2);
        program.LineTo(0, 0);
        return new Drawing("neutral-block", program);
    }

    private static Nest NewNest(int quadrant)
    {
        var nest = new Nest { Units = Units.Inches };
        nest.PlateDefaults.Size = new Size(12, 24); // 12 deep (Y), 24 along the clamp edge (X)
        nest.PlateDefaults.Quadrant = quadrant;
        nest.PlateDefaults.EdgeSpacing = new Spacing(EdgeSpacing, EdgeSpacing, EdgeSpacing, EdgeSpacing);
        nest.PlateDefaults.PartSpacing = 0.25;
        new ClampSettings { Enabled = true, Centers = Centers }.ApplyToNewNest(nest);
        return nest;
    }

    /// <summary>Independent oracle: part boxes against clamps grown by the bottom edge spacing.</summary>
    private static void AssertClearOfClamps(Plate plate)
    {
        var left = plate.Quadrant is 2 or 3 ? -plate.Size.Length : 0;
        var bottom = plate.Quadrant is 3 or 4 ? -plate.Size.Width : 0;
        Assert.NotEmpty(plate.ClampZones);
        foreach (var zone in plate.ClampZones)
        {
            var keepOut = zone.Bounds.Translate(left, bottom);
            foreach (var part in plate.Parts)
            {
                var box = part.BoundingBox;
                var overlapX = System.Math.Min(box.Right, keepOut.Right + EdgeSpacing)
                    - System.Math.Max(box.Left, keepOut.Left - EdgeSpacing);
                var overlapY = System.Math.Min(box.Top, keepOut.Top + EdgeSpacing)
                    - System.Math.Max(box.Bottom, keepOut.Bottom - EdgeSpacing);
                Assert.False(overlapX > 1e-6 && overlapY > 1e-6,
                    $"{part.BaseDrawing.Name} at {box} enters {zone.Id} at {keepOut}");
            }
        }
    }

    [Theory]
    [InlineData("Rectangles", 1)]
    [InlineData("Default", 1)]
    [InlineData("Rectangles", 3)]
    public void AutoNestAvoidsMachineClampsAndTheyRoundTrip(string engine, int quadrant)
    {
        const int quantity = 40;
        var drawing = Block();
        var nest = NewNest(quadrant);
        nest.Drawings.Add(drawing);
        var rail = nest.PlateDefaults.ClampRail;
        Assert.NotNull(rail);
        using var manager = new PlateManager(nest);
        var template = manager.GetOrCreateEmpty();
        Assert.Equal(3, template.ClampZones.Count);

        var item = new NestItem
        {
            Drawing = drawing,
            Quantity = quantity,
            StepAngle = OpenNest.Math.Angle.HalfPI,
            RotationStart = 0,
            RotationEnd = OpenNest.Math.Angle.HalfPI,
        };
        var result = NestPipeline.Run(new NestPipelineRequest(engine, [item],
            NestStockBuilder.FromTemplate(template, null)));
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(quantity, Assert.Single(result.Raw.Fulfillment).Placed);

        var applied = NestPipelineCommit.ApplyToEmptyPlates(result, manager);

        Assert.True(applied.Count >= 2, $"expected several sheets, got {applied.Count}");
        foreach (var plate in applied)
        {
            Assert.Same(rail, plate.ClampRail);
            Assert.True(rail.Matches(plate));
            AssertClearOfClamps(plate);
        }
        if (quadrant == 1)
        {
            // The edge between clamps stays usable: some part sits in the clamp band.
            Assert.Contains(applied.SelectMany(p => p.Parts),
                p => p.BoundingBox.Bottom < 0.625 + EdgeSpacing);
        }

        using var stream = new MemoryStream();
        Assert.True(new NestWriter(nest).Write(stream));
        var loaded = new NestReader(new MemoryStream(stream.ToArray())).Read();

        Assert.Equal(rail, loaded.PlateDefaults.ClampRail);
        Assert.Equal(applied.Count, loaded.Plates.Count); // trailing empty sheet not written
        for (var i = 0; i < applied.Count; i++)
        {
            var reloaded = loaded.Plates[i];
            Assert.Same(loaded.PlateDefaults.ClampRail, reloaded.ClampRail);
            Assert.Equal(applied[i].ClampZones.Select(z => (z.Id, z.Bounds.X, z.Bounds.Y, z.Bounds.Length, z.Bounds.Width)),
                reloaded.ClampZones.Select(z => (z.Id, z.Bounds.X, z.Bounds.Y, z.Bounds.Length, z.Bounds.Width)));
            Assert.Equal(applied[i].Parts.Select(p => (p.Location, p.Rotation)),
                reloaded.Parts.Select(p => (p.Location, p.Rotation)));
            AssertClearOfClamps(reloaded);
        }
        Assert.Equal(3, loaded.CreatePlate().ClampZones.Count);
    }
}
