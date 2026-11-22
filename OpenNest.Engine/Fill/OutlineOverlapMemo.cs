using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenNest.Geometry;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Remembers whether two spacing outlines overlap, by the exact coordinates of their
    /// perimeters. Auto Nest fills one box with one part once per remaining quantity, and each
    /// of those fills tests the same outlines at the same places; triangulating an outline to
    /// test it costs far more than comparing its vertices.
    ///
    /// The answer for a pair is the one <see cref="SpacingOutline.Overlaps"/> gave for outlines
    /// of the same vertices, bit for bit and in the same order, so a remembered verdict is the
    /// verdict the test would give: the memo changes how long a fill takes, never its result.
    /// Outlines with cutouts are not remembered and are tested directly.
    ///
    /// One memo serves the fills of one solve, from any thread, and goes with it. It is bounded
    /// meanwhile: outlines and verdicts are kept in two generations, the older dropped when the
    /// newer fills up, so what a run of fills keeps asking for stays and the rest goes.
    /// </summary>
    internal sealed class OutlineOverlapMemo
    {
        /// <summary>Perimeter vertices a generation of outlines holds: 16 bytes each.</summary>
        private const int DefaultMaxVertices = 2_000_000;

        /// <summary>Verdicts a generation holds.</summary>
        private const int DefaultMaxVerdicts = 500_000;

        private readonly object gate = new();
        private readonly int maxVertices;
        private readonly int maxVerdicts;

        private Dictionary<Key, Shape> shapes = new();
        private Dictionary<Key, Shape> olderShapes = new();
        private int vertices;

        private Dictionary<(Shape, Shape), bool> verdicts = new();
        private Dictionary<(Shape, Shape), bool> olderVerdicts = new();

        public OutlineOverlapMemo()
            : this(DefaultMaxVertices, DefaultMaxVerdicts) { }

        /// <param name="maxVertices">Perimeter vertices a generation of outlines may hold.</param>
        /// <param name="maxVerdicts">Verdicts a generation may hold.</param>
        internal OutlineOverlapMemo(int maxVertices, int maxVerdicts)
        {
            this.maxVertices = maxVertices;
            this.maxVerdicts = maxVerdicts;
        }

        /// <summary>
        /// The outline's entry: one per distinct perimeter, so two outlines of the same vertices
        /// share it. Null for an outline with cutouts, which the memo does not hold.
        /// </summary>
        public Shape ShapeOf(SpacingOutline outline)
        {
            if (outline.Holes.Count != 0)
                return null;

            var key = new Key(outline.Perimeter);
            lock (gate)
            {
                if (shapes.TryGetValue(key, out var shape))
                    return shape;

                if (!olderShapes.Remove(key, out shape))
                    shape = new Shape();

                if (vertices + key.VertexCount > maxVertices && shapes.Count > 0)
                {
                    olderShapes = shapes;
                    shapes = new Dictionary<Key, Shape>();
                    vertices = 0;
                }

                shapes[key] = shape;
                vertices += key.VertexCount;
                return shape;
            }
        }

        /// <summary>
        /// <c>a.Overlaps(b)</c>, remembered when both outlines have an entry
        /// (<paramref name="shapeA"/> and <paramref name="shapeB"/>, from <see cref="ShapeOf"/>).
        /// </summary>
        public bool Overlaps(SpacingOutline a, Shape shapeA, SpacingOutline b, Shape shapeB)
        {
            if (shapeA == null || shapeB == null)
                return a.Overlaps(b);

            var pair = (shapeA, shapeB);
            lock (gate)
            {
                if (verdicts.TryGetValue(pair, out var known))
                    return known;
                if (olderVerdicts.TryGetValue(pair, out known))
                {
                    Keep(pair, known);
                    return known;
                }
            }

            var overlaps = a.Overlaps(b);
            lock (gate)
                Keep(pair, overlaps);
            return overlaps;
        }

        // Caller holds the gate.
        private void Keep((Shape, Shape) pair, bool overlaps)
        {
            if (verdicts.Count >= maxVerdicts)
            {
                olderVerdicts = verdicts;
                verdicts = new Dictionary<(Shape, Shape), bool>();
            }

            verdicts[pair] = overlaps;
        }

        /// <summary>One distinct perimeter; verdicts are kept by the two entries of a pair.</summary>
        internal sealed class Shape { }

        /// <summary>A perimeter's vertices, equal only to the same coordinates bit for bit in the
        /// same order.</summary>
        private readonly struct Key : IEquatable<Key>
        {
            private readonly double[] coordinates;
            private readonly int hash;

            public Key(Polygon perimeter)
            {
                var source = perimeter.Vertices;
                coordinates = new double[source.Count * 2];
                var code = new HashCode();
                for (var i = 0; i < source.Count; i++)
                {
                    coordinates[2 * i] = source[i].X;
                    coordinates[2 * i + 1] = source[i].Y;
                    code.Add(BitConverter.DoubleToInt64Bits(source[i].X));
                    code.Add(BitConverter.DoubleToInt64Bits(source[i].Y));
                }

                hash = code.ToHashCode();
            }

            public int VertexCount => coordinates.Length / 2;

            public bool Equals(Key other) =>
                hash == other.hash
                && MemoryMarshal.Cast<double, long>(coordinates)
                    .SequenceEqual(MemoryMarshal.Cast<double, long>(other.coordinates));

            public override bool Equals(object obj) => obj is Key other && Equals(other);

            public override int GetHashCode() => hash;
        }
    }
}
