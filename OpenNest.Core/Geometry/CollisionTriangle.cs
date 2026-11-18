namespace OpenNest.Geometry
{
    /// <summary>
    /// One ear of <see cref="ConvexDecomposition.Triangulate(Polygon)"/> as the collision clip loop
    /// reads its triangle polygon: the open vertices and the bounding-box sides, without building
    /// the polygon. Every value is bit for bit what the loop reads from the polygon.
    /// </summary>
    internal readonly struct CollisionTriangle
    {
        public CollisionTriangle(Vector prev, Vector curr, Vector next)
        {
            // The polygon is [prev, curr, next], closed with prev by Polygon.Close only when prev
            // and next differ by its fuzzy equality. Its open vertices drop a last vertex exactly
            // equal to the first: the closing vertex, or next when it was not closed.
            V0 = prev;
            V1 = curr;
            V2 = next;
            Count = prev != next || !(prev.X == next.X && prev.Y == next.Y) ? 3 : 2;

            // Polygon.UpdateBounds over the same vertices; the closing vertex repeats prev.
            var minX = prev.X;
            var maxX = prev.X;
            var minY = prev.Y;
            var maxY = prev.Y;
            Include(curr, ref minX, ref maxX, ref minY, ref maxY);
            Include(next, ref minX, ref maxX, ref minY, ref maxY);

            // Box sides as Box computes them from its location and size.
            Left = minX;
            Bottom = minY;
            Right = minX + (maxX - minX);
            Top = minY + (maxY - minY);
        }

        public Vector V0 { get; }
        public Vector V1 { get; }
        public Vector V2 { get; }

        /// <summary>Open vertices: 3, or 2 when next is exactly prev.</summary>
        public int Count { get; }

        public double Left { get; }
        public double Right { get; }
        public double Bottom { get; }
        public double Top { get; }

        public Vector this[int index] => index switch
        {
            0 => V0,
            1 => V1,
            _ => V2,
        };

        private static void Include(Vector vertex, ref double minX, ref double maxX, ref double minY, ref double maxY)
        {
            if (vertex.X < minX)
                minX = vertex.X;
            else if (vertex.X > maxX)
                maxX = vertex.X;

            if (vertex.Y < minY)
                minY = vertex.Y;
            else if (vertex.Y > maxY)
                maxY = vertex.Y;
        }
    }
}
