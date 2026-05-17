using OpenNest;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using Xunit;
using System.Collections.Generic;

namespace OpenNest.Tests.Fill
{
    public class CompactorTests
    {
        [Fact]
        public void DirectionalDistance_ArcVsInclinedLine_DoesNotOverPush()
        {
            // Arc (top semicircle) pushed upward toward a 45° inclined line.
            // The critical angle on the arc gives a shorter distance than any
            // sampled vertex (endpoints + cardinal extremes).
            var arc = new Arc(5, 0, 2, 0, System.Math.PI);
            var line = new Line(new Vector(3, 4), new Vector(7, 6));

            var moving = new List<Entity> { arc };
            var stationary = new List<Entity> { line };
            var direction = new Vector(0, 1); // push up

            var dist = SpatialQuery.DirectionalDistance(moving, stationary, direction);

            // Move the arc up by the computed distance, then verify no overlap.
            // The topmost reachable point on the arc at the critical angle θ ≈ 2.034
            // (between π/2 and π) should just touch the line.
            Assert.True(dist < double.MaxValue, "Should find a finite distance");
            Assert.True(dist > 0, "Should be a positive distance");

            // Verify: after moving, the closest point on the arc should be within
            // tolerance of the line, not past it.
            var theta = System.Math.Atan2(
                line.pt2.X - line.pt1.X, -(line.pt2.Y - line.pt1.Y));
            theta = OpenNest.Math.Angle.NormalizeRad(theta + System.Math.PI);
            var qx = arc.Center.X + arc.Radius * System.Math.Cos(theta);
            var qy = arc.Center.Y + arc.Radius * System.Math.Sin(theta) + dist;

            // The moved point should be on or just touching the line, not past it.
            // Line equation: (y - 4) / (x - 3) = (6 - 4) / (7 - 3) = 0.5
            // y = 0.5x + 2.5
            var lineYAtQx = 0.5 * qx + 2.5;
            Assert.True(qy <= lineYAtQx + 0.001,
                $"Arc point ({qx:F4}, {qy:F4}) should not be past line (line Y={lineYAtQx:F4} at X={qx:F4}). " +
                $"dist={dist:F6}, overshot by {qy - lineYAtQx:F6}");
        }

        [Fact]
        public void DirectionalDistance_ArcVsInclinedLine_BetterThanVertexSampling()
        {
            // Same geometry — verify the analytical Phase 3 finds a shorter
            // distance than the Phase 1/2 vertex sampling alone would.
            var arc = new Arc(5, 0, 2, 0, System.Math.PI);
            var line = new Line(new Vector(3, 4), new Vector(7, 6));

            // Phase 1/2 vertex-only distance: sample arc endpoints + cardinal extreme.
            var vertices = new[]
            {
                new Vector(7, 0),  // arc endpoint θ=0
                new Vector(3, 0),  // arc endpoint θ=π
                new Vector(5, 2),  // cardinal extreme θ=π/2
            };

            var vertexMin = double.MaxValue;
            foreach (var v in vertices)
            {
                var d = SpatialQuery.RayEdgeDistance(v.X, v.Y,
                    line.pt1.X, line.pt1.Y, line.pt2.X, line.pt2.Y, 0, 1);
                if (d < vertexMin) vertexMin = d;
            }

            // Full directional distance (includes Phase 3 arc-to-line).
            var moving = new List<Entity> { arc };
            var stationary = new List<Entity> { line };
            var fullDist = SpatialQuery.DirectionalDistance(moving, stationary, new Vector(0, 1));

            Assert.True(fullDist < vertexMin,
                $"Full distance ({fullDist:F6}) should be less than vertex-only ({vertexMin:F6})");
        }
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

        private static Part MakeRectPart(double x, double y, double w, double h)
        {
            var drawing = MakeRectDrawing(w, h);
            var part = new Part(drawing) { Location = new Vector(x, y) };
            part.UpdateBounds();
            return part;
        }

        private static Drawing MakeTriangleDrawing(params Vector[] points)
        {
            var pgm = new OpenNest.CNC.Program();
            pgm.Codes.Add(new OpenNest.CNC.RapidMove(points[0]));

            for (var i = 1; i < points.Length; i++)
                pgm.Codes.Add(new OpenNest.CNC.LinearMove(points[i]));

            pgm.Codes.Add(new OpenNest.CNC.LinearMove(points[0]));
            return new Drawing("triangle", pgm);
        }

        private static Part MakeTrianglePart(params Vector[] points)
        {
            var part = new Part(MakeTriangleDrawing(points));
            part.UpdateBounds();
            return part;
        }

        private static Part MakeTrianglePart(double x, double y, params Vector[] points)
        {
            var part = MakeTrianglePart(points);
            part.Location = new Vector(x, y);
            part.UpdateBounds();
            return part;
        }

        [Fact]
        public void Push_Left_MovesPartTowardEdge()
        {
            var workArea = new Box(0, 0, 100, 100);
            var part = MakeRectPart(50, 0, 10, 10);
            var moving = new List<Part> { part };
            var obstacles = new List<Part>();

            var distance = Compactor.Push(moving, obstacles, workArea, 0, PushDirection.Left);

            Assert.True(distance > 0);
            Assert.True(part.BoundingBox.Left < 1);
        }

        [Fact]
        public void Push_Left_StopsAtObstacle()
        {
            var workArea = new Box(0, 0, 100, 100);
            var obstacle = MakeRectPart(0, 0, 10, 10);
            var part = MakeRectPart(50, 0, 10, 10);
            var moving = new List<Part> { part };
            var obstacles = new List<Part> { obstacle };

            Compactor.Push(moving, obstacles, workArea, 0, PushDirection.Left);

            Assert.True(part.BoundingBox.Left >= obstacle.BoundingBox.Right - 0.1);
        }

        [Fact]
        public void Push_Down_MovesPartTowardEdge()
        {
            var workArea = new Box(0, 0, 100, 100);
            var part = MakeRectPart(0, 50, 10, 10);
            var moving = new List<Part> { part };
            var obstacles = new List<Part>();

            var distance = Compactor.Push(moving, obstacles, workArea, 0, PushDirection.Down);

            Assert.True(distance > 0);
            Assert.True(part.BoundingBox.Bottom < 1);
        }

        [Fact]
        public void Push_ReturnsZero_WhenAlreadyAtEdge()
        {
            var workArea = new Box(0, 0, 100, 100);
            var part = MakeRectPart(0, 0, 10, 10);
            var moving = new List<Part> { part };
            var obstacles = new List<Part>();

            var distance = Compactor.Push(moving, obstacles, workArea, 0, PushDirection.Left);

            Assert.Equal(0, distance);
        }

        [Fact]
        public void Push_WithSpacing_MovesLessThanWithout()
        {
            var workArea = new Box(0, 0, 100, 100);

            // Push without spacing.
            var obstacle1 = MakeRectPart(0, 0, 10, 10);
            var part1 = MakeRectPart(50, 0, 10, 10);
            var distNoSpacing = Compactor.Push(new List<Part> { part1 }, new List<Part> { obstacle1 }, workArea, 0, PushDirection.Left);

            // Push with spacing.
            var obstacle2 = MakeRectPart(0, 0, 10, 10);
            var part2 = MakeRectPart(50, 0, 10, 10);
            var distWithSpacing = Compactor.Push(new List<Part> { part2 }, new List<Part> { obstacle2 }, workArea, 2, PushDirection.Left);

            // Spacing should cause the part to stop at a different position than without spacing.
            Assert.NotEqual(distNoSpacing, distWithSpacing);
        }

        [Fact]
        public void Push_Up_AllowsSharedDiagonalEdgeToSeparate()
        {
            var workArea = new Box(0, 0, 20, 20);
            var obstacle = MakeTrianglePart(
                new Vector(0, 0),
                new Vector(10, 0),
                new Vector(0, 10));
            var movingPart = MakeTrianglePart(
                new Vector(0, 10),
                new Vector(10, 0),
                new Vector(10, 10));

            var distance = Compactor.Push(
                new List<Part> { movingPart },
                new List<Part> { obstacle },
                workArea,
                0,
                PushDirection.Up);

            Assert.True(distance > 0);
            Assert.True(movingPart.BoundingBox.Top > 19.9);
            Assert.False(movingPart.Intersects(obstacle, out _));
        }

        [Fact]
        public void Push_Up_MovesAfterRightTriangleIsPushedLeftIntoSharedEdge()
        {
            var workArea = new Box(0, 0, 24, 24);
            var leftTriangle = MakeTrianglePart(
                2, 2,
                new Vector(0, 0),
                new Vector(8, 0),
                new Vector(4, 10));
            var rightTriangle = MakeTrianglePart(
                14, 4,
                new Vector(0, 10),
                new Vector(8, 10),
                new Vector(4, 0));

            var moving = new List<Part> { rightTriangle };
            var obstacles = new List<Part> { leftTriangle };

            var leftDistance = Compactor.Push(moving, obstacles, workArea, 0, PushDirection.Left);
            var yBeforePushUp = rightTriangle.Location.Y;
            var bottomBeforePushUp = rightTriangle.BoundingBox.Bottom;

            var upDistance = Compactor.Push(moving, obstacles, workArea, 0, PushDirection.Up);

            Assert.True(leftDistance > 0);
            Assert.True(upDistance > 0);
            Assert.True(rightTriangle.Location.Y > yBeforePushUp);
            Assert.True(rightTriangle.BoundingBox.Bottom > bottomBeforePushUp);
            Assert.False(rightTriangle.Intersects(leftTriangle, out _));
        }

        [Fact]
        public void Push_Left_BlocksWhenSharedDiagonalEdgeWouldOverlap()
        {
            var workArea = new Box(0, 0, 20, 20);
            var obstacle = MakeTrianglePart(
                new Vector(0, 0),
                new Vector(10, 0),
                new Vector(0, 10));
            var movingPart = MakeTrianglePart(
                new Vector(0, 10),
                new Vector(10, 0),
                new Vector(10, 10));

            var distance = Compactor.Push(
                new List<Part> { movingPart },
                new List<Part> { obstacle },
                workArea,
                0,
                PushDirection.Left);

            Assert.Equal(0, distance);
            Assert.Equal(0, movingPart.BoundingBox.Left);
        }

        [Fact]
        public void Push_AngleLeft_MovesPartTowardEdge()
        {
            var workArea = new Box(0, 0, 100, 100);
            var part = MakeRectPart(50, 0, 10, 10);
            var moving = new List<Part> { part };
            var obstacles = new List<Part>();

            // direction = left
            var direction = new Vector(System.Math.Cos(System.Math.PI), System.Math.Sin(System.Math.PI));
            var distance = Compactor.Push(moving, obstacles, workArea, 0, direction);

            Assert.True(distance > 0);
            Assert.True(part.BoundingBox.Left < 1);
        }

        [Fact]
        public void Push_AngleDown_MovesPartTowardEdge()
        {
            var workArea = new Box(0, 0, 100, 100);
            var part = MakeRectPart(0, 50, 10, 10);
            var moving = new List<Part> { part };
            var obstacles = new List<Part>();

            // direction = down
            var angle = 3 * System.Math.PI / 2;
            var direction = new Vector(System.Math.Cos(angle), System.Math.Sin(angle));
            var distance = Compactor.Push(moving, obstacles, workArea, 0, direction);

            Assert.True(distance > 0);
            Assert.True(part.BoundingBox.Bottom < 1);
        }

        [Fact]
        public void PushBoundingBox_Left_MovesPartTowardEdge()
        {
            var workArea = new Box(0, 0, 100, 100);
            var part = MakeRectPart(50, 0, 10, 10);
            var moving = new List<Part> { part };
            var obstacles = new List<Part>();

            var distance = Compactor.PushBoundingBox(moving, obstacles, workArea, 0, PushDirection.Left);

            Assert.True(distance > 0);
            Assert.True(part.BoundingBox.Left < 1);
        }

        [Fact]
        public void PushBoundingBox_StopsAtObstacle()
        {
            var workArea = new Box(0, 0, 100, 100);
            var obstacle = MakeRectPart(0, 0, 10, 10);
            var part = MakeRectPart(50, 0, 10, 10);
            var moving = new List<Part> { part };
            var obstacles = new List<Part> { obstacle };

            Compactor.PushBoundingBox(moving, obstacles, workArea, 0, PushDirection.Left);

            Assert.True(part.BoundingBox.Left >= obstacle.BoundingBox.Right - 0.1);
        }
    }
}
