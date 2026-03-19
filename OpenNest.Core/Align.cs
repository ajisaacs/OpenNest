using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest
{
    public static class Align
    {
        public static void Vertically(Entity fixedEntity, Entity movableEntity)
        {
            movableEntity.Offset(fixedEntity.BoundingBox.Center.X - movableEntity.BoundingBox.Center.X, 0);
        }

        public static void Vertically(Entity fixedEntity, List<Entity> entities)
        {
            entities.ForEach(entity => Vertically(fixedEntity, entity));
        }

        public static void Vertically(Part fixedPart, Part movablePart)
        {
            movablePart.Offset(fixedPart.BoundingBox.Center.X - movablePart.BoundingBox.Center.X, 0);
        }

        public static void Vertically(Part fixedPart, List<Part> parts)
        {
            parts.ForEach(part => Vertically(fixedPart, part));
        }

        public static void Horizontally(Entity fixedEntity, Entity movableEntity)
        {
            movableEntity.Offset(0, fixedEntity.BoundingBox.Center.Y - movableEntity.BoundingBox.Center.Y);
        }

        public static void Horizontally(Entity fixedEntity, List<Entity> entities)
        {
            entities.ForEach(entity => Horizontally(fixedEntity, entity));
        }

        public static void Horizontally(Part fixedPart, Part movablePart)
        {
            movablePart.Offset(0, fixedPart.BoundingBox.Center.Y - movablePart.BoundingBox.Center.Y);
        }

        public static void Horizontally(Part fixedPart, List<Part> parts)
        {
            parts.ForEach(part => Horizontally(fixedPart, part));
        }

        public static void Left(Entity fixedEntity, Entity movableEntity)
        {
            movableEntity.Offset(fixedEntity.BoundingBox.Left - movableEntity.BoundingBox.Left, 0);
        }

        public static void Left(Entity fixedEntity, List<Entity> entities)
        {
            entities.ForEach(entity => Left(fixedEntity, entity));
        }

        public static void Left(Part fixedPart, Part movablePart)
        {
            movablePart.Offset(fixedPart.BoundingBox.Left - movablePart.BoundingBox.Left, 0);
        }

        public static void Left(Part fixedPart, List<Part> parts)
        {
            parts.ForEach(part => Left(fixedPart, part));
        }

        public static void Right(Entity fixedEntity, Entity movableEntity)
        {
            movableEntity.Offset(fixedEntity.BoundingBox.Right - movableEntity.BoundingBox.Right, 0);
        }

        public static void Right(Entity fixedEntity, List<Entity> entities)
        {
            entities.ForEach(entity => Right(fixedEntity, entity));
        }

        public static void Right(Part fixedPart, Part movablePart)
        {
            movablePart.Offset(fixedPart.BoundingBox.Right - movablePart.BoundingBox.Right, 0);
        }

        public static void Right(Part fixedPart, List<Part> parts)
        {
            parts.ForEach(part => Right(fixedPart, part));
        }

        public static void Top(Entity fixedEntity, Entity movableEntity)
        {
            movableEntity.Offset(0, fixedEntity.BoundingBox.Top - movableEntity.BoundingBox.Top);
        }

        public static void Top(Entity fixedEntity, List<Entity> entities)
        {
            entities.ForEach(entity => Top(fixedEntity, entity));
        }

        public static void Top(Part fixedPart, Part movablePart)
        {
            movablePart.Offset(0, fixedPart.BoundingBox.Top - movablePart.BoundingBox.Top);
        }

        public static void Top(Part fixedPart, List<Part> parts)
        {
            parts.ForEach(part => Top(fixedPart, part));
        }

        public static void Bottom(Entity fixedEntity, Entity movableEntity)
        {
            movableEntity.Offset(0, fixedEntity.BoundingBox.Bottom - movableEntity.BoundingBox.Bottom);
        }

        public static void Bottom(Entity fixedEntity, List<Entity> entities)
        {
            entities.ForEach(entity => Bottom(fixedEntity, entity));
        }

        public static void Bottom(Part fixedPart, Part movablePart)
        {
            movablePart.Offset(0, fixedPart.BoundingBox.Bottom - movablePart.BoundingBox.Bottom);
        }

        public static void Bottom(Part fixedPart, List<Part> parts)
        {
            parts.ForEach(part => Bottom(fixedPart, part));
        }

        public static void EvenlyDistributeHorizontally(List<Part> parts)
        {
            if (parts.Count < 3)
                return;

            var list = new List<Part>(parts);
            list.Sort((p1, p2) => p1.BoundingBox.Center.X.CompareTo(p2.BoundingBox.Center.X));

            var lastIndex = list.Count - 1;

            var first = list[0];
            var last = list[lastIndex];

            var start = first.BoundingBox.Center.X;
            var end = last.BoundingBox.Center.X;
            var diff = end - start;

            var spacing = diff / lastIndex;

            for (int i = 1; i < lastIndex; ++i)
            {
                var part = list[i];
                var newX = start + i * spacing;
                var curX = part.BoundingBox.Center.X;

                part.Offset(newX - curX, 0);
            }
        }

        public static void EvenlyDistributeVertically(List<Part> parts)
        {
            if (parts.Count < 3)
                return;

            var list = new List<Part>(parts);
            list.Sort((p1, p2) => p1.BoundingBox.Center.Y.CompareTo(p2.BoundingBox.Center.Y));

            var lastIndex = list.Count - 1;

            var first = list[0];
            var last = list[lastIndex];

            var start = first.BoundingBox.Center.Y;
            var end = last.BoundingBox.Center.Y;
            var diff = end - start;

            var spacing = diff / lastIndex;

            for (int i = 1; i < lastIndex; ++i)
            {
                var part = list[i];
                var newX = start + i * spacing;
                var curX = part.BoundingBox.Center.Y;

                part.Offset(0, newX - curX);
            }
        }
    }
}
