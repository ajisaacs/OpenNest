using System.Text;
using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Geometry;
using OpenNest.IO;

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
        Assert.Contains("Inspect the posted machine program", text);
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
        Assert.NotNull(overlap.Overlap);
        Assert.Equal(new Vector(1.5, 1.5), overlap.Location);
        Assert.Equal(1, overlap.Overlap.Area, 6);
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
        var finding = Assert.Single(Find(Analyze(part), PostVerificationKind.MissingLeadIn));
        Assert.Equal(Vector.Zero, finding.Location);
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
    public void ManualCutoffAndRapidCrossingClampRequireFreshPostingConsent()
    {
        var nest = NestWith(Rectangle(50, 20, 4));
        var plate = Assert.Single(nest.Plates);
        plate.ClampZones.Add(new ClampZone("front-3", new Box(10, 0, 1.5, 0.625)));
        var cutoff = new CutOff(new Vector(10.5, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(cutoff);
        plate.RegenerateCutOffs(new CutOffSettings());
        var report = PostVerificationAnalyzer.Analyze(nest);
        Assert.Contains(report.Findings, f => f.Kind == PostVerificationKind.Overlap
            && f.Message.Contains("front-3") && f.Message.Contains("cut-off") && f.PlateNumber == 1);
        Assert.Contains(report.Findings, f => f.Kind == PostVerificationKind.RapidCrossing
            && f.Message.Contains("front-3") && f.RapidStart.HasValue && f.RapidEnd.HasValue);
        Assert.False(report.CanPost(false));
        Assert.True(report.CanPost(true));
        Assert.False(PostVerificationAnalyzer.Analyze(nest).CanPost(false));
        plate.ClampZones.Clear();
        var withoutClamps = PostVerificationAnalyzer.Analyze(nest);
        Assert.DoesNotContain(withoutClamps.Findings, f => f.Message.Contains("front-3"));
    }

    [Fact]
    public void InvalidManualCutoffGeometryIsIncompleteNotClear()
    {
        var nest = NestWith(Rectangle(40, 20, 4));
        var plate = nest.Plates[0];
        plate.ClampZones.Add(new ClampZone("front", new Box(10, 0, 1.5, 0.625)));
        plate.CutOffs.Add(new CutOff(new Vector(double.NaN, 0), CutOffAxis.Vertical));
        var report = PostVerificationAnalyzer.Analyze(nest);
        Assert.Contains(report.Findings, f => f.Kind == PostVerificationKind.Incomplete
            && f.Message.Contains("Clamp/cut-off check"));
        Assert.False(report.CanPost(false));
    }

    [Fact]
    public void PartMaterialOnClampAppearsInPrePostOverlapFindings()
    {
        var nest = NestWith(Rectangle(10, 0, 4));
        nest.Plates[0].ClampZones.Add(new ClampZone("front-1", new Box(10, 0, 1.5, 0.625)));
        var finding = Assert.Single(PostVerificationAnalyzer.Analyze(nest).Findings.Where(f =>
            f.Kind == PostVerificationKind.Overlap && f.Message.Contains("front-1")));
        Assert.Equal(1, finding.PartNumber);
        Assert.Null(finding.OtherPartNumber);
        Assert.NotNull(finding.Overlap);
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
        var crossing = Assert.Single(Find(report, PostVerificationKind.RapidCrossing).Where(f =>
            f.PartNumber == 3 && f.OtherPartNumber == 1));
        Assert.NotNull(crossing.RapidStart);
        Assert.NotNull(crossing.RapidEnd);
        Assert.Equal(new[] { new Vector(0, 2), new Vector(4, 2) }, crossing.ContactPoints);
        Assert.Equal((crossing.RapidStart.Value + crossing.RapidEnd.Value) * 0.5, crossing.Location);
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

    [Theory]
    [InlineData(2.5, true)] // The overcut retraces part of the first cut: the square is cut free.
    [InlineData(3.5, false)] // It runs on past the whole first cut: not a recognized overcut.
    public void Rapid_SeamOvercutAlongTheFirstCutClosesTheContour(double overcutEnd, bool closed)
    {
        var part = Rectangle(0, 0, 4);
        var program = new Program();
        program.MoveTo(2, 0);
        program.LineTo(3, 0);
        program.LineTo(4, 0);
        program.LineTo(4, 4);
        program.LineTo(0, 4);
        program.LineTo(0, 0);
        program.LineTo(overcutEnd, 0);
        part.RestoreLeadInProgram(program, false);
        var report = Analyze(part, Scribe(-2, 2), Scribe(6, 2));
        Assert.Equal(closed, Find(report, PostVerificationKind.RapidCrossing).Any(f => f.PartNumber == 3));
        Assert.Empty(Find(report, PostVerificationKind.Incomplete));
    }

    // P260810-02's SULLYS-009, as converted from a source CAM: each hole's arc runs about 0.03 past
    // its pierce, the slot's last line runs 0.03 along its first, and the perimeter keeps a 0.02 tab.
    // Arc centers carry their own endpoint rounding, so a hole's two halves disagree by about 1e-7.
    [Fact]
    public void Rapid_ConvertedSeamOvercutsAreCheckedNotLeftIncomplete()
    {
        var part = new Part(new Drawing("SULLYS-009", ReadProgram(Sullys009Clean)));
        Assert.True(part.RestoreLeadInProgram(ReadProgram(Sullys009Cut), true));
        var report = Analyze(part);
        Assert.Empty(Find(report, PostVerificationKind.Incomplete));
        // Holes 2 and 4 really are cut free and then crossed by the rapid leaving them; holes 1
        // and 3 are left from their overcut end without touching them again.
        Assert.Equal(new[] { 2, 4 }, Find(report, PostVerificationKind.RapidCrossing)
            .Select(f => int.Parse(f.Message.Split("contour ")[1].Split(' ')[0])).Order());
    }

    private static Program ReadProgram(string text)
    {
        var reader = new ProgramReader(new MemoryStream(Encoding.UTF8.GetBytes(text)));
        var program = reader.Read();
        reader.Close();
        return program;
    }

    private const string Sullys009Clean = """
        G91
        G00X0.07Y0
        G03X-0.14Y0I-0.07J0
        G03X0.133669Y0.02909I0.0699998916J-0.0000000236
        G01X0.006331Y-0.02909
        G00X0.11Y0
        G03X0.14Y0I0.07J0
        G03X-0.133669Y-0.02909I-0.0699998916J0.0000000236
        G01X-0.006331Y0.02909
        G00X0Y0.5
        G03X0.14Y0I0.07J0
        G03X-0.133669Y-0.02909I-0.0699998916J0.0000000236
        G01X-0.006331Y0.02909
        G00X-0.18Y-0.07
        G03X0Y0.14I0J0.07
        G03X0.02909Y-0.133669I-0.0000000236J-0.0699998916
        G01X-0.02909Y-0.006331
        G00X-1.3285Y-0.394
        G01X0.4765Y0
        G03X0Y0.428I0J0.214
        G01X-0.953Y0
        G03X0Y-0.428I0J-0.214
        G01X0.5065Y0
        G01X-0.03Y0
        G00X-0.2665Y-0.411
        G01X-0.03Y0
        G02X0Y1.25I0J0.625
        G01X2.1875Y0
        G02X0.0625Y-0.0625I0J-0.0625
        G01X0Y-1.125
        G02X-0.0625Y-0.0625I-0.0625J0
        G01X-2.1375Y0
        G01X-0.02Y0
        """;

    private const string Sullys009Cut = """
        G91
        G01X0.07Y0:LEADIN
        G03X-0.14Y0I-0.07J0
        G03X0.133669Y0.02909I0.0699998916J-0.0000000236
        G00X0.186331Y-0.02909
        G01X-0.07Y0:LEADIN
        G03X0.14Y0I0.07J0
        G03X-0.133669Y-0.02909I-0.0699998916J0.0000000236
        G00X0.063669Y0.52909
        G01X-0.07Y0:LEADIN
        G03X0.14Y0I0.07J0
        G03X-0.133669Y-0.02909I-0.0699998916J0.0000000236
        G00X-0.186331Y0.02909
        G01X0Y-0.07:LEADIN
        G03X0Y0.14I0J0.07
        G03X0.02909Y-0.133669I-0.0000000236J-0.0699998916
        G00X-1.35759Y-0.275331
        G01X0Y-0.125:LEADIN
        G01X0.4765Y0
        G03X0Y0.428I0J0.214
        G01X-0.953Y0
        G03X0Y-0.428I0J-0.214
        G01X0.5065Y0
        G00X-0.3715Y-0.540904
        G01X0.075Y0.129904:LEADIN
        G01X-0.03Y0
        G02X0Y1.25I0J0.625
        G01X2.1875Y0
        G02X0.0625Y-0.0625I0J-0.0625
        G01X0Y-1.125
        G02X-0.0625Y-0.0625I-0.0625J0
        G01X-2.1375Y0
        G01X0.03Y-0.051961:LEADOUT
        """;

    [Fact]
    public void Rapid_CutoffTravelStillCrossesEarlierRealCuts()
    {
        var report = Analyze(Rectangle(0, 0, 4), Scribe(-2, 2), Cutoff(6, 2));
        var crossing = Assert.Single(Find(report, PostVerificationKind.RapidCrossing).Where(f =>
            f.PartNumber == 3 && f.OtherPartNumber == 1));
        Assert.NotNull(crossing.RapidStart);
        Assert.NotNull(crossing.RapidEnd);
        Assert.Equal((crossing.RapidStart.Value + crossing.RapidEnd.Value) * 0.5, crossing.Location);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Rapid_CutoutCutInPiecesAroundTabsIsRetained(bool leads)
    {
        var report = Analyze(TabbedCutout(leads), Scribe(-2, 5), Scribe(12, 5));
        Assert.Empty(Find(report, PostVerificationKind.Incomplete));
    }

    [Fact]
    public void Rapid_LeadOutBridgingATabGapIsIncomplete()
    {
        var part = TabbedCutout(true);
        var codes = part.Program.Codes;
        // The second piece's lead-out runs across its tab to the first piece's start.
        codes[codes.FindLastIndex(code => code is LinearMove { Layer: LayerType.Leadout })] = new LinearMove(3, 5.1) { Layer = LayerType.Leadout };
        var report = Analyze(part, Scribe(-2, 5), Scribe(12, 5));
        Assert.NotEmpty(Find(report, PostVerificationKind.Incomplete));
    }

    [Fact]
    public void Rapid_OpenCutsAcrossAClosedContourAreIncomplete()
    {
        var part = Rectangle(0, 0, 10);
        var placed = Square(0, 0, 10);
        // Two cuts from edge to edge release the strip between them.
        placed.MoveTo(0, 3);
        placed.LineTo(10, 3);
        placed.MoveTo(10, 6);
        placed.LineTo(0, 6);
        part.RestoreLeadInProgram(placed, false);
        var report = Analyze(part, Scribe(-2, 5), Scribe(12, 5));
        Assert.Contains(Find(report, PostVerificationKind.Incomplete),
            finding => finding.Message.Contains("Multiple interrupted/open"));
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

    // A 10x10 part whose 4x4 cutout is cut in two pieces, leaving 0.2 tabs on its left and
    // right edges; leads enter and leave each piece from inside the slug.
    private static Part TabbedCutout(bool leads)
    {
        var clean = Square(3, 3, 4);
        clean.Codes.AddRange(Square(0, 0, 10).Codes);
        var part = new Part(new Drawing("tabbed", clean));
        var program = new Program();
        if (leads)
        {
            program.MoveTo(3.2, 5.3);
            program.Codes.Add(new LinearMove(3, 5.1) { Layer = LayerType.Leadin });
        }
        else
            program.MoveTo(3, 5.1);
        program.LineTo(3, 7);
        program.LineTo(7, 7);
        program.LineTo(7, 5.1);
        if (leads)
        {
            program.Codes.Add(new LinearMove(6.8, 5.3) { Layer = LayerType.Leadout });
            program.MoveTo(6.8, 4.7);
            program.Codes.Add(new LinearMove(7, 4.9) { Layer = LayerType.Leadin });
        }
        else
            program.MoveTo(7, 4.9);
        program.LineTo(7, 3);
        program.LineTo(3, 3);
        program.LineTo(3, 4.9);
        if (leads)
            program.Codes.Add(new LinearMove(3.2, 4.7) { Layer = LayerType.Leadout });
        program.Codes.AddRange(Square(0, 0, 10).Codes);
        part.RestoreLeadInProgram(program, false);
        return part;
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
    };
}
