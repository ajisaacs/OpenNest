# OpenNest xUnit Test Suite Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Convert the ad-hoc OpenNest.Test console harness into a proper xUnit test suite with test data hosted in a separate git repo.

**Architecture:** Replace the console app with an xUnit test project. A `TestData` helper resolves the test data path from env var `OPENNEST_TEST_DATA` or fallback `../OpenNest.Test.Data/`. Tests skip with a message if data is missing. Test classes: `FillTests` (full plate fills), `RemnantFillTests` (filling remnant areas). All tests assert part count >= target and zero overlaps.

**Tech Stack:** C# / .NET 8, xUnit, OpenNest.Core + Engine + IO

---

### Task 1: Set up test data repo and push fixture files

**Step 1: Clone the empty repo next to OpenNest**

```bash
cd C:/Users/AJ/Desktop/Projects
git clone https://git.thecozycat.net/aj/OpenNest.Test.git OpenNest.Test.Data
```

**Step 2: Copy fixture files into the repo**

```bash
cp ~/Desktop/"N0308-017.zip" OpenNest.Test.Data/
cp ~/Desktop/"N0308-008.zip" OpenNest.Test.Data/
cp ~/Desktop/"30pcs Fill.zip" OpenNest.Test.Data/
```

**Step 3: Commit and push**

```bash
cd OpenNest.Test.Data
git add .
git commit -m "feat: add initial test fixture nest files"
git push
```

---

### Task 2: Convert OpenNest.Test from console app to xUnit project

**Files:**
- Modify: `OpenNest.Test/OpenNest.Test.csproj`
- Delete: `OpenNest.Test/Program.cs`
- Create: `OpenNest.Test/TestData.cs`

**Step 1: Replace the csproj with xUnit configuration**

Overwrite `OpenNest.Test/OpenNest.Test.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\OpenNest.Core\OpenNest.Core.csproj" />
    <ProjectReference Include="..\OpenNest.Engine\OpenNest.Engine.csproj" />
    <ProjectReference Include="..\OpenNest.IO\OpenNest.IO.csproj" />
  </ItemGroup>

</Project>
```

**Step 2: Delete Program.cs**

```bash
rm OpenNest.Test/Program.cs
```

**Step 3: Create TestData helper**

Create `OpenNest.Test/TestData.cs`:

```csharp
using OpenNest.IO;

namespace OpenNest.Test;

public static class TestData
{
    private static readonly string? BasePath = ResolveBasePath();

    public static bool IsAvailable => BasePath != null;

    public static string SkipReason =>
        "Test data not found. Set OPENNEST_TEST_DATA env var or clone " +
        "https://git.thecozycat.net/aj/OpenNest.Test.git to ../OpenNest.Test.Data/";

    public static string GetPath(string filename)
    {
        if (BasePath == null)
            throw new InvalidOperationException(SkipReason);

        var path = Path.Combine(BasePath, filename);

        if (!File.Exists(path))
            throw new FileNotFoundException($"Test fixture not found: {path}");

        return path;
    }

    public static Nest LoadNest(string filename)
    {
        var reader = new NestReader(GetPath(filename));
        return reader.Read();
    }

    public static Plate CleanPlateFrom(Plate reference)
    {
        var plate = new Plate();
        plate.Size = reference.Size;
        plate.PartSpacing = reference.PartSpacing;
        plate.EdgeSpacing = reference.EdgeSpacing;
        plate.Quadrant = reference.Quadrant;
        return plate;
    }

    private static string? ResolveBasePath()
    {
        // 1. Environment variable
        var envPath = Environment.GetEnvironmentVariable("OPENNEST_TEST_DATA");

        if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
            return envPath;

        // 2. Sibling directory (../OpenNest.Test.Data/ relative to solution root)
        var dir = AppContext.BaseDirectory;

        // Walk up from bin/Debug/net8.0-windows to find the solution root.
        for (var i = 0; i < 6; i++)
        {
            var parent = Directory.GetParent(dir);

            if (parent == null)
                break;

            dir = parent.FullName;
            var candidate = Path.Combine(dir, "OpenNest.Test.Data");

            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
```

**Step 4: Build**

Run: `dotnet build OpenNest.Test/OpenNest.Test.csproj`
Expected: Build succeeds.

**Step 5: Commit**

```bash
git add OpenNest.Test/
git commit -m "refactor: convert OpenNest.Test to xUnit project with TestData helper"
```

---

### Task 3: Add FillTests

**Files:**
- Create: `OpenNest.Test/FillTests.cs`

**Step 1: Create FillTests.cs**

```csharp
using OpenNest.Geometry;
using Xunit;
using Xunit.Abstractions;

namespace OpenNest.Test;

public class FillTests
{
    private readonly ITestOutputHelper _output;

    public FillTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Fill")]
    public void N0308_008_HingePlate_FillsAtLeast75()
    {
        Skip.IfNot(TestData.IsAvailable, TestData.SkipReason);

        var nest = TestData.LoadNest("N0308-008.zip");
        var hinge = nest.Drawings.First(d => d.Name.Contains("HINGE PLATE #2"));
        var plate = TestData.CleanPlateFrom(nest.Plates[0]);

        var engine = new NestEngine(plate);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        engine.Fill(new NestItem { Drawing = hinge, Quantity = 0 });
        sw.Stop();

        _output.WriteLine($"Parts: {plate.Parts.Count} | Time: {sw.ElapsedMilliseconds}ms");

        Assert.True(plate.Parts.Count >= 75,
            $"Expected >= 75 parts, got {plate.Parts.Count}");
        AssertNoOverlaps(plate.Parts.ToList());
    }

    [Fact]
    [Trait("Category", "Fill")]
    public void RemainderStripRefill_30pcs_FillsAtLeast32()
    {
        Skip.IfNot(TestData.IsAvailable, TestData.SkipReason);

        var nest = TestData.LoadNest("30pcs Fill.zip");
        var drawing = nest.Drawings.First();
        var plate = TestData.CleanPlateFrom(nest.Plates[0]);

        var engine = new NestEngine(plate);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        engine.Fill(new NestItem { Drawing = drawing, Quantity = 0 });
        sw.Stop();

        _output.WriteLine($"Parts: {plate.Parts.Count} | Time: {sw.ElapsedMilliseconds}ms");

        Assert.True(plate.Parts.Count >= 32,
            $"Expected >= 32 parts, got {plate.Parts.Count}");
        AssertNoOverlaps(plate.Parts.ToList());
    }

    private void AssertNoOverlaps(List<Part> parts)
    {
        for (var i = 0; i < parts.Count; i++)
        {
            for (var j = i + 1; j < parts.Count; j++)
            {
                if (parts[i].Intersects(parts[j], out _))
                    Assert.Fail($"Overlap detected: part [{i}] vs [{j}]");
            }
        }
    }
}
```

**Step 2: Run tests**

Run: `dotnet test OpenNest.Test/ -v normal`
Expected: 2 tests pass (or skip if data missing).

**Step 3: Commit**

```bash
git add OpenNest.Test/FillTests.cs
git commit -m "test: add FillTests for full plate fill and remainder strip refill"
```

---

### Task 4: Add RemnantFillTests

**Files:**
- Create: `OpenNest.Test/RemnantFillTests.cs`

**Step 1: Create RemnantFillTests.cs**

```csharp
using OpenNest.Geometry;
using Xunit;
using Xunit.Abstractions;

namespace OpenNest.Test;

public class RemnantFillTests
{
    private readonly ITestOutputHelper _output;

    public RemnantFillTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "Remnant")]
    public void N0308_017_PT02_RemnantFillsAtLeast10()
    {
        Skip.IfNot(TestData.IsAvailable, TestData.SkipReason);

        var nest = TestData.LoadNest("N0308-017.zip");
        var plate = nest.Plates[0];
        var pt02 = nest.Drawings.First(d => d.Name.Contains("PT02"));
        var remnant = plate.GetRemnants()[0];

        _output.WriteLine($"Remnant: ({remnant.X:F2},{remnant.Y:F2}) {remnant.Width:F2}x{remnant.Height:F2}");

        var countBefore = plate.Parts.Count;
        var engine = new NestEngine(plate);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        engine.Fill(new NestItem { Drawing = pt02, Quantity = 0 }, remnant);
        sw.Stop();

        var added = plate.Parts.Count - countBefore;
        _output.WriteLine($"Added: {added} parts | Time: {sw.ElapsedMilliseconds}ms");

        Assert.True(added >= 10, $"Expected >= 10 parts in remnant, got {added}");

        var newParts = plate.Parts.Skip(countBefore).ToList();
        AssertNoOverlaps(newParts);
        AssertNoCrossOverlaps(plate.Parts.Take(countBefore).ToList(), newParts);
    }

    [Fact]
    [Trait("Category", "Remnant")]
    public void N0308_008_HingePlate_RemnantFillsAtLeast8()
    {
        Skip.IfNot(TestData.IsAvailable, TestData.SkipReason);

        var nest = TestData.LoadNest("N0308-008.zip");
        var plate = nest.Plates[0];
        var hinge = nest.Drawings.First(d => d.Name.Contains("HINGE PLATE #2"));
        var remnants = plate.GetRemnants();

        _output.WriteLine($"Remnant 0: ({remnants[0].X:F2},{remnants[0].Y:F2}) {remnants[0].Width:F2}x{remnants[0].Height:F2}");

        var countBefore = plate.Parts.Count;
        var engine = new NestEngine(plate);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        engine.Fill(new NestItem { Drawing = hinge, Quantity = 0 }, remnants[0]);
        sw.Stop();

        var added = plate.Parts.Count - countBefore;
        _output.WriteLine($"Added: {added} parts | Time: {sw.ElapsedMilliseconds}ms");

        Assert.True(added >= 8, $"Expected >= 8 parts in remnant, got {added}");

        var newParts = plate.Parts.Skip(countBefore).ToList();
        AssertNoOverlaps(newParts);
        AssertNoCrossOverlaps(plate.Parts.Take(countBefore).ToList(), newParts);
    }

    private void AssertNoOverlaps(List<Part> parts)
    {
        for (var i = 0; i < parts.Count; i++)
        {
            for (var j = i + 1; j < parts.Count; j++)
            {
                if (parts[i].Intersects(parts[j], out _))
                    Assert.Fail($"Overlap detected: part [{i}] vs [{j}]");
            }
        }
    }

    private void AssertNoCrossOverlaps(List<Part> existing, List<Part> added)
    {
        for (var i = 0; i < existing.Count; i++)
        {
            for (var j = 0; j < added.Count; j++)
            {
                if (existing[i].Intersects(added[j], out _))
                    Assert.Fail($"Cross-overlap: existing [{i}] vs added [{j}]");
            }
        }
    }
}
```

**Step 2: Run all tests**

Run: `dotnet test OpenNest.Test/ -v normal`
Expected: 4 tests pass.

**Step 3: Commit**

```bash
git add OpenNest.Test/RemnantFillTests.cs
git commit -m "test: add RemnantFillTests for remnant area filling"
```

---

### Task 5: Add test project to solution and final verification

**Step 1: Add to solution**

```bash
cd C:/Users/AJ/Desktop/Projects/OpenNest
dotnet sln OpenNest.sln add OpenNest.Test/OpenNest.Test.csproj
```

**Step 2: Build entire solution**

Run: `dotnet build OpenNest.sln`
Expected: All projects build, 0 errors.

**Step 3: Run all tests**

Run: `dotnet test OpenNest.sln -v normal`
Expected: 4 tests pass, 0 failures.

**Step 4: Commit**

```bash
git add OpenNest.sln
git commit -m "chore: add OpenNest.Test to solution"
```
