using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Converters
{
    public enum ContourClassification
    {
        Perimeter,
        Hole,
        Etch,
        Open
    }

    public sealed class ContourInfo
    {
        public Shape Shape { get; }
        public ContourClassification Type { get; private set; }
        public string Label { get; private set; }

        private ContourInfo(Shape shape, ContourClassification type, string label)
        {
            Shape = shape;
            Type = type;
            Label = label;
        }

        public string DirectionLabel
        {
            get
            {
                if (Type == ContourClassification.Open || Type == ContourClassification.Etch)
                    return "Open";
                var poly = Shape.ToPolygon();
                if (poly == null || poly.Vertices.Count < 3)
                    return "?";
                return poly.RotationDirection() == RotationType.CW ? "CW" : "CCW";
            }
        }

        public string DimensionLabel
        {
            get
            {
                if (Shape.Entities.Count == 1 && Shape.Entities[0] is Circle c)
                    return $"Circle R{c.Radius:0.#}";
                Shape.UpdateBounds();
                var box = Shape.BoundingBox;
                return $"{box.Width:0.#} x {box.Length:0.#}";
            }
        }

        public void Reverse()
        {
            Shape.Reverse();
        }

        public void SetLabel(string label)
        {
            Label = label;
        }

        public static List<ContourInfo> Classify(List<Shape> shapes)
        {
            if (shapes.Count == 0)
                return new List<ContourInfo>();

            // Ensure bounding boxes are up to date before comparing
            foreach (var s in shapes)
                s.UpdateBounds();

            // Find perimeter — largest bounding box area
            var perimeterIndex = 0;
            var maxArea = shapes[0].BoundingBox.Area();
            for (var i = 1; i < shapes.Count; i++)
            {
                var area = shapes[i].BoundingBox.Area();
                if (area > maxArea)
                {
                    maxArea = area;
                    perimeterIndex = i;
                }
            }

            var result = new List<ContourInfo>();
            var holeCount = 0;
            var etchCount = 0;
            var openCount = 0;

            // Non-perimeter shapes first (matches CNC cut order: holes before perimeter)
            for (var i = 0; i < shapes.Count; i++)
            {
                if (i == perimeterIndex) continue;
                var shape = shapes[i];
                var type = ClassifyShape(shape);

                string label;
                switch (type)
                {
                    case ContourClassification.Hole:
                        holeCount++;
                        label = $"Hole {holeCount}";
                        break;
                    case ContourClassification.Etch:
                        etchCount++;
                        label = etchCount == 1 ? "Etch" : $"Etch {etchCount}";
                        break;
                    default:
                        openCount++;
                        label = openCount == 1 ? "Open" : $"Open {openCount}";
                        break;
                }

                result.Add(new ContourInfo(shape, type, label));
            }

            // Perimeter last
            result.Add(new ContourInfo(shapes[perimeterIndex], ContourClassification.Perimeter, "Perimeter"));

            return result;
        }

        private static ContourClassification ClassifyShape(Shape shape)
        {
            // Check etch layer — all entities must be on ETCH layer
            if (shape.Entities.Count > 0 &&
                shape.Entities.All(e => string.Equals(e.Layer?.Name, "ETCH", StringComparison.OrdinalIgnoreCase)))
                return ContourClassification.Etch;

            if (shape.IsClosed())
                return ContourClassification.Hole;

            return ContourClassification.Open;
        }
    }
}
