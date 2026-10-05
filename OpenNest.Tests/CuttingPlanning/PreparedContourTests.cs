using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class PreparedContourTests
{
    [Fact]
    public void Emit_RespectsEveryExplicitEntryAndContourOrder()
    {
        var source = Holes();
        var prepared = PreparedContours.Capture(source, ExplicitContourTests.Parameters());
        var choices = new[]
        {
            prepared.ClosestEntry(1, new Vector(20, 3)),
            prepared.ClosestEntry(0, new Vector(20, 3)),
            prepared.ClosestEntry(2, new Vector(20, 5))
        };
        var emitted = prepared.Emit(choices);
        var calls = emitted.Codes.OfType<SubProgramCall>().ToArray();
        Assert.Equal(new[] { new Vector(7, 3), new Vector(3, 3) }, calls.Select(c => c.Offset));
        var cuts = ExecutionMotionReader.Read(emitted, Vector.Zero, null, default).Motions
            .Where(m => m.Layer == LayerType.Display && !m.Rapid).ToArray();
        Assert.Equal(new Vector(8, 3), cuts[0].Start);
        Assert.Equal(new Vector(4, 3), cuts[1].Start);
        Assert.Equal(source.ToString(), Holes().ToString());
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("omitted")]
    [InlineData("foreign")]
    [InlineData("perimeter-first")]
    public void Emit_RejectsIncompleteOrForeignAccounting(string fault)
    {
        var prepared = PreparedContours.Capture(Holes(), ExplicitContourTests.Parameters());
        var choices = Enumerable.Range(0, 3).Select(i => prepared.ClosestEntry(i, new Vector(20, 5))).ToArray();
        switch (fault)
        {
            case "duplicate": choices[1] = choices[0]; break;
            case "omitted": choices = choices[..2]; break;
            case "foreign": choices[0] = PreparedContours.Capture(Holes(), ExplicitContourTests.Parameters()).ClosestEntry(0, Vector.Zero); break;
            case "perimeter-first": Array.Reverse(choices); break;
        }
        Assert.Throws<ArgumentException>(() => prepared.Emit(choices));
    }

    [Fact]
    public void Prepared_OwnsGeometryAndDeepSettings()
    {
        var source = Holes();
        var settings = ExplicitContourTests.Parameters();
        var prepared = PreparedContours.Capture(source, settings);
        var choices = Enumerable.Range(0, 3).Select(i => prepared.ClosestEntry(i, new Vector(20, 5))).ToArray();
        var before = ExplicitContourTests.Fingerprint(prepared.Emit(choices));
        source.Codes.Clear();
        ((LineLeadIn)settings.ExternalLeadIn).Length = 100;
        settings.TabsEnabled = true;
        Assert.Equal(before, ExplicitContourTests.Fingerprint(prepared.Emit(choices)));
    }

    [Theory]
    [MemberData(nameof(ExplicitContourTests.RetainedStyles), MemberType = typeof(ExplicitContourTests))]
    public void ExplicitEmission_OrdinarySingleContourMatchesLegacy(string style, bool reverse, bool internalContour)
    {
        var source = ExplicitContourTests.Square(reverse);
        var target = source.ToGeometry().First(e => e.Layer != SpecialLayers.Rapid);
        var parameters = ExplicitContourTests.Parameters(style);
        if (internalContour)
        {
            source.MoveTo(-5, -5); source.LineTo(-5, 15); source.LineTo(15, 15);
            source.LineTo(15, -5); source.LineTo(-5, -5);
            parameters.ExternalLeadIn = new NoLeadIn();
        }
        var prepared = PreparedContours.Capture(source, parameters);
        var choice = prepared.ClosestEntry(0, Vector.Zero);
        var choices = internalContour
            ? new[] { choice, prepared.Entry(1, 0, new Vector(-5, -5)) }
            : new[] { choice };
        var legacy = new ContourCuttingStrategy { Parameters = parameters }.ApplySingle(source,
            Vector.Zero, target, internalContour ? ContourType.Internal : ContourType.External).Program;
        Assert.Equal(ExplicitContourTests.Fingerprint(legacy), ExplicitContourTests.Fingerprint(prepared.Emit(choices)));
    }

    [Fact]
    public void Entries_AreStableBoundedNativeAndGeometricallyUnique()
    {
        var prepared = PreparedContours.Capture(ExplicitContourTests.Square(false), new CuttingParameters());
        var approach = new Vector(-1, 5);
        var entries = prepared.Entries(0, approach);
        Assert.Equal(prepared.ClosestEntry(0, approach), entries[0]);
        Assert.Equal(new[] { new Vector(0, 5), new Vector(0, 0), new Vector(0, 10),
            new Vector(5, 10), new Vector(10, 10), new Vector(10, 5), new Vector(10, 0), new Vector(5, 0) },
            entries.Select(e => e.Point));
        Assert.Equal(entries, prepared.Entries(0, approach));
        Assert.Equal(entries.Take(3), prepared.Entries(0, approach, 3));
        Assert.Single(prepared.Entries(0, approach, 1));
        Assert.Throws<NotSupportedException>(() => ((IList<ContourChoice>)entries).Clear());
        Assert.Throws<ArgumentOutOfRangeException>(() => prepared.Entries(0, approach, 0));
        Assert.Throws<OperationCanceledException>(() => prepared.Entries(0, approach,
            token: new CancellationToken(true)));
        Assert.Throws<OperationCanceledException>(() => PreparedContours.Capture(
            ExplicitContourTests.Square(false), new CuttingParameters(), new CancellationToken(true)));
    }

    [Fact]
    public void Entries_IncludeNativeArcMidpointAndCircleAngles()
    {
        var source = new Program();
        source.MoveTo(1, 0); source.ArcTo(-1, 0, 0, 0, RotationType.CCW); source.LineTo(1, 0);
        var prepared = PreparedContours.Capture(source, new CuttingParameters());
        var entries = prepared.Entries(0, new Vector(0, -2));
        Assert.Contains(entries, e => e.Point.DistanceTo(new Vector(0, 1)) < 1e-8);
        Assert.Contains(entries, e => e.Point.DistanceTo(new Vector(-1, 0)) < 1e-8);
        prepared = PreparedContours.Capture(Holes(), new CuttingParameters());
        var circles = prepared.Entries(0, new Vector(5, 5));
        Assert.InRange(circles.Count, 8, 9);
        Assert.Equal(circles, prepared.Entries(0, new Vector(5, 5)));
        Assert.All(circles, e => Assert.Equal(1, e.Point.DistanceTo(new Vector(3, 3)), 8));
        Assert.Equal(circles.Count, circles.Select(e => e.Point).Distinct().Count());
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(3, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 1, 3, 3)]
    [InlineData(0, 0, 3, 3)]
    [InlineData(0, 0, double.NaN, 3)]
    [InlineData(0, 0, double.PositiveInfinity, 3)]
    public void Entry_RejectsInvalidNativeChoices(int contour, int entity, double x, double y)
    {
        var prepared = PreparedContours.Capture(Holes(), new CuttingParameters());
        Assert.Throws<ArgumentException>(() => prepared.Entry(contour, entity, new Vector(x, y)));
    }

    [Fact]
    public void Emit_RevalidatesRecordCopiesAndRejectsUnownedRecords()
    {
        var prepared = PreparedContours.Capture(ExplicitContourTests.Square(false), new CuttingParameters());
        var choice = prepared.ClosestEntry(0, Vector.Zero);
        Assert.Throws<ArgumentException>(() => prepared.Emit([choice with { EntityOrdinal = 100 }]));
        Assert.Throws<ArgumentException>(() => prepared.Emit([choice with { Point = new Vector(5, 5) }]));
        Assert.Throws<ArgumentException>(() => prepared.Emit([new ContourChoice(0, 0, Vector.Zero)]));
        Assert.Throws<ArgumentException>(() => prepared.Emit([null]));
    }

    [Fact]
    public void Capture_AccountsForDuplicateContoursWithoutJoiningOrDroppingThem()
    {
        var source = ExplicitContourTests.Square(false);
        foreach (var ignored in new[] { 0, 1 })
        {
            source.MoveTo(2, 2); source.LineTo(2, 4); source.LineTo(4, 4);
            source.LineTo(4, 2); source.LineTo(2, 2);
            source.MoveTo(7, 7); source.ArcTo(7, 7, 6, 7, RotationType.CCW);
        }
        var prepared = PreparedContours.Capture(source, new CuttingParameters());
        Assert.Equal(5, prepared.Count);
        var choices = Enumerable.Range(0, 5).Select(i => prepared.ClosestEntry(i, Vector.Zero)).ToArray();
        var motions = ExecutionMotionReader.Read(prepared.Emit(choices), Vector.Zero, null, default).Motions;
        Assert.Equal(14, motions.Count(m => !m.Rapid && m.Layer == LayerType.Display));
    }

    [Theory]
    [InlineData("open")]
    [InlineData("lead")]
    [InlineData("layer")]
    [InlineData("derived")]
    [InlineData("mode")]
    public void Capture_RefusesUnsupportedOrUncleanExecution(string fault)
    {
        var source = ExplicitContourTests.Square(false);
        if (fault == "open") source.Codes.RemoveAt(source.Codes.Count - 1);
        if (fault == "lead") ((LinearMove)source.Codes[1]).Layer = LayerType.Leadin;
        if (fault == "layer") ((LinearMove)source.Codes[1]).Layer = (LayerType)100;
        if (fault == "derived") source.Codes[1] = new CustomLinearMove();
        if (fault == "mode") typeof(Program).GetField("mode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(source, (Mode)100);
        Assert.ThrowsAny<Exception>(() => PreparedContours.Capture(source, new CuttingParameters()));
    }

    private sealed class CustomLinearMove : LinearMove { }

    [Fact]
    public void ExplicitCircle_RoundsActualEntryClampsAndSharesOwnedSubprograms()
    {
        var source = Holes();
        var parameters = ExplicitContourTests.Parameters();
        parameters.ArcCircleLeadIn = new LineLeadIn { Length = 10 };
        parameters.RoundLeadInAngles = true;
        parameters.LeadInAngleIncrement = 90;
        var prepared = PreparedContours.Capture(source, parameters);
        var choices = new[] { prepared.ClosestEntry(0, new Vector(5, 5)),
            prepared.ClosestEntry(1, new Vector(9, 5)), prepared.ClosestEntry(2, Vector.Zero) };
        var result = prepared.Emit(choices);
        var calls = result.Codes.OfType<SubProgramCall>().ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Same(calls[0].Program, calls[1].Program);
        var motions = ExecutionMotionReader.Read(result, Vector.Zero, null, default).Motions;
        var leads = motions.Where(m => m.Layer == LayerType.Leadin).Take(2).ToArray();
        Assert.All(leads, m => Assert.True(m.Length < 2));
        Assert.True(choices[0].Point.DistanceTo(leads[0].End) > 0.1);
        Assert.Equal(new Vector(4, 3), leads[0].End);
        Assert.True(leads[0].Start!.Value.DistanceTo(new Vector(3, 3)) <= 0.95 + 1e-8);
        var before = ExplicitContourTests.Fingerprint(prepared.Emit(choices));
        calls[0].Program.Codes.Clear(); result.Codes.Clear(); source.Codes.Clear();
        Assert.Equal(before, ExplicitContourTests.Fingerprint(prepared.Emit(choices)));
        Assert.NotSame(calls[0].Program, prepared.Emit(choices).Codes.OfType<SubProgramCall>().First().Program);
    }

    [Theory]
    [InlineData("normal")]
    [InlineData("breaker")]
    [InlineData("machine")]
    public void ExplicitTab_RetainsLegacyGapAndOwnsTabSize(string style)
    {
        var parameters = ExplicitContourTests.Parameters();
        parameters.TabsEnabled = true;
        parameters.TabConfig = style switch
        {
            "breaker" => new BreakerTab { Size = 0.2 },
            "machine" => new MachineTab { Size = 0.2 },
            _ => new NormalTab { Size = 0.2 }
        };
        var source = ExplicitContourTests.Square(false);
        var approach = new Vector(-1, 5);
        var prepared = PreparedContours.Capture(source, parameters);
        var choice = prepared.ClosestEntry(0, approach);
        var result = prepared.Emit([choice]);
        var legacy = new ContourCuttingStrategy { Parameters = parameters }.Apply(source, approach).Program;
        Assert.Equal(ExplicitContourTests.Fingerprint(legacy), ExplicitContourTests.Fingerprint(result));
        var cuts = ExecutionMotionReader.Read(result, Vector.Zero, null, default).Motions
            .Where(m => !m.Rapid && m.Layer == LayerType.Display).ToArray();
        Assert.True(cuts[0].Start!.Value.DistanceTo(cuts[^1].End) > 0.1);
        parameters.TabConfig.Size = 8; parameters.TabsEnabled = false;
        Assert.Equal(ExplicitContourTests.Fingerprint(result), ExplicitContourTests.Fingerprint(prepared.Emit([choice])));
    }

    [Fact]
    public void PrefixAndCompletePrograms_EmitScribesExactlyOnceAndOwnThem()
    {
        var source = Holes();
        source.MoveTo(1, 1); source.Codes.Add(new LinearMove(new Vector(2, 1)) { Layer = LayerType.Scribe });
        var prepared = PreparedContours.Capture(source, new CuttingParameters());
        var choices = Enumerable.Range(0, 3).Select(i => prepared.ClosestEntry(i, Vector.Zero)).ToArray();
        Assert.Throws<ArgumentException>(() => prepared.EmitPrefix([choices[2]]));
        foreach (var count in new[] { 0, 1, 2, 3 })
        {
            var result = prepared.EmitPrefix(choices.Take(count).ToArray());
            var motions = ExecutionMotionReader.Read(result, Vector.Zero, null, default).Motions;
            Assert.Single(motions.Where(m => m.Layer == LayerType.Scribe));
            result.Codes.Clear();
        }
        Assert.Single(ExecutionMotionReader.Read(prepared.Emit(choices), Vector.Zero, null, default).Motions
            .Where(m => m.Layer == LayerType.Scribe));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Capture_ExpandsSharedSubprogramFramesAndOwnsTheirMotion(bool incremental)
    {
        var hole = new Program();
        hole.MoveTo(1, 0); hole.ArcTo(1, 0, 0, 0, RotationType.CCW);
        if (incremental) hole.Mode = Mode.Incremental;
        var source = ExplicitContourTests.Square(false);
        source.Codes.Add(new SubProgramCall { Id = 1, Program = hole, Offset = new Vector(3, 3) });
        source.Codes.Add(new SubProgramCall { Id = 1, Program = hole, Offset = new Vector(7, 3) });
        var prepared = PreparedContours.Capture(source, ExplicitContourTests.Parameters());
        Assert.Equal(3, prepared.Count);
        var choices = Enumerable.Range(0, 3).Select(i => prepared.ClosestEntry(i, new Vector(20, 3))).ToArray();
        Assert.Equal(new Vector(4, 3), choices[0].Point);
        Assert.Equal(new Vector(8, 3), choices[1].Point);
        var before = ExplicitContourTests.Fingerprint(prepared.Emit(choices));
        hole.Codes.Clear(); source.Codes.Clear();
        Assert.Equal(before, ExplicitContourTests.Fingerprint(prepared.Emit(choices)));
    }

    [Fact]
    public void Prefix_RejectsDuplicateContoursEvenBeforeThePerimeter()
    {
        var prepared = PreparedContours.Capture(Holes(), new CuttingParameters());
        var choice = prepared.ClosestEntry(0, Vector.Zero);
        Assert.Throws<ArgumentException>(() => prepared.EmitPrefix([choice, choice]));
    }

    [Theory]
    [InlineData("none", false)]
    [InlineData("none", true)]
    [InlineData("line", false)]
    [InlineData("line", true)]
    [InlineData("arc", false)]
    [InlineData("arc", true)]
    public void ExplicitLeadOut_RetainsLegacyGeometryAndOwnership(string style, bool reversed)
    {
        var source = ExplicitContourTests.Square(reversed);
        var parameters = ExplicitContourTests.Parameters();
        parameters.ExternalLeadOut = style switch
        {
            "line" => new LineLeadOut { Length = 0.2, ApproachAngle = 45 },
            "arc" => new ArcLeadOut { Radius = 0.2 },
            _ => new NoLeadOut()
        };
        var approach = new Vector(-1, 5);
        var prepared = PreparedContours.Capture(source, parameters);
        var choice = prepared.ClosestEntry(0, approach);
        var emitted = prepared.Emit([choice]);
        var legacy = new ContourCuttingStrategy { Parameters = parameters }.Apply(source, approach).Program;
        Assert.Equal(ExplicitContourTests.Fingerprint(legacy), ExplicitContourTests.Fingerprint(emitted));
        if (parameters.ExternalLeadOut is LineLeadOut line) line.Length = 9;
        if (parameters.ExternalLeadOut is ArcLeadOut arc) arc.Radius = 9;
        Assert.Equal(ExplicitContourTests.Fingerprint(emitted), ExplicitContourTests.Fingerprint(prepared.Emit([choice])));
    }

    internal static Program Holes()
    {
        var p = ExplicitContourTests.Square(false);
        foreach (var x in new[] { 3.0, 7.0 })
        {
            p.MoveTo(x + 1, 3); p.ArcTo(x + 1, 3, x, 3, RotationType.CCW);
        }
        return p;
    }
}
