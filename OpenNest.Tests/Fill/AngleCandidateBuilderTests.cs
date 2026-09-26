using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Engine.ML;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Fill;

// The Debug integration check uses process-wide PerfCounters.
[Collection(nameof(OpenNest.Tests.BestFit.FillCacheCollection))]
public class AngleCandidateBuilderTests
{
    private static Drawing MakeRectDrawing(double w, double h)
    {
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new OpenNest.CNC.RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(w, 0)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(w, h)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, h)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, 0)));
        return new Drawing("rect", pgm);
    }

    private static ClassificationResult MakeClassification(
        double primaryAngle = 0,
        PartType type = PartType.Irregular
    ) => new ClassificationResult { PrimaryAngle = primaryAngle, Type = type };

    [Theory]
    [InlineData(PartType.Circle)]
    [InlineData(PartType.Rectangle)]
    public void Characterization_ClassifiedParts_PreserveExactBaseAngleOrder(PartType type)
    {
        var builder = new AngleCandidateBuilder();
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };
        var primary = Angle.ToRadians(7);

        var angles = builder.Build(item, MakeClassification(primary, type), new Box(0, 0, 100, 50));

        var expected = type == PartType.Circle ? new[] { 0.0 } : new[] { primary, primary + Angle.HalfPI };
        Assert.Equal(expected, angles);
    }

    [Theory]
    [InlineData(PartType.Circle)]
    [InlineData(PartType.Rectangle)]
    [InlineData(PartType.Irregular)]
    public void Characterization_Constraints_OverrideEveryClassification(PartType type)
    {
        var builder = new AngleCandidateBuilder();
        var item = new NestItem
        {
            Drawing = MakeRectDrawing(20, 10),
            RotationStart = 0,
            RotationEnd = Angle.HalfPI,
            StepAngle = Angle.ToRadians(30),
        };

        var angles = builder.Build(item, MakeClassification(0.1, type), new Box(0, 0, 100, 50));

        AssertAngleOrder(new[] { 0.0, 30, 60, 90 }.Select(Angle.ToRadians), angles);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Characterization_NonpositiveConstraintStep_UsesFiveDegrees(double step)
    {
        var builder = new AngleCandidateBuilder();
        var item = new NestItem
        {
            Drawing = MakeRectDrawing(20, 10),
            RotationStart = Angle.ToRadians(10),
            RotationEnd = Angle.ToRadians(20),
            StepAngle = step,
        };

        var angles = builder.Build(item, MakeClassification(), new Box(0, 0, 100, 50));

        AssertAngleOrder(new[] { 10.0, 15, 20 }.Select(Angle.ToRadians), angles);
    }

    [Fact]
    public void Characterization_ReversedConstraints_FallBackToStart()
    {
        var builder = new AngleCandidateBuilder();
        var item = new NestItem
        {
            Drawing = MakeRectDrawing(20, 10),
            RotationStart = Angle.ToRadians(40),
            RotationEnd = Angle.ToRadians(10),
        };

        var angles = builder.Build(item, MakeClassification(), new Box(0, 0, 100, 50));

        Assert.Equal(new[] { item.RotationStart }, angles);
    }

    [Fact]
    public void Characterization_KnownGoodAngles_PreserveBaseOrderAndPruning()
    {
        var builder = new AngleCandidateBuilder();
        builder.RecordProductive(new List<AngleResult>
        {
            new() { AngleDeg = 0, PartCount = 1 },
            new() { AngleDeg = 45, PartCount = 2 },
            new() { AngleDeg = 45, PartCount = 3 },
            new() { AngleDeg = 97, PartCount = 1 },
            new() { AngleDeg = 65, PartCount = 0 },
            new() { AngleDeg = 120, PartCount = -1 },
        });
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };

        var angles = builder.Build(item, MakeClassification(Angle.ToRadians(7)), new Box(0, 0, 100, 50));

        AssertAngleOrder(new[] { 7.0, 97, 0, 45 }.Select(Angle.ToRadians), angles);
    }

    private static void AssertAngleOrder(IEnumerable<double> expected, List<double> actual)
    {
        var values = expected.ToArray();
        Assert.Equal(values.Length, actual.Count);
        for (var i = 0; i < values.Length; i++)
            Assert.Equal(values[i], actual[i], precision: 12);
    }

    // These delegates are deterministic control-flow doubles, not ONNX accuracy evidence.
    private sealed class PredictionCalls
    {
        public bool Available { get; set; } = true;
        public PartFeatures? Features { get; set; } = new();
        public List<double>? Prediction { get; set; }
        public List<string> Calls { get; } = new();
        public Drawing? ExtractedDrawing { get; private set; }
        public bool? IncludeBitmask { get; private set; }
        public PartFeatures? PredictedFeatures { get; private set; }
        public double SheetWidth { get; private set; }
        public double SheetHeight { get; private set; }

        public AngleCandidateBuilder CreateBuilder() => new(
            () =>
            {
                Calls.Add("available");
                return Available;
            },
            (drawing, includeBitmask) =>
            {
                Calls.Add("extract");
                ExtractedDrawing = drawing;
                IncludeBitmask = includeBitmask;
                return Features!;
            },
            (features, width, height) =>
            {
                Calls.Add("predict");
                PredictedFeatures = features;
                SheetWidth = width;
                SheetHeight = height;
                return Prediction!;
            });
    }

    // The existing repeated-add sweep includes a final value just below PI (near 180°).
    // Preserve it: replacing the loop with 36 integer-indexed samples changes behavior.
    private static IEnumerable<double> FallbackAngles() =>
        new[] { 7.0, 97 }.Concat(Enumerable.Range(0, 37).Select(i => i * 5.0)).Select(Angle.ToRadians);

    [Fact]
    public void Build_Unavailable_SkipsExtractionAndPrediction_PreservesFallbackOrder()
    {
        var calls = new PredictionCalls { Available = false };
        var builder = calls.CreateBuilder();
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };

        var angles = builder.Build(item, MakeClassification(Angle.ToRadians(7)), new Box(0, 0, 100, 50));

        Assert.Equal(new[] { "available" }, calls.Calls);
        AssertAngleOrder(FallbackAngles(), angles);
    }

    [Fact]
    public void Build_Available_ExtractsScalarsOnce_ThenForwardsFeaturesAndWorkAreaDimensions()
    {
        var calls = new PredictionCalls();
        var builder = calls.CreateBuilder();
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };
        var workArea = new Box(3, 5, 123, 47);

        var angles = builder.Build(item, MakeClassification(Angle.ToRadians(7)), workArea);

        Assert.Equal(new[] { "available", "extract", "predict" }, calls.Calls);
        Assert.Same(item.Drawing, calls.ExtractedDrawing);
        Assert.Equal(false, calls.IncludeBitmask);
        Assert.Same(calls.Features, calls.PredictedFeatures);
        // Box.Width is Y and Box.Length is X; preserve this existing argument order.
        Assert.Equal(47, calls.SheetWidth);
        Assert.Equal(123, calls.SheetHeight);
        AssertAngleOrder(FallbackAngles(), angles);
    }

    [Fact]
    public void Build_NullFeatures_SkipsPrediction_PreservesFallbackOrder()
    {
        var calls = new PredictionCalls { Features = null };
        var builder = calls.CreateBuilder();
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };

        var angles = builder.Build(item, MakeClassification(Angle.ToRadians(7)), new Box(0, 0, 100, 50));

        Assert.Equal(new[] { "available", "extract" }, calls.Calls);
        Assert.Equal(false, calls.IncludeBitmask);
        AssertAngleOrder(FallbackAngles(), angles);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_NullOrEmptyPrediction_PreservesFallbackOrder(bool empty)
    {
        var calls = new PredictionCalls { Prediction = empty ? new List<double>() : null };
        var builder = calls.CreateBuilder();
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };

        var angles = builder.Build(item, MakeClassification(Angle.ToRadians(7)), new Box(0, 0, 100, 50));

        Assert.Equal(new[] { "available", "extract", "predict" }, calls.Calls);
        AssertAngleOrder(FallbackAngles(), angles);
        if (empty)
        {
            Assert.Empty(calls.Prediction!);
            Assert.NotSame(calls.Prediction, angles);
        }
    }

    [Fact]
    public void Build_NonemptyPrediction_PreservesPredictionThenBaseThenSweepOrder()
    {
        var predicted = new[] { 42.0, 0, 97 }.Select(Angle.ToRadians).ToList();
        var original = predicted.ToArray();
        var calls = new PredictionCalls { Prediction = predicted };
        var builder = calls.CreateBuilder();
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };

        var angles = builder.Build(item, MakeClassification(Angle.ToRadians(7)), new Box(0, 0, 100, 50));

        var expected = new[] { 42.0, 0, 97, 7 }
            .Concat(Enumerable.Range(1, 36).Select(i => i * 5.0))
            .Select(Angle.ToRadians);
        AssertAngleOrder(expected, angles);
        Assert.Equal(original, predicted);
        Assert.NotSame(predicted, angles);
        Assert.Equal(new[] { "available", "extract", "predict" }, calls.Calls);
    }

    [Fact]
    public void Build_DuplicatePrediction_KeepsPredictionDuplicates_ButDeduplicatesAddedAnglesByTolerance()
    {
        var primary = Angle.ToRadians(7);
        var predicted = new List<double>
        {
            Angle.ToRadians(42), Angle.ToRadians(42), primary + Tolerance.Epsilon / 2, Angle.HalfPI,
        };
        var original = predicted.ToArray();
        var calls = new PredictionCalls { Prediction = predicted };
        var builder = calls.CreateBuilder();
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };

        var angles = builder.Build(item, MakeClassification(primary), new Box(0, 0, 100, 50));

        // Existing behavior preserves the prediction prefix verbatim, even duplicates.
        var expected = original.Concat(new[] { primary + Angle.HalfPI })
            .Concat(Enumerable.Range(0, 37).Where(i => i != 18).Select(i => Angle.ToRadians(i * 5)));
        AssertAngleOrder(expected, angles);
        Assert.Equal(original, angles.Take(original.Length));
        Assert.Equal(original, predicted);
        Assert.NotSame(predicted, angles);
    }

    [Fact]
    public void Build_Unavailable_WithCardinalBase_DeduplicatesSweepWithoutReordering()
    {
        var calls = new PredictionCalls { Available = false };
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };

        var angles = calls.CreateBuilder().Build(item, MakeClassification(), new Box(0, 0, 100, 50));

        var expected = new[] { 0.0, 90 }
            .Concat(Enumerable.Range(1, 36).Where(i => i != 18).Select(i => i * 5.0))
            .Select(Angle.ToRadians);
        AssertAngleOrder(expected, angles);
        Assert.Equal(new[] { "available" }, calls.Calls);
    }

    [Theory]
    [InlineData("circle")]
    [InlineData("rectangle")]
    [InlineData("constraints-circle")]
    [InlineData("constraints-rectangle")]
    [InlineData("constraints-irregular")]
    [InlineData("known-good")]
    public void Build_BypassBranches_DoNotCheckAvailabilityOrExtractOrPredict(string branch)
    {
        var calls = new PredictionCalls();
        var builder = calls.CreateBuilder();
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };
        var classification = MakeClassification(Angle.ToRadians(7));
        var expected = new[] { 7.0, 97, 45 };
        if (branch.EndsWith("circle"))
        {
            classification.Type = PartType.Circle;
            expected = new[] { 0.0 };
        }
        else if (branch.EndsWith("rectangle"))
        {
            classification.Type = PartType.Rectangle;
            expected = new[] { 7.0, 97 };
        }
        if (branch.StartsWith("constraints-"))
        {
            item.RotationStart = Angle.ToRadians(10);
            item.RotationEnd = Angle.ToRadians(20);
            item.StepAngle = 0;
            expected = new[] { 10.0, 15, 20 };
        }
        if (branch == "known-good")
            builder.RecordProductive(new List<AngleResult> { new() { AngleDeg = 45, PartCount = 1 } });

        var angles = builder.Build(item, classification, new Box(0, 0, 100, 50));

        Assert.Empty(calls.Calls);
        AssertAngleOrder(expected.Select(Angle.ToRadians), angles);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_ForceFullSweep_IgnoresKnownGoodPruning_AndStillHonorsAvailability(bool available)
    {
        var calls = new PredictionCalls { Available = available };
        var builder = calls.CreateBuilder();
        builder.ForceFullSweep = true;
        builder.RecordProductive(new List<AngleResult> { new() { AngleDeg = 45, PartCount = 1 } });
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };

        var angles = builder.Build(item, MakeClassification(Angle.ToRadians(7)), new Box(0, 0, 100, 50));

        AssertAngleOrder(FallbackAngles(), angles);
        Assert.Equal(available ? new[] { "available", "extract", "predict" } : new[] { "available" }, calls.Calls);
    }

    [Fact]
    public void Build_OnlyNonproductiveAngles_DoNotPruneOrBypassAvailability()
    {
        var calls = new PredictionCalls { Available = false };
        var builder = calls.CreateBuilder();
        builder.RecordProductive(new List<AngleResult>
        {
            new() { AngleDeg = 45, PartCount = 0 },
            new() { AngleDeg = 60, PartCount = -1 },
        });
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };

        var angles = builder.Build(item, MakeClassification(Angle.ToRadians(7)), new Box(0, 0, 100, 50));

        AssertAngleOrder(FallbackAngles(), angles);
        Assert.Equal(new[] { "available" }, calls.Calls);
    }

#if DEBUG
    [Fact]
    public void Build_Available_RealExtraction_DoesNotScanBitmaskCells()
    {
        var extractionCalls = 0;
        var predictionCalls = 0;
        var predictedFeatures = (PartFeatures?)null;
        var builder = new AngleCandidateBuilder(
            () => true,
            (drawing, includeBitmask) =>
            {
                extractionCalls++;
                return FeatureExtractor.Extract(drawing, includeBitmask);
            },
            (features, width, height) =>
            {
                predictionCalls++;
                predictedFeatures = features;
                return null!;
            });
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };

        PerfCounters.Reset();
        try
        {
            var angles = builder.Build(item, MakeClassification(Angle.ToRadians(7)), new Box(0, 0, 100, 50));

            Assert.Equal(1, extractionCalls);
            Assert.Equal(1, predictionCalls);
            Assert.Equal(0, PerfCounters.FeatureBitmaskCells);
            Assert.NotNull(predictedFeatures);
            Assert.Null(predictedFeatures.Bitmask);
            AssertAngleOrder(FallbackAngles(), angles);
        }
        finally
        {
            PerfCounters.Reset();
        }
    }
#endif

    [Fact]
    public void Build_ReturnsAtLeastTwoAngles()
    {
        var builder = new AngleCandidateBuilder();
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };
        var workArea = new Box(0, 0, 100, 100);

        var angles = builder.Build(item, MakeClassification(), workArea);

        Assert.True(angles.Count >= 2);
    }

    [Fact]
    public void Build_RectangleType_NarrowWorkArea_UsesBaseAnglesOnly()
    {
        var builder = new AngleCandidateBuilder();
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };
        var narrowArea = new Box(0, 0, 100, 8); // narrower than part's longest side

        var angles = builder.Build(item, MakeClassification(0, PartType.Rectangle), narrowArea);

        // Rectangle classification always returns exactly 2 angles regardless of work area
        Assert.Equal(2, angles.Count);
    }

    [Fact]
    public void ForceFullSweep_ProducesFullSweep()
    {
        var builder = new AngleCandidateBuilder { ForceFullSweep = true };
        var item = new NestItem { Drawing = MakeRectDrawing(5, 5) };
        var workArea = new Box(0, 0, 100, 100);

        var angles = builder.Build(item, MakeClassification(), workArea);

        // Full sweep at 5deg steps = ~36 angles (0 to 175), plus base angles
        Assert.True(angles.Count > 10);
    }

    [Fact]
    public void RecordProductive_PrunesSubsequentBuilds()
    {
        var builder = new AngleCandidateBuilder { ForceFullSweep = true };
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };
        var workArea = new Box(0, 0, 100, 8);

        // First build — full sweep
        var firstAngles = builder.Build(item, MakeClassification(), workArea);

        // Record some as productive
        var productive = new List<AngleResult>
        {
            new AngleResult { AngleDeg = 0, PartCount = 5 },
            new AngleResult { AngleDeg = 45, PartCount = 3 },
        };
        builder.RecordProductive(productive);

        // Second build — should be pruned to known-good + base angles
        builder.ForceFullSweep = false;
        var secondAngles = builder.Build(item, MakeClassification(), workArea);

        Assert.True(
            secondAngles.Count < firstAngles.Count,
            $"Pruned ({secondAngles.Count}) should be fewer than full ({firstAngles.Count})"
        );
    }

    [Fact]
    public void Build_RectanglePart_ReturnsTwoAngles()
    {
        var builder = new AngleCandidateBuilder();
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };
        var workArea = new Box(0, 0, 100, 100);
        var classification = MakeClassification(0, PartType.Rectangle);

        var angles = builder.Build(item, classification, workArea);

        Assert.Equal(2, angles.Count);
    }

    [Fact]
    public void Build_CirclePart_ReturnsOneAngle()
    {
        var builder = new AngleCandidateBuilder();
        var item = new NestItem { Drawing = MakeRectDrawing(10, 10) };
        var workArea = new Box(0, 0, 100, 100);
        var classification = MakeClassification(0, PartType.Circle);

        var angles = builder.Build(item, classification, workArea);

        Assert.Single(angles);
        Assert.Equal(0, angles[0]);
    }

    [Fact]
    public void Build_UserConstraints_OverrideRectangleClassification()
    {
        var builder = new AngleCandidateBuilder();
        var item = new NestItem
        {
            Drawing = MakeRectDrawing(100, 50),
            RotationStart = Angle.ToRadians(10),
            RotationEnd = Angle.ToRadians(90),
            StepAngle = Angle.ToRadians(10),
        };
        var classification = MakeClassification(0, PartType.Rectangle);
        var workArea = new Box(0, 0, 1000, 500);

        var angles = builder.Build(item, classification, workArea);

        Assert.True(
            angles.Count > 2,
            $"User constraints should override rect classification, got {angles.Count} angles"
        );
    }

    [Fact]
    public void Build_UserConstraints_StartingAtZero_AreRespected()
    {
        var builder = new AngleCandidateBuilder();
        var item = new NestItem
        {
            Drawing = MakeRectDrawing(100, 50),
            RotationStart = 0,
            RotationEnd = System.Math.PI,
            StepAngle = Angle.ToRadians(45),
        };
        var classification = MakeClassification(0, PartType.Rectangle);
        var workArea = new Box(0, 0, 1000, 500);

        var angles = builder.Build(item, classification, workArea);

        // Start=0, End=PI is NOT "no constraints" — it's a real 0-180 range
        Assert.True(
            angles.Count > 2,
            $"0-to-PI constraint should produce multiple angles, got {angles.Count}"
        );
    }
}
