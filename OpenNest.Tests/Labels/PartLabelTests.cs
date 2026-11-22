using OpenNest.Bending;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Data;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;
using OpenNest.IO;
using OpenNest.Labels;
using OpenNest.Math;
using OpenNest.Tests.CuttingPlanning;

namespace OpenNest.Tests.Labels;

/// <summary>
/// The etched part label (docs/label-box.md): the stroke font, the bend group letters, the text
/// and strokes in a drawing's label box, and Plan Cutting emitting them as scribe moves.
/// </summary>
public class PartLabelTests
{
    private const double Quarter = System.Math.PI / 2;

    private static Bend BendAt(double lineAngle, double length = 4)
    {
        var start = new Vector(2, 2);
        return new Bend
        {
            StartPoint = start,
            EndPoint = new Vector(start.X + length * System.Math.Cos(lineAngle), start.Y + length * System.Math.Sin(lineAngle)),
            Direction = BendDirection.Up,
        };
    }

    // A 12 inch square with a 3.5 x 0.5 label box at (6, 6).
    private static Drawing Panel(string name = "1234", params Bend[] bends)
    {
        var drawing = new Drawing(name, LeadPathValidationTests.Rectangle(0, 0, 12, 12))
        {
            LabelBox = new LabelBox(new Vector(6, 6), 3.5, 0.5, 0),
        };
        drawing.Bends.AddRange(bends);
        return drawing;
    }

    private readonly record struct Bounds(double Left, double Bottom, double Width, double Height)
    {
        public double Right => Left + Width;

        public double Top => Bottom + Height;
    }

    private static Bounds BoundsOf(IEnumerable<List<Vector>> strokes)
    {
        var points = strokes.SelectMany(s => s).ToList();
        var minX = points.Min(p => p.X);
        var minY = points.Min(p => p.Y);
        return new Bounds(minX, minY, points.Max(p => p.X) - minX, points.Max(p => p.Y) - minY);
    }

    // ---- stroke font ----

    [Fact]
    public void Font_DrawsEveryDigitLetterAndTheDash_AsStrokesInsideTheCharacterCell()
    {
        foreach (var character in "0123456789-ABCDEFGHIJKLMNOPQRSTUVWXYZ")
        {
            Assert.True(StrokeFont.Has(character), character.ToString());
            var strokes = StrokeFont.Strokes(character.ToString(), 1);
            Assert.NotEmpty(strokes);
            Assert.All(strokes, s => Assert.True(s.Count >= 2));
            var bounds = BoundsOf(strokes);
            Assert.InRange(bounds.Left, -1e-9, StrokeFont.GlyphWidth + 1e-9);
            Assert.InRange(bounds.Right, -1e-9, StrokeFont.GlyphWidth + 1e-9);
            Assert.InRange(bounds.Bottom, -1e-9, 1 + 1e-9);
            Assert.InRange(bounds.Top, -1e-9, 1 + 1e-9);
        }
    }

    [Fact]
    public void Font_AdvanceMatchesTheLabelBoxSizing_AndLowercaseIsCapitals()
    {
        Assert.Equal(LabelBoxOptions.CharacterAdvance, StrokeFont.Advance);

        var width = StrokeFont.Measure("1234-AB") * 0.25;
        var options = LabelBoxOptions.ForText(7, 0.25, 0.125);
        Assert.True(width <= options.Width + 1e-9);

        Assert.Equal(BoundsOf(StrokeFont.Strokes("AB", 1)), BoundsOf(StrokeFont.Strokes("ab", 1)));
    }

    [Fact]
    public void Font_ScalesWithHeight_AndLeavesAGapForACharacterItLacks()
    {
        var one = BoundsOf(StrokeFont.Strokes("H", 1));
        var four = BoundsOf(StrokeFont.Strokes("H", 4));
        Assert.Equal(one.Width * 4, four.Width, 9);
        Assert.Equal(one.Height * 4, four.Height, 9);

        Assert.False(StrokeFont.Has('#'));
        var gap = BoundsOf(StrokeFont.Strokes("H#H", 1));
        var tight = BoundsOf(StrokeFont.Strokes("HH", 1));
        Assert.True(gap.Width > tight.Width);
        Assert.Empty(StrokeFont.Strokes("", 1));
        Assert.Equal(0, StrokeFont.Measure(""));
    }

    // ---- bend groups ----

    [Fact]
    public void GroupLetters_AAcrossTheGrain_BWithIt_AndEachOtherStepItsOwnLetter()
    {
        Assert.Equal('A', PartLabels.GroupLetter(Quarter, 0));
        Assert.Equal('B', PartLabels.GroupLetter(0, 0));

        // Undirected: a bend line and its reverse are the same group.
        Assert.Equal(PartLabels.GroupLetter(Quarter, 0), PartLabels.GroupLetter(Quarter + System.Math.PI, 0));

        // Every 15 degree step has a letter of its own, so no two groups share one.
        var letters = Enumerable.Range(0, BendGrain.BucketCount)
            .Select(step => PartLabels.GroupLetter(step * BendGrain.BucketStep, 0))
            .ToList();
        Assert.Equal(BendGrain.BucketCount, letters.Distinct().Count());
        Assert.Equal(['B', 'C', 'D', 'E', 'F', 'G', 'A', 'H', 'I', 'J', 'K', 'L'], letters);
    }

    [Fact]
    public void GroupLetters_FollowThePartsRotationAgainstTheGrain()
    {
        var bends = new[] { BendAt(Quarter), BendAt(Quarter, 2) };

        Assert.Equal("A", PartLabels.GroupLetters(bends, 0, 0));
        Assert.Equal("B", PartLabels.GroupLetters(bends, Quarter, 0));
        // A grain turned with the part gives the same answer as neither turning.
        Assert.Equal("A", PartLabels.GroupLetters(bends, Quarter, Quarter));
    }

    [Fact]
    public void GroupLetters_AreDistinctAndAlphabetical_AndSkipEmptyBends()
    {
        var bends = new[] { BendAt(0), BendAt(Quarter), BendAt(Quarter), BendAt(0.3), BendAt(1, length: 0) };

        Assert.Equal("ABC", PartLabels.GroupLetters(bends, 0, 0));
        Assert.Equal(string.Empty, PartLabels.GroupLetters(null, 0, 0));
        Assert.Equal(string.Empty, PartLabels.GroupLetters([], 0, 0));
    }

    // ---- text ----

    [Fact]
    public void Text_IsTheNameThenTheBendGroups()
    {
        var drawing = Panel("1234", BendAt(0), BendAt(Quarter));

        Assert.Equal("1234-AB", PartLabels.Text(drawing, 0, 0));
        // Turned a quarter, the same bends are in the same two groups: A and B swap.
        Assert.Equal("1234-AB", PartLabels.Text(drawing, Quarter, 0));
        Assert.Equal("1234", PartLabels.Text(Panel("1234"), 0, 0));
    }

    [Fact]
    public void Text_UppercasesTheNameAndDropsWhatTheFontLacks()
    {
        Assert.Equal("N26-2GT", PartLabels.Text(Panel("n26-2gt"), 0, 0));
        Assert.Equal("BRACKETA", PartLabels.Text(Panel("bracket (a)"), 0, 0));
        Assert.Equal(string.Empty, PartLabels.Text(Panel("()"), 0, 0));
        Assert.Equal("A", PartLabels.Text(Panel("()", BendAt(Quarter)), 0, 0));
        Assert.Equal(string.Empty, PartLabels.Text(null, 0, 0));
    }

    // ---- strokes ----

    [Fact]
    public void Strokes_AreCenteredInTheBoxAtTheBoxHeight()
    {
        // H, A and B fill their whole character cell, so the strokes' bounds are the text's.
        var drawing = Panel("HH", BendAt(0), BendAt(Quarter));

        var bounds = BoundsOf(PartLabels.Strokes(drawing, 0, 0));

        var width = StrokeFont.Measure("HH-AB") * 0.5;
        Assert.Equal(0.5, bounds.Height, 9);
        Assert.Equal(width, bounds.Width, 9);
        Assert.Equal(6, bounds.Left + bounds.Width / 2, 9);
        Assert.Equal(6, bounds.Bottom + bounds.Height / 2, 9);
    }

    [Fact]
    public void Strokes_LeaveTheProgramFrameBySourceOffset_AndTurnWithThePart()
    {
        var drawing = Panel("HH");
        drawing.Source.Offset = new Vector(1, 2);

        var upright = BoundsOf(PartLabels.Strokes(drawing, 0, 0));
        Assert.Equal(5, upright.Left + upright.Width / 2, 9);
        Assert.Equal(4, upright.Bottom + upright.Height / 2, 9);

        // A quarter turn about the program origin: (5, 4) goes to (-4, 5), and the text stands on end.
        var turned = BoundsOf(PartLabels.Strokes(drawing, Quarter, 0));
        Assert.Equal(-4, turned.Left + turned.Width / 2, 9);
        Assert.Equal(5, turned.Bottom + turned.Height / 2, 9);
        Assert.Equal(upright.Width, turned.Height, 9);
        Assert.Equal(upright.Height, turned.Width, 9);
    }

    [Fact]
    public void Strokes_ShrinkALongNameToFitTheBox_AndSkipOneThatWouldBeTooSmall()
    {
        // 3.5 fits "1234-AB" (7 characters) at the full 0.5 height; "123456789-AB" is 11.
        var shrunk = BoundsOf(PartLabels.Strokes(Panel("123456789", BendAt(0), BendAt(Quarter)), 0, 0));
        Assert.True(shrunk.Width <= 3.5 + 1e-9);
        Assert.InRange(shrunk.Height, 0.25 - 1e-9, 0.5);

        Assert.Null(PartLabels.Strokes(Panel("1234567890123456789012345"), 0, 0));
    }

    [Fact]
    public void Strokes_AreNull_WithoutAValidBoxOrAnyText()
    {
        var none = Panel("1234");
        none.LabelBox = null;
        Assert.Null(PartLabels.Strokes(none, 0, 0));

        var broken = Panel("1234");
        broken.LabelBox.Width = 0;
        Assert.Null(PartLabels.Strokes(broken, 0, 0));

        Assert.Null(PartLabels.Strokes(Panel("()"), 0, 0));
        Assert.Null(PartLabels.Strokes(Panel("1234"), double.NaN, 0));
        Assert.Null(PartLabels.Strokes(null, 0, 0));
    }

    [Fact]
    public void AddTo_AppendsScribeMovesEachStrokeStartedByARapid_OrLeavesTheProgramAlone()
    {
        var drawing = Panel("1234", BendAt(Quarter));
        var program = (Program)drawing.Program.Clone();
        var before = program.Codes.Count;

        Assert.True(PartLabels.AddTo(program, drawing, 0, 0));

        var added = program.Codes.Skip(before).ToList();
        Assert.IsType<RapidMove>(added[0]);
        Assert.All(added.OfType<LinearMove>(), move => Assert.Equal(LayerType.Scribe, move.Layer));
        Assert.True(added.OfType<LinearMove>().Count() > 10);

        var untouched = (Program)drawing.Program.Clone();
        drawing.LabelBox = null;
        Assert.False(PartLabels.AddTo(untouched, drawing, 0, 0));
        Assert.Equal(before, untouched.Codes.Count);
    }

    // ---- Plan Cutting ----

    private static CuttingParameters Parameters(bool labels) =>
        Parameters(labels ? PartLabelMode.PartNumberAndBendGroups : PartLabelMode.Off);

    private static CuttingParameters Parameters(PartLabelMode mode)
    {
        var parameters = ExplicitContourTests.Parameters();
        parameters.LabelMode = mode;
        return parameters;
    }

    private static (Plate Plate, Part Part) PlateOf(Drawing drawing, CuttingParameters parameters, double grain = 0,
        double rotation = 0)
    {
        var plate = new Plate(30, 30) { CuttingParameters = parameters, GrainAngle = grain };
        var part = new Part(drawing, new Vector(1, 1));
        if (rotation != 0)
            part.Rotate(rotation);
        plate.Parts.Add(part);
        return (plate, part);
    }

    private static List<ExecutionMotion> ScribeOf(CuttingPlanResult result)
    {
        var proposal = Assert.Single(result.ProposedOrder);
        return ExecutionMotionReader.Read(proposal.CopyProgram(), proposal.Location, null, default).Motions
            .Where(m => !m.Rapid && m.Layer == LayerType.Scribe).ToList();
    }

    [Fact]
    public void PlanCutting_EtchesTheLabelInsideTheBoxOnTheSheet_WhenOn()
    {
        var drawing = Panel("1234", BendAt(0), BendAt(Quarter));
        var (plate, _) = PlateOf(drawing, Parameters(true));

        var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, Vector.Zero,
            confirmedParameters: Parameters(true)));

        Assert.True(result.Status == CuttingPlanStatus.Ready, string.Join("\n", result.Findings.Select(f => f.Message)));
        var marks = ScribeOf(result);
        Assert.True(marks.Count > 10);
        // The box is centered at (6, 6) on the part, which sits at (1, 1) on the sheet.
        foreach (var mark in marks)
        {
            Assert.InRange(mark.End.X, 7 - 1.75 - 1e-6, 7 + 1.75 + 1e-6);
            Assert.InRange(mark.End.Y, 7 - 0.25 - 1e-6, 7 + 0.25 + 1e-6);
        }
    }

    [Fact]
    public void PlanCutting_EtchesNothing_WhenOffOrWithoutABox()
    {
        var (offPlate, _) = PlateOf(Panel("1234", BendAt(0)), Parameters(false));
        var off = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(offPlate, Vector.Zero,
            confirmedParameters: Parameters(false)));
        Assert.Equal(CuttingPlanStatus.Ready, off.Status);
        Assert.Empty(ScribeOf(off));

        var unboxed = Panel("1234", BendAt(0));
        unboxed.LabelBox = null;
        var (onPlate, _) = PlateOf(unboxed, Parameters(true));
        var on = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(onPlate, Vector.Zero,
            confirmedParameters: Parameters(true)));
        Assert.Equal(CuttingPlanStatus.Ready, on.Status);
        Assert.Empty(ScribeOf(on));
    }

    [Fact]
    public void PlanCutting_LabelsTheGroupsForThePartsRotationOnTheSheet()
    {
        // One bend across the grain: group A upright, group B when the part is turned a quarter.
        var drawing = Panel("7", BendAt(Quarter));

        var (upright, _) = PlateOf(drawing, Parameters(true));
        var (turned, _) = PlateOf(drawing, Parameters(true), rotation: Quarter);
        var a = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(upright, Vector.Zero, confirmedParameters: Parameters(true)));
        var b = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(turned, Vector.Zero, confirmedParameters: Parameters(true)));

        Assert.Equal(CuttingPlanStatus.Ready, a.Status);
        Assert.Equal(CuttingPlanStatus.Ready, b.Status);
        // "7-A" and "7-B" differ in their last glyph, so their strokes differ in count.
        Assert.NotEqual(ScribeOf(a).Count, ScribeOf(b).Count);
    }

    // ---- the option ----

    [Fact]
    public void PlanCutting_PartNumberMode_EtchesTheNumberWithoutTheGroups()
    {
        // "7" alone is one glyph; "7-A" adds a dash and a letter, so it has more strokes.
        var drawing = Panel("7", BendAt(Quarter));
        var strokesOf = (PartLabelMode mode) =>
        {
            var (plate, _) = PlateOf(drawing, Parameters(mode));
            var result = CuttingPlanService.Plan(CuttingPlanRequest.ForPlate(plate, Vector.Zero,
                confirmedParameters: Parameters(mode)));
            Assert.Equal(CuttingPlanStatus.Ready, result.Status);
            return ScribeOf(result).Count;
        };

        var numberOnly = strokesOf(PartLabelMode.PartNumber);
        var full = strokesOf(PartLabelMode.PartNumberAndBendGroups);

        Assert.True(numberOnly > 0);
        Assert.True(full > numberOnly);
        Assert.Equal(0, strokesOf(PartLabelMode.Off));
    }

    // ---- the option ----

    [Fact]
    public void Text_PartNumberMode_OmitsTheGroups_AndOffIsEmpty()
    {
        var drawing = Panel("1234", BendAt(0), BendAt(Quarter));

        Assert.Equal("1234", PartLabels.Text(drawing, 0, 0, PartLabelMode.PartNumber));
        Assert.Equal("1234-AB", PartLabels.Text(drawing, 0, 0, PartLabelMode.PartNumberAndBendGroups));
        Assert.Equal("1234-AB", PartLabels.Text(drawing, 0, 0));
        Assert.Equal(string.Empty, PartLabels.Text(drawing, 0, 0, PartLabelMode.Off));
        // A name the font cannot draw is nothing to etch in number mode, even with bends.
        Assert.Equal(string.Empty, PartLabels.Text(Panel("()", BendAt(Quarter)), 0, 0, PartLabelMode.PartNumber));
    }

    [Fact]
    public void AddTo_OffMode_LeavesTheProgramAlone()
    {
        var drawing = Panel("1234", BendAt(Quarter));
        var program = (Program)drawing.Program.Clone();
        var before = program.Codes.Count;

        Assert.False(PartLabels.AddTo(program, drawing, 0, 0, PartLabelMode.Off));
        Assert.Equal(before, program.Codes.Count);
        Assert.True(PartLabels.AddTo(program, drawing, 0, 0, PartLabelMode.PartNumber));
    }

    [Fact]
    public void Option_IsOffByDefault_AndSurvivesEveryCopyAndSave()
    {
        Assert.Equal(PartLabelMode.Off, new CuttingParameters().LabelMode);

        foreach (var mode in new[] { PartLabelMode.PartNumber, PartLabelMode.PartNumberAndBendGroups })
        {
            var on = Parameters(mode);
            Assert.Equal(mode, OwnedCuttingParameters.Copy(on).LabelMode);
            Assert.Equal(mode, CuttingParametersSerializer.Deserialize(CuttingParametersSerializer.Serialize(on)).LabelMode);

            var config = MachineCuttingDefaults.ToCuttingConfig(on);
            Assert.Equal(mode, config.LabelMode);
            Assert.Equal(mode, MachineCuttingDefaults.CuttingOf(config).LabelMode);
        }

        Assert.Equal(PartLabelMode.Off,
            CuttingParametersSerializer.Deserialize(CuttingParametersSerializer.Serialize(Parameters(false))).LabelMode);
        Assert.Equal(PartLabelMode.Off, MachineCuttingDefaults.CuttingOf(null).LabelMode);
    }

    [Fact]
    public void Fingerprint_SeparatesTheThreeModes_AndKeepsTheOldFullLabelOne()
    {
        var off = StateFingerprint.Of(Parameters(PartLabelMode.Off));
        var number = StateFingerprint.Of(Parameters(PartLabelMode.PartNumber));
        var full = StateFingerprint.Of(Parameters(PartLabelMode.PartNumberAndBendGroups));

        Assert.NotEqual(off, number);
        Assert.NotEqual(off, full);
        Assert.NotEqual(number, full);
    }

    [Fact]
    public void OldOnOffOption_LoadsAsTheFullLabel()
    {
        // Settings saved before the mode existed wrote the option as "etchPartLabels": true.
        var current = CuttingParametersSerializer.Serialize(Parameters(PartLabelMode.PartNumberAndBendGroups));
        Assert.DoesNotContain("etchPartLabels", current);
        Assert.Contains("\"labelMode\":\"PartNumberAndBendGroups\"", current);

        var on = current.Replace("\"labelMode\":\"PartNumberAndBendGroups\"", "\"etchPartLabels\":true");
        Assert.Equal(PartLabelMode.PartNumberAndBendGroups, CuttingParametersSerializer.Deserialize(on).LabelMode);

        var off = current.Replace("\"labelMode\":\"PartNumberAndBendGroups\"", "\"etchPartLabels\":false");
        Assert.Equal(PartLabelMode.Off, CuttingParametersSerializer.Deserialize(off).LabelMode);

        var missing = CuttingParametersSerializer.Serialize(Parameters(PartLabelMode.Off));
        Assert.DoesNotContain("labelMode", missing);
        Assert.Equal(PartLabelMode.Off, CuttingParametersSerializer.Deserialize(missing).LabelMode);

        // The new key wins when both are present.
        var both = current.Replace("\"labelMode\":\"PartNumberAndBendGroups\"",
            "\"labelMode\":\"PartNumber\",\"etchPartLabels\":true");
        Assert.Equal(PartLabelMode.PartNumber, CuttingParametersSerializer.Deserialize(both).LabelMode);
    }

    [Fact]
    public void OldOnOffOption_InAMachineThicknessConfig_LoadsAsTheFullLabel()
    {
        var options = new System.Text.Json.JsonSerializerOptions();
        var old = System.Text.Json.JsonSerializer.Deserialize<ThicknessCuttingConfig>(
            "{\"EtchPartLabels\":true}", options);
        Assert.Equal(PartLabelMode.PartNumberAndBendGroups, MachineCuttingDefaults.CuttingOf(old).LabelMode);

        var off = System.Text.Json.JsonSerializer.Deserialize<ThicknessCuttingConfig>("{}", options);
        Assert.Equal(PartLabelMode.Off, MachineCuttingDefaults.CuttingOf(off).LabelMode);

        // Saving it again writes the mode, not the old key.
        var saved = System.Text.Json.JsonSerializer.Serialize(
            MachineCuttingDefaults.ToCuttingConfig(MachineCuttingDefaults.CuttingOf(old)), options);
        Assert.Contains("\"LabelMode\":\"PartNumberAndBendGroups\"", saved);
        Assert.DoesNotContain("EtchPartLabels", saved);
    }
}
