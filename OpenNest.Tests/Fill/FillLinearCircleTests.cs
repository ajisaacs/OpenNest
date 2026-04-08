using OpenNest;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using OpenNest.Math;
using Xunit;
using Xunit.Abstractions;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Tests.Fill
{
    public class FillLinearCircleTests
    {
        private readonly ITestOutputHelper _output;

        public FillLinearCircleTests(ITestOutputHelper output) => _output = output;

        private static Drawing MakeCircleDrawing(double radius)
        {
            var pgm = new Program();
            var startPt = new Vector(radius * 2, radius); // rightmost point
            pgm.Codes.Add(new RapidMove(startPt));
            pgm.Codes.Add(new ArcMove(startPt, new Vector(radius, radius), RotationType.CCW));
            return new Drawing("circle", pgm);
        }

        private static Drawing MakeRingDrawing(double outerRadius, double innerRadius)
        {
            var pgm = new Program();
            // Outer circle (CCW)
            var outerStart = new Vector(outerRadius * 2, outerRadius);
            pgm.Codes.Add(new RapidMove(outerStart));
            pgm.Codes.Add(new ArcMove(outerStart, new Vector(outerRadius, outerRadius), RotationType.CCW));
            // Inner circle (CW = hole)
            var innerStart = new Vector(outerRadius + innerRadius, outerRadius);
            pgm.Codes.Add(new RapidMove(innerStart));
            pgm.Codes.Add(new ArcMove(innerStart, new Vector(outerRadius, outerRadius), RotationType.CW));
            return new Drawing("ring", pgm);
        }

        [Theory]
        [InlineData(2.0, 0.125)]   // 4" diameter circle, 1/8" spacing
        [InlineData(1.0, 0.125)]   // 2" diameter circle
        [InlineData(3.0, 0.0625)]  // 6" diameter circle, 1/16" spacing
        [InlineData(0.5, 0.25)]    // 1" diameter circle, 1/4" spacing
        public void CircleFill_OffsetBoundaries_DoNotOverlap(double radius, double spacing)
        {
            var drawing = MakeCircleDrawing(radius);
            var workArea = new Box(0, 0, 48, 48);
            var engine = new FillLinear(workArea, spacing);
            var parts = engine.Fill(drawing, 0, NestDirection.Horizontal);

            _output.WriteLine($"Circle R={radius}, spacing={spacing}: {parts.Count} parts");

            AssertNoOffsetOverlap(parts, spacing, radius * 2);
        }

        [Theory]
        [InlineData(2.0, 1.5, 0.125)]  // Ring: outer R=2, inner R=1.5
        [InlineData(1.5, 1.0, 0.125)]  // Ring: outer R=1.5, inner R=1.0
        public void RingFill_OffsetBoundaries_DoNotOverlap(double outerR, double innerR, double spacing)
        {
            var drawing = MakeRingDrawing(outerR, innerR);
            var workArea = new Box(0, 0, 48, 48);
            var engine = new FillLinear(workArea, spacing);
            var parts = engine.Fill(drawing, 0, NestDirection.Horizontal);

            _output.WriteLine($"Ring outerR={outerR}, innerR={innerR}, spacing={spacing}: {parts.Count} parts");

            AssertNoOffsetOverlap(parts, spacing, outerR * 2);
        }

        private void AssertNoOffsetOverlap(List<Part> parts, double spacing, double expectedDiameter)
        {
            if (parts.Count < 2)
            {
                _output.WriteLine("  Only 1 part placed, skipping overlap check");
                return;
            }

            var halfSpacing = spacing / 2;
            var radius = expectedDiameter / 2;
            var minGap = double.MaxValue;
            var violationCount = 0;

            // For circular parts, the center is at Location + (radius, radius).
            for (var i = 0; i < parts.Count; i++)
            {
                var ci = parts[i].Location + new Vector(radius, radius);

                for (var j = i + 1; j < parts.Count; j++)
                {
                    var cj = parts[j].Location + new Vector(radius, radius);
                    var centerDist = ci.DistanceTo(cj);

                    // Gap between raw circle perimeters
                    var rawGap = centerDist - expectedDiameter;

                    // Gap between offset circle perimeters (halfSpacing each side)
                    var offsetGap = centerDist - expectedDiameter - spacing;

                    if (rawGap < minGap)
                        minGap = rawGap;

                    if (rawGap < spacing - Tolerance.Epsilon)
                    {
                        violationCount++;
                        if (violationCount <= 5)
                        {
                            _output.WriteLine($"  SPACING VIOLATION parts[{i}] vs parts[{j}]: " +
                                $"centerDist={centerDist:F6}, rawGap={rawGap:F6}, offsetGap={offsetGap:F6}, " +
                                $"expected>={spacing:F4}");
                        }
                    }
                }
            }

            _output.WriteLine($"  Min gap={minGap:F6}, expected>={spacing:F4}, violations={violationCount}");

            if (violationCount > 0)
            {
                var maxDeficit = spacing - minGap;
                _output.WriteLine($"  Max deficit={maxDeficit:F6}");
                Assert.Fail($"{violationCount} pairs violate spacing: min gap={minGap:F6}, expected>={spacing}, deficit={maxDeficit:F6}");
            }
        }
    }
}
