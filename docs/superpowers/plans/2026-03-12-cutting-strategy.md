# Cutting Strategy Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add lead-in, lead-out, and tab classes to OpenNest.Core that generate ICode instructions for CNC cutting approach/exit geometry.

**Architecture:** New `CuttingStrategy/` folder under `OpenNest.Core/CNC/` containing abstract base classes and concrete implementations for lead-ins, lead-outs, and tabs. A `ContourCuttingStrategy` orchestrator uses `ShapeProfile` + `ClosestPointTo` to sequence and apply cutting parameters. Original Drawing/Program geometry is never modified.

**Tech Stack:** .NET 8, C#, OpenNest.Core (ICode, Program, Vector, Shape, ShapeProfile, Angle)

**Spec:** `docs/superpowers/specs/2026-03-12-cutting-strategy-design.md`

---

## Chunk 1: LeadIn Hierarchy

### Task 1: LeadIn abstract base class

**Files:**
- Create: `OpenNest.Core/CNC/CuttingStrategy/LeadIns/LeadIn.cs`

- [ ] **Step 1: Create the abstract base class**

```csharp
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    public abstract class LeadIn
    {
        public abstract List<ICode> Generate(Vector contourStartPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW);

        public abstract Vector GetPiercePoint(Vector contourStartPoint, double contourNormalAngle);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build OpenNest.Core`
Expected: success

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Core/CNC/CuttingStrategy/LeadIns/LeadIn.cs
git commit -m "feat: add LeadIn abstract base class"
```

---

### Task 2: NoLeadIn

**Files:**
- Create: `OpenNest.Core/CNC/CuttingStrategy/LeadIns/NoLeadIn.cs`

- [ ] **Step 1: Create NoLeadIn**

```csharp
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    public class NoLeadIn : LeadIn
    {
        public override List<ICode> Generate(Vector contourStartPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            return new List<ICode>
            {
                new RapidMove(contourStartPoint)
            };
        }

        public override Vector GetPiercePoint(Vector contourStartPoint, double contourNormalAngle)
        {
            return contourStartPoint;
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build OpenNest.Core`
Expected: success

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Core/CNC/CuttingStrategy/LeadIns/NoLeadIn.cs
git commit -m "feat: add NoLeadIn (Type 0)"
```

---

### Task 3: LineLeadIn

**Files:**
- Create: `OpenNest.Core/CNC/CuttingStrategy/LeadIns/LineLeadIn.cs`

- [ ] **Step 1: Create LineLeadIn**

Pierce point is offset from contour start along `contourNormalAngle + Angle.ToRadians(ApproachAngle)` by `Length`.

```csharp
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.CNC.CuttingStrategy
{
    public class LineLeadIn : LeadIn
    {
        public double Length { get; set; }
        public double ApproachAngle { get; set; } = 90.0;

        public override List<ICode> Generate(Vector contourStartPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            var piercePoint = GetPiercePoint(contourStartPoint, contourNormalAngle);

            return new List<ICode>
            {
                new RapidMove(piercePoint),
                new LinearMove(contourStartPoint)
            };
        }

        public override Vector GetPiercePoint(Vector contourStartPoint, double contourNormalAngle)
        {
            var approachAngle = contourNormalAngle + Angle.ToRadians(ApproachAngle);
            return new Vector(
                contourStartPoint.X + Length * System.Math.Cos(approachAngle),
                contourStartPoint.Y + Length * System.Math.Sin(approachAngle));
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build OpenNest.Core`
Expected: success

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Core/CNC/CuttingStrategy/LeadIns/LineLeadIn.cs
git commit -m "feat: add LineLeadIn (Type 1)"
```

---

### Task 4: LineArcLeadIn

**Files:**
- Create: `OpenNest.Core/CNC/CuttingStrategy/LeadIns/LineArcLeadIn.cs`

- [ ] **Step 1: Create LineArcLeadIn**

Geometry: Pierce → [Line] → Arc start → [Arc] → Contour start. Arc center at `contourStartPoint + ArcRadius` along normal. Arc rotation uses `winding` parameter.

```csharp
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.CNC.CuttingStrategy
{
    public class LineArcLeadIn : LeadIn
    {
        public double LineLength { get; set; }
        public double ApproachAngle { get; set; } = 135.0;
        public double ArcRadius { get; set; }

        public override List<ICode> Generate(Vector contourStartPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            var piercePoint = GetPiercePoint(contourStartPoint, contourNormalAngle);

            var arcCenterX = contourStartPoint.X + ArcRadius * System.Math.Cos(contourNormalAngle);
            var arcCenterY = contourStartPoint.Y + ArcRadius * System.Math.Sin(contourNormalAngle);
            var arcCenter = new Vector(arcCenterX, arcCenterY);

            var lineAngle = contourNormalAngle + Angle.ToRadians(ApproachAngle);
            var arcStart = new Vector(
                arcCenterX + ArcRadius * System.Math.Cos(lineAngle),
                arcCenterY + ArcRadius * System.Math.Sin(lineAngle));

            return new List<ICode>
            {
                new RapidMove(piercePoint),
                new LinearMove(arcStart),
                new ArcMove(contourStartPoint, arcCenter, winding)
            };
        }

        public override Vector GetPiercePoint(Vector contourStartPoint, double contourNormalAngle)
        {
            var arcCenterX = contourStartPoint.X + ArcRadius * System.Math.Cos(contourNormalAngle);
            var arcCenterY = contourStartPoint.Y + ArcRadius * System.Math.Sin(contourNormalAngle);

            var lineAngle = contourNormalAngle + Angle.ToRadians(ApproachAngle);
            var arcStartX = arcCenterX + ArcRadius * System.Math.Cos(lineAngle);
            var arcStartY = arcCenterY + ArcRadius * System.Math.Sin(lineAngle);

            return new Vector(
                arcStartX + LineLength * System.Math.Cos(lineAngle),
                arcStartY + LineLength * System.Math.Sin(lineAngle));
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build OpenNest.Core`
Expected: success

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Core/CNC/CuttingStrategy/LeadIns/LineArcLeadIn.cs
git commit -m "feat: add LineArcLeadIn (Type 2)"
```

---

### Task 5: ArcLeadIn

**Files:**
- Create: `OpenNest.Core/CNC/CuttingStrategy/LeadIns/ArcLeadIn.cs`

- [ ] **Step 1: Create ArcLeadIn**

Pierce point is diametrically opposite contour start on the arc circle.

```csharp
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    public class ArcLeadIn : LeadIn
    {
        public double Radius { get; set; }

        public override List<ICode> Generate(Vector contourStartPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            var piercePoint = GetPiercePoint(contourStartPoint, contourNormalAngle);

            var arcCenter = new Vector(
                contourStartPoint.X + Radius * System.Math.Cos(contourNormalAngle),
                contourStartPoint.Y + Radius * System.Math.Sin(contourNormalAngle));

            return new List<ICode>
            {
                new RapidMove(piercePoint),
                new ArcMove(contourStartPoint, arcCenter, winding)
            };
        }

        public override Vector GetPiercePoint(Vector contourStartPoint, double contourNormalAngle)
        {
            var arcCenterX = contourStartPoint.X + Radius * System.Math.Cos(contourNormalAngle);
            var arcCenterY = contourStartPoint.Y + Radius * System.Math.Sin(contourNormalAngle);

            return new Vector(
                arcCenterX + Radius * System.Math.Cos(contourNormalAngle),
                arcCenterY + Radius * System.Math.Sin(contourNormalAngle));
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build OpenNest.Core`
Expected: success

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Core/CNC/CuttingStrategy/LeadIns/ArcLeadIn.cs
git commit -m "feat: add ArcLeadIn (Type 3)"
```

---

### Task 6: LineLineLeadIn

**Files:**
- Create: `OpenNest.Core/CNC/CuttingStrategy/LeadIns/LineLineLeadIn.cs`

- [ ] **Step 1: Create LineLineLeadIn**

Two-segment approach: pierce → midpoint → contour start.

```csharp
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.CNC.CuttingStrategy
{
    public class LineLineLeadIn : LeadIn
    {
        public double Length1 { get; set; }
        public double ApproachAngle1 { get; set; } = 90.0;
        public double Length2 { get; set; }
        public double ApproachAngle2 { get; set; } = 90.0;

        public override List<ICode> Generate(Vector contourStartPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            var piercePoint = GetPiercePoint(contourStartPoint, contourNormalAngle);

            var secondAngle = contourNormalAngle + Angle.ToRadians(ApproachAngle1);
            var midPoint = new Vector(
                contourStartPoint.X + Length2 * System.Math.Cos(secondAngle),
                contourStartPoint.Y + Length2 * System.Math.Sin(secondAngle));

            return new List<ICode>
            {
                new RapidMove(piercePoint),
                new LinearMove(midPoint),
                new LinearMove(contourStartPoint)
            };
        }

        public override Vector GetPiercePoint(Vector contourStartPoint, double contourNormalAngle)
        {
            var secondAngle = contourNormalAngle + Angle.ToRadians(ApproachAngle1);
            var midX = contourStartPoint.X + Length2 * System.Math.Cos(secondAngle);
            var midY = contourStartPoint.Y + Length2 * System.Math.Sin(secondAngle);

            var firstAngle = secondAngle + Angle.ToRadians(ApproachAngle2);
            return new Vector(
                midX + Length1 * System.Math.Cos(firstAngle),
                midY + Length1 * System.Math.Sin(firstAngle));
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build OpenNest.Core`
Expected: success

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Core/CNC/CuttingStrategy/LeadIns/LineLineLeadIn.cs
git commit -m "feat: add LineLineLeadIn (Type 5)"
```

---

### Task 7: CleanHoleLeadIn

**Files:**
- Create: `OpenNest.Core/CNC/CuttingStrategy/LeadIns/CleanHoleLeadIn.cs`

- [ ] **Step 1: Create CleanHoleLeadIn**

Same geometry as LineArcLeadIn but with hard-coded 135° angle. Kerf property stored for the paired lead-out to use.

```csharp
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.CNC.CuttingStrategy
{
    public class CleanHoleLeadIn : LeadIn
    {
        public double LineLength { get; set; }
        public double ArcRadius { get; set; }
        public double Kerf { get; set; }

        public override List<ICode> Generate(Vector contourStartPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            var piercePoint = GetPiercePoint(contourStartPoint, contourNormalAngle);

            var arcCenterX = contourStartPoint.X + ArcRadius * System.Math.Cos(contourNormalAngle);
            var arcCenterY = contourStartPoint.Y + ArcRadius * System.Math.Sin(contourNormalAngle);
            var arcCenter = new Vector(arcCenterX, arcCenterY);

            var lineAngle = contourNormalAngle + Angle.ToRadians(135.0);
            var arcStart = new Vector(
                arcCenterX + ArcRadius * System.Math.Cos(lineAngle),
                arcCenterY + ArcRadius * System.Math.Sin(lineAngle));

            return new List<ICode>
            {
                new RapidMove(piercePoint),
                new LinearMove(arcStart),
                new ArcMove(contourStartPoint, arcCenter, winding)
            };
        }

        public override Vector GetPiercePoint(Vector contourStartPoint, double contourNormalAngle)
        {
            var arcCenterX = contourStartPoint.X + ArcRadius * System.Math.Cos(contourNormalAngle);
            var arcCenterY = contourStartPoint.Y + ArcRadius * System.Math.Sin(contourNormalAngle);

            var lineAngle = contourNormalAngle + Angle.ToRadians(135.0);
            var arcStartX = arcCenterX + ArcRadius * System.Math.Cos(lineAngle);
            var arcStartY = arcCenterY + ArcRadius * System.Math.Sin(lineAngle);

            return new Vector(
                arcStartX + LineLength * System.Math.Cos(lineAngle),
                arcStartY + LineLength * System.Math.Sin(lineAngle));
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build OpenNest.Core`
Expected: success

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Core/CNC/CuttingStrategy/LeadIns/CleanHoleLeadIn.cs
git commit -m "feat: add CleanHoleLeadIn"
```

---

## Chunk 2: LeadOut Hierarchy

### Task 8: LeadOut abstract base class

**Files:**
- Create: `OpenNest.Core/CNC/CuttingStrategy/LeadOuts/LeadOut.cs`

- [ ] **Step 1: Create the abstract base class**

```csharp
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    public abstract class LeadOut
    {
        public abstract List<ICode> Generate(Vector contourEndPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build OpenNest.Core`
Expected: success

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Core/CNC/CuttingStrategy/LeadOuts/LeadOut.cs
git commit -m "feat: add LeadOut abstract base class"
```

---

### Task 9: NoLeadOut

**Files:**
- Create: `OpenNest.Core/CNC/CuttingStrategy/LeadOuts/NoLeadOut.cs`

- [ ] **Step 1: Create NoLeadOut**

```csharp
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    public class NoLeadOut : LeadOut
    {
        public override List<ICode> Generate(Vector contourEndPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            return new List<ICode>();
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build OpenNest.Core`
Expected: success

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Core/CNC/CuttingStrategy/LeadOuts/NoLeadOut.cs
git commit -m "feat: add NoLeadOut (Type 0)"
```

---

### Task 10: LineLeadOut

**Files:**
- Create: `OpenNest.Core/CNC/CuttingStrategy/LeadOuts/LineLeadOut.cs`

- [ ] **Step 1: Create LineLeadOut**

```csharp
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.CNC.CuttingStrategy
{
    public class LineLeadOut : LeadOut
    {
        public double Length { get; set; }
        public double ApproachAngle { get; set; } = 90.0;

        public override List<ICode> Generate(Vector contourEndPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            var overcutAngle = contourNormalAngle + Angle.ToRadians(ApproachAngle);
            var endPoint = new Vector(
                contourEndPoint.X + Length * System.Math.Cos(overcutAngle),
                contourEndPoint.Y + Length * System.Math.Sin(overcutAngle));

            return new List<ICode>
            {
                new LinearMove(endPoint)
            };
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build OpenNest.Core`
Expected: success

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Core/CNC/CuttingStrategy/LeadOuts/LineLeadOut.cs
git commit -m "feat: add LineLeadOut (Type 1)"
```

---

### Task 11: ArcLeadOut

**Files:**
- Create: `OpenNest.Core/CNC/CuttingStrategy/LeadOuts/ArcLeadOut.cs`

- [ ] **Step 1: Create ArcLeadOut**

Arc curves away from the part. End point is a quarter turn from contour end.

```csharp
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    public class ArcLeadOut : LeadOut
    {
        public double Radius { get; set; }

        public override List<ICode> Generate(Vector contourEndPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            var arcCenterX = contourEndPoint.X + Radius * System.Math.Cos(contourNormalAngle);
            var arcCenterY = contourEndPoint.Y + Radius * System.Math.Sin(contourNormalAngle);
            var arcCenter = new Vector(arcCenterX, arcCenterY);

            var endPoint = new Vector(
                arcCenterX + Radius * System.Math.Cos(contourNormalAngle + System.Math.PI / 2),
                arcCenterY + Radius * System.Math.Sin(contourNormalAngle + System.Math.PI / 2));

            return new List<ICode>
            {
                new ArcMove(endPoint, arcCenter, winding)
            };
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build OpenNest.Core`
Expected: success

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Core/CNC/CuttingStrategy/LeadOuts/ArcLeadOut.cs
git commit -m "feat: add ArcLeadOut (Type 3)"
```

---

### Task 12: MicrotabLeadOut

**Files:**
- Create: `OpenNest.Core/CNC/CuttingStrategy/LeadOuts/MicrotabLeadOut.cs`

- [ ] **Step 1: Create MicrotabLeadOut**

Returns empty list — the `ContourCuttingStrategy` handles trimming the last cutting move.

```csharp
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    public class MicrotabLeadOut : LeadOut
    {
        public double GapSize { get; set; } = 0.03;

        public override List<ICode> Generate(Vector contourEndPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            return new List<ICode>();
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build OpenNest.Core`
Expected: success

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Core/CNC/CuttingStrategy/LeadOuts/MicrotabLeadOut.cs
git commit -m "feat: add MicrotabLeadOut (Type 4)"
```

---

## Chunk 3: Tab Hierarchy

### Task 13: Tab abstract base class

**Files:**
- Create: `OpenNest.Core/CNC/CuttingStrategy/Tabs/Tab.cs`

- [ ] **Step 1: Create Tab base class**

```csharp
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    public abstract class Tab
    {
        public double Size { get; set; } = 0.03;
        public LeadIn TabLeadIn { get; set; }
        public LeadOut TabLeadOut { get; set; }

        public abstract List<ICode> Generate(
            Vector tabStartPoint, Vector tabEndPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build OpenNest.Core`
Expected: success

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Core/CNC/CuttingStrategy/Tabs/Tab.cs
git commit -m "feat: add Tab abstract base class"
```

---

### Task 14: NormalTab

**Files:**
- Create: `OpenNest.Core/CNC/CuttingStrategy/Tabs/NormalTab.cs`

- [ ] **Step 1: Create NormalTab**

```csharp
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    public class NormalTab : Tab
    {
        public double CutoutMinWidth { get; set; }
        public double CutoutMinHeight { get; set; }
        public double CutoutMaxWidth { get; set; }
        public double CutoutMaxHeight { get; set; }

        public override List<ICode> Generate(
            Vector tabStartPoint, Vector tabEndPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            var codes = new List<ICode>();

            if (TabLeadOut != null)
                codes.AddRange(TabLeadOut.Generate(tabStartPoint, contourNormalAngle, winding));

            codes.Add(new RapidMove(tabEndPoint));

            if (TabLeadIn != null)
                codes.AddRange(TabLeadIn.Generate(tabEndPoint, contourNormalAngle, winding));

            return codes;
        }

        public bool AppliesToCutout(double cutoutWidth, double cutoutHeight)
        {
            return cutoutWidth >= CutoutMinWidth && cutoutWidth <= CutoutMaxWidth
                && cutoutHeight >= CutoutMinHeight && cutoutHeight <= CutoutMaxHeight;
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build OpenNest.Core`
Expected: success

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Core/CNC/CuttingStrategy/Tabs/NormalTab.cs
git commit -m "feat: add NormalTab"
```

---

### Task 15: BreakerTab

**Files:**
- Create: `OpenNest.Core/CNC/CuttingStrategy/Tabs/BreakerTab.cs`

- [ ] **Step 1: Create BreakerTab**

```csharp
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    public class BreakerTab : Tab
    {
        public double BreakerDepth { get; set; }
        public double BreakerLeadInLength { get; set; }
        public double BreakerAngle { get; set; }

        public override List<ICode> Generate(
            Vector tabStartPoint, Vector tabEndPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            var codes = new List<ICode>();

            if (TabLeadOut != null)
                codes.AddRange(TabLeadOut.Generate(tabStartPoint, contourNormalAngle, winding));

            var scoreAngle = contourNormalAngle + System.Math.PI;
            var scoreEnd = new Vector(
                tabStartPoint.X + BreakerDepth * System.Math.Cos(scoreAngle),
                tabStartPoint.Y + BreakerDepth * System.Math.Sin(scoreAngle));
            codes.Add(new LinearMove(scoreEnd));
            codes.Add(new RapidMove(tabEndPoint));

            if (TabLeadIn != null)
                codes.AddRange(TabLeadIn.Generate(tabEndPoint, contourNormalAngle, winding));

            return codes;
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build OpenNest.Core`
Expected: success

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Core/CNC/CuttingStrategy/Tabs/BreakerTab.cs
git commit -m "feat: add BreakerTab"
```

---

### Task 16: MachineTab

**Files:**
- Create: `OpenNest.Core/CNC/CuttingStrategy/Tabs/MachineTab.cs`

- [ ] **Step 1: Create MachineTab**

```csharp
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    public class MachineTab : Tab
    {
        public int MachineTabId { get; set; }

        public override List<ICode> Generate(
            Vector tabStartPoint, Vector tabEndPoint, double contourNormalAngle,
            RotationType winding = RotationType.CW)
        {
            return new List<ICode>
            {
                new RapidMove(tabEndPoint)
            };
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build OpenNest.Core`
Expected: success

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Core/CNC/CuttingStrategy/Tabs/MachineTab.cs
git commit -m "feat: add MachineTab"
```

---

## Chunk 4: Configuration Classes

### Task 17: ContourType enum, SequenceParameters, AssignmentParameters

**Files:**
- Create: `OpenNest.Core/CNC/CuttingStrategy/ContourType.cs`
- Create: `OpenNest.Core/CNC/CuttingStrategy/SequenceParameters.cs`
- Create: `OpenNest.Core/CNC/CuttingStrategy/AssignmentParameters.cs`

- [ ] **Step 1: Create ContourType.cs**

```csharp
namespace OpenNest.CNC.CuttingStrategy
{
    public enum ContourType
    {
        External,
        Internal,
        ArcCircle
    }
}
```

- [ ] **Step 2: Create SequenceParameters.cs**

```csharp
namespace OpenNest.CNC.CuttingStrategy
{
    // Values match PEP Technology's numbering scheme (value 6 intentionally skipped)
    public enum SequenceMethod
    {
        RightSide = 1,
        LeastCode = 2,
        Advanced = 3,
        BottomSide = 4,
        EdgeStart = 5,
        LeftSide = 7,
        RightSideAlt = 8
    }

    public class SequenceParameters
    {
        public SequenceMethod Method { get; set; } = SequenceMethod.Advanced;
        public double SmallCutoutWidth { get; set; } = 1.5;
        public double SmallCutoutHeight { get; set; } = 1.5;
        public double MediumCutoutWidth { get; set; } = 8.0;
        public double MediumCutoutHeight { get; set; } = 8.0;
        public double DistanceMediumSmall { get; set; }
        public bool AlternateRowsColumns { get; set; } = true;
        public bool AlternateCutoutsWithinRowColumn { get; set; } = true;
        public double MinDistanceBetweenRowsColumns { get; set; } = 0.25;
    }
}
```

- [ ] **Step 3: Create AssignmentParameters.cs**

```csharp
namespace OpenNest.CNC.CuttingStrategy
{
    public class AssignmentParameters
    {
        public SequenceMethod Method { get; set; } = SequenceMethod.Advanced;
        public string Preference { get; set; } = "ILAT";
        public double MinGeometryLength { get; set; } = 0.01;
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build OpenNest.Core`
Expected: success

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Core/CNC/CuttingStrategy/ContourType.cs \
  OpenNest.Core/CNC/CuttingStrategy/SequenceParameters.cs \
  OpenNest.Core/CNC/CuttingStrategy/AssignmentParameters.cs
git commit -m "feat: add ContourType, SequenceParameters, AssignmentParameters"
```

---

### Task 18: CuttingParameters

**Files:**
- Create: `OpenNest.Core/CNC/CuttingStrategy/CuttingParameters.cs`

- [ ] **Step 1: Create CuttingParameters**

Note: `InternalLeadIn` default uses `ApproachAngle` (not `Angle`) per the naming convention.

```csharp
namespace OpenNest.CNC.CuttingStrategy
{
    public class CuttingParameters
    {
        public int Id { get; set; }

        public string MachineName { get; set; }
        public string MaterialName { get; set; }
        public string Grade { get; set; }
        public double Thickness { get; set; }

        public double Kerf { get; set; }
        public double PartSpacing { get; set; }

        public LeadIn ExternalLeadIn { get; set; } = new NoLeadIn();
        public LeadOut ExternalLeadOut { get; set; } = new NoLeadOut();

        public LeadIn InternalLeadIn { get; set; } = new LineLeadIn { Length = 0.125, ApproachAngle = 90 };
        public LeadOut InternalLeadOut { get; set; } = new NoLeadOut();

        public LeadIn ArcCircleLeadIn { get; set; } = new NoLeadIn();
        public LeadOut ArcCircleLeadOut { get; set; } = new NoLeadOut();

        public Tab TabConfig { get; set; }
        public bool TabsEnabled { get; set; }

        public SequenceParameters Sequencing { get; set; } = new SequenceParameters();
        public AssignmentParameters Assignment { get; set; } = new AssignmentParameters();
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build OpenNest.Core`
Expected: success

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Core/CNC/CuttingStrategy/CuttingParameters.cs
git commit -m "feat: add CuttingParameters"
```

---

## Chunk 5: ContourCuttingStrategy Orchestrator

### Task 19: ContourCuttingStrategy

**Files:**
- Create: `OpenNest.Core/CNC/CuttingStrategy/ContourCuttingStrategy.cs`

**Reference files:**
- `OpenNest.Core/Plate.cs` — `Quadrant` (int 1-4), `Size` (Size with `.Width`, `.Length`)
- `OpenNest.Core/CNC/Program.cs` — `ToGeometry()` returns `List<Entity>`, `Codes` field
- `OpenNest.Core/Geometry/ShapeProfile.cs` — constructor takes `List<Entity>`, has `.Perimeter` (Shape) and `.Cutouts` (List&lt;Shape&gt;)
- `OpenNest.Core/Geometry/Shape.cs` — `ClosestPointTo(Vector pt, out Entity entity)`, `Entities` list

- [ ] **Step 1: Create ContourCuttingStrategy with exit point computation and contour type detection**

```csharp
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    public class ContourCuttingStrategy
    {
        public CuttingParameters Parameters { get; set; }

        public Program Apply(Program partProgram, Plate plate)
        {
            var exitPoint = GetExitPoint(plate);
            var entities = partProgram.ToGeometry();
            var profile = new ShapeProfile(entities);

            // Find closest point on perimeter from exit point
            var perimeterPoint = profile.Perimeter.ClosestPointTo(exitPoint, out var perimeterEntity);

            // Chain cutouts by nearest-neighbor from perimeter point, then reverse
            // so farthest cutouts are cut first, nearest-to-perimeter cut last
            var orderedCutouts = SequenceCutouts(profile.Cutouts, perimeterPoint);
            orderedCutouts.Reverse();

            // Build output program: cutouts first (farthest to nearest), perimeter last
            var result = new Program();
            var currentPoint = exitPoint;

            foreach (var cutout in orderedCutouts)
            {
                var contourType = DetectContourType(cutout);
                var closestPt = cutout.ClosestPointTo(currentPoint, out var entity);
                var normal = ComputeNormal(closestPt, entity, contourType);
                var winding = DetermineWinding(cutout);

                var leadIn = SelectLeadIn(contourType);
                var leadOut = SelectLeadOut(contourType);

                result.Codes.AddRange(leadIn.Generate(closestPt, normal, winding));
                // Contour re-indexing: split shape entities at closestPt so cutting
                // starts there, convert to ICode, and add to result.Codes
                throw new System.NotImplementedException("Contour re-indexing not yet implemented");
                result.Codes.AddRange(leadOut.Generate(closestPt, normal, winding));

                currentPoint = closestPt;
            }

            // Perimeter last
            {
                var perimeterPt = profile.Perimeter.ClosestPointTo(currentPoint, out perimeterEntity);
                var normal = ComputeNormal(perimeterPt, perimeterEntity, ContourType.External);
                var winding = DetermineWinding(profile.Perimeter);

                var leadIn = SelectLeadIn(ContourType.External);
                var leadOut = SelectLeadOut(ContourType.External);

                result.Codes.AddRange(leadIn.Generate(perimeterPt, normal, winding));
                throw new System.NotImplementedException("Contour re-indexing not yet implemented");
                result.Codes.AddRange(leadOut.Generate(perimeterPt, normal, winding));
            }

            return result;
        }

        private Vector GetExitPoint(Plate plate)
        {
            var w = plate.Size.Width;
            var l = plate.Size.Length;

            return plate.Quadrant switch
            {
                1 => new Vector(0, 0),        // Q1 TopRight origin → exit BottomLeft
                2 => new Vector(w, 0),         // Q2 TopLeft origin → exit BottomRight
                3 => new Vector(w, l),         // Q3 BottomLeft origin → exit TopRight
                4 => new Vector(0, l),         // Q4 BottomRight origin → exit TopLeft
                _ => new Vector(0, 0)
            };
        }

        private List<Shape> SequenceCutouts(List<Shape> cutouts, Vector startPoint)
        {
            var remaining = new List<Shape>(cutouts);
            var ordered = new List<Shape>();
            var currentPoint = startPoint;

            while (remaining.Count > 0)
            {
                var nearest = remaining[0];
                var nearestPt = nearest.ClosestPointTo(currentPoint);
                var nearestDist = nearestPt.DistanceTo(currentPoint);

                for (var i = 1; i < remaining.Count; i++)
                {
                    var pt = remaining[i].ClosestPointTo(currentPoint);
                    var dist = pt.DistanceTo(currentPoint);
                    if (dist < nearestDist)
                    {
                        nearest = remaining[i];
                        nearestPt = pt;
                        nearestDist = dist;
                    }
                }

                ordered.Add(nearest);
                remaining.Remove(nearest);
                currentPoint = nearestPt;
            }

            return ordered;
        }

        private ContourType DetectContourType(Shape cutout)
        {
            if (cutout.Entities.Count == 1 && cutout.Entities[0] is Circle)
                return ContourType.ArcCircle;

            return ContourType.Internal;
        }

        private double ComputeNormal(Vector point, Entity entity, ContourType contourType)
        {
            double normal;

            if (entity is Line line)
            {
                // Perpendicular to line direction
                var tangent = line.EndPoint.AngleFrom(line.StartPoint);
                normal = tangent + Math.Angle.HalfPI;
            }
            else if (entity is Arc arc)
            {
                // Radial direction from center to point
                normal = point.AngleFrom(arc.Center);
            }
            else if (entity is Circle circle)
            {
                normal = point.AngleFrom(circle.Center);
            }
            else
            {
                normal = 0;
            }

            // For internal contours, flip the normal (point into scrap)
            if (contourType == ContourType.Internal || contourType == ContourType.ArcCircle)
                normal += System.Math.PI;

            return Math.Angle.NormalizeRad(normal);
        }

        private RotationType DetermineWinding(Shape shape)
        {
            // Use signed area: positive = CCW, negative = CW
            var area = shape.Area();
            return area >= 0 ? RotationType.CCW : RotationType.CW;
        }

        private LeadIn SelectLeadIn(ContourType contourType)
        {
            return contourType switch
            {
                ContourType.ArcCircle => Parameters.ArcCircleLeadIn ?? Parameters.InternalLeadIn,
                ContourType.Internal => Parameters.InternalLeadIn,
                _ => Parameters.ExternalLeadIn
            };
        }

        private LeadOut SelectLeadOut(ContourType contourType)
        {
            return contourType switch
            {
                ContourType.ArcCircle => Parameters.ArcCircleLeadOut ?? Parameters.InternalLeadOut,
                ContourType.Internal => Parameters.InternalLeadOut,
                _ => Parameters.ExternalLeadOut
            };
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build OpenNest.Core`
Expected: success. If `Shape.Area()` signed-area convention is wrong for winding detection, adjust `DetermineWinding` — check `Shape.Area()` implementation to confirm sign convention.

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Core/CNC/CuttingStrategy/ContourCuttingStrategy.cs
git commit -m "feat: add ContourCuttingStrategy orchestrator

Exit point from plate quadrant, nearest-neighbor cutout
sequencing via ShapeProfile + ClosestPointTo, contour type
detection, and normal angle computation."
```

---

### Task 20: Full solution build verification

- [ ] **Step 1: Build entire solution**

Run: `dotnet build OpenNest.sln`
Expected: success with no errors. Warnings are acceptable.

- [ ] **Step 2: Verify file structure**

Run: `find OpenNest.Core/CNC/CuttingStrategy -name '*.cs' | sort`
Expected output should match the spec's file structure (21 files total).

- [ ] **Step 3: Final commit if any fixups needed**

```bash
git add -A OpenNest.Core/CNC/CuttingStrategy/
git commit -m "chore: fixup cutting strategy build issues"
```
