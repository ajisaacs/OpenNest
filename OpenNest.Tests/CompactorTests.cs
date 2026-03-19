using OpenNest;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using Xunit;
using System.Collections.Generic;

namespace OpenNest.Tests
{
    public class CompactorTests
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

        private static Part MakeRectPart(double x, double y, double w, double h)
        {
            var drawing = MakeRectDrawing(w, h);
            var part = new Part(drawing) { Location = new Vector(x, y) };
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
        public void Push_AngleLeft_MovesPartTowardEdge()
        {
            var workArea = new Box(0, 0, 100, 100);
            var part = MakeRectPart(50, 0, 10, 10);
            var moving = new List<Part> { part };
            var obstacles = new List<Part>();

            // angle = π = push left
            var distance = Compactor.Push(moving, obstacles, workArea, 0, System.Math.PI);

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

            // angle = 3π/2 = push down
            var distance = Compactor.Push(moving, obstacles, workArea, 0, 3 * System.Math.PI / 2);

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
