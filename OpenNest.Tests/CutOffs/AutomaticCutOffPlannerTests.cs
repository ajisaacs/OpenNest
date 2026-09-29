using System.Reflection;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.CutOffs;

public class AutomaticCutOffPlannerTests
{
    private static Plate MakePlate(int quadrant = 1, double occupied = 80)
    {
        var plate = new Plate(81, 120) { Quadrant = quadrant, PartSpacing = 0.5, Quantity = 2 };
        var x = quadrant is 2 or 3 ? -occupied : 10;
        var y = quadrant is 3 or 4 ? -40 : 20;
        plate.Parts.Add(Rectangle(occupied - 10, 20, new Vector(x, y)));
        return plate;
    }

    private static Part Rectangle(double length, double width, Vector location)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(length, 0));
        program.Codes.Add(new LinearMove(length, width));
        program.Codes.Add(new LinearMove(0, width));
        program.Codes.Add(new LinearMove(0, 0));
        var drawing = new Drawing("rectangle", program);
        Assert.Equal(length * width, drawing.Area, 8);
        return new Part(drawing, location);
    }

    private static AutomaticCutOffPlan Plan(Plate plate, double spacing = 35,
        CutOffSettings? settings = null) => AutomaticCutOffPlanner.Create(
            plate, new AutomaticCutOffOptions { Spacing = spacing }, settings ?? new CutOffSettings());

    private static double[] Positions(AutomaticCutOffPlan plan) =>
        plan.Definitions.Select(c => c.Position.X).ToArray();

    private static (Vector From, Vector To)[] Segments(Program program) =>
        Enumerable.Range(0, program.Codes.Count / 2).Select(i => (
            Assert.IsType<RapidMove>(program.Codes[2 * i]).EndPoint,
            Assert.IsType<LinearMove>(program.Codes[2 * i + 1]).EndPoint)).ToArray();

    [Fact]
    public void UsedEnvelope_StopsBeforeUnusedTail_AndPreviewsAreDetached()
    {
        var plate = MakePlate();
        var plan = Plan(plate);
        var separator = 80 + 0.5 + Tolerance.Epsilon;

        Assert.Equal(new[] { 35, 70, separator }, Positions(plan));
        Assert.All(plan.Definitions, c => Assert.Equal(CutOffAxis.Vertical, c.Axis));
        Assert.Equal(80, plan.OccupiedSpan);
        Assert.Equal(separator, plan.UsedSpan);
        Assert.Equal(120 - separator, plan.TailLength);
        Assert.Equal(separator, plan.TailSeparatorX);
        Assert.True(plan.HasSeparatedTail);
        Assert.False(plan.HasBlockingDiagnostics);
        Assert.Equal(plan.Definitions.Count, plan.PreviewParts.Count);
        for (var i = 0; i < plan.Definitions.Count; i++)
        {
            Assert.Same(plan.Definitions[i].Drawing, plan.PreviewParts[i].BaseDrawing);
            Assert.DoesNotContain(plan.PreviewParts[i], plate.Parts);
        }
        var segment = Assert.Single(Segments(plan.Definitions[^1].Drawing.Program));
        Assert.Equal(new Vector(separator, 0), segment.From);
        Assert.Equal(new Vector(separator, 81), segment.To);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MinimumTail_SkipsOnlyFinalSeparatorWithoutExtendingSkeletonGrid(int quadrant)
    {
        // The proposed separator is just before X=105, with a tail shorter than 16.
        // Skipping it must not add the otherwise-next nominal line at X=105.
        var plate = MakePlate(quadrant, occupied: 104);
        var before = Plan(plate);
        var options = new AutomaticCutOffOptions { Spacing = 35, MinimumTailLength = 16 };

        var plan = AutomaticCutOffPlanner.Create(plate, options, new CutOffSettings());

        Assert.Equal(before.Definitions.Take(before.Definitions.Count - 1).Select(c => c.Position.X), Positions(plan));
        Assert.False(plan.HasSeparatedTail);
        Assert.Null(plan.TailSeparatorX);
        Assert.Equal(0, plan.TailLength);
        Assert.Equal(120, plan.UsedSpan);
        Assert.Contains(plan.Diagnostics, d => d.Message.Contains("minimum tail length"));
        Assert.Empty(plate.CutOffs);
        Assert.Single(plate.Parts);
    }

    [Theory]
    [InlineData(1, -0.01, false)]
    [InlineData(1, 0, true)]
    [InlineData(1, 0.01, true)]
    [InlineData(2, -0.01, false)]
    [InlineData(2, 0, true)]
    [InlineData(2, 0.01, true)]
    [InlineData(3, -0.01, false)]
    [InlineData(3, 0, true)]
    [InlineData(3, 0.01, true)]
    [InlineData(4, -0.01, false)]
    [InlineData(4, 0, true)]
    [InlineData(4, 0.01, true)]
    public void MinimumTail_TwelveInchesIsInclusiveAfterSeparatorClearance(int quadrant, double extra, bool keep)
    {
        var occupied = 120 - 12 - 0.5 - Tolerance.Epsilon - extra;
        var plate = MakePlate(quadrant, occupied);
        var options = new AutomaticCutOffOptions { Spacing = 35, MinimumTailLength = 12 };

        var plan = AutomaticCutOffPlanner.Create(plate, options, new CutOffSettings());

        Assert.Equal(keep, plan.HasSeparatedTail);
        Assert.Equal(keep ? 4 : 3, plan.Definitions.Count);
        if (keep)
            Assert.Equal(12 + extra, plan.TailLength, 6);
    }

    [Fact]
    public void MinimumTail_DoesNotRemoveExistingSeparatorWhenRerunWithHigherMinimum()
    {
        var plate = MakePlate(occupied: 110);
        var settings = new CutOffSettings();
        var original = Plan(plate);
        plate.CutOffs.AddRange(original.Definitions);
        plate.RegenerateCutOffs(settings);
        var definitions = plate.CutOffs.ToArray();
        var parts = plate.Parts.ToArray();
        var programs = definitions.Select(c => c.Drawing.Program).ToArray();

        var plan = AutomaticCutOffPlanner.Create(plate,
            new AutomaticCutOffOptions { Spacing = 35, MinimumTailLength = 12 }, settings);

        Assert.Empty(plan.Definitions);
        Assert.False(plan.HasSeparatedTail);
        Assert.Equal(definitions, plate.CutOffs.ToArray());
        Assert.Equal(parts, plate.Parts.ToArray());
        Assert.Equal(programs, plate.CutOffs.Select(c => c.Drawing.Program));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void MinimumTail_InvalidValueIsRejected(double minimum)
    {
        var plate = MakePlate();
        Assert.Throws<ArgumentException>(() => AutomaticCutOffPlanner.Create(plate,
            new AutomaticCutOffOptions { Spacing = 35, MinimumTailLength = minimum }, new CutOffSettings()));
        Assert.Empty(plate.CutOffs);
    }

    [Theory]
    [InlineData(2, 0.5)]
    [InlineData(0.5, 2)]
    [InlineData(2, 2)]
    [InlineData(0, 0)]
    public void TailUsesGreaterClearance_NotSum(double partSpacing, double clearance)
    {
        var plate = MakePlate();
        plate.PartSpacing = partSpacing;
        var plan = Plan(plate, settings: new CutOffSettings { PartClearance = clearance });
        Assert.Equal(80 + System.Math.Max(partSpacing, clearance) + Tolerance.Epsilon,
            plan.TailSeparatorX);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, -1)]
    [InlineData(3, -1)]
    [InlineData(4, 1)]
    public void AllQuadrants_AdvanceXFromOrigin_AcrossPhysicalWidth(int quadrant, int sign)
    {
        var plate = MakePlate(quadrant);
        plate.EdgeSpacing = new Spacing(10, 10);
        var settings = new CutOffSettings { Overtravel = 3 };
        var plan = Plan(plate, settings: settings);
        Assert.Equal(new[] { sign * 35.0, sign * 70.0, sign * (80.5 + Tolerance.Epsilon) },
            Positions(plan));
        var bounds = plate.BoundingBox(false);
        var segment = Assert.Single(Segments(plan.Definitions[^1].Drawing.Program));
        Assert.Equal(bounds.Bottom, segment.From.Y);
        Assert.Equal(bounds.Top + 3, segment.To.Y);
        Assert.Equal(80.5 + Tolerance.Epsilon, plan.UsedSpan);
    }

    [Fact]
    public void Planning_DoesNotMutateAnyLiveObjectsOrSequence()
    {
        var plate = MakePlate();
        var drawing = plate.Parts[0].BaseDrawing;
        var nest = new Nest("test");
        nest.Drawings.Add(drawing);
        nest.Plates.Add(plate);
        var manual = new CutOff(new Vector(25, 0), CutOffAxis.Vertical)
        { StartLimit = 5, EndLimit = 12 };
        plate.CutOffs.Add(manual);
        plate.RegenerateCutOffs(new CutOffSettings());
        var cutoffPart = plate.Parts[^1];
        plate.Parts.Remove(cutoffPart);
        plate.Parts.Insert(0, cutoffPart);
        var parts = plate.Parts.ToArray();
        var definitions = plate.CutOffs.ToArray();
        var programs = parts.Select(p => p.Program).ToArray();
        var text = programs.Select(p => p.ToString()).ToArray();
        var locations = parts.Select(p => p.Location).ToArray();
        var bounds = parts.Select(p => p.BoundingBox).ToArray();
        var drawingProgram = drawing.Program;
        var cutoffProgram = manual.Drawing.Program;
        var nested = drawing.Quantity.Nested;
        var changes = 0;
        plate.PartAdded += (_, _) => changes++;
        plate.PartRemoved += (_, _) => changes++;
        plate.PartChanged += (_, _) => changes++;

        var plan = Plan(plate);

        Assert.NotEmpty(plan.Definitions);
        Assert.Equal(parts, plate.Parts.ToArray());
        Assert.Equal(definitions, plate.CutOffs.ToArray());
        Assert.Equal(programs, plate.Parts.Select(p => p.Program));
        Assert.Equal(text, plate.Parts.Select(p => p.Program.ToString()));
        Assert.Equal(locations, plate.Parts.Select(p => p.Location));
        Assert.Equal(bounds, plate.Parts.Select(p => p.BoundingBox));
        Assert.Same(drawingProgram, drawing.Program);
        Assert.Same(cutoffProgram, manual.Drawing.Program);
        Assert.Equal(nested, drawing.Quantity.Nested);
        Assert.Same(drawing, Assert.Single(nest.Drawings));
        Assert.Equal(5, manual.StartLimit);
        Assert.Equal(12, manual.EndLimit);
        Assert.Equal(0, changes);
    }

    [Theory]
    [InlineData(CutDirection.AwayFromOrigin)]
    [InlineData(CutDirection.TowardOrigin)]
    public void RepeatedLines_UseCurrentManualSegmentation_WithoutIncreasingClearance(CutDirection direction)
    {
        var plate = MakePlate();
        plate.PartSpacing = 8;
        var settings = new CutOffSettings
        { PartClearance = 1, Overtravel = 2, MinSegmentLength = 0.5, CutDirection = direction };
        var plan = Plan(plate, settings: settings);
        var automatic = plan.Definitions[0];
        var manual = new CutOff(automatic.Position, automatic.Axis);
        manual.Regenerate(plate, settings, Plate.BuildPerimeterCache(plate));
        Assert.Equal(manual.Drawing.Program.ToString(), automatic.Drawing.Program.ToString());
        var segments = Segments(automatic.Drawing.Program);
        Assert.Equal(2, segments.Length);
        Assert.All(segments, s => Assert.True(
            System.Math.Max(s.From.Y, s.To.Y) <= 19 ||
            System.Math.Min(s.From.Y, s.To.Y) >= 41));
        Assert.Contains(segments, s => System.Math.Abs(s.To.Y - 19) < 0.01 ||
            System.Math.Abs(s.From.Y - 19) < 0.01);
        Assert.Equal(1, settings.PartClearance);
    }

    [Fact]
    public void EmptyPlate_AndCutOffOnlyPlate_AreNeverChopped()
    {
        var plate = new Plate(81, 120);
        Assert.Empty(Plan(plate).Definitions);
        var old = new CutOff(new Vector(110, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(old);
        plate.RegenerateCutOffs(new CutOffSettings());
        var plan = Plan(plate);
        Assert.Empty(plan.Definitions);
        Assert.Empty(plan.PreviewParts);
        Assert.Equal(0, plan.OccupiedSpan);
        Assert.Equal(0, plan.UsedSpan);
        Assert.Equal(0, plan.TailLength);
        Assert.False(plan.HasSeparatedTail);
    }

    [Fact]
    public void OldCutOffParts_DoNotExtendTheEnvelope()
    {
        var plate = MakePlate();
        plate.CutOffs.Add(new CutOff(new Vector(115, 0), CutOffAxis.Vertical));
        plate.RegenerateCutOffs(new CutOffSettings());
        Assert.Equal(new[] { 35, 70, 80.5 + Tolerance.Epsilon }, Positions(Plan(plate)));
    }

    [Fact]
    public void UsedSpanSmallerThanPitch_OnlyGeneratesSeparator()
    {
        var plan = Plan(MakePlate(occupied: 20));
        Assert.Equal(new[] { 20.5 + Tolerance.Epsilon }, Positions(plan));
    }

    [Fact]
    public void ExactPitchAtSeparator_DoesNotGenerateBoundaryTwice()
    {
        var plate = MakePlate(occupied: 69.5 - Tolerance.Epsilon);
        Assert.Equal(new[] { 35.0, 70.0 }, Positions(Plan(plate)));
    }

    [Theory]
    [InlineData(119.5)]
    [InlineData(120)]
    public void NoRoomForSeparator_UsesFullSheet_NoEdgeDuplicate(double occupied)
    {
        var plan = Plan(MakePlate(occupied: occupied), spacing: 40);
        Assert.Equal(new[] { 40.0, 80.0 }, Positions(plan));
        Assert.Equal(120, plan.UsedSpan);
        Assert.False(plan.HasSeparatedTail);
        Assert.Null(plan.TailSeparatorX);
        Assert.Equal(0, plan.TailLength);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.Epsilon)]
    [InlineData(0.000001)]
    [InlineData(0.00001)]
    [InlineData(0.001)]
    public void InvalidOrExcessivePitch_IsRejected(double pitch)
    {
        Assert.ThrowsAny<ArgumentException>(() => Plan(MakePlate(), pitch));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidSheetDimensions_AreRejected(double value)
    {
        var plate = MakePlate();
        plate.Size = new Size(value, 120);
        Assert.ThrowsAny<ArgumentException>(() => Plan(plate));
        plate.Size = new Size(81, value);
        Assert.ThrowsAny<ArgumentException>(() => Plan(plate));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidPartSpacingAndSettings_AreRejected(double value)
    {
        var plate = MakePlate();
        plate.PartSpacing = value;
        Assert.ThrowsAny<ArgumentException>(() => Plan(plate));
        plate.PartSpacing = 0;
        Assert.ThrowsAny<ArgumentException>(() => Plan(plate,
            settings: new CutOffSettings { PartClearance = value }));
        Assert.ThrowsAny<ArgumentException>(() => Plan(plate,
            settings: new CutOffSettings { Overtravel = value }));
        Assert.ThrowsAny<ArgumentException>(() => Plan(plate,
            settings: new CutOffSettings { MinSegmentLength = value }));
    }

    [Fact]
    public void InvalidQuadrantOrDirection_IsRejected()
    {
        var plate = MakePlate();
        Assert.ThrowsAny<ArgumentException>(() => Plan(plate,
            settings: new CutOffSettings { CutDirection = (CutDirection)42 }));
        // The public setter coerces bad quadrants; simulate damaged state explicitly.
        typeof(Plate).GetField("quadrant", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(plate, 0);
        Assert.ThrowsAny<ArgumentException>(() => Plan(plate));
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => AutomaticCutOffPlanner.Create(null!, new(), new()));
        Assert.Throws<ArgumentNullException>(() => AutomaticCutOffPlanner.Create(MakePlate(), null!, new()));
        Assert.Throws<ArgumentNullException>(() => AutomaticCutOffPlanner.Create(MakePlate(), new(), null!));
    }

    [Theory]
    [InlineData(-11, 0)]
    [InlineData(50, 0)]
    [InlineData(0, -21)]
    [InlineData(0, 42)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    public void InvalidOrOutOfSheetGeometry_IsRejected(double dx, double dy)
    {
        var plate = MakePlate();
        plate.Parts[0].Offset(dx, dy);
        Assert.ThrowsAny<ArgumentException>(() => Plan(plate));
    }

    [Fact]
    public void EmptyOrNonfiniteProgramGeometry_IsRejected_EvenWithOldBounds()
    {
        var plate = MakePlate();
        plate.Parts[0].Program.Codes.Clear();
        Assert.ThrowsAny<ArgumentException>(() => Plan(plate));
        plate = MakePlate();
        ((Motion)plate.Parts[0].Program.Codes[2]).EndPoint = new Vector(double.NaN, 2);
        Assert.ThrowsAny<ArgumentException>(() => Plan(plate));
    }

    [Fact]
    public void EmptyRepeatedCuts_AreDiagnostic_NotAcceptedPartitions()
    {
        var plate = new Plate(81, 120);
        plate.Parts.Add(Rectangle(80, 81, new Vector()));
        var plan = Plan(plate);
        Assert.Single(plan.Definitions);
        Assert.True(plan.HasSeparatedTail);
        Assert.Equal(2, plan.Diagnostics.Count(d => d.Code == AutomaticCutOffDiagnosticCode.EmptyCut));
    }

    [Fact]
    public void FilteredSeparator_DoesNotClaimSeparatedTail()
    {
        var plan = Plan(MakePlate(), settings: new CutOffSettings { MinSegmentLength = 100 });
        Assert.Empty(plan.Definitions);
        Assert.False(plan.HasSeparatedTail);
        Assert.Equal(0, plan.TailLength);
        Assert.Null(plan.TailSeparatorX);
        Assert.Contains(plan.Diagnostics, d => d.Code == AutomaticCutOffDiagnosticCode.NoSafeTailSeparator);
    }

    [Fact]
    public void ApplyingDefinitions_ThenRerunning_PreservesPartsProgramsQuantitiesAndSequence()
    {
        var plate = MakePlate();
        var real = plate.Parts[0];
        var secondReal = new Part(real.BaseDrawing, new Vector(0, 60));
        plate.Parts.Add(secondReal);
        var secondProgram = secondReal.Program;
        var secondLocation = secondReal.Location;
        var program = real.Program;
        var text = program.ToString();
        var location = real.Location;
        var nested = real.BaseDrawing.Quantity.Nested;
        var settings = new CutOffSettings { Overtravel = 2, PartClearance = 0.75 };
        var manual = new CutOff(new Vector(15, 0), CutOffAxis.Horizontal)
        { StartLimit = 5, EndLimit = 15 };
        plate.CutOffs.Add(manual);
        plate.RegenerateCutOffs(settings);
        var manualPart = plate.Parts[^1];
        plate.Parts.Remove(manualPart);
        plate.Parts.Insert(0, manualPart);
        var first = Plan(plate, settings: settings);
        foreach (var definition in first.Definitions)
            plate.CutOffs.Add(definition);
        plate.RegenerateCutOffs(settings);
        var second = Plan(plate, settings: settings);
        Assert.Empty(second.Definitions);
        Assert.True(second.HasSeparatedTail);
        Assert.Equal(first.UsedSpan, second.UsedSpan);
        Assert.Equal(3, second.Diagnostics.Count(d => d.Code == AutomaticCutOffDiagnosticCode.ExistingCutOff));
        plate.RegenerateCutOffs(settings);
        Assert.Equal(4, plate.Parts.Count(p => p.BaseDrawing.IsCutOff));
        Assert.All(plate.CutOffs, c => Assert.Single(plate.Parts.Where(p => ReferenceEquals(p.BaseDrawing, c.Drawing))));
        Assert.Same(manual.Drawing, plate.Parts[0].BaseDrawing);
        Assert.Same(real, plate.Parts[1]);
        Assert.Same(secondReal, plate.Parts[2]);
        Assert.Same(secondProgram, secondReal.Program);
        Assert.Equal(secondLocation, secondReal.Location);
        Assert.Same(program, real.Program);
        Assert.Equal(text, real.Program.ToString());
        Assert.Equal(location, real.Location);
        Assert.Equal(nested, real.BaseDrawing.Quantity.Nested);
        Assert.Equal(5, manual.StartLimit);
        Assert.Equal(15, manual.EndLimit);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EquivalentFullSpanLimits_AndCoordinateTolerance_SuppressDuplicates(bool explicitLimits)
    {
        var plate = MakePlate();
        var existing = new CutOff(new Vector(35 + Tolerance.Epsilon / 2, 999), CutOffAxis.Vertical);
        if (explicitLimits)
        {
            existing.StartLimit = 0;
            existing.EndLimit = 83;
        }
        plate.CutOffs.Add(existing);
        var plan = Plan(plate, settings: new CutOffSettings { Overtravel = 2 });
        Assert.Equal(new[] { 70, 80.5 + Tolerance.Epsilon }, Positions(plan));
        Assert.False(plan.HasBlockingDiagnostics);
        Assert.Contains(plan.Diagnostics, d => d.Code == AutomaticCutOffDiagnosticCode.ExistingCutOff);
    }

    [Fact]
    public void DifferentAxisOrDifferentCoordinate_DoesNotSuppress()
    {
        var plate = MakePlate();
        plate.CutOffs.Add(new CutOff(new Vector(35, 35), CutOffAxis.Horizontal));
        plate.CutOffs.Add(new CutOff(new Vector(35.01, 0), CutOffAxis.Vertical));
        Assert.Equal(3, Plan(plate).Definitions.Count);
    }

    [Theory]
    [InlineData(35)]
    [InlineData(80.50001)]
    public void SameLineLimitedManualCut_BlocksWholePlan_WithoutMutatingManual(double x)
    {
        var plate = MakePlate();
        var manual = new CutOff(new Vector(x, 0), CutOffAxis.Vertical)
        { StartLimit = 10, EndLimit = 60 };
        plate.CutOffs.Add(manual);
        var program = manual.Drawing.Program;
        var plan = Plan(plate);
        Assert.True(plan.HasBlockingDiagnostics);
        Assert.Empty(plan.Definitions);
        Assert.Empty(plan.PreviewParts);
        Assert.False(plan.HasSeparatedTail);
        Assert.Contains(plan.Diagnostics, d => d.IsBlocking &&
            d.Code == AutomaticCutOffDiagnosticCode.LimitedCutOffConflict);
        Assert.Same(program, manual.Drawing.Program);
        Assert.Equal(10, manual.StartLimit);
        Assert.Equal(60, manual.EndLimit);
    }

    [Fact]
    public void ExistingFullSpanAlongsideLimitedConflict_DoesNotHideConflict()
    {
        var plate = MakePlate();
        plate.CutOffs.Add(new CutOff(new Vector(35, 0), CutOffAxis.Vertical));
        plate.CutOffs.Add(new CutOff(new Vector(35, 0), CutOffAxis.Vertical) { EndLimit = 40 });
        Assert.True(Plan(plate).HasBlockingDiagnostics);
    }

    [Fact]
    public void ExistingSeparator_MetadataUsesItsActualToleranceCloseCoordinate()
    {
        var plate = MakePlate();
        var x = 80.5 + Tolerance.Epsilon * 1.5;
        plate.CutOffs.Add(new CutOff(new Vector(x, 0), CutOffAxis.Vertical));
        var plan = Plan(plate);
        Assert.True(plan.HasSeparatedTail);
        Assert.Equal(x, plan.TailSeparatorX);
        Assert.Equal(x, plan.UsedSpan);
        Assert.Equal(120 - x, plan.TailLength);
    }

    [Fact]
    public void ToleranceCloseExistingSeparator_ThatStillTouchesClearance_DoesNotClaimTail()
    {
        var plate = MakePlate();
        plate.PartSpacing = 0;
        plate.CutOffs.Add(new CutOff(new Vector(80, 0), CutOffAxis.Vertical));
        var plan = Plan(plate, settings: new CutOffSettings { PartClearance = 0 });
        Assert.False(plan.HasSeparatedTail);
        Assert.Equal(0, plan.TailLength);
        Assert.DoesNotContain(plan.Definitions, c => c.Position.X > 70);
        Assert.Contains(plan.Diagnostics, d => d.Code == AutomaticCutOffDiagnosticCode.NoSafeTailSeparator);
    }

    [Fact]
    public void FractionalPitch_UsesIntegerMultiplicationWithoutCumulativeDrift()
    {
        var plate = MakePlate(occupied: 11);
        var plan = Plan(plate, spacing: 0.7);
        var repeated = plan.Definitions.Take(plan.Definitions.Count - 1).ToArray();
        Assert.NotEmpty(repeated);
        for (var i = 0; i < repeated.Length; i++)
            Assert.Equal((i + 1) * 0.7, repeated[i].Position.X);
    }

    [Fact]
    public void ConcaveOrSlopedPart_UsesPerimeterNotJustItsBoundingBox()
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(30, 0));
        program.Codes.Add(new LinearMove(0, 30));
        program.Codes.Add(new LinearMove(0, 0));
        var plate = new Plate(81, 120);
        plate.Parts.Add(new Part(new Drawing("triangle", program), new Vector(10, 10)));
        var plan = Plan(plate, spacing: 20, settings: new CutOffSettings { PartClearance = 0 });
        var segments = Segments(plan.Definitions[0].Drawing.Program);
        Assert.Equal(2, segments.Length);
        Assert.Equal(30, segments[1].From.Y, 8); // Bounding-box fallback would start at 40.
        Assert.Contains(plan.Diagnostics, d => d.Code == AutomaticCutOffDiagnosticCode.SegmentedCut);
    }

    [Fact]
    public void ValidButStalePartBounds_AreRejectedRatherThanUsingUnsafeBroadphase()
    {
        var plate = MakePlate();
        ((Motion)plate.Parts[0].Program.Codes[2]).EndPoint = new Vector(90, 20);
        Assert.ThrowsAny<ArgumentException>(() => Plan(plate));
    }

    private static Plate MakeArcPlate(double centerError)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(0, 0));
        program.Codes.Add(new LinearMove(10, 0));
        program.Codes.Add(new ArcMove(10, 20, 10, 10 + centerError, RotationType.CCW));
        program.Codes.Add(new LinearMove(0, 20));
        program.Codes.Add(new LinearMove(0, 0));
        var material = ConvertProgram.ToGeometry(program).Where(e => SpecialLayers.IsMaterial(e.Layer));
        Assert.True(Assert.Single(ShapeBuilder.GetShapes(material)).IsClosed());
        var drawing = new Drawing("D shape", program);
        Assert.Equal(200 + 50 * System.Math.PI, drawing.Area, 2); // Drawing area tessellates arcs.
        var plate = new Plate(81, 120) { PartSpacing = 0 };
        plate.Parts.Add(new Part(drawing, new Vector(10, 10)));
        return plate;
    }

    [Theory]
    [InlineData(0.0001, false)]
    [InlineData(0.0000075, true)]
    public void ConvertedArcOutsideCachedBounds_IsRejectedBeforeAcceptance(double centerError, bool repeatedCut)
    {
        var plate = MakeArcPlate(centerError);
        var real = plate.Parts[0];
        var cached = real.BoundingBox;
        var raw = real.Program.BoundingBox().Translate(real.Location);
        Assert.Equal(raw.Right, cached.Right); // Neither cache refresh nor raw-program bounds reveal the error.
        Assert.Equal(30 - centerError, cached.Right, 10);
        var arc = Assert.Single(ConvertProgram.ToGeometry(real.Program).OfType<Arc>());
        arc.Offset(real.Location);
        Assert.Equal(30, arc.BoundingBox.Right);
        var protrusion = arc.BoundingBox.Right - cached.Right;
        Assert.True(protrusion > 0);
        Assert.Equal(repeatedCut, protrusion < AutomaticCutOffPlanner.GeometryTolerance);

        // A separator crosses the larger protrusion. Even sub-tolerance protrusions are
        // unsafe when a repeated line falls between the cached and converted bounds.
        var unsafeX = cached.Right + (repeatedCut ? protrusion / 2 : AutomaticCutOffPlanner.GeometryTolerance);
        var unsafeLine = new Line(new Vector(unsafeX, 0), new Vector(unsafeX, 81));
        Assert.True(arc.Intersects(unsafeLine, out var intersections));
        Assert.Equal(2, intersections.Count);
        if (repeatedCut)
            plate.Parts.Add(Rectangle(10, 10, new Vector(60, 50)));
        var settings = new CutOffSettings { PartClearance = 0 };
        var manual = new CutOff(new Vector(100, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(manual);
        plate.RegenerateCutOffs(settings);
        var parts = plate.Parts.ToArray();
        var definitions = plate.CutOffs.ToArray();
        var programs = parts.Select(p => p.Program).ToArray();
        var text = programs.Select(p => p.ToString()).ToArray();
        var locations = parts.Select(p => p.Location).ToArray();
        var bounds = parts.Select(p => p.BoundingBox).ToArray();
        var manualProgram = manual.Drawing.Program;
        var originalCenter = Assert.IsType<ArcMove>(real.Program.Codes[2]).CenterPoint;
        var changes = 0;
        plate.PartAdded += (_, _) => changes++;
        plate.PartRemoved += (_, _) => changes++;
        plate.PartChanged += (_, _) => changes++;
        AutomaticCutOffPlan? plan = null;

        Assert.Throws<ArgumentException>(() =>
        {
            plan = Plan(plate, spacing: repeatedCut ? unsafeX : 35, settings: settings);
            foreach (var definition in plan.Definitions)
                plate.CutOffs.Add(definition);
            plate.RegenerateCutOffs(settings);
        });

        Assert.Null(plan); // No unsafe proposal can escape to the normal acceptance path.
        Assert.Equal(parts, plate.Parts.ToArray());
        Assert.Equal(definitions, plate.CutOffs.ToArray());
        Assert.Equal(programs, plate.Parts.Select(p => p.Program));
        Assert.Equal(text, plate.Parts.Select(p => p.Program.ToString()));
        Assert.Equal(locations, plate.Parts.Select(p => p.Location));
        Assert.Equal(bounds, plate.Parts.Select(p => p.BoundingBox));
        Assert.Same(manualProgram, manual.Drawing.Program);
        Assert.Equal(originalCenter.X, Assert.IsType<ArcMove>(real.Program.Codes[2]).CenterPoint.X);
        Assert.Equal(originalCenter.Y, Assert.IsType<ArcMove>(real.Program.Codes[2]).CenterPoint.Y);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void ExactArcWithinCachedBounds_PlansAndMaterializesSafeCuts()
    {
        var plate = MakeArcPlate(0);
        var real = plate.Parts[0];
        var program = real.Program;
        var settings = new CutOffSettings { PartClearance = 0 };
        var plan = Plan(plate, spacing: 25, settings: settings);
        Assert.True(plan.HasSeparatedTail);
        Assert.False(plan.HasBlockingDiagnostics);
        Assert.Equal(30, plan.OccupiedSpan);
        Assert.Equal(new[] { 25, 30 + AutomaticCutOffPlanner.GeometryTolerance }, Positions(plan));
        Assert.Same(real, Assert.Single(plate.Parts));
        Assert.Empty(plate.CutOffs);
        var previewSegments = plan.PreviewParts.Select(p => Segments(p.Program)).ToArray();

        foreach (var definition in plan.Definitions)
            plate.CutOffs.Add(definition);
        plate.RegenerateCutOffs(settings);

        var cuts = plate.Parts.Where(p => p.BaseDrawing.IsCutOff).ToArray();
        Assert.Equal(plan.Definitions.Count, cuts.Length);
        for (var i = 0; i < cuts.Length; i++)
            Assert.Equal(previewSegments[i], Segments(cuts[i].Program));
        var repeated = Segments(cuts[0].Program);
        Assert.Equal(2, repeated.Length);
        Assert.Equal(20 - System.Math.Sqrt(75), repeated[0].To.Y, 8);
        Assert.Equal(20 + System.Math.Sqrt(75), repeated[1].From.Y, 8);
        var separator = Assert.Single(Segments(cuts[1].Program));
        Assert.Equal(new Vector(plan.TailSeparatorX!.Value, 0), separator.From);
        Assert.Equal(new Vector(plan.TailSeparatorX.Value, 81), separator.To);
        var arc = Assert.Single(ConvertProgram.ToGeometry(real.Program).OfType<Arc>());
        arc.Offset(real.Location);
        Assert.False(arc.Intersects(new Line(separator.From, separator.To), out _));
        Assert.Same(real, plate.Parts[0]);
        Assert.Same(program, real.Program);
        var repeatedPlan = Plan(plate, spacing: 25, settings: settings);
        Assert.Empty(repeatedPlan.Definitions);
        Assert.True(repeatedPlan.HasSeparatedTail);
        Assert.Equal(plan.TailSeparatorX, repeatedPlan.TailSeparatorX);
    }

    [Fact]
    public void InvalidArcCenterAndRecursiveProgram_AreRejectedBeforeGeometryConversion()
    {
        var plate = MakePlate();
        plate.Parts[0].Program.Codes.Add(new ArcMove(new Vector(), new Vector(double.NaN, 0)));
        Assert.ThrowsAny<ArgumentException>(() => Plan(plate));
        plate = MakePlate();
        var program = plate.Parts[0].Program;
        program.Codes.Add(new SubProgramCall { Program = program });
        Assert.ThrowsAny<ArgumentException>(() => Plan(plate));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ApplyAndRepeat_IsIdempotentInEveryQuadrant(int quadrant)
    {
        var plate = MakePlate(quadrant);
        var settings = new CutOffSettings();
        var first = Plan(plate, settings: settings);
        foreach (var definition in first.Definitions)
            plate.CutOffs.Add(definition);
        plate.RegenerateCutOffs(settings);
        var second = Plan(plate, settings: settings);
        Assert.Empty(second.Definitions);
        Assert.True(second.HasSeparatedTail);
        Assert.Equal(first.TailSeparatorX, second.TailSeparatorX);
        Assert.Equal(first.TailLength, second.TailLength);
        Assert.Equal(3, plate.Parts.Count(p => p.BaseDrawing.IsCutOff));
    }

    [Fact]
    public void ExistingSeparator_IsVerifiedUnderCurrentSettings_NotStaleProgram()
    {
        var plate = MakePlate();
        var existing = new CutOff(new Vector(80.5 + Tolerance.Epsilon, 0), CutOffAxis.Vertical);
        existing.Regenerate(plate, new CutOffSettings());
        Assert.NotEmpty(existing.Drawing.Program.Codes);
        plate.CutOffs.Add(existing);
        var program = existing.Drawing.Program;
        var plan = Plan(plate, settings: new CutOffSettings { MinSegmentLength = 100 });
        Assert.False(plan.HasSeparatedTail);
        Assert.Contains(plan.Diagnostics, d => d.Code == AutomaticCutOffDiagnosticCode.NoSafeTailSeparator);
        Assert.Same(program, existing.Drawing.Program);
    }
}
