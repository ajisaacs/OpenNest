using System.Drawing;
using System.Drawing.Drawing2D;
using OpenNest.CNC;
using OpenNest.Controls;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.WinForms.Tests;

public class GraphicsHelperPlacementTests
{
    [Theory]
    [InlineData(Mode.Absolute, false)]
    [InlineData(Mode.Absolute, true)]
    [InlineData(Mode.Incremental, false)]
    [InlineData(Mode.Incremental, true)]
    public void PlacementTranslatesEveryOutlineAndLeadPointWithoutChangingProgram(Mode mode, bool splitPaths)
    {
        var program = new CNC.Program(Mode.Absolute);
        program.Codes.AddRange(new ICode[]
        {
            new RapidMove(-1, 0),
            new LinearMove(0, 0) { Layer = LayerType.Leadin },
            new LinearMove(0, 4),
            new LinearMove(3, 4),
            new ArcMove(4, 3, 3, 3, RotationType.CW),
            new LinearMove(4, 0),
            new LinearMove(0, 0),
            new ArcMove(-1, -1, -1, 0, RotationType.CW) { Layer = LayerType.Leadout },
            new RapidMove(3, 2),
            new ArcMove(3, 2, 2, 2),
        });
        program.Mode = mode;
        var before = NestWriter.GetProgramText(program);
        var instructions = program.Codes.ToArray();
        using var local = program.GetGraphicsPath();
        program.GetGraphicsPaths(Vector.Zero, out var localCut, out var localLead);
        using (localCut)
        using (localLead)
        {
            Assert.True(local.PointCount > 0);
            Assert.True(localCut.PointCount > 0);
            Assert.True(localLead.PointCount > 0);
            foreach (var origin in new[] { new Vector(10.25, 6.5), new Vector(-7.5, 12.25), Vector.Zero })
            {
                if (!splitPaths)
                {
                    using var placed = program.GetGraphicsPath(origin);
                    AssertTranslated(local, placed, origin);
                }
                else
                {
                    program.GetGraphicsPaths(origin, out var cut, out var lead);
                    using (cut)
                    using (lead)
                    {
                        AssertTranslated(localCut, cut, origin);
                        AssertTranslated(localLead, lead, origin);
                    }
                }
            }
        }
        Assert.Equal(before, NestWriter.GetProgramText(program));
        Assert.Equal(instructions, program.Codes);
        Assert.Equal(mode, program.Mode);
    }

    [Fact]
    public void IncrementalSubprogramOffsetsStayRelativeToProgramOrigin()
    {
        var hole = new CNC.Program(Mode.Incremental);
        hole.Codes.AddRange(new ICode[]
        {
            new RapidMove(1, 0),
            new ArcMove(0, 0, -1, 0),
        });
        var program = new CNC.Program(Mode.Incremental);
        program.Codes.Add(new SubProgramCall(hole, 0) { Offset = new Vector(3, 4) });
        program.Codes.Add(new SubProgramCall(hole, 0) { Offset = new Vector(9, 4) });
        var before = NestWriter.GetSubProgramsText(program);
        using var local = program.GetGraphicsPath();
        using var placed = program.GetGraphicsPath(new Vector(20, 30));
        AssertTranslated(local, placed, new Vector(20, 30));
        Assert.Equal(new RectangleF(22, 33, 8, 2), placed.GetBounds());
        program.GetGraphicsPaths(new Vector(20, 30), out var cut, out var lead);
        using (cut)
        using (lead)
        {
            AssertTranslated(local, cut, new Vector(20, 30));
            Assert.Equal(0, lead.PointCount);
        }
        Assert.Same(hole, ((SubProgramCall)program.Codes[0]).Program);
        Assert.Same(hole, ((SubProgramCall)program.Codes[1]).Program);
        Assert.Equal(before, NestWriter.GetSubProgramsText(program));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedAbsolutePartsFollowRepeatedMovesAndViewTransform(bool splitPaths) => StaTestThread.Run(() =>
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "absolute-coordinate-parts.nest");
        var nest = new NestReader(fixture).Read();
        Assert.Equal(2, nest.Plates.Count);
        Assert.Equal(3, nest.Plates.Sum(plate => plate.Parts.Count));
        using var view = new PlateView();
        view.Matrix.Reset();
        view.Matrix.Scale(2, -2);
        view.Matrix.Translate(40, 80, MatrixOrder.Append);
        foreach (var part in nest.Plates.SelectMany(plate => plate.Parts))
        {
            Assert.Equal(Mode.Absolute, part.Program.Mode);
            part.HasManualLeadIns = splitPaths;
            var before = NestWriter.GetProgramText(part.Program);
            var initial = part.Location;
            var layout = LayoutPart.Create(part, view);
            try
            {
                foreach (var location in new[] { initial, new Vector(4.25, 10.75), new Vector(15.5, 3.25), initial })
                {
                    part.Location = location;
                    layout.Path.Dispose();
                    layout.Update(view);
                    using var expected = part.Program.GetGraphicsPath();
                    using var translation = new Matrix();
                    translation.Translate((float)location.X, (float)location.Y);
                    expected.Transform(translation);
                    expected.Transform(view.Matrix);
                    AssertTranslated(expected, layout.Path, Vector.Zero);
                    if (splitPaths)
                        Assert.Equal(0, layout.LeadInPath.PointCount);
                    Assert.Equal(before, NestWriter.GetProgramText(part.Program));
                }
            }
            finally
            {
                layout.Path.Dispose();
                layout.LeadInPath?.Dispose();
            }
        }
    }, TimeSpan.FromMinutes(1), "Absolute-coordinate placement test did not complete.");

    private static void AssertTranslated(GraphicsPath local, GraphicsPath placed, Vector offset)
    {
        Assert.Equal(local.PointCount, placed.PointCount);
        Assert.Equal(local.PathTypes, placed.PathTypes);
        var expected = local.PathPoints;
        var actual = placed.PathPoints;
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.InRange(actual[i].X - (expected[i].X + offset.X), -0.00001, 0.00001);
            Assert.InRange(actual[i].Y - (expected[i].Y + offset.Y), -0.00001, 0.00001);
        }
    }
}
