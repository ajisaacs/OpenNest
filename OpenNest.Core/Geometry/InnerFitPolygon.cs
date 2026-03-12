using Clipper2Lib;

namespace OpenNest.Geometry
{
    /// <summary>
    /// Computes the Inner-Fit Polygon (IFP) — the feasible region where a part's
    /// reference point can be placed so the part stays entirely within the plate boundary.
    /// For a rectangular plate, the IFP is the plate shrunk by the part's bounding dimensions.
    /// </summary>
    public static class InnerFitPolygon
    {
        /// <summary>
        /// Computes the IFP for placing a part polygon inside a rectangular work area.
        /// The result is a polygon representing all valid reference point positions.
        /// </summary>
        public static Polygon Compute(Box workArea, Polygon partPolygon)
        {
            // Get the part's bounding box relative to its reference point (origin).
            var verts = partPolygon.Vertices;

            if (verts.Count < 3)
                return new Polygon();

            var minX = verts[0].X;
            var maxX = verts[0].X;
            var minY = verts[0].Y;
            var maxY = verts[0].Y;

            for (var i = 1; i < verts.Count; i++)
            {
                if (verts[i].X < minX) minX = verts[i].X;
                if (verts[i].X > maxX) maxX = verts[i].X;
                if (verts[i].Y < minY) minY = verts[i].Y;
                if (verts[i].Y > maxY) maxY = verts[i].Y;
            }

            // The IFP is the work area shrunk inward by the part's extent in each direction.
            // The reference point can range from (workArea.Left - minX) to (workArea.Right - maxX)
            // and (workArea.Bottom - minY) to (workArea.Top - maxY).
            var ifpLeft = workArea.X - minX;
            var ifpRight = workArea.Right - maxX;
            var ifpBottom = workArea.Y - minY;
            var ifpTop = workArea.Top - maxY;

            // If the part doesn't fit, return an empty polygon.
            if (ifpRight < ifpLeft || ifpTop < ifpBottom)
                return new Polygon();

            var result = new Polygon();
            result.Vertices.Add(new Vector(ifpLeft, ifpBottom));
            result.Vertices.Add(new Vector(ifpRight, ifpBottom));
            result.Vertices.Add(new Vector(ifpRight, ifpTop));
            result.Vertices.Add(new Vector(ifpLeft, ifpTop));
            result.Close();

            return result;
        }

        /// <summary>
        /// Computes the feasible region for placing a part given already-placed parts.
        /// FeasibleRegion = IFP(plate, part) - union(NFP(placed_i, part))
        /// Returns the polygon representing valid placement positions, or an empty
        /// polygon if no valid position exists.
        /// </summary>
        public static Polygon ComputeFeasibleRegion(Polygon ifp, Polygon[] nfps)
        {
            if (ifp.Vertices.Count < 3)
                return new Polygon();

            if (nfps == null || nfps.Length == 0)
                return ifp;

            var ifpPath = NoFitPolygon.ToClipperPath(ifp);
            var ifpPaths = new PathsD { ifpPath };

            // Union all NFPs.
            var nfpPaths = new PathsD();

            foreach (var nfp in nfps)
            {
                if (nfp.Vertices.Count >= 3)
                {
                    var path = NoFitPolygon.ToClipperPath(nfp);
                    nfpPaths.Add(path);
                }
            }

            if (nfpPaths.Count == 0)
                return ifp;

            var nfpUnion = Clipper.Union(nfpPaths, FillRule.NonZero);

            // Subtract the NFP union from the IFP.
            var feasible = Clipper.Difference(ifpPaths, nfpUnion, FillRule.NonZero);

            if (feasible.Count == 0)
                return new Polygon();

            // Find the polygon with the bottom-left-most point.
            // This ensures we pick the correct region for placement.
            PathD bestPath = null;
            var bestY = double.MaxValue;
            var bestX = double.MaxValue;

            foreach (var path in feasible)
            {
                foreach (var pt in path)
                {
                    if (pt.y < bestY || (pt.y == bestY && pt.x < bestX))
                    {
                        bestY = pt.y;
                        bestX = pt.x;
                        bestPath = path;
                    }
                }
            }

            return bestPath != null ? NoFitPolygon.FromClipperPath(bestPath) : new Polygon();
        }

        /// <summary>
        /// Finds the bottom-left-most point on a polygon boundary.
        /// "Bottom-left" means: minimize Y first, then minimize X.
        /// Returns Vector.Invalid if the polygon has no vertices.
        /// </summary>
        public static Vector FindBottomLeftPoint(Polygon polygon)
        {
            if (polygon.Vertices.Count == 0)
                return Vector.Invalid;

            var best = polygon.Vertices[0];

            for (var i = 1; i < polygon.Vertices.Count; i++)
            {
                var v = polygon.Vertices[i];

                if (v.Y < best.Y || (v.Y == best.Y && v.X < best.X))
                    best = v;
            }

            return best;
        }
    }
}
