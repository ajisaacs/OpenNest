using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.Tests.Diagnostics;

public class PostVerificationAnalyzerTests
{
    [Fact]
    public void EmptyReport_ListsAllChecksAndLimits()
    {
        var report = PostVerificationAnalyzer.Analyze(new Nest());
        Assert.Empty(report.Findings);
        Assert.False(report.HasWarnings);
        Assert.True(report.CanPost(false));
        Assert.True(report.CanPost(true));
        var text = report.ToDisplayText();
        Assert.Contains("Overlap", text);
        Assert.Contains("Missing lead-ins", text);
        Assert.Contains("Rapid crossings", text);
        Assert.Contains("not a physical safety certification", text);
        Assert.Contains("retract", text);
        Assert.Contains("routing", text);
    }

    [Fact]
    public void Overlap_UsesCleanMaterialAndOriginalOneBasedIndices()
    {
        var a = Rectangle(0, 0, 4);
        var b = Rectangle(1, 1, 1);
        // Deliberately unrelated placed toolpaths: overlap must use clean drawings.
        a.Program.Codes.Clear();
        a.Program.MoveTo(100, 100);
        a.Program.LineTo(101, 101);
        var report = Analyze(Cutoff(30, 30), a, Scribe(40, 40), b);
        var overlap = Assert.Single(Find(report, PostVerificationKind.Overlap));
        Assert.Equal((1, 2, 4), (overlap.PlateNumber, overlap.PartNumber, overlap.OtherPartNumber));
        Assert.True(report.HasWarnings);
        Assert.False(report.CanPost(false));
        Assert.True(report.CanPost(true));
        Assert.False(report.CanPost(false)); // Consent is supplied per invocation, never latched.
    }

    [Fact]
    public void Overlap_SubtractsHoles()
    {
        var program = Square(0, 0, 10);
        program.Codes.AddRange(Square(2, 2, 6).Codes);
        var frame = Part(program);
        var report = Analyze(frame, Rectangle(3, 3, 1));
        Assert.Empty(Find(report, PostVerificationKind.Overlap));
        Assert.Empty(Find(report, PostVerificationKind.Incomplete));
    }

    [Fact]
    public void MissingLeadIn_ManualFlagDoesNotProveCoverage()
    {
        var part = Rectangle(0, 0, 4);
        part.HasManualLeadIns = true;
        Assert.Single(Find(Analyze(part), PostVerificationKind.MissingLeadIn));
    }

    [Fact]
    public void MissingLeadIn_ActualLeadDoesNotRequireManualFlag()
    {
        var part = Rectangle(0, 0, 4);
        part.ApplyLeadIns(Leads(), new Vector(-2, -2));
        part.HasManualLeadIns = false;
        Assert.Empty(Find(Analyze(part), PostVerificationKind.MissingLeadIn));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingLeadIn_NoLeadOrZeroLengthWarns(bool zeroLength)
    {
        var part = Rectangle(0, 0, 4);
        var parameters = Leads();
        parameters.ExternalLeadIn = zeroLength ? new LineLeadIn { Length = 0 } : new NoLeadIn();
        part.ApplyLeadIns(parameters, new Vector(-2, -2));
        Assert.Single(Find(Analyze(part), PostVerificationKind.MissingLeadIn));
    }

    [Fact]
    public void MissingLeadIn_PartialApplySingleLeavesOtherContourUncovered()
    {
        var program = Square(0, 0, 10);
        program.Codes.AddRange(Square(2, 2, 2).Codes);
        var part = Part(program);
        part.ApplySingleLeadIn(Leads(), Vector.Zero,
            new Line(Vector.Zero, new Vector(10, 0)), ContourType.External);
        Assert.True(part.HasManualLeadIns);
        Assert.Single(Find(Analyze(part), PostVerificationKind.MissingLeadIn));
    }

    [Fact]
    public void ScribeAndCutoff_AreExemptFromOverlapAndLeadInChecks()
    {
        var report = Analyze(Scribe(0, 0), Cutoff(0, 0));
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void Rapid_ThroughPreviouslyCutPartWarns()
    {
        var report = Analyze(Rectangle(0, 0, 4), Scribe(-2, 2), Scribe(6, 2));
        Assert.Contains(Find(report, PostVerificationKind.RapidCrossing), f =>
            f.PartNumber == 3 && f.OtherPartNumber == 1);
    }

    [Fact]
    public void Rapid_DoesNotUseFuturePartsAsObstacles()
    {
        var report = Analyze(Scribe(-2, 2), Scribe(6, 2), Rectangle(0, 0, 4));
        Assert.Empty(Find(report, PostVerificationKind.RapidCrossing));
    }

    [Theory]
    [InlineData(-2, -2, false)] // Start-only departure.
    [InlineData(2, 2, true)] // Departure into the interior.
    [InlineData(4, 0, true)] // Along edge / arrival on another endpoint.
    public void Rapid_HandlesDepartureVersusInteriorAndBoundary(double x, double y, bool warning)
    {
        var report = Analyze(Rectangle(0, 0, 4), Scribe(x, y));
        Assert.Equal(warning, Find(report, PostVerificationKind.RapidCrossing).Any());
    }

    [Theory]
    [InlineData(-2, 4, 6, 4)] // Tangent along top edge.
    [InlineData(-2, 2, 0, 2)] // Endpoint arrival.
    [InlineData(1, 1, 2, 2)] // Entirely inside.
    public void Rapid_ConservativelyFlagsContactAndInterior(double x1, double y1, double x2, double y2)
    {
        var report = Analyze(Rectangle(0, 0, 4), Scribe(x1, y1), Scribe(x2, y2));
        Assert.Contains(Find(report, PostVerificationKind.RapidCrossing), f => f.PartNumber == 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rapid_ActualGapNotStaleTabSettingControlsObstacle(bool staleFlag)
    {
        var closed = Rectangle(0, 0, 4);
        closed.CuttingParameters = new CuttingParameters { TabsEnabled = staleFlag };
        Assert.Contains(Find(Analyze(closed, Scribe(-2, 2), Scribe(6, 2)),
            PostVerificationKind.RapidCrossing), f => f.PartNumber == 3);

        var gapped = Rectangle(0, 0, 4);
        ((LinearMove)gapped.Program.Codes[^1]).EndPoint = new Vector(0, 0.25);
        gapped.CuttingParameters = new CuttingParameters { TabsEnabled = staleFlag };
        Assert.Empty(Find(Analyze(gapped, Scribe(-2, 2), Scribe(6, 2)),
            PostVerificationKind.RapidCrossing));
    }

    [Fact]
    public void Rapid_RealGeneratedTabRemainsGapWithStaleDisabledParameters()
    {
        var part = Rectangle(0, 0, 4);
        var parameters = Leads();
        parameters.TabsEnabled = true;
        parameters.TabConfig = new NormalTab { Size = 0.25 };
        part.ApplyLeadIns(parameters, new Vector(-2, 0));
        parameters.TabsEnabled = false;
        Assert.Empty(Find(Analyze(part, Scribe(-2, 2), Scribe(6, 2)), PostVerificationKind.RapidCrossing));
    }

    [Fact]
    public void Rapid_CutoffTravelStillCrossesEarlierRealCuts()
    {
        var report = Analyze(Rectangle(0, 0, 4), Scribe(-2, 2), Cutoff(6, 2));
        Assert.Contains(Find(report, PostVerificationKind.RapidCrossing), f =>
            f.PartNumber == 3 && f.OtherPartNumber == 1);
    }

    [Theory]
    [InlineData(Mode.Absolute)]
    [InlineData(Mode.Incremental)]
    public void Rapid_SamePartSharedHoleCallsUseOffsetsAndNonzeroLeadOrigins(Mode mode)
    {
        var part = Rectangle(100, 50, 20);
        var hole = CircleWithLead();
        hole.Mode = mode;
        var placed = new Program();
        placed.Codes.Add(new SubProgramCall { Program = hole, Offset = new Vector(5, 5), Id = 7 });
        placed.MoveTo(2, 5);
        placed.LineTo(2, 5); // Zero-length cut doesn't create an obstacle.
        placed.MoveTo(8, 5); // Cross completed first hole.
        placed.Codes.Add(new SubProgramCall { Program = hole, Offset = new Vector(15, 5), Id = 7 });
        placed.Mode = mode;
        Assert.True(part.RestoreLeadInProgram(placed, false));
        var report = Analyze(part);
        Assert.Contains(Find(report, PostVerificationKind.RapidCrossing), f =>
            f.PartNumber == 1 && f.OtherPartNumber == 1);
        Assert.Empty(Find(report, PostVerificationKind.Incomplete));
    }

    [Theory]
    [InlineData(Mode.Absolute)]
    [InlineData(Mode.Incremental)]
    public void Rapid_NonzeroInitialLeadRapidIsNotAppliedTwice(Mode mode)
    {
        var part = Rectangle(100, 50, 4);
        var placed = Square(0, 0, 4);
        placed.Codes.Insert(0, new RapidMove(-2, 0));
        placed.Codes[1] = new LinearMove(0, 0) { Layer = LayerType.Leadin };
        placed.MoveTo(-2, 2);
        placed.MoveTo(6, 2);
        placed.Mode = mode;
        part.RestoreLeadInProgram(placed, false);
        var crossings = Find(Analyze(part), PostVerificationKind.RapidCrossing);
        Assert.Single(crossings);
        Assert.Empty(Find(Analyze(part), PostVerificationKind.MissingLeadIn));
    }

    [Theory]
    [InlineData(RotationType.CW)]
    [InlineData(RotationType.CCW)]
    public void Rapid_FullCircleArcTangentIsNotLostToChordApproximation(RotationType rotation)
    {
        var program = new Program();
        program.MoveTo(1, 0);
        program.ArcTo(1, 0, 0, 0, rotation);
        var report = Analyze(Part(program), Scribe(-2, 1), Scribe(2, 1));
        Assert.Contains(Find(report, PostVerificationKind.RapidCrossing), f => f.PartNumber == 3);
    }

    [Fact]
    public void Rapid_SamePartFutureContourIsNotAnObstacle()
    {
        var part = Rectangle(0, 0, 10);
        var placed = new Program();
        placed.MoveTo(-2, 2);
        placed.MoveTo(6, 2);
        placed.Codes.AddRange(Square(0, 0, 4).Codes);
        part.RestoreLeadInProgram(placed, false);
        Assert.Empty(Find(Analyze(part), PostVerificationKind.RapidCrossing));
    }

    [Fact]
    public void MultiplePlates_AreAllCheckedWithoutSharingObstacles()
    {
        var nest = NestWith(Rectangle(0, 0, 4));
        var second = nest.CreatePlate();
        second.Parts.Add(Scribe(-2, 2));
        second.Parts.Add(Scribe(6, 2));
        second.Parts.Add(Rectangle(0, 0, 4));
        second.Parts.Add(Rectangle(1, 1, 1));
        var report = PostVerificationAnalyzer.Analyze(nest);
        Assert.Contains(Find(report, PostVerificationKind.MissingLeadIn), f => f.PlateNumber == 1);
        Assert.Contains(Find(report, PostVerificationKind.MissingLeadIn), f => f.PlateNumber == 2);
        Assert.All(Find(report, PostVerificationKind.Overlap), f => Assert.Equal(2, f.PlateNumber));
        Assert.DoesNotContain(Find(report, PostVerificationKind.RapidCrossing), f =>
            f.PlateNumber == 2 && f.PartNumber <= 3);
    }

    [Theory]
    [InlineData("recursive")]
    [InlineData("missing-call")]
    [InlineData("null-code")]
    [InlineData("nonfinite")]
    [InlineData("bad-arc")]
    [InlineData("empty")]
    [InlineData("null-codes")]
    public void MalformedPlacedPrograms_WarnInsteadOfClearing(string fault)
    {
        var part = Rectangle(0, 0, 4);
        var program = part.Program;
        switch (fault)
        {
            case "recursive": program.Codes.Add(new SubProgramCall { Program = program }); break;
            case "missing-call": program.Codes.Add(new SubProgramCall()); break;
            case "null-code": program.Codes.Add(null); break;
            case "nonfinite": program.MoveTo(double.NaN, 0); break;
            case "bad-arc": program.ArcTo(1, 1, 0, 0, RotationType.CW); break;
            case "empty": program.Codes.Clear(); break;
            case "null-codes": program.Codes = null; break;
        }
        var report = Analyze(part);
        Assert.NotEmpty(Find(report, PostVerificationKind.Incomplete));
        Assert.True(report.HasWarnings);
        Assert.False(report.CanPost(false));
        Assert.DoesNotContain("No warnings", report.ToDisplayText());
    }

    [Fact]
    public void MalformedCleanProgram_OverlapIssuePreservesInputIndex()
    {
        var part = Rectangle(0, 0, 4);
        part.BaseDrawing.Program.Codes.RemoveAt(4);
        var report = Analyze(Cutoff(30, 30), part);
        Assert.Contains(Find(report, PostVerificationKind.Incomplete), f => f.PartNumber == 2);
    }

    [Fact]
    public void Rapid_ResumedContourBecomesObstacleOnlyAfterItsLastCut()
    {
        var part = Rectangle(0, 0, 4);
        var placed = Square(0, 0, 4);
        placed.Codes.Insert(3, new RapidMove(4, 4)); // Pause, with no uncut gap.
        placed.MoveTo(-2, 2);
        placed.MoveTo(6, 2);
        part.RestoreLeadInProgram(placed, false);
        Assert.Single(Find(Analyze(part), PostVerificationKind.RapidCrossing));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rapid_InternalGapDoesNotBecomeAnObstacle(bool staleFlag)
    {
        var part = Rectangle(0, 0, 4);
        var placed = Square(0, 0, 4);
        placed.Codes.Insert(3, new RapidMove(3.75, 4)); // Leave a gap on the top edge.
        placed.MoveTo(-2, 2);
        placed.MoveTo(6, 2);
        part.RestoreLeadInProgram(placed, false);
        part.CuttingParameters = new CuttingParameters { TabsEnabled = staleFlag };
        Assert.Empty(Find(Analyze(part), PostVerificationKind.RapidCrossing));
    }

    [Fact]
    public void MissingPlacedCuts_CannotPassForACleanMaterialPart()
    {
        var part = Rectangle(0, 0, 4);
        part.Program.Codes.Clear();
        part.Program.MoveTo(0, 0);
        var report = Analyze(part);
        Assert.NotEmpty(Find(report, PostVerificationKind.Incomplete));
        Assert.False(report.CanPost(false));
    }

    [Fact]
    public void InvalidArcDirection_IsIncomplete()
    {
        var part = Part(CircleWithLead());
        ((ArcMove)part.Program.Codes[^1]).Rotation = (RotationType)99;
        Assert.NotEmpty(Find(Analyze(part), PostVerificationKind.Incomplete));
    }

    [Fact]
    public void Rapid_LeadOutThatCompletesTabGapIsNotTreatedAsRetained()
    {
        var part = Rectangle(0, 0, 4);
        ((LinearMove)part.Program.Codes[^1]).EndPoint = new Vector(0, 0.25);
        part.Program.Codes.Add(new LinearMove(0, 0) { Layer = LayerType.Leadout });
        var report = Analyze(part, Scribe(-2, 2), Scribe(6, 2));
        Assert.True(Find(report, PostVerificationKind.RapidCrossing).Any()
            || Find(report, PostVerificationKind.Incomplete).Any());
    }

    [Fact]
    public void Rapid_OutOfOrderFragmentsCannotSilentlyPassAsTabbed()
    {
        var part = Rectangle(0, 0, 4);
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(4, 0);
        program.MoveTo(4, 4);
        program.LineTo(0, 4);
        program.MoveTo(4, 0);
        program.LineTo(4, 4);
        program.MoveTo(0, 4);
        program.LineTo(0, 0);
        part.RestoreLeadInProgram(program, false);
        var report = Analyze(part, Scribe(-2, 2), Scribe(6, 2));
        Assert.True(Find(report, PostVerificationKind.RapidCrossing).Any()
            || Find(report, PostVerificationKind.Incomplete).Any());
    }

    [Fact]
    public void Cancellation_ThrowsWithoutChangingInputs()
    {
        var part = Rectangle(0, 0, 4);
        var before = part.Program.ToString();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            PostVerificationAnalyzer.Analyze(NestWith(part), cancellation.Token));
        Assert.Equal(before, part.Program.ToString());
    }

    [Fact]
    public void Analyze_PreservesProgramsPosesSharedCallsAndCuttingState()
    {
        var part = Rectangle(100, 50, 20);
        var sub = CircleWithLead();
        var program = Square(0, 0, 20);
        program.Codes.Insert(0, new SubProgramCall { Program = sub, Offset = new Vector(5, 5) });
        program.Codes.Insert(1, new SubProgramCall { Program = sub, Offset = new Vector(15, 5) });
        program.Mode = Mode.Incremental;
        part.RestoreLeadInProgram(program, true);
        part.CuttingParameters = Leads();
        var nest = NestWith(part);
        var codes = program.Codes.ToArray();
        var before = program.ToString();
        var beforeSub = sub.ToString();
        var parameters = part.CuttingParameters;
        var bounds = part.BoundingBox;
        var quantity = part.BaseDrawing.Quantity.Nested;
        var report = PostVerificationAnalyzer.Analyze(nest);
        Assert.Same(program, part.Program);
        Assert.Equal(codes, program.Codes);
        Assert.Equal(before, program.ToString());
        Assert.Equal(beforeSub, sub.ToString());
        Assert.Same(sub, ((SubProgramCall)program[0]).Program);
        Assert.Same(sub, ((SubProgramCall)program[1]).Program);
        Assert.Equal(new Vector(100, 50), part.Location);
        Assert.Same(bounds, part.BoundingBox);
        Assert.True(part.HasManualLeadIns);
        Assert.True(part.LeadInsLocked);
        Assert.Same(parameters, part.CuttingParameters);
        Assert.Equal(quantity, part.BaseDrawing.Quantity.Nested);
        Assert.True(((ICollection<PostVerificationFinding>)report.Findings).IsReadOnly);
        var display = report.ToDisplayText();
        part.Program.Codes.Clear();
        Assert.Equal(display, report.ToDisplayText());
    }

    private static PostVerificationFinding[] Find(PostVerificationReport report, PostVerificationKind kind) =>
        report.Findings.Where(f => f.Kind == kind).ToArray();

    private static PostVerificationReport Analyze(params Part[] parts) =>
        PostVerificationAnalyzer.Analyze(NestWith(parts));

    private static Nest NestWith(params Part[] parts)
    {
        var nest = new Nest();
        var plate = nest.CreatePlate();
        foreach (var part in parts)
            plate.Parts.Add(part);
        return nest;
    }

    private static Part Part(Program program) => new(new Drawing("fixture", program));

    private static Part Rectangle(double x, double y, double size) =>
        new(new Drawing("rectangle", Square(0, 0, size)), new Vector(x, y));

    private static Program Square(double x, double y, double size)
    {
        var program = new Program();
        program.MoveTo(x, y);
        program.LineTo(x + size, y);
        program.LineTo(x + size, y + size);
        program.LineTo(x, y + size);
        program.LineTo(x, y);
        return program;
    }

    private static Part Scribe(double x, double y)
    {
        var program = new Program();
        program.MoveTo(x, y);
        program.Codes.Add(new LinearMove(x, y) { Layer = LayerType.Scribe });
        return Part(program);
    }

    private static Part Cutoff(double x, double y)
    {
        var program = new Program();
        program.MoveTo(x, y);
        program.LineTo(x + 1, y);
        return new Part(new Drawing("cutoff", program) { IsCutOff = true });
    }

    private static Program CircleWithLead()
    {
        var program = new Program();
        program.MoveTo(0.5, 0);
        program.Codes.Add(new LinearMove(1, 0) { Layer = LayerType.Leadin });
        program.Codes.Add(new ArcMove(1, 0, 0, 0) { Layer = LayerType.Display });
        return program;
    }

    private static CuttingParameters Leads() => new()
    {
        ExternalLeadIn = new LineLeadIn { Length = 0.5 },
        InternalLeadIn = new LineLeadIn { Length = 0.5 },
        ArcCircleLeadIn = new LineLeadIn { Length = 0.5 }
    };
}
