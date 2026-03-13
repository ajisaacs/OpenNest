# Best-Fit Pair Finding Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a pair-finding engine that arranges two copies of a part in the tightest configuration, then tiles that pair across a plate.

**Architecture:** Strategy pattern where `RotationSlideStrategy` instances (parameterized by angle) generate candidate pair configurations by sliding one part against another using existing raycast collision. A `PairEvaluator` scores candidates by bounding area, a `BestFitFilter` prunes bad fits, and a `TileEvaluator` simulates tiling the best pairs onto a plate.

**Tech Stack:** .NET Framework 4.8, C# 7.3, OpenNest.Engine (class library referencing OpenNest.Core)

---

## Important Context

### Codebase Conventions
- **All angles are in radians** — use `Angle.ToRadians()`, `Angle.HalfPI`, `Angle.TwoPI`
- **Always use `var`** instead of explicit types
- **`OpenNest.Math` shadows `System.Math`** — use `System.Math` fully qualified
- **Legacy `.csproj`** — every new `.cs` file must be added to `OpenNest.Engine.csproj` `<Compile>` items
- **No test project exists** — skip TDD steps, verify by building

### Key Existing Types
- `Vector` (struct, `OpenNest.Geometry`) — 2D point, has `Rotate()`, `Offset()`, `DistanceTo()`, operators
- `Box` (class, `OpenNest.Geometry`) — AABB with `Left/Right/Top/Bottom/Width/Height`, `Contains()`, `Intersects()`
- `Part` (class, `OpenNest`) — wraps `Drawing` + `Program`, has `Location`, `Rotation`, `Rotate()`, `Offset()`, `Clone()`, `BoundingBox`
- `Drawing` (class, `OpenNest`) — has `Program`, `Area`, `Name`
- `Program` (class, `OpenNest.CNC`) — G-code program, has `BoundingBox()`, `Rotate()`, `Clone()`
- `Plate` (class, `OpenNest`) — has `Size` (Width/Height), `EdgeSpacing`, `PartSpacing`, `WorkArea()`
- `Shape` (class, `OpenNest.Geometry`) — closed contour, has `Intersects(Shape)`, `Area()`, `ToPolygon()`, `OffsetEntity()`
- `Polygon` (class, `OpenNest.Geometry`) — vertex list, has `FindBestRotation()`, `Rotate()`, `Offset()`
- `ConvexHull.Compute(IList<Vector>)` — returns closed `Polygon`
- `BoundingRectangleResult` — `Angle`, `Width`, `Height`, `Area` from rotating calipers

### Key Existing Methods (in `Helper`)
- `Helper.GetShapes(IEnumerable<Entity>)` — builds `Shape` list from geometry entities
- `Helper.GetPartLines(Part, PushDirection)` — gets polygon edges facing a direction (uses chord tolerance 0.01)
- `Helper.DirectionalDistance(movingLines, stationaryLines, PushDirection)` — raycasts to find minimum contact distance
- `Helper.OppositeDirection(PushDirection)` — flips direction
- `ConvertProgram.ToGeometry(Program)` — converts CNC program to geometry entities

### How Existing Push/Contact Works (in `FillLinear`)
```
1. Create partA at position
2. Clone to partB, offset by bounding box dimension along axis
3. Get facing lines: movingLines = GetPartLines(partB, pushDir)
4. Get facing lines: stationaryLines = GetPartLines(partA, oppositeDir)
5. slideDistance = DirectionalDistance(movingLines, stationaryLines, pushDir)
6. copyDistance = bboxDim - slideDistance + spacing
```
The best-fit system adapts this: part2 is rotated, offset perpendicular to the push axis, then pushed toward part1.

### Hull Edge Angles (existing pattern in `NestEngine`)
```
1. Convert part to polygon via ConvertProgram.ToGeometry → GetShapes → ToPolygonWithTolerance
2. Compute convex hull via ConvexHull.Compute(vertices)
3. Extract edge angles: atan2(dy, dx) for each hull edge
4. Deduplicate angles (within Tolerance.Epsilon)
```

---

## Task 1: PairCandidate Data Class

**Files:**
- Create: `OpenNest.Engine/BestFit/PairCandidate.cs`
- Modify: `OpenNest.Engine/OpenNest.Engine.csproj` (add Compile entry)

**Step 1: Create directory and file**

```csharp
using OpenNest.Geometry;

namespace OpenNest.Engine.BestFit
{
    public class PairCandidate
    {
        public Drawing Drawing { get; set; }
        public double Part1Rotation { get; set; }
        public double Part2Rotation { get; set; }
        public Vector Part2Offset { get; set; }
        public int StrategyType { get; set; }
        public int TestNumber { get; set; }
        public double Spacing { get; set; }
    }
}
```

**Step 2: Add to .csproj**

Add inside the `<ItemGroup>` that contains `<Compile>` entries, before `</ItemGroup>`:
```xml
<Compile Include="BestFit\PairCandidate.cs" />
```

**Step 3: Build to verify**

Run: `msbuild OpenNest.Engine/OpenNest.Engine.csproj /p:Configuration=Debug /v:q`
Expected: Build succeeded

**Step 4: Commit**

```
feat: add PairCandidate data class for best-fit pair finding
```

---

## Task 2: BestFitResult Data Class

**Files:**
- Create: `OpenNest.Engine/BestFit/BestFitResult.cs`
- Modify: `OpenNest.Engine/OpenNest.Engine.csproj`

**Step 1: Create file**

```csharp
namespace OpenNest.Engine.BestFit
{
    public class BestFitResult
    {
        public PairCandidate Candidate { get; set; }
        public double RotatedArea { get; set; }
        public double BoundingWidth { get; set; }
        public double BoundingHeight { get; set; }
        public double OptimalRotation { get; set; }
        public bool Keep { get; set; }
        public string Reason { get; set; }
        public double TrueArea { get; set; }

        public double Utilization
        {
            get { return RotatedArea > 0 ? TrueArea / RotatedArea : 0; }
        }

        public double LongestSide
        {
            get { return System.Math.Max(BoundingWidth, BoundingHeight); }
        }

        public double ShortestSide
        {
            get { return System.Math.Min(BoundingWidth, BoundingHeight); }
        }
    }

    public enum BestFitSortField
    {
        Area,
        LongestSide,
        ShortestSide,
        Type,
        OriginalSequence,
        Keep,
        WhyKeepDrop
    }
}
```

**Step 2: Add to .csproj**

```xml
<Compile Include="BestFit\BestFitResult.cs" />
```

**Step 3: Build to verify**

Run: `msbuild OpenNest.Engine/OpenNest.Engine.csproj /p:Configuration=Debug /v:q`

**Step 4: Commit**

```
feat: add BestFitResult data class and BestFitSortField enum
```

---

## Task 3: IBestFitStrategy Interface

**Files:**
- Create: `OpenNest.Engine/BestFit/IBestFitStrategy.cs`
- Modify: `OpenNest.Engine/OpenNest.Engine.csproj`

**Step 1: Create file**

```csharp
using System.Collections.Generic;

namespace OpenNest.Engine.BestFit
{
    public interface IBestFitStrategy
    {
        int Type { get; }
        string Description { get; }
        List<PairCandidate> GenerateCandidates(Drawing drawing, double spacing, double stepSize);
    }
}
```

**Step 2: Add to .csproj**

```xml
<Compile Include="BestFit\IBestFitStrategy.cs" />
```

**Step 3: Build to verify**

**Step 4: Commit**

```
feat: add IBestFitStrategy interface
```

---

## Task 4: RotationSlideStrategy

This is the core algorithm. It generates pair candidates by:
1. Creating part1 at origin
2. Creating part2 with a specific rotation
3. For each push direction (Left, Down):
   - For each perpendicular offset (stepping across the part):
     - Place part2 far away along the push axis
     - Use `DirectionalDistance` to find contact
     - Record position as a candidate

**Files:**
- Create: `OpenNest.Engine/BestFit/RotationSlideStrategy.cs`
- Modify: `OpenNest.Engine/OpenNest.Engine.csproj`

**Step 1: Create file**

```csharp
using System;
using System.Collections.Generic;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.BestFit
{
    public class RotationSlideStrategy : IBestFitStrategy
    {
        private const double ChordTolerance = 0.01;

        public RotationSlideStrategy(double part2Rotation, int type, string description)
        {
            Part2Rotation = part2Rotation;
            Type = type;
            Description = description;
        }

        public double Part2Rotation { get; }
        public int Type { get; }
        public string Description { get; }

        public List<PairCandidate> GenerateCandidates(Drawing drawing, double spacing, double stepSize)
        {
            var candidates = new List<PairCandidate>();

            var part1 = new Part(drawing);
            var bbox1 = part1.Program.BoundingBox();
            part1.Offset(-bbox1.Location.X, -bbox1.Location.Y);
            part1.UpdateBounds();

            var part2Template = new Part(drawing);
            if (!Part2Rotation.IsEqualTo(0))
                part2Template.Rotate(Part2Rotation);
            var bbox2 = part2Template.Program.BoundingBox();
            part2Template.Offset(-bbox2.Location.X, -bbox2.Location.Y);
            part2Template.UpdateBounds();

            var testNumber = 0;

            // Slide along horizontal axis (push left toward part1)
            GenerateCandidatesForAxis(
                part1, part2Template, drawing, spacing, stepSize,
                PushDirection.Left, candidates, ref testNumber);

            // Slide along vertical axis (push down toward part1)
            GenerateCandidatesForAxis(
                part1, part2Template, drawing, spacing, stepSize,
                PushDirection.Down, candidates, ref testNumber);

            return candidates;
        }

        private void GenerateCandidatesForAxis(
            Part part1, Part part2Template, Drawing drawing,
            double spacing, double stepSize, PushDirection pushDir,
            List<PairCandidate> candidates, ref int testNumber)
        {
            var bbox1 = part1.BoundingBox;
            var bbox2 = part2Template.BoundingBox;

            // Determine perpendicular range based on push direction
            double perpMin, perpMax, pushStartOffset;
            bool isHorizontalPush = (pushDir == PushDirection.Left || pushDir == PushDirection.Right);

            if (isHorizontalPush)
            {
                // Pushing horizontally: perpendicular axis is Y
                perpMin = -(bbox2.Height + spacing);
                perpMax = bbox1.Height + bbox2.Height + spacing;
                pushStartOffset = bbox1.Width + bbox2.Width + spacing * 2;
            }
            else
            {
                // Pushing vertically: perpendicular axis is X
                perpMin = -(bbox2.Width + spacing);
                perpMax = bbox1.Width + bbox2.Width + spacing;
                pushStartOffset = bbox1.Height + bbox2.Height + spacing * 2;
            }

            var part1Lines = Helper.GetOffsetPartLines(part1, spacing / 2);
            var opposite = Helper.OppositeDirection(pushDir);

            for (var offset = perpMin; offset <= perpMax; offset += stepSize)
            {
                var part2 = (Part)part2Template.Clone();

                if (isHorizontalPush)
                    part2.Offset(pushStartOffset, offset);
                else
                    part2.Offset(offset, pushStartOffset);

                var movingLines = Helper.GetOffsetPartLines(part2, spacing / 2);
                var slideDist = Helper.DirectionalDistance(movingLines, part1Lines, pushDir);

                if (slideDist >= double.MaxValue || slideDist < 0)
                    continue;

                // Move part2 to contact position
                var contactOffset = GetPushVector(pushDir, slideDist);
                var finalPosition = part2.Location + contactOffset;

                candidates.Add(new PairCandidate
                {
                    Drawing = drawing,
                    Part1Rotation = 0,
                    Part2Rotation = Part2Rotation,
                    Part2Offset = finalPosition,
                    StrategyType = Type,
                    TestNumber = testNumber++,
                    Spacing = spacing
                });
            }
        }

        private static Vector GetPushVector(PushDirection direction, double distance)
        {
            switch (direction)
            {
                case PushDirection.Left: return new Vector(-distance, 0);
                case PushDirection.Right: return new Vector(distance, 0);
                case PushDirection.Down: return new Vector(0, -distance);
                case PushDirection.Up: return new Vector(0, distance);
                default: return Vector.Zero;
            }
        }
    }
}
```

**Step 2: Add to .csproj**

```xml
<Compile Include="BestFit\RotationSlideStrategy.cs" />
```

**Step 3: Build to verify**

Run: `msbuild OpenNest.Engine/OpenNest.Engine.csproj /p:Configuration=Debug /v:q`

**Step 4: Commit**

```
feat: add RotationSlideStrategy with directional push contact algorithm
```

---

## Task 5: PairEvaluator

Scores each candidate by computing the combined bounding box, finding the optimal rotation (via rotating calipers on the convex hull), and checking for overlaps.

**Files:**
- Create: `OpenNest.Engine/BestFit/PairEvaluator.cs`
- Modify: `OpenNest.Engine/OpenNest.Engine.csproj`

**Step 1: Create file**

```csharp
using System.Collections.Generic;
using System.Linq;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Engine.BestFit
{
    public class PairEvaluator
    {
        private const double ChordTolerance = 0.01;

        public BestFitResult Evaluate(PairCandidate candidate)
        {
            var drawing = candidate.Drawing;

            // Build part1 at origin
            var part1 = new Part(drawing);
            var bbox1 = part1.Program.BoundingBox();
            part1.Offset(-bbox1.Location.X, -bbox1.Location.Y);
            part1.UpdateBounds();

            // Build part2 with rotation and offset
            var part2 = new Part(drawing);
            if (!candidate.Part2Rotation.IsEqualTo(0))
                part2.Rotate(candidate.Part2Rotation);
            var bbox2 = part2.Program.BoundingBox();
            part2.Offset(-bbox2.Location.X, -bbox2.Location.Y);
            part2.Location = candidate.Part2Offset;
            part2.UpdateBounds();

            // Check overlap via shape intersection
            var overlaps = CheckOverlap(part1, part2, candidate.Spacing);

            // Collect all polygon vertices for convex hull / optimal rotation
            var allPoints = GetPartVertices(part1);
            allPoints.AddRange(GetPartVertices(part2));

            // Find optimal bounding rectangle via rotating calipers
            double bestArea, bestWidth, bestHeight, bestRotation;

            if (allPoints.Count >= 3)
            {
                var hull = ConvexHull.Compute(allPoints);
                var result = RotatingCalipers.MinimumBoundingRectangle(hull);
                bestArea = result.Area;
                bestWidth = result.Width;
                bestHeight = result.Height;
                bestRotation = result.Angle;
            }
            else
            {
                var combinedBox = ((IEnumerable<IBoundable>)new[] { part1, part2 }).GetBoundingBox();
                bestArea = combinedBox.Area();
                bestWidth = combinedBox.Width;
                bestHeight = combinedBox.Height;
                bestRotation = 0;
            }

            var trueArea = drawing.Area * 2;

            return new BestFitResult
            {
                Candidate = candidate,
                RotatedArea = bestArea,
                BoundingWidth = bestWidth,
                BoundingHeight = bestHeight,
                OptimalRotation = bestRotation,
                TrueArea = trueArea,
                Keep = !overlaps,
                Reason = overlaps ? "Overlap detected" : "Valid"
            };
        }

        private bool CheckOverlap(Part part1, Part part2, double spacing)
        {
            var shapes1 = GetPartShapes(part1);
            var shapes2 = GetPartShapes(part2);

            for (var i = 0; i < shapes1.Count; i++)
            {
                for (var j = 0; j < shapes2.Count; j++)
                {
                    List<Vector> pts;

                    if (shapes1[i].Intersects(shapes2[j], out pts))
                        return true;
                }
            }

            return false;
        }

        private List<Shape> GetPartShapes(Part part)
        {
            var entities = ConvertProgram.ToGeometry(part.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid);
            var shapes = Helper.GetShapes(entities);
            shapes.ForEach(s => s.Offset(part.Location));
            return shapes;
        }

        private List<Vector> GetPartVertices(Part part)
        {
            var entities = ConvertProgram.ToGeometry(part.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid);
            var shapes = Helper.GetShapes(entities);
            var points = new List<Vector>();

            foreach (var shape in shapes)
            {
                var polygon = shape.ToPolygonWithTolerance(ChordTolerance);
                polygon.Offset(part.Location);

                foreach (var vertex in polygon.Vertices)
                    points.Add(vertex);
            }

            return points;
        }
    }
}
```

**Step 2: Add to .csproj**

```xml
<Compile Include="BestFit\PairEvaluator.cs" />
```

**Step 3: Build to verify**

**Step 4: Commit**

```
feat: add PairEvaluator with overlap detection and optimal rotation
```

---

## Task 6: BestFitFilter

**Files:**
- Create: `OpenNest.Engine/BestFit/BestFitFilter.cs`
- Modify: `OpenNest.Engine/OpenNest.Engine.csproj`

**Step 1: Create file**

```csharp
using System.Collections.Generic;

namespace OpenNest.Engine.BestFit
{
    public class BestFitFilter
    {
        public double MaxPlateWidth { get; set; }
        public double MaxPlateHeight { get; set; }
        public double MaxAspectRatio { get; set; } = 5.0;
        public double MinUtilization { get; set; } = 0.3;

        public void Apply(List<BestFitResult> results)
        {
            foreach (var result in results)
            {
                if (!result.Keep)
                    continue;

                if (result.ShortestSide > System.Math.Min(MaxPlateWidth, MaxPlateHeight))
                {
                    result.Keep = false;
                    result.Reason = "Exceeds plate dimensions";
                    continue;
                }

                var aspect = result.LongestSide / result.ShortestSide;

                if (aspect > MaxAspectRatio)
                {
                    result.Keep = false;
                    result.Reason = string.Format("Aspect ratio {0:F1} exceeds max {1}", aspect, MaxAspectRatio);
                    continue;
                }

                if (result.Utilization < MinUtilization)
                {
                    result.Keep = false;
                    result.Reason = string.Format("Utilization {0:P0} below minimum", result.Utilization);
                    continue;
                }

                result.Reason = "Valid";
            }
        }
    }
}
```

**Step 2: Add to .csproj**

```xml
<Compile Include="BestFit\BestFitFilter.cs" />
```

**Step 3: Build to verify**

**Step 4: Commit**

```
feat: add BestFitFilter with plate size, aspect ratio, and utilization rules
```

---

## Task 7: TileResult and TileEvaluator

**Files:**
- Create: `OpenNest.Engine/BestFit/Tiling/TileResult.cs`
- Create: `OpenNest.Engine/BestFit/Tiling/TileEvaluator.cs`
- Modify: `OpenNest.Engine/OpenNest.Engine.csproj`

**Step 1: Create TileResult.cs**

```csharp
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Engine.BestFit.Tiling
{
    public class TileResult
    {
        public BestFitResult BestFit { get; set; }
        public int PairsNested { get; set; }
        public int PartsNested { get; set; }
        public int Rows { get; set; }
        public int Columns { get; set; }
        public double Utilization { get; set; }
        public List<PairPlacement> Placements { get; set; }
        public bool PairRotated { get; set; }
    }

    public class PairPlacement
    {
        public Vector Position { get; set; }
        public double PairRotation { get; set; }
    }
}
```

**Step 2: Create TileEvaluator.cs**

```csharp
using System;
using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.BestFit.Tiling
{
    public class TileEvaluator
    {
        public TileResult Evaluate(BestFitResult bestFit, Plate plate)
        {
            var plateWidth = plate.Size.Width - plate.EdgeSpacing.Left - plate.EdgeSpacing.Right;
            var plateHeight = plate.Size.Height - plate.EdgeSpacing.Top - plate.EdgeSpacing.Bottom;

            var result1 = TryTile(bestFit, plateWidth, plateHeight, false);
            var result2 = TryTile(bestFit, plateWidth, plateHeight, true);
            return result1.PartsNested >= result2.PartsNested ? result1 : result2;
        }

        private TileResult TryTile(BestFitResult bestFit, double plateWidth, double plateHeight, bool rotatePair)
        {
            var pairWidth = rotatePair ? bestFit.BoundingHeight : bestFit.BoundingWidth;
            var pairHeight = rotatePair ? bestFit.BoundingWidth : bestFit.BoundingHeight;
            var spacing = bestFit.Candidate.Spacing;

            var cols = (int)System.Math.Floor((plateWidth + spacing) / (pairWidth + spacing));
            var rows = (int)System.Math.Floor((plateHeight + spacing) / (pairHeight + spacing));
            var pairsNested = cols * rows;
            var partsNested = pairsNested * 2;

            var usedArea = partsNested * (bestFit.TrueArea / 2);
            var plateArea = plateWidth * plateHeight;

            var placements = new List<PairPlacement>();

            for (var row = 0; row < rows; row++)
            {
                for (var col = 0; col < cols; col++)
                {
                    placements.Add(new PairPlacement
                    {
                        Position = new Vector(
                            col * (pairWidth + spacing),
                            row * (pairHeight + spacing)),
                        PairRotation = rotatePair ? Angle.HalfPI : 0
                    });
                }
            }

            return new TileResult
            {
                BestFit = bestFit,
                PairsNested = pairsNested,
                PartsNested = partsNested,
                Rows = rows,
                Columns = cols,
                Utilization = plateArea > 0 ? usedArea / plateArea : 0,
                Placements = placements,
                PairRotated = rotatePair
            };
        }
    }
}
```

**Step 3: Add to .csproj**

```xml
<Compile Include="BestFit\Tiling\TileResult.cs" />
<Compile Include="BestFit\Tiling\TileEvaluator.cs" />
```

**Step 4: Build to verify**

**Step 5: Commit**

```
feat: add TileEvaluator and TileResult for pair tiling on plates
```

---

## Task 8: BestFitFinder (Orchestrator)

Computes hull edge angles from the drawing, builds `RotationSlideStrategy` instances for each angle in `{0, pi/2, pi, 3pi/2} + hull edges + hull edges + pi`, runs all strategies, evaluates, filters, and sorts.

**Files:**
- Create: `OpenNest.Engine/BestFit/BestFitFinder.cs`
- Modify: `OpenNest.Engine/OpenNest.Engine.csproj`

**Step 1: Create file**

```csharp
using System.Collections.Generic;
using System.Linq;
using OpenNest.Converters;
using OpenNest.Engine.BestFit.Tiling;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.BestFit
{
    public class BestFitFinder
    {
        private readonly PairEvaluator _evaluator;
        private readonly BestFitFilter _filter;

        public BestFitFinder(double maxPlateWidth, double maxPlateHeight)
        {
            _evaluator = new PairEvaluator();
            _filter = new BestFitFilter
            {
                MaxPlateWidth = maxPlateWidth,
                MaxPlateHeight = maxPlateHeight
            };
        }

        public List<BestFitResult> FindBestFits(
            Drawing drawing,
            double spacing = 0.25,
            double stepSize = 0.25,
            BestFitSortField sortBy = BestFitSortField.Area)
        {
            var strategies = BuildStrategies(drawing);

            var allCandidates = new List<PairCandidate>();

            foreach (var strategy in strategies)
                allCandidates.AddRange(strategy.GenerateCandidates(drawing, spacing, stepSize));

            var results = allCandidates.Select(c => _evaluator.Evaluate(c)).ToList();

            _filter.Apply(results);

            results = SortResults(results, sortBy);

            for (var i = 0; i < results.Count; i++)
                results[i].Candidate.TestNumber = i;

            return results;
        }

        public List<TileResult> FindAndTile(
            Drawing drawing, Plate plate,
            double spacing = 0.25, double stepSize = 0.25, int topN = 10)
        {
            var bestFits = FindBestFits(drawing, spacing, stepSize);
            var tileEvaluator = new TileEvaluator();

            return bestFits
                .Where(r => r.Keep)
                .Take(topN)
                .Select(r => tileEvaluator.Evaluate(r, plate))
                .OrderByDescending(t => t.PartsNested)
                .ThenByDescending(t => t.Utilization)
                .ToList();
        }

        private List<IBestFitStrategy> BuildStrategies(Drawing drawing)
        {
            var angles = GetRotationAngles(drawing);
            var strategies = new List<IBestFitStrategy>();
            var type = 1;

            foreach (var angle in angles)
            {
                var desc = string.Format("{0:F1} deg rotated, offset slide", Angle.ToDegrees(angle));
                strategies.Add(new RotationSlideStrategy(angle, type++, desc));
            }

            return strategies;
        }

        private List<double> GetRotationAngles(Drawing drawing)
        {
            var angles = new List<double>
            {
                0,
                Angle.HalfPI,
                System.Math.PI,
                Angle.HalfPI * 3
            };

            // Add hull edge angles
            var hullAngles = GetHullEdgeAngles(drawing);

            foreach (var hullAngle in hullAngles)
            {
                AddUniqueAngle(angles, hullAngle);
                AddUniqueAngle(angles, Angle.NormalizeRad(hullAngle + System.Math.PI));
            }

            return angles;
        }

        private List<double> GetHullEdgeAngles(Drawing drawing)
        {
            var entities = ConvertProgram.ToGeometry(drawing.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid);
            var shapes = Helper.GetShapes(entities);

            var points = new List<Vector>();

            foreach (var shape in shapes)
            {
                var polygon = shape.ToPolygonWithTolerance(0.1);
                points.AddRange(polygon.Vertices);
            }

            if (points.Count < 3)
                return new List<double>();

            var hull = ConvexHull.Compute(points);
            var vertices = hull.Vertices;
            var n = hull.IsClosed() ? vertices.Count - 1 : vertices.Count;
            var hullAngles = new List<double>();

            for (var i = 0; i < n; i++)
            {
                var next = (i + 1) % n;
                var dx = vertices[next].X - vertices[i].X;
                var dy = vertices[next].Y - vertices[i].Y;

                if (dx * dx + dy * dy < Tolerance.Epsilon)
                    continue;

                var angle = Angle.NormalizeRad(System.Math.Atan2(dy, dx));
                AddUniqueAngle(hullAngles, angle);
            }

            return hullAngles;
        }

        private static void AddUniqueAngle(List<double> angles, double angle)
        {
            angle = Angle.NormalizeRad(angle);

            foreach (var existing in angles)
            {
                if (existing.IsEqualTo(angle))
                    return;
            }

            angles.Add(angle);
        }

        private List<BestFitResult> SortResults(List<BestFitResult> results, BestFitSortField sortBy)
        {
            switch (sortBy)
            {
                case BestFitSortField.Area:
                    return results.OrderBy(r => r.RotatedArea).ToList();
                case BestFitSortField.LongestSide:
                    return results.OrderBy(r => r.LongestSide).ToList();
                case BestFitSortField.ShortestSide:
                    return results.OrderBy(r => r.ShortestSide).ToList();
                case BestFitSortField.Type:
                    return results.OrderBy(r => r.Candidate.StrategyType)
                        .ThenBy(r => r.Candidate.TestNumber).ToList();
                case BestFitSortField.OriginalSequence:
                    return results.OrderBy(r => r.Candidate.TestNumber).ToList();
                case BestFitSortField.Keep:
                    return results.OrderByDescending(r => r.Keep)
                        .ThenBy(r => r.RotatedArea).ToList();
                case BestFitSortField.WhyKeepDrop:
                    return results.OrderBy(r => r.Reason)
                        .ThenBy(r => r.RotatedArea).ToList();
                default:
                    return results;
            }
        }
    }
}
```

**Step 2: Add to .csproj**

```xml
<Compile Include="BestFit\BestFitFinder.cs" />
```

**Step 3: Build full solution to verify all references resolve**

Run: `msbuild OpenNest.sln /p:Configuration=Debug /v:q`

**Step 4: Commit**

```
feat: add BestFitFinder orchestrator with hull edge angle strategies
```

---

## Task 9: Final Integration Build and Smoke Test

**Step 1: Clean build of entire solution**

Run: `msbuild OpenNest.sln /t:Rebuild /p:Configuration=Debug /v:q`
Expected: Build succeeded, 0 errors

**Step 2: Verify all new files are included**

Check that all 8 new files appear in the build output by reviewing the .csproj has these entries:
```xml
<Compile Include="BestFit\PairCandidate.cs" />
<Compile Include="BestFit\BestFitResult.cs" />
<Compile Include="BestFit\IBestFitStrategy.cs" />
<Compile Include="BestFit\RotationSlideStrategy.cs" />
<Compile Include="BestFit\PairEvaluator.cs" />
<Compile Include="BestFit\BestFitFilter.cs" />
<Compile Include="BestFit\Tiling\TileResult.cs" />
<Compile Include="BestFit\Tiling\TileEvaluator.cs" />
<Compile Include="BestFit\BestFitFinder.cs" />
```

**Step 3: Final commit**

If any build fixes were needed, commit them:
```
fix: resolve build issues in best-fit pair finding engine
```
