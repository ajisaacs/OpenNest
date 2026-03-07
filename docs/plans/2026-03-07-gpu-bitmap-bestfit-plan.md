# GPU Bitmap Best Fit Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add GPU-accelerated bitmap overlap testing to the best fit pair evaluator using ILGPU, alongside the existing geometry evaluator.

**Architecture:** New `OpenNest.Gpu` project holds `PartBitmap` and `GpuPairEvaluator`. Engine gets an `IPairEvaluator` interface that both geometry and GPU paths implement. `BestFitFinder` accepts the interface; `NestEngine` selects which evaluator via a `UseGpu` flag.

**Tech Stack:** .NET 8, ILGPU 1.5+, ILGPU.Algorithms

---

### Task 1: Add `Polygon.ContainsPoint` to Core

**Files:**
- Modify: `OpenNest.Core/Geometry/Polygon.cs:610` (before closing brace)

**Step 1: Add ContainsPoint method**

Insert before the closing `}` of the `Polygon` class (line 611):

```csharp
public bool ContainsPoint(Vector pt)
{
    var n = IsClosed() ? Vertices.Count - 1 : Vertices.Count;

    if (n < 3)
        return false;

    var inside = false;

    for (var i = 0, j = n - 1; i < n; j = i++)
    {
        var vi = Vertices[i];
        var vj = Vertices[j];

        if ((vi.Y > pt.Y) != (vj.Y > pt.Y) &&
            pt.X < (vj.X - vi.X) * (pt.Y - vi.Y) / (vj.Y - vi.Y) + vi.X)
        {
            inside = !inside;
        }
    }

    return inside;
}
```

This is the standard even-odd ray casting algorithm. Casts a ray rightward from `pt`, toggles `inside` at each edge crossing.

**Step 2: Build to verify**

Run: `dotnet build OpenNest.Core/OpenNest.Core.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add OpenNest.Core/Geometry/Polygon.cs
git commit -m "feat: add Polygon.ContainsPoint using ray casting"
```

---

### Task 2: Extract `IPairEvaluator` interface in Engine

**Files:**
- Create: `OpenNest.Engine/BestFit/IPairEvaluator.cs`
- Modify: `OpenNest.Engine/BestFit/PairEvaluator.cs`

**Step 1: Create the interface**

```csharp
using System.Collections.Generic;

namespace OpenNest.Engine.BestFit
{
    public interface IPairEvaluator
    {
        List<BestFitResult> EvaluateAll(List<PairCandidate> candidates);
    }
}
```

**Step 2: Make `PairEvaluator` implement the interface**

In `PairEvaluator.cs`, change the class declaration (line 9) to:

```csharp
public class PairEvaluator : IPairEvaluator
```

Add the `EvaluateAll` method. This wraps the existing per-candidate `Evaluate` in a `Parallel.ForEach`, matching the current behavior in `BestFitFinder.FindBestFits()`:

```csharp
public List<BestFitResult> EvaluateAll(List<PairCandidate> candidates)
{
    var resultBag = new System.Collections.Concurrent.ConcurrentBag<BestFitResult>();

    System.Threading.Tasks.Parallel.ForEach(candidates, c =>
    {
        resultBag.Add(Evaluate(c));
    });

    return resultBag.ToList();
}
```

Add `using System.Linq;` if not already present (it is — line 2).

**Step 3: Update `BestFitFinder` to use `IPairEvaluator`**

In `BestFitFinder.cs`:

Change the field and constructor to accept an optional evaluator:

```csharp
public class BestFitFinder
{
    private readonly IPairEvaluator _evaluator;
    private readonly BestFitFilter _filter;

    public BestFitFinder(double maxPlateWidth, double maxPlateHeight, IPairEvaluator evaluator = null)
    {
        _evaluator = evaluator ?? new PairEvaluator();
        _filter = new BestFitFilter
        {
            MaxPlateWidth = maxPlateWidth,
            MaxPlateHeight = maxPlateHeight
        };
    }
```

Replace the evaluation `Parallel.ForEach` block in `FindBestFits()` (lines 44-52) with:

```csharp
var results = _evaluator.EvaluateAll(allCandidates);
```

Remove the `ConcurrentBag<BestFitResult>` and the second `Parallel.ForEach` — those lines (44-52) are fully replaced by the single call above.

**Step 4: Build to verify**

Run: `dotnet build OpenNest.Engine/OpenNest.Engine.csproj`
Expected: Build succeeded

**Step 5: Build full solution to verify nothing broke**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded (NestEngine still creates BestFitFinder with 2 args — still valid)

**Step 6: Commit**

```bash
git add OpenNest.Engine/BestFit/IPairEvaluator.cs OpenNest.Engine/BestFit/PairEvaluator.cs OpenNest.Engine/BestFit/BestFitFinder.cs
git commit -m "refactor: extract IPairEvaluator interface from PairEvaluator"
```

---

### Task 3: Create `OpenNest.Gpu` project with `PartBitmap`

**Files:**
- Create: `OpenNest.Gpu/OpenNest.Gpu.csproj`
- Create: `OpenNest.Gpu/PartBitmap.cs`
- Modify: `OpenNest.sln` (add project)

**Step 1: Create project**

```bash
cd "C:\Users\aisaacs\Desktop\Projects\OpenNest"
dotnet new classlib -n OpenNest.Gpu --framework net8.0-windows
rm OpenNest.Gpu/Class1.cs
dotnet sln OpenNest.sln add OpenNest.Gpu/OpenNest.Gpu.csproj
```

**Step 2: Edit csproj**

Replace the generated `OpenNest.Gpu.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <RootNamespace>OpenNest.Gpu</RootNamespace>
    <AssemblyName>OpenNest.Gpu</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\OpenNest.Core\OpenNest.Core.csproj" />
    <ProjectReference Include="..\OpenNest.Engine\OpenNest.Engine.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="ILGPU" Version="1.5.1" />
    <PackageReference Include="ILGPU.Algorithms" Version="1.5.1" />
  </ItemGroup>
</Project>
```

**Step 3: Create `PartBitmap.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Gpu
{
    public class PartBitmap
    {
        public int[] Cells { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double CellSize { get; set; }
        public double OriginX { get; set; }
        public double OriginY { get; set; }

        public static PartBitmap FromDrawing(Drawing drawing, double cellSize, double spacingDilation = 0)
        {
            var polygons = GetClosedPolygons(drawing);

            if (polygons.Count == 0)
                return new PartBitmap { Cells = Array.Empty<int>(), Width = 0, Height = 0, CellSize = cellSize };

            var minX = double.MaxValue;
            var minY = double.MaxValue;
            var maxX = double.MinValue;
            var maxY = double.MinValue;

            foreach (var poly in polygons)
            {
                poly.UpdateBounds();
                var bb = poly.BoundingBox;
                if (bb.Left < minX) minX = bb.Left;
                if (bb.Bottom < minY) minY = bb.Bottom;
                if (bb.Right > maxX) maxX = bb.Right;
                if (bb.Top > maxY) maxY = bb.Top;
            }

            // Expand bounds by dilation amount
            minX -= spacingDilation;
            minY -= spacingDilation;
            maxX += spacingDilation;
            maxY += spacingDilation;

            var width = (int)System.Math.Ceiling((maxX - minX) / cellSize);
            var height = (int)System.Math.Ceiling((maxY - minY) / cellSize);

            if (width <= 0 || height <= 0)
                return new PartBitmap { Cells = Array.Empty<int>(), Width = 0, Height = 0, CellSize = cellSize };

            var cells = new int[width * height];
            var dilationCells = (int)System.Math.Ceiling(spacingDilation / cellSize);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var px = minX + (x + 0.5) * cellSize;
                    var py = minY + (y + 0.5) * cellSize;
                    var pt = new Vector(px, py);

                    foreach (var poly in polygons)
                    {
                        if (poly.ContainsPoint(pt))
                        {
                            cells[y * width + x] = 1;
                            break;
                        }
                    }
                }
            }

            // Dilate: expand filled cells outward by dilationCells
            if (dilationCells > 0)
                Dilate(cells, width, height, dilationCells);

            return new PartBitmap
            {
                Cells = cells,
                Width = width,
                Height = height,
                CellSize = cellSize,
                OriginX = minX,
                OriginY = minY
            };
        }

        private static List<Polygon> GetClosedPolygons(Drawing drawing)
        {
            var entities = ConvertProgram.ToGeometry(drawing.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid);
            var shapes = Helper.GetShapes(entities);

            var polygons = new List<Polygon>();

            foreach (var shape in shapes)
            {
                if (!shape.IsClosed())
                    continue;

                var polygon = shape.ToPolygonWithTolerance(0.05);
                polygon.Close();
                polygons.Add(polygon);
            }

            return polygons;
        }

        private static void Dilate(int[] cells, int width, int height, int radius)
        {
            var source = (int[])cells.Clone();

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (source[y * width + x] != 1)
                        continue;

                    for (var dy = -radius; dy <= radius; dy++)
                    {
                        for (var dx = -radius; dx <= radius; dx++)
                        {
                            var nx = x + dx;
                            var ny = y + dy;

                            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                                cells[ny * width + nx] = 1;
                        }
                    }
                }
            }
        }
    }
}
```

**Step 4: Build**

Run: `dotnet build OpenNest.Gpu/OpenNest.Gpu.csproj`
Expected: Build succeeded (ILGPU NuGet restored)

**Step 5: Commit**

```bash
git add OpenNest.Gpu/ OpenNest.sln
git commit -m "feat: add OpenNest.Gpu project with PartBitmap rasterizer"
```

---

### Task 4: Implement `GpuPairEvaluator` with ILGPU kernel

**Files:**
- Create: `OpenNest.Gpu/GpuPairEvaluator.cs`

**Step 1: Create the evaluator**

```csharp
using System;
using System.Collections.Generic;
using ILGPU;
using ILGPU.Runtime;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;

namespace OpenNest.Gpu
{
    public class GpuPairEvaluator : IPairEvaluator, IDisposable
    {
        private readonly Context _context;
        private readonly Accelerator _accelerator;
        private readonly Drawing _drawing;
        private readonly PartBitmap _bitmap;
        private readonly double _spacing;

        public const double DefaultCellSize = 0.05;

        public GpuPairEvaluator(Drawing drawing, double spacing, double cellSize = DefaultCellSize)
        {
            _drawing = drawing;
            _spacing = spacing;
            _context = Context.CreateDefault();
            _accelerator = _context.GetPreferredDevice(preferCPU: false)
                .CreateAccelerator(_context);

            var dilation = spacing / 2.0;
            _bitmap = PartBitmap.FromDrawing(drawing, cellSize, dilation);
        }

        public List<BestFitResult> EvaluateAll(List<PairCandidate> candidates)
        {
            if (_bitmap.Width == 0 || _bitmap.Height == 0 || candidates.Count == 0)
                return new List<BestFitResult>();

            var bitmapWidth = _bitmap.Width;
            var bitmapHeight = _bitmap.Height;
            var cellSize = (float)_bitmap.CellSize;
            var candidateCount = candidates.Count;

            // Pack candidate parameters: offsetX, offsetY, rotation, unused
            var candidateParams = new float[candidateCount * 4];

            for (var i = 0; i < candidateCount; i++)
            {
                candidateParams[i * 4 + 0] = (float)candidates[i].Part2Offset.X;
                candidateParams[i * 4 + 1] = (float)candidates[i].Part2Offset.Y;
                candidateParams[i * 4 + 2] = (float)candidates[i].Part2Rotation;
                candidateParams[i * 4 + 3] = 0f;
            }

            // Results: overlapCount, minX, minY, maxX, maxY per candidate
            var resultData = new int[candidateCount * 5];

            // Initialize min to large, max to small
            for (var i = 0; i < candidateCount; i++)
            {
                resultData[i * 5 + 0] = 0;                  // overlapCount
                resultData[i * 5 + 1] = int.MaxValue;       // minX
                resultData[i * 5 + 2] = int.MaxValue;       // minY
                resultData[i * 5 + 3] = int.MinValue;       // maxX
                resultData[i * 5 + 4] = int.MinValue;       // maxY
            }

            using var gpuBitmap = _accelerator.Allocate1D(_bitmap.Cells);
            using var gpuParams = _accelerator.Allocate1D(candidateParams);
            using var gpuResults = _accelerator.Allocate1D(resultData);

            var kernel = _accelerator.LoadAutoGroupedStreamKernel<
                Index1D,
                ArrayView<int>,
                ArrayView<float>,
                ArrayView<int>,
                int, int, float, float, float>(EvaluateKernel);

            kernel(
                candidateCount,
                gpuBitmap.View,
                gpuParams.View,
                gpuResults.View,
                bitmapWidth,
                bitmapHeight,
                cellSize,
                (float)_bitmap.OriginX,
                (float)_bitmap.OriginY);

            _accelerator.Synchronize();
            gpuResults.CopyToCPU(resultData);

            var trueArea = _drawing.Area * 2;
            var results = new List<BestFitResult>(candidateCount);

            for (var i = 0; i < candidateCount; i++)
            {
                var overlapCount = resultData[i * 5 + 0];
                var minX = resultData[i * 5 + 1];
                var minY = resultData[i * 5 + 2];
                var maxX = resultData[i * 5 + 3];
                var maxY = resultData[i * 5 + 4];

                var hasOverlap = overlapCount > 0;
                var hasBounds = minX <= maxX && minY <= maxY;

                double boundingWidth = 0, boundingHeight = 0, area = 0;

                if (hasBounds)
                {
                    boundingWidth = (maxX - minX + 1) * _bitmap.CellSize;
                    boundingHeight = (maxY - minY + 1) * _bitmap.CellSize;
                    area = boundingWidth * boundingHeight;
                }

                results.Add(new BestFitResult
                {
                    Candidate = candidates[i],
                    RotatedArea = area,
                    BoundingWidth = boundingWidth,
                    BoundingHeight = boundingHeight,
                    OptimalRotation = 0,
                    TrueArea = trueArea,
                    Keep = !hasOverlap && hasBounds,
                    Reason = hasOverlap ? "Overlap detected" : hasBounds ? "Valid" : "No bounds"
                });
            }

            return results;
        }

        private static void EvaluateKernel(
            Index1D index,
            ArrayView<int> bitmap,
            ArrayView<float> candidateParams,
            ArrayView<int> results,
            int bitmapWidth, int bitmapHeight,
            float cellSize, float originX, float originY)
        {
            var paramIdx = index * 4;
            var offsetX = candidateParams[paramIdx + 0];
            var offsetY = candidateParams[paramIdx + 1];
            var rotation = candidateParams[paramIdx + 2];

            // Convert world offset to cell offset relative to bitmap origin
            var offsetCellsX = (offsetX - originX) / cellSize;
            var offsetCellsY = (offsetY - originY) / cellSize;

            var cosR = IntrinsicMath.Cos(rotation);
            var sinR = IntrinsicMath.Sin(rotation);

            var halfW = bitmapWidth * 0.5f;
            var halfH = bitmapHeight * 0.5f;

            var overlapCount = 0;
            var minX = int.MaxValue;
            var minY = int.MaxValue;
            var maxX = int.MinValue;
            var maxY = int.MinValue;

            for (var y = 0; y < bitmapHeight; y++)
            {
                for (var x = 0; x < bitmapWidth; x++)
                {
                    var cell1 = bitmap[y * bitmapWidth + x];

                    // Transform (x,y) to part2 space: rotate around center then offset
                    var cx = x - halfW;
                    var cy = y - halfH;
                    var rx = cx * cosR - cy * sinR;
                    var ry = cx * sinR + cy * cosR;
                    var bx = (int)(rx + halfW + offsetCellsX - x);
                    var by = (int)(ry + halfH + offsetCellsY - y);

                    // Lookup part2 bitmap cell at transformed position
                    var bx2 = x + bx;
                    var by2 = y + by;
                    var cell2 = 0;

                    if (bx2 >= 0 && bx2 < bitmapWidth && by2 >= 0 && by2 < bitmapHeight)
                        cell2 = bitmap[by2 * bitmapWidth + bx2];

                    if (cell1 == 1 && cell2 == 1)
                        overlapCount++;

                    if (cell1 == 1 || cell2 == 1)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            var resultIdx = index * 5;
            results[resultIdx + 0] = overlapCount;
            results[resultIdx + 1] = minX;
            results[resultIdx + 2] = minY;
            results[resultIdx + 3] = maxX;
            results[resultIdx + 4] = maxY;
        }

        public void Dispose()
        {
            _accelerator?.Dispose();
            _context?.Dispose();
        }
    }
}
```

Note: The kernel uses `IntrinsicMath.Cos`/`Sin` which ILGPU compiles to GPU intrinsics. The `int.MaxValue`/`int.MinValue` initialization for bounds tracking is done CPU-side before upload.

**Step 2: Build**

Run: `dotnet build OpenNest.Gpu/OpenNest.Gpu.csproj`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add OpenNest.Gpu/GpuPairEvaluator.cs
git commit -m "feat: add GpuPairEvaluator with ILGPU bitmap overlap kernel"
```

---

### Task 5: Wire GPU evaluator into `NestEngine`

**Files:**
- Modify: `OpenNest.Engine/NestEngine.cs`
- Modify: `OpenNest/OpenNest.csproj` (add reference to OpenNest.Gpu)

**Step 1: Add `UseGpu` property to `NestEngine`**

At the top of the `NestEngine` class (after the existing properties around line 23), add:

```csharp
public bool UseGpu { get; set; }
```

**Step 2: Update `FillWithPairs` to use GPU evaluator when enabled**

In `NestEngine.cs`, the `FillWithPairs(NestItem item, Box workArea)` method (line 268) creates a `BestFitFinder`. Change it to optionally pass a GPU evaluator.

Add at the top of the file:

```csharp
using OpenNest.Engine.BestFit;
```

(Already present — line 6.)

Replace the `FillWithPairs(NestItem item, Box workArea)` method body. The key change is lines 270-271 where the finder is created:

```csharp
private List<Part> FillWithPairs(NestItem item, Box workArea)
{
    IPairEvaluator evaluator = null;

    if (UseGpu)
    {
        try
        {
            evaluator = new Gpu.GpuPairEvaluator(item.Drawing, Plate.PartSpacing);
        }
        catch
        {
            // GPU not available, fall back to geometry
        }
    }

    var finder = new BestFitFinder(Plate.Size.Width, Plate.Size.Height, evaluator);
    var bestFits = finder.FindBestFits(item.Drawing, Plate.PartSpacing, stepSize: 0.25);

    var keptResults = bestFits.Where(r => r.Keep).Take(50).ToList();
    Debug.WriteLine($"[FillWithPairs] Total: {bestFits.Count}, Kept: {bestFits.Count(r => r.Keep)}, Trying: {keptResults.Count}");

    var resultBag = new System.Collections.Concurrent.ConcurrentBag<(int count, List<Part> parts)>();

    System.Threading.Tasks.Parallel.For(0, keptResults.Count, i =>
    {
        var result = keptResults[i];
        var pairParts = BuildPairParts(result, item.Drawing);
        var angles = FindHullEdgeAngles(pairParts);
        var engine = new FillLinear(workArea, Plate.PartSpacing);
        var filled = FillPattern(engine, pairParts, angles);

        if (filled != null && filled.Count > 0)
            resultBag.Add((filled.Count, filled));
    });

    List<Part> best = null;

    foreach (var (count, parts) in resultBag)
    {
        if (best == null || count > best.Count)
            best = parts;
    }

    (evaluator as IDisposable)?.Dispose();

    Debug.WriteLine($"[FillWithPairs] Best pair result: {best?.Count ?? 0} parts");
    return best ?? new List<Part>();
}
```

**Step 3: Add OpenNest.Gpu reference to UI project**

In `OpenNest/OpenNest.csproj`, add to the `<ItemGroup>` with other project references:

```xml
<ProjectReference Include="..\OpenNest.Gpu\OpenNest.Gpu.csproj" />
```

**Step 4: Build full solution**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add OpenNest.Engine/NestEngine.cs OpenNest/OpenNest.csproj
git commit -m "feat: wire GpuPairEvaluator into NestEngine with UseGpu flag"
```

---

### Task 6: Add UI toggle for GPU mode

**Files:**
- Modify: `OpenNest/Forms/MainForm.cs`
- Modify: `OpenNest/Forms/MainForm.Designer.cs`

This task adds a "Use GPU" checkbox menu item under the Tools menu. The exact placement depends on the existing menu structure.

**Step 1: Check existing menu structure**

Read `MainForm.Designer.cs` to find the Tools menu items and their initialization to determine where to add the GPU toggle. Look for `mnuTools` items.

**Step 2: Add menu item field**

In `MainForm.Designer.cs`, add a field declaration near the other menu fields:

```csharp
private System.Windows.Forms.ToolStripMenuItem mnuToolsUseGpu;
```

**Step 3: Initialize menu item**

In the `InitializeComponent()` method, initialize the item and add it to the Tools menu `DropDownItems`:

```csharp
this.mnuToolsUseGpu = new System.Windows.Forms.ToolStripMenuItem();
this.mnuToolsUseGpu.Name = "mnuToolsUseGpu";
this.mnuToolsUseGpu.Text = "Use GPU for Best Fit";
this.mnuToolsUseGpu.CheckOnClick = true;
this.mnuToolsUseGpu.Click += new System.EventHandler(this.UseGpu_Click);
```

Add `this.mnuToolsUseGpu` to the Tools menu's `DropDownItems` array.

**Step 4: Add click handler in MainForm.cs**

```csharp
private void UseGpu_Click(object sender, EventArgs e)
{
    // The CheckOnClick property handles toggling automatically
}
```

**Step 5: Pass the flag when creating NestEngine**

Find where `NestEngine` is created in the codebase (likely in auto-nest or fill actions) and set `UseGpu = mnuToolsUseGpu.Checked` on the engine after creation.

This requires reading the code to find the exact creation points. Search for `new NestEngine(` in the codebase.

**Step 6: Build and verify**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded

**Step 7: Commit**

```bash
git add OpenNest/Forms/MainForm.cs OpenNest/Forms/MainForm.Designer.cs
git commit -m "feat: add Use GPU toggle in Tools menu"
```

---

### Task 7: Smoke test

**Step 1: Run the application**

Run: `dotnet run --project OpenNest/OpenNest.csproj`

**Step 2: Manual verification**

1. Open a nest file with parts
2. Verify the geometry path still works (GPU unchecked) — auto-nest a plate
3. Enable "Use GPU for Best Fit" in Tools menu
4. Auto-nest the same plate with GPU enabled
5. Compare part counts — GPU results should be close to geometry results (not exact due to bitmap approximation)
6. Check Debug output for `[FillWithPairs]` timing differences

**Step 3: Commit any fixes**

If any issues found, fix and commit with appropriate message.
