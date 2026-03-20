using OpenNest.Geometry;
using System;
using System.Collections.Generic;

namespace OpenNest.Engine.Nfp
{
    /// <summary>
    /// Caches computed No-Fit Polygons keyed by (DrawingA.Id, RotationA, DrawingB.Id, RotationB).
    /// NFPs are computed on first access and stored for reuse during optimization.
    /// Thread-safe for concurrent reads after pre-computation.
    /// </summary>
    public class NfpCache
    {
        private readonly Dictionary<NfpKey, Polygon> cache = new Dictionary<NfpKey, Polygon>();
        private readonly Dictionary<int, Dictionary<double, Polygon>> polygonCache
            = new Dictionary<int, Dictionary<double, Polygon>>();
        private readonly Dictionary<(int drawingId, double rotation), Polygon> ifpCache
            = new Dictionary<(int drawingId, double rotation), Polygon>();

        /// <summary>
        /// Registers a pre-computed polygon for a drawing at a specific rotation.
        /// Call this during initialization before computing NFPs.
        /// </summary>
        public void RegisterPolygon(int drawingId, double rotation, Polygon polygon)
        {
            if (!polygonCache.TryGetValue(drawingId, out var rotations))
            {
                rotations = new Dictionary<double, Polygon>();
                polygonCache[drawingId] = rotations;
            }

            rotations[rotation] = polygon;

            // Clear IFP cache if a polygon is updated (though usually they aren't).
            ifpCache.Remove((drawingId, rotation));
        }

        /// <summary>
        /// Gets or computes the IFP for a drawing at a specific rotation within a work area.
        /// </summary>
        public Polygon GetIfp(int drawingId, double rotation, Box workArea)
        {
            if (ifpCache.TryGetValue((drawingId, rotation), out var ifp))
                return ifp;

            var polygon = GetPolygon(drawingId, rotation);
            if (polygon == null)
                return new Polygon();

            ifp = InnerFitPolygon.Compute(workArea, polygon);
            ifpCache[(drawingId, rotation)] = ifp;
            return ifp;
        }

        /// <summary>
        /// Gets the polygon for a drawing at a specific rotation.
        /// </summary>
        public Polygon GetPolygon(int drawingId, double rotation)
        {
            if (polygonCache.TryGetValue(drawingId, out var rotations))
            {
                if (rotations.TryGetValue(rotation, out var polygon))
                    return polygon;
            }

            return null;
        }

        /// <summary>
        /// Gets or computes the NFP between two drawings at their respective rotations.
        /// The NFP is computed from the stationary polygon (drawingA at rotationA) and
        /// the orbiting polygon (drawingB at rotationB).
        /// </summary>
        public Polygon Get(int drawingIdA, double rotationA, int drawingIdB, double rotationB)
        {
            var key = new NfpKey(drawingIdA, rotationA, drawingIdB, rotationB);

            if (cache.TryGetValue(key, out var nfp))
                return nfp;

            var polyA = GetPolygon(drawingIdA, rotationA);
            var polyB = GetPolygon(drawingIdB, rotationB);

            if (polyA == null || polyB == null)
                return new Polygon();

            nfp = NoFitPolygon.Compute(polyA, polyB);
            cache[key] = nfp;
            return nfp;
        }

        /// <summary>
        /// Pre-computes all NFPs for every combination of registered polygons.
        /// Call after all polygons are registered to front-load computation.
        /// </summary>
        public void PreComputeAll()
        {
            var entries = new List<(int drawingId, double rotation)>();

            foreach (var kvp in polygonCache)
            {
                foreach (var rot in kvp.Value)
                    entries.Add((kvp.Key, rot.Key));
            }

            for (var i = 0; i < entries.Count; i++)
            {
                for (var j = 0; j < entries.Count; j++)
                {
                    Get(entries[i].drawingId, entries[i].rotation,
                        entries[j].drawingId, entries[j].rotation);
                }
            }
        }

        /// <summary>
        /// Number of cached NFP entries.
        /// </summary>
        public int Count => cache.Count;

        private readonly struct NfpKey : IEquatable<NfpKey>
        {
            public readonly int DrawingIdA;
            public readonly double RotationA;
            public readonly int DrawingIdB;
            public readonly double RotationB;

            public NfpKey(int drawingIdA, double rotationA, int drawingIdB, double rotationB)
            {
                DrawingIdA = drawingIdA;
                RotationA = rotationA;
                DrawingIdB = drawingIdB;
                RotationB = rotationB;
            }

            public bool Equals(NfpKey other)
            {
                return DrawingIdA == other.DrawingIdA
                       && RotationA == other.RotationA
                       && DrawingIdB == other.DrawingIdB
                       && RotationB == other.RotationB;
            }

            public override bool Equals(object obj) => obj is NfpKey key && Equals(key);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 31 + DrawingIdA;
                    hash = hash * 31 + RotationA.GetHashCode();
                    hash = hash * 31 + DrawingIdB;
                    hash = hash * 31 + RotationB.GetHashCode();
                    return hash;
                }
            }
        }
    }
}
