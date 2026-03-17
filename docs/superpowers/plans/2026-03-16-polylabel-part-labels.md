# Polylabel Part Label Positioning Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Position part ID labels at the visual center of each part using the polylabel (pole of inaccessibility) algorithm, so labels are readable and don't overlap adjacent parts.

**Architecture:** Add a `PolyLabel` static class in `OpenNest.Geometry` that finds the point inside a polygon farthest from all edges (including holes). `LayoutPart` caches this point in program-local coordinates and transforms it each frame for rendering.

**Tech Stack:** .NET 8, xUnit, WinForms (System.Drawing)

**Spec:** `docs/superpowers/specs/2026-03-16-polylabel-part-labels-design.md`

---

## Chunk 1: Polylabel Algorithm

### Task 1: PolyLabel — square polygon test + implementation

**Files:**
- Create: `OpenNest.Core/Geometry/PolyLabel.cs`
- Create: `OpenNest.Tests/PolyLabelTests.cs`

- [ ] **Step 1: Write the failing test for a square polygon**

```csharp
// OpenNest.Tests/PolyLabelTests.cs
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class PolyLabelTests
{
    private static Polygon Square(double size)
    {
        var p = new Polygon();
        p.Vertices.Add(new Vector(0, 0));
        p.Vertices.Add(new Vector(size, 0));
        p.Vertices.Add(new Vector(size, size));
        p.Vertices.Add(new Vector(0, size));
        return p;
    }

    [Fact]
    public void Square_ReturnsCenterPoint()
    {
        var poly = Square(100);

        var result = PolyLabel.Find(poly);

        Assert.Equal(50, result.X, 1.0);
        Assert.Equal(50, result.Y, 1.0);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OpenNest.Tests --filter PolyLabelTests.Square_ReturnsCenterPoint`
Expected: FAIL — `PolyLabel` does not exist.

- [ ] **Step 3: Implement PolyLabel.Find**

```csharp
// OpenNest.Core/Geometry/PolyLabel.cs
using System;
using System.Collections.Generic;

namespace OpenNest.Geometry
{
    public static class PolyLabel
    {
        public static Vector Find(Polygon outer, IList<Polygon> holes = null, double precision = 0.5)
        {
            if (outer.Vertices.Count < 3)
                return outer.Vertices.Count > 0
                    ? outer.Vertices[0]
                    : new Vector();

            var minX = double.MaxValue;
            var minY = double.MaxValue;
            var maxX = double.MinValue;
            var maxY = double.MinValue;

            for (var i = 0; i < outer.Vertices.Count; i++)
            {
                var v = outer.Vertices[i];
                if (v.X < minX) minX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.X > maxX) maxX = v.X;
                if (v.Y > maxY) maxY = v.Y;
            }

            var width = maxX - minX;
            var height = maxY - minY;
            var cellSize = System.Math.Min(width, height);

            if (cellSize == 0)
                return new Vector((minX + maxX) / 2, (minY + maxY) / 2);

            var halfCell = cellSize / 2;

            var queue = new List<Cell>();

            for (var x = minX; x < maxX; x += cellSize)
                for (var y = minY; y < maxY; y += cellSize)
                    queue.Add(new Cell(x + halfCell, y + halfCell, halfCell, outer, holes));

            queue.Sort((a, b) => b.MaxDist.CompareTo(a.MaxDist));

            var bestCell = GetCentroidCell(outer, holes);

            for (var i = 0; i < queue.Count; i++)
                if (queue[i].Dist > bestCell.Dist)
                {
                    bestCell = queue[i];
                    break;
                }

            while (queue.Count > 0)
            {
                var cell = queue[0];
                queue.RemoveAt(0);

                if (cell.Dist > bestCell.Dist)
                    bestCell = cell;

                if (cell.MaxDist - bestCell.Dist <= precision)
                    continue;

                halfCell = cell.HalfSize / 2;

                var newCells = new[]
                {
                    new Cell(cell.X - halfCell, cell.Y - halfCell, halfCell, outer, holes),
                    new Cell(cell.X + halfCell, cell.Y - halfCell, halfCell, outer, holes),
                    new Cell(cell.X - halfCell, cell.Y + halfCell, halfCell, outer, holes),
                    new Cell(cell.X + halfCell, cell.Y + halfCell, halfCell, outer, holes),
                };

                for (var i = 0; i < newCells.Length; i++)
                {
                    if (newCells[i].MaxDist > bestCell.Dist + precision)
                        InsertSorted(queue, newCells[i]);
                }
            }

            return new Vector(bestCell.X, bestCell.Y);
        }

        private static void InsertSorted(List<Cell> list, Cell cell)
        {
            var idx = 0;
            while (idx < list.Count && list[idx].MaxDist > cell.MaxDist)
                idx++;
            list.Insert(idx, cell);
        }

        private static Cell GetCentroidCell(Polygon outer, IList<Polygon> holes)
        {
            var area = 0.0;
            var cx = 0.0;
            var cy = 0.0;
            var verts = outer.Vertices;

            for (int i = 0, j = verts.Count - 1; i < verts.Count; j = i++)
            {
                var a = verts[i];
                var b = verts[j];
                var cross = a.X * b.Y - b.X * a.Y;
                cx += (a.X + b.X) * cross;
                cy += (a.Y + b.Y) * cross;
                area += cross;
            }

            area *= 0.5;

            if (System.Math.Abs(area) < 1e-10)
                return new Cell(verts[0].X, verts[0].Y, 0, outer, holes);

            cx /= (6 * area);
            cy /= (6 * area);

            return new Cell(cx, cy, 0, outer, holes);
        }

        private static double PointToPolygonDist(double x, double y, Polygon polygon)
        {
            var minDist = double.MaxValue;
            var verts = polygon.Vertices;

            for (int i = 0, j = verts.Count - 1; i < verts.Count; j = i++)
            {
                var a = verts[i];
                var b = verts[j];

                var dx = b.X - a.X;
                var dy = b.Y - a.Y;

                if (dx != 0 || dy != 0)
                {
                    var t = ((x - a.X) * dx + (y - a.Y) * dy) / (dx * dx + dy * dy);

                    if (t > 1)
                    {
                        a = b;
                    }
                    else if (t > 0)
                    {
                        a = new Vector(a.X + dx * t, a.Y + dy * t);
                    }
                }

                var segDx = x - a.X;
                var segDy = y - a.Y;
                var dist = System.Math.Sqrt(segDx * segDx + segDy * segDy);

                if (dist < minDist)
                    minDist = dist;
            }

            return minDist;
        }

        private sealed class Cell
        {
            public readonly double X;
            public readonly double Y;
            public readonly double HalfSize;
            public readonly double Dist;
            public readonly double MaxDist;

            public Cell(double x, double y, double halfSize, Polygon outer, IList<Polygon> holes)
            {
                X = x;
                Y = y;
                HalfSize = halfSize;

                var pt = new Vector(x, y);
                var inside = outer.ContainsPoint(pt);

                if (inside && holes != null)
                {
                    for (var i = 0; i < holes.Count; i++)
                    {
                        if (holes[i].ContainsPoint(pt))
                        {
                            inside = false;
                            break;
                        }
                    }
                }

                Dist = PointToAllEdgesDist(x, y, outer, holes);

                if (!inside)
                    Dist = -Dist;

                MaxDist = Dist + HalfSize * System.Math.Sqrt(2);
            }
        }

        private static double PointToAllEdgesDist(double x, double y, Polygon outer, IList<Polygon> holes)
        {
            var minDist = PointToPolygonDist(x, y, outer);

            if (holes != null)
            {
                for (var i = 0; i < holes.Count; i++)
                {
                    var d = PointToPolygonDist(x, y, holes[i]);
                    if (d < minDist)
                        minDist = d;
                }
            }

            return minDist;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test OpenNest.Tests --filter PolyLabelTests.Square_ReturnsCenterPoint`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Core/Geometry/PolyLabel.cs OpenNest.Tests/PolyLabelTests.cs
git commit -m "feat(geometry): add PolyLabel algorithm with square test"
```

### Task 2: PolyLabel — additional shape tests

**Files:**
- Modify: `OpenNest.Tests/PolyLabelTests.cs`

- [ ] **Step 1: Add tests for L-shape, triangle, thin rectangle, C-shape, hole, and degenerate**

```csharp
// Append to PolyLabelTests.cs

[Fact]
public void Triangle_ReturnsIncenter()
{
    var p = new Polygon();
    p.Vertices.Add(new Vector(0, 0));
    p.Vertices.Add(new Vector(100, 0));
    p.Vertices.Add(new Vector(50, 86.6));

    var result = PolyLabel.Find(p);

    // Incenter of equilateral triangle is at (50, ~28.9)
    Assert.Equal(50, result.X, 1.0);
    Assert.Equal(28.9, result.Y, 1.0);
    Assert.True(p.ContainsPoint(result));
}

[Fact]
public void LShape_ReturnsPointInBottomLobe()
{
    // L-shape: 100x100 with 50x50 cut from top-right
    var p = new Polygon();
    p.Vertices.Add(new Vector(0, 0));
    p.Vertices.Add(new Vector(100, 0));
    p.Vertices.Add(new Vector(100, 50));
    p.Vertices.Add(new Vector(50, 50));
    p.Vertices.Add(new Vector(50, 100));
    p.Vertices.Add(new Vector(0, 100));

    var result = PolyLabel.Find(p);

    Assert.True(p.ContainsPoint(result));
    // The bottom 100x50 lobe is the widest region
    Assert.True(result.Y < 50, $"Expected label in bottom lobe, got Y={result.Y}");
}

[Fact]
public void ThinRectangle_CenteredOnBothAxes()
{
    var p = new Polygon();
    p.Vertices.Add(new Vector(0, 0));
    p.Vertices.Add(new Vector(200, 0));
    p.Vertices.Add(new Vector(200, 10));
    p.Vertices.Add(new Vector(0, 10));

    var result = PolyLabel.Find(p);

    Assert.Equal(100, result.X, 1.0);
    Assert.Equal(5, result.Y, 1.0);
    Assert.True(p.ContainsPoint(result));
}

[Fact]
public void SquareWithLargeHole_AvoidsHole()
{
    var outer = Square(100);

    var hole = new Polygon();
    hole.Vertices.Add(new Vector(20, 20));
    hole.Vertices.Add(new Vector(80, 20));
    hole.Vertices.Add(new Vector(80, 80));
    hole.Vertices.Add(new Vector(20, 80));

    var result = PolyLabel.Find(outer, new[] { hole });

    // Point should be inside outer but outside hole
    Assert.True(outer.ContainsPoint(result));
    Assert.False(hole.ContainsPoint(result));
}

[Fact]
public void CShape_ReturnsPointInLeftBar()
{
    // C-shape opening to the right: left bar is 20 wide, top/bottom arms are 20 tall
    var p = new Polygon();
    p.Vertices.Add(new Vector(0, 0));
    p.Vertices.Add(new Vector(100, 0));
    p.Vertices.Add(new Vector(100, 20));
    p.Vertices.Add(new Vector(20, 20));
    p.Vertices.Add(new Vector(20, 80));
    p.Vertices.Add(new Vector(100, 80));
    p.Vertices.Add(new Vector(100, 100));
    p.Vertices.Add(new Vector(0, 100));

    var result = PolyLabel.Find(p);

    Assert.True(p.ContainsPoint(result));
    // Label should be in the left vertical bar (x < 20), not at bbox center (50, 50)
    Assert.True(result.X < 20, $"Expected label in left bar, got X={result.X}");
}

[Fact]
public void DegeneratePolygon_ReturnsFallback()
{
    var p = new Polygon();
    p.Vertices.Add(new Vector(5, 5));

    var result = PolyLabel.Find(p);

    Assert.Equal(5, result.X, 0.01);
    Assert.Equal(5, result.Y, 0.01);
}
```

- [ ] **Step 2: Run all PolyLabel tests**

Run: `dotnet test OpenNest.Tests --filter PolyLabelTests`
Expected: All PASS

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Tests/PolyLabelTests.cs
git commit -m "test(geometry): add PolyLabel tests for L, C, triangle, thin rect, hole"
```

## Chunk 2: Label Rendering

### Task 3: Update LayoutPart label positioning

**Files:**
- Modify: `OpenNest/LayoutPart.cs`

- [ ] **Step 1: Add cached label point field and computation method**

Add a `Vector? _labelPoint` field and a method to compute it from the part's geometry. Uses `ShapeProfile` to identify the outer contour and holes.

```csharp
// Add field near the top of the class (after the existing private fields):
private Vector? _labelPoint;

// Add method:
private Vector ComputeLabelPoint()
{
    var entities = ConvertProgram.ToGeometry(BasePart.Program);
    var nonRapid = entities.Where(e => e.Layer != SpecialLayers.Rapid).ToList();

    if (nonRapid.Count == 0)
    {
        var bbox = BasePart.Program.BoundingBox();
        return new Vector(bbox.Location.X + bbox.Width / 2, bbox.Location.Y + bbox.Length / 2);
    }

    var profile = new ShapeProfile(nonRapid);
    var outer = profile.Perimeter.ToPolygonWithTolerance(0.1);

    List<Polygon> holes = null;

    if (profile.Cutouts.Count > 0)
    {
        holes = new List<Polygon>();
        foreach (var cutout in profile.Cutouts)
            holes.Add(cutout.ToPolygonWithTolerance(0.1));
    }

    return PolyLabel.Find(outer, holes);
}
```

- [ ] **Step 2: Invalidate the cache when IsDirty is set**

Modify the `IsDirty` property to clear `_labelPoint`:

```csharp
// Replace:
internal bool IsDirty { get; set; }

// With:
private bool _isDirty;
internal bool IsDirty
{
    get => _isDirty;
    set
    {
        _isDirty = value;
        if (value) _labelPoint = null;
    }
}
```

- [ ] **Step 3: Add screen-space label point field and compute it in Update()**

Compute the polylabel in program-local coordinates (cached, expensive) and transform to screen space in `Update()` (cheap, runs on every zoom/pan).

```csharp
// Add field:
private PointF _labelScreenPoint;

// Replace existing Update():
public void Update(DrawControl plateView)
{
    Path = GraphicsHelper.GetGraphicsPath(BasePart.Program, BasePart.Location);
    Path.Transform(plateView.Matrix);

    _labelPoint ??= ComputeLabelPoint();
    var labelPt = new PointF(
        (float)(_labelPoint.Value.X + BasePart.Location.X),
        (float)(_labelPoint.Value.Y + BasePart.Location.Y));
    var pts = new[] { labelPt };
    plateView.Matrix.TransformPoints(pts);
    _labelScreenPoint = pts[0];

    IsDirty = false;
}
```

Note: setting `IsDirty = false` at the end of `Update()` will NOT clear `_labelPoint` because the setter only clears when `value` is `true`.

- [ ] **Step 4: Update Draw(Graphics g, string id) to use the cached screen point**

```csharp
// Replace the existing Draw(Graphics g, string id) method body.
// Old code (lines 85-101 of LayoutPart.cs):
//   if (IsSelected) { ... } else { ... }
//   var pt = Path.PointCount > 0 ? Path.PathPoints[0] : PointF.Empty;
//   g.DrawString(id, programIdFont, Brushes.Black, pt.X, pt.Y);

// New code:
public void Draw(Graphics g, string id)
{
    if (IsSelected)
    {
        g.FillPath(selectedBrush, Path);
        g.DrawPath(selectedPen, Path);
    }
    else
    {
        g.FillPath(brush, Path);
        g.DrawPath(pen, Path);
    }

    using var sf = new StringFormat
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center
    };
    g.DrawString(id, programIdFont, Brushes.Black, _labelScreenPoint.X, _labelScreenPoint.Y, sf);
}
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds with no errors.

- [ ] **Step 6: Commit**

```bash
git add OpenNest/LayoutPart.cs
git commit -m "feat(ui): position part labels at polylabel center"
```

### Task 4: Manual visual verification

- [ ] **Step 1: Run the application and verify labels**

Run the OpenNest application, load a nest with multiple parts, and verify:
- Labels appear centered inside each part.
- Labels don't overlap adjacent part edges.
- Labels stay centered when zooming and panning.
- Parts with holes have labels placed in the solid material, not in the hole.

- [ ] **Step 2: Run all tests**

Run: `dotnet test OpenNest.Tests`
Expected: All tests pass.

- [ ] **Step 3: Final commit if any tweaks needed**
