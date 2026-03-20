# NFP Best-Fit Strategy Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the brute-force slide-based best-fit pair sampling with NFP-based candidate generation that produces mathematically exact interlocking positions.

**Architecture:** New `NfpSlideStrategy : IBestFitStrategy` generates `PairCandidate` offsets from NFP boundary vertices/edges. Shared polygon helper extracted from `AutoNester` to avoid duplication. `BestFitFinder.BuildStrategies` swaps to the new strategy. Everything downstream (evaluator, filter, tiling) stays unchanged.

**Tech Stack:** C# / .NET 8, xunit, existing `NoFitPolygon` (Minkowski sum via Clipper2), `ShapeProfile`, `ConvertProgram`

**Spec:** `docs/superpowers/specs/2026-03-20-nfp-bestfit-strategy-design.md`

---

### Task 1: Extract `PolygonHelper` from `AutoNester`

**Files:**
- Create: `OpenNest.Engine/BestFit/PolygonHelper.cs`
- Modify: `OpenNest.Engine/Nfp/AutoNester.cs:204-343`
- Test: `OpenNest.Tests/PolygonHelperTests.cs`

- [ ] **Step 1: Add shared test helpers and write tests for `PolygonHelper`**

Add `MakeSquareDrawing` and `MakeLShapeDrawing` to `OpenNest.Tests/TestHelpers.cs`:

```csharp
public static Drawing MakeSquareDrawing(double size = 10)
{
    var pgm = new Program();
    pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
    pgm.Codes.Add(new LinearMove(new Vector(size, 0)));
    pgm.Codes.Add(new LinearMove(new Vector(size, size)));
    pgm.Codes.Add(new LinearMove(new Vector(0, size)));
    pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
    return new Drawing("square", pgm);
}

public static Drawing MakeLShapeDrawing()
{
    var pgm = new Program();
    pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
    pgm.Codes.Add(new LinearMove(new Vector(10, 0)));
    pgm.Codes.Add(new LinearMove(new Vector(10, 5)));
    pgm.Codes.Add(new LinearMove(new Vector(5, 5)));
    pgm.Codes.Add(new LinearMove(new Vector(5, 10)));
    pgm.Codes.Add(new LinearMove(new Vector(0, 10)));
    pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
    return new Drawing("lshape", pgm);
}
```

Then create `OpenNest.Tests/PolygonHelperTests.cs`:

```csharp
using OpenNest.CNC;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests;

public class PolygonHelperTests
{

    [Fact]
    public void ExtractPerimeterPolygon_ReturnsPolygon_ForValidDrawing()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        var result = PolygonHelper.ExtractPerimeterPolygon(drawing, 0);
        Assert.NotNull(result.Polygon);
        Assert.True(result.Polygon.Vertices.Count >= 4);
    }

    [Fact]
    public void ExtractPerimeterPolygon_InflatesPolygon_WhenSpacingNonZero()
    {
        var drawing = TestHelpers.MakeSquareDrawing(10);
        var noSpacing = PolygonHelper.ExtractPerimeterPolygon(drawing, 0);
        var withSpacing = PolygonHelper.ExtractPerimeterPolygon(drawing, 1);

        // Inflated polygon should have a larger bounding box.
        noSpacing.Polygon.UpdateBounds();
        withSpacing.Polygon.UpdateBounds();
        Assert.True(withSpacing.Polygon.BoundingBox.Width > noSpacing.Polygon.BoundingBox.Width);
    }

    [Fact]
    public void ExtractPerimeterPolygon_ReturnsNull_ForEmptyDrawing()
    {
        var pgm = new Program();
        var drawing = new Drawing("empty", pgm);
        var result = PolygonHelper.ExtractPerimeterPolygon(drawing, 0);
        Assert.Null(result.Polygon);
    }

    [Fact]
    public void ExtractPerimeterPolygon_CorrectionVector_ReflectsOriginDifference()
    {
        // Square drawing: program bbox starts at (0,0) due to rapid move,
        // perimeter bbox also starts at (0,0) — correction should be near zero.
        var drawing = TestHelpers.MakeSquareDrawing();
        var result = PolygonHelper.ExtractPerimeterPolygon(drawing, 0);
        Assert.NotNull(result.Polygon);
        // For a simple square starting at origin, correction should be small.
        Assert.True(System.Math.Abs(result.Correction.X) < 1);
        Assert.True(System.Math.Abs(result.Correction.Y) < 1);
    }

    [Fact]
    public void RotatePolygon_AtZero_ReturnsSamePolygon()
    {
        var polygon = new Polygon();
        polygon.Vertices.Add(new Vector(0, 0));
        polygon.Vertices.Add(new Vector(10, 0));
        polygon.Vertices.Add(new Vector(10, 10));
        polygon.Vertices.Add(new Vector(0, 10));
        polygon.UpdateBounds();

        var rotated = PolygonHelper.RotatePolygon(polygon, 0);
        Assert.Same(polygon, rotated);
    }

    [Fact]
    public void RotatePolygon_At90Degrees_SwapsDimensions()
    {
        var polygon = new Polygon();
        polygon.Vertices.Add(new Vector(0, 0));
        polygon.Vertices.Add(new Vector(20, 0));
        polygon.Vertices.Add(new Vector(20, 10));
        polygon.Vertices.Add(new Vector(0, 10));
        polygon.UpdateBounds();

        var rotated = PolygonHelper.RotatePolygon(polygon, Angle.HalfPI);
        rotated.UpdateBounds();

        // Width and height should swap (approximately).
        Assert.True(System.Math.Abs(rotated.BoundingBox.Width - 10) < 0.1);
        Assert.True(System.Math.Abs(rotated.BoundingBox.Length - 20) < 0.1);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~PolygonHelperTests" --no-build 2>&1 || dotnet test OpenNest.Tests --filter "FullyQualifiedName~PolygonHelperTests"`
Expected: Build error — `PolygonHelper` does not exist yet.

- [ ] **Step 3: Create `PolygonHelper.cs`**

Create `OpenNest.Engine/BestFit/PolygonHelper.cs`:

```csharp
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Engine.BestFit
{
    public static class PolygonHelper
    {
        public static PolygonExtractionResult ExtractPerimeterPolygon(Drawing drawing, double halfSpacing)
        {
            var entities = ConvertProgram.ToGeometry(drawing.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid)
                .ToList();

            if (entities.Count == 0)
                return new PolygonExtractionResult(null, Vector.Zero);

            var definedShape = new ShapeProfile(entities);
            var perimeter = definedShape.Perimeter;

            if (perimeter == null)
                return new PolygonExtractionResult(null, Vector.Zero);

            // Compute the perimeter bounding box before inflation for coordinate correction.
            perimeter.UpdateBounds();
            var perimeterBb = perimeter.BoundingBox;

            // Inflate by half-spacing if spacing is non-zero.
            Shape inflated;

            if (halfSpacing > 0)
            {
                var offsetEntity = perimeter.OffsetEntity(halfSpacing, OffsetSide.Left);
                inflated = offsetEntity as Shape ?? perimeter;
            }
            else
            {
                inflated = perimeter;
            }

            // Convert to polygon with circumscribed arcs for tight nesting.
            var polygon = inflated.ToPolygonWithTolerance(0.01, circumscribe: true);

            if (polygon.Vertices.Count < 3)
                return new PolygonExtractionResult(null, Vector.Zero);

            // Compute correction: difference between program origin and perimeter origin.
            // Part.CreateAtOrigin normalizes to program bbox; polygon normalizes to perimeter bbox.
            var programBb = drawing.Program.BoundingBox();
            var correction = new Vector(
                perimeterBb.Left - programBb.Location.X,
                perimeterBb.Bottom - programBb.Location.Y);

            // Normalize: move reference point to origin.
            polygon.UpdateBounds();
            var bb = polygon.BoundingBox;
            polygon.Offset(-bb.Left, -bb.Bottom);

            return new PolygonExtractionResult(polygon, correction);
        }

        public static Polygon RotatePolygon(Polygon polygon, double angle)
        {
            if (angle.IsEqualTo(0))
                return polygon;

            var result = new Polygon();
            var cos = System.Math.Cos(angle);
            var sin = System.Math.Sin(angle);

            foreach (var v in polygon.Vertices)
            {
                result.Vertices.Add(new Vector(
                    v.X * cos - v.Y * sin,
                    v.X * sin + v.Y * cos));
            }

            // Re-normalize to origin.
            result.UpdateBounds();
            var bb = result.BoundingBox;
            result.Offset(-bb.Left, -bb.Bottom);

            return result;
        }
    }

    public record PolygonExtractionResult(Polygon Polygon, Vector Correction);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~PolygonHelperTests"`
Expected: All 6 tests PASS.

- [ ] **Step 5: Update `AutoNester` to delegate to `PolygonHelper`**

In `OpenNest.Engine/Nfp/AutoNester.cs`, replace the private `ExtractPerimeterPolygon` and `RotatePolygon` methods (lines 204-343) with delegates to `PolygonHelper`:

```csharp
private static Polygon ExtractPerimeterPolygon(Drawing drawing, double halfSpacing)
{
    return BestFit.PolygonHelper.ExtractPerimeterPolygon(drawing, halfSpacing).Polygon;
}

private static Polygon RotatePolygon(Polygon polygon, double angle)
{
    return BestFit.PolygonHelper.RotatePolygon(polygon, angle);
}
```

- [ ] **Step 6: Build solution to verify no regressions**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds with no errors.

- [ ] **Step 7: Commit**

```bash
git add OpenNest.Engine/BestFit/PolygonHelper.cs OpenNest.Tests/PolygonHelperTests.cs OpenNest.Tests/TestHelpers.cs OpenNest.Engine/Nfp/AutoNester.cs
git commit -m "refactor: extract PolygonHelper from AutoNester for shared polygon operations"
```

---

### Task 2: Create `NfpSlideStrategy`

**Files:**
- Create: `OpenNest.Engine/BestFit/NfpSlideStrategy.cs`
- Test: `OpenNest.Tests/NfpSlideStrategyTests.cs`

- [ ] **Step 1: Write tests for `NfpSlideStrategy`**

Create `OpenNest.Tests/NfpSlideStrategyTests.cs`:

```csharp
using OpenNest.CNC;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests;

public class NfpSlideStrategyTests
{
    [Fact]
    public void GenerateCandidates_ReturnsNonEmpty_ForSquare()
    {
        var strategy = new NfpSlideStrategy(0, 1, "0 deg NFP");
        var drawing = TestHelpers.MakeSquareDrawing();
        var candidates = strategy.GenerateCandidates(drawing, 0.25, 0.25);
        Assert.NotEmpty(candidates);
    }

    [Fact]
    public void GenerateCandidates_AllCandidatesHaveCorrectDrawing()
    {
        var strategy = new NfpSlideStrategy(0, 1, "0 deg NFP");
        var drawing = TestHelpers.MakeSquareDrawing();
        var candidates = strategy.GenerateCandidates(drawing, 0.25, 0.25);
        Assert.All(candidates, c => Assert.Same(drawing, c.Drawing));
    }

    [Fact]
    public void GenerateCandidates_Part1RotationIsAlwaysZero()
    {
        var strategy = new NfpSlideStrategy(Angle.HalfPI, 1, "90 deg NFP");
        var drawing = TestHelpers.MakeSquareDrawing();
        var candidates = strategy.GenerateCandidates(drawing, 0.25, 0.25);
        Assert.All(candidates, c => Assert.Equal(0, c.Part1Rotation));
    }

    [Fact]
    public void GenerateCandidates_Part2RotationMatchesStrategy()
    {
        var rotation = Angle.HalfPI;
        var strategy = new NfpSlideStrategy(rotation, 1, "90 deg NFP");
        var drawing = TestHelpers.MakeSquareDrawing();
        var candidates = strategy.GenerateCandidates(drawing, 0.25, 0.25);
        Assert.All(candidates, c => Assert.Equal(rotation, c.Part2Rotation));
    }

    [Fact]
    public void GenerateCandidates_NoDuplicateOffsets()
    {
        var strategy = new NfpSlideStrategy(0, 1, "0 deg NFP");
        var drawing = TestHelpers.MakeSquareDrawing();
        var candidates = strategy.GenerateCandidates(drawing, 0.25, 0.25);

        var uniqueOffsets = candidates
            .Select(c => (System.Math.Round(c.Part2Offset.X, 6), System.Math.Round(c.Part2Offset.Y, 6)))
            .Distinct()
            .Count();
        Assert.Equal(candidates.Count, uniqueOffsets);
    }

    [Fact]
    public void GenerateCandidates_MoreCandidates_WithSmallerStepSize()
    {
        var strategy = new NfpSlideStrategy(0, 1, "0 deg NFP");
        var drawing = TestHelpers.MakeSquareDrawing();
        var largeStep = strategy.GenerateCandidates(drawing, 0.25, 5.0);
        var smallStep = strategy.GenerateCandidates(drawing, 0.25, 0.5);
        // Smaller step should add more edge samples.
        Assert.True(smallStep.Count >= largeStep.Count);
    }

    [Fact]
    public void GenerateCandidates_ReturnsEmpty_ForEmptyDrawing()
    {
        var strategy = new NfpSlideStrategy(0, 1, "0 deg NFP");
        var pgm = new Program();
        var drawing = new Drawing("empty", pgm);
        var candidates = strategy.GenerateCandidates(drawing, 0.25, 0.25);
        Assert.Empty(candidates);
    }

    [Fact]
    public void GenerateCandidates_LShape_ProducesMoreCandidates_ThanSquare()
    {
        var strategy = new NfpSlideStrategy(0, 1, "0 deg NFP");
        var square = TestHelpers.MakeSquareDrawing();
        var lshape = TestHelpers.MakeLShapeDrawing();

        var squareCandidates = strategy.GenerateCandidates(square, 0.25, 0.25);
        var lshapeCandidates = strategy.GenerateCandidates(lshape, 0.25, 0.25);

        // L-shape NFP has more vertices/edges than square NFP.
        Assert.True(lshapeCandidates.Count > squareCandidates.Count);
    }

    [Fact]
    public void GenerateCandidates_At180Degrees_ProducesCandidates()
    {
        var strategy = new NfpSlideStrategy(System.Math.PI, 1, "180 deg NFP");
        var drawing = TestHelpers.MakeSquareDrawing();
        var candidates = strategy.GenerateCandidates(drawing, 0.25, 0.25);
        Assert.NotEmpty(candidates);
        Assert.All(candidates, c => Assert.Equal(System.Math.PI, c.Part2Rotation));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~NfpSlideStrategyTests" --no-build 2>&1 || dotnet test OpenNest.Tests --filter "FullyQualifiedName~NfpSlideStrategyTests"`
Expected: Build error — `NfpSlideStrategy` does not exist yet.

- [ ] **Step 3: Create `NfpSlideStrategy.cs`**

Create `OpenNest.Engine/BestFit/NfpSlideStrategy.cs`:

```csharp
using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;

namespace OpenNest.Engine.BestFit
{
    public class NfpSlideStrategy : IBestFitStrategy
    {
        private readonly double _part2Rotation;

        public NfpSlideStrategy(double part2Rotation, int type, string description)
        {
            _part2Rotation = part2Rotation;
            Type = type;
            Description = description;
        }

        public int Type { get; }
        public string Description { get; }

        public List<PairCandidate> GenerateCandidates(Drawing drawing, double spacing, double stepSize)
        {
            var candidates = new List<PairCandidate>();

            var halfSpacing = spacing / 2;

            // Extract stationary polygon (Part1 at rotation 0), with spacing applied.
            var stationaryResult = PolygonHelper.ExtractPerimeterPolygon(drawing, halfSpacing);

            if (stationaryResult.Polygon == null)
                return candidates;

            var stationaryPoly = stationaryResult.Polygon;

            // Extract orbiting polygon (Part2 at _part2Rotation).
            // Reuse stationary result if rotation is 0, otherwise rotate.
            var orbitingPoly = PolygonHelper.RotatePolygon(stationaryResult.Polygon, _part2Rotation);

            // Compute NFP.
            var nfp = NoFitPolygon.Compute(stationaryPoly, orbitingPoly);

            if (nfp == null || nfp.Vertices.Count < 3)
                return candidates;

            // Coordinate correction: NFP offsets are in polygon-space.
            // Part.CreateAtOrigin uses program bbox origin.
            var correction = stationaryResult.Correction;

            // Walk NFP boundary — vertices + edge samples.
            var verts = nfp.Vertices;
            var vertCount = nfp.IsClosed() ? verts.Count - 1 : verts.Count;
            var testNumber = 0;

            for (var i = 0; i < vertCount; i++)
            {
                // Add vertex candidate.
                var offset = ApplyCorrection(verts[i], correction);
                candidates.Add(MakeCandidate(drawing, offset, spacing, testNumber++));

                // Add edge samples for long edges.
                var next = (i + 1) % vertCount;
                var dx = verts[next].X - verts[i].X;
                var dy = verts[next].Y - verts[i].Y;
                var edgeLength = System.Math.Sqrt(dx * dx + dy * dy);

                if (edgeLength > stepSize)
                {
                    var steps = (int)(edgeLength / stepSize);
                    for (var s = 1; s < steps; s++)
                    {
                        var t = (double)s / steps;
                        var sample = new Vector(
                            verts[i].X + dx * t,
                            verts[i].Y + dy * t);
                        var sampleOffset = ApplyCorrection(sample, correction);
                        candidates.Add(MakeCandidate(drawing, sampleOffset, spacing, testNumber++));
                    }
                }
            }

            return candidates;
        }

        private static Vector ApplyCorrection(Vector nfpVertex, Vector correction)
        {
            return new Vector(nfpVertex.X + correction.X, nfpVertex.Y + correction.Y);
        }

        private PairCandidate MakeCandidate(Drawing drawing, Vector offset, double spacing, int testNumber)
        {
            return new PairCandidate
            {
                Drawing = drawing,
                Part1Rotation = 0,
                Part2Rotation = _part2Rotation,
                Part2Offset = offset,
                StrategyType = Type,
                TestNumber = testNumber,
                Spacing = spacing
            };
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~NfpSlideStrategyTests"`
Expected: All 9 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Engine/BestFit/NfpSlideStrategy.cs OpenNest.Tests/NfpSlideStrategyTests.cs
git commit -m "feat: add NfpSlideStrategy for NFP-based best-fit candidate generation"
```

---

### Task 3: Wire `NfpSlideStrategy` into `BestFitFinder`

**Files:**
- Modify: `OpenNest.Engine/BestFit/BestFitFinder.cs:78-91`
- Test: `OpenNest.Tests/NfpBestFitIntegrationTests.cs`

- [ ] **Step 1: Write integration test**

Create `OpenNest.Tests/NfpBestFitIntegrationTests.cs`:

```csharp
using OpenNest.CNC;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class NfpBestFitIntegrationTests
{
    [Fact]
    public void FindBestFits_ReturnsKeptResults_ForSquare()
    {
        var finder = new BestFitFinder(120, 60);
        var drawing = TestHelpers.MakeSquareDrawing();
        var results = finder.FindBestFits(drawing);
        Assert.NotEmpty(results);
        Assert.NotEmpty(results.Where(r => r.Keep));
    }

    [Fact]
    public void FindBestFits_ResultsHaveValidDimensions()
    {
        var finder = new BestFitFinder(120, 60);
        var drawing = TestHelpers.MakeSquareDrawing();
        var results = finder.FindBestFits(drawing);

        foreach (var result in results.Where(r => r.Keep))
        {
            Assert.True(result.BoundingWidth > 0);
            Assert.True(result.BoundingHeight > 0);
            Assert.True(result.RotatedArea > 0);
        }
    }

    [Fact]
    public void FindBestFits_LShape_HasBetterUtilization_ThanBoundingBox()
    {
        var finder = new BestFitFinder(120, 60);
        var drawing = TestHelpers.MakeLShapeDrawing();
        var results = finder.FindBestFits(drawing);

        // At least one kept result should have >50% utilization
        // (L-shapes interlock well, bounding box alone would be ~50%).
        var bestUtilization = results
            .Where(r => r.Keep)
            .Max(r => r.Utilization);
        Assert.True(bestUtilization > 0.5);
    }

    [Fact]
    public void FindBestFits_NoOverlaps_InKeptResults()
    {
        var finder = new BestFitFinder(120, 60);
        var drawing = TestHelpers.MakeSquareDrawing();
        var results = finder.FindBestFits(drawing);

        // All kept results should be non-overlapping (verified by PairEvaluator).
        Assert.All(results.Where(r => r.Keep), r =>
            Assert.Equal("Valid", r.Reason));
    }
}
```

- [ ] **Step 2: Swap `BuildStrategies` to use `NfpSlideStrategy`**

In `OpenNest.Engine/BestFit/BestFitFinder.cs`, replace the `BuildStrategies` method (lines 78-91):

```csharp
private List<IBestFitStrategy> BuildStrategies(Drawing drawing)
{
    var angles = GetRotationAngles(drawing);
    var strategies = new List<IBestFitStrategy>();
    var type = 1;

    foreach (var angle in angles)
    {
        var desc = $"{Angle.ToDegrees(angle):F1} deg NFP";
        strategies.Add(new NfpSlideStrategy(angle, type++, desc));
    }

    return strategies;
}
```

Add `using OpenNest.Math;` to the top of the file if not already present.

- [ ] **Step 3: Run integration tests**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~NfpBestFitIntegrationTests"`
Expected: All 4 tests PASS with the NFP pipeline.

- [ ] **Step 4: Run all tests to check for regressions**

Run: `dotnet test OpenNest.Tests`
Expected: All tests PASS.

- [ ] **Step 5: Build full solution**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add OpenNest.Engine/BestFit/BestFitFinder.cs OpenNest.Tests/NfpBestFitIntegrationTests.cs
git commit -m "feat: wire NfpSlideStrategy into BestFitFinder pipeline"
```

---

### Task 4: Remove `AutoNester.Optimize` calls

The `Optimize` calls are dead weight for single-drawing fills (as discussed). Now that NFP is properly integrated via best-fit, remove the no-op optimization passes.

**Files:**
- Modify: `OpenNest.Engine/NestEngineBase.cs:133-134`
- Modify: `OpenNest.Engine/NfpNestEngine.cs:52-53`
- Modify: `OpenNest.Engine/StripNestEngine.cs:126-127`
- Modify: `OpenNest/Controls/PlateView.cs` (the line calling `AutoNester.Optimize`)

- [ ] **Step 1: Remove `AutoNester.Optimize` call from `NestEngineBase.cs`**

In `OpenNest.Engine/NestEngineBase.cs`, remove lines 133-134:
```csharp
// NFP optimization pass — re-place parts using geometry-aware BLF.
allParts = AutoNester.Optimize(allParts, Plate);
```

- [ ] **Step 2: Remove `AutoNester.Optimize` call from `NfpNestEngine.cs`**

In `OpenNest.Engine/NfpNestEngine.cs`, remove lines 52-53:
```csharp
// NFP optimization pass — re-place parts using geometry-aware BLF.
parts = AutoNester.Optimize(parts, Plate);
```

- [ ] **Step 3: Remove `AutoNester.Optimize` call from `StripNestEngine.cs`**

In `OpenNest.Engine/StripNestEngine.cs`, remove lines 126-127:
```csharp
// NFP optimization pass — re-place parts using geometry-aware BLF.
allParts = AutoNester.Optimize(allParts, Plate);
```

- [ ] **Step 4: Remove `AutoNester.Optimize` call from `PlateView.cs`**

In `OpenNest/Controls/PlateView.cs`, find the line calling `AutoNester.Optimize(result, workArea, spacing)` and replace:
```csharp
return AutoNester.Optimize(result, workArea, spacing);
```
with:
```csharp
return result;
```

- [ ] **Step 5: Run all tests**

Run: `dotnet test OpenNest.Tests`
Expected: All tests PASS.

- [ ] **Step 6: Build full solution**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds.

- [ ] **Step 7: Commit**

```bash
git add OpenNest.Engine/NestEngineBase.cs OpenNest.Engine/NfpNestEngine.cs OpenNest.Engine/StripNestEngine.cs OpenNest/Controls/PlateView.cs
git commit -m "perf: remove no-op AutoNester.Optimize calls from fill pipelines"
```
