using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Geometry
{
    public class ShapeProfile
    {
        public ShapeProfile(Shape shape)
        {
            Update(shape.Entities);
        }

        public ShapeProfile(List<Entity> entities)
        {
            Update(entities);
        }

        private void Update(List<Entity> entities)
        {
            var shapes = ShapeBuilder.GetShapes(entities);

            Perimeter = shapes[0];
            Cutouts = new List<Shape>();

            for (var i = 1; i < shapes.Count; i++)
            {
                var bb = shapes[i].BoundingBox;
                var perimBB = Perimeter.BoundingBox;

                if (bb.Width * bb.Length > perimBB.Width * perimBB.Length)
                {
                    Cutouts.Add(Perimeter);
                    Perimeter = shapes[i];
                }
                else
                {
                    Cutouts.Add(shapes[i]);
                }
            }
        }

        public Shape Perimeter { get; set; }

        public List<Shape> Cutouts { get; set; }

        /// <summary>
        /// Ensures CNC-standard winding: perimeter CW (kerf left = outward),
        /// cutouts CCW (kerf left = inward). Reverses contours in-place as needed.
        /// </summary>
        public void NormalizeWinding()
        {
            EnsureWinding(Perimeter, RotationType.CW);

            foreach (var cutout in Cutouts)
                EnsureWinding(cutout, RotationType.CCW);
        }

        /// <summary>
        /// Returns the entities in normalized winding order (perimeter first, then cutouts).
        /// </summary>
        public List<Entity> ToNormalizedEntities()
        {
            NormalizeWinding();
            var result = new List<Entity>(Perimeter.Entities);

            foreach (var cutout in Cutouts)
                result.AddRange(cutout.Entities);

            return result;
        }

        /// <summary>
        /// Convenience method: builds a ShapeProfile from raw entities,
        /// normalizes winding, and returns the corrected entity list.
        /// </summary>
        public static List<Entity> NormalizeEntities(IEnumerable<Entity> entities)
        {
            var cloned = entities.CloneAll();
            var profile = new ShapeProfile(cloned);
            return profile.ToNormalizedEntities();
        }

        private static void EnsureWinding(Shape shape, RotationType desired)
        {
            var poly = shape.ToPolygon();

            if (poly != null && poly.Vertices.Count >= 3
                && poly.RotationDirection() != desired)
            {
                shape.Reverse();
            }
        }
    }
}
