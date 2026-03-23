using System.Collections.Generic;

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
    }
}
