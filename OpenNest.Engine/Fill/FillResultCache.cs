using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OpenNest.Geometry;

namespace OpenNest.Engine.Fill;

/// <summary>
/// Caches fill results by drawing and box dimensions so repeated fills
/// of the same size don't recompute. Parts are stored normalized to origin
/// and offset to the actual location on retrieval.
/// </summary>
public static class FillResultCache
{
    private static readonly ConcurrentDictionary<CacheKey, List<Part>> _cache = new();

    /// <summary>
    /// Returns a cached fill result for the given drawing and box dimensions,
    /// offset to the target location. Returns null on cache miss.
    /// </summary>
    public static List<Part> Get(Drawing drawing, Box targetBox, double spacing)
    {
        var key = new CacheKey(drawing, targetBox.Width, targetBox.Length, spacing);

        if (!_cache.TryGetValue(key, out var cached) || cached.Count == 0)
            return null;

        var offset = targetBox.Location;
        var result = new List<Part>(cached.Count);

        foreach (var part in cached)
            result.Add(part.CloneAtOffset(offset));

        return result;
    }

    /// <summary>
    /// Stores a fill result normalized to origin (0,0).
    /// </summary>
    public static void Store(Drawing drawing, Box sourceBox, double spacing, List<Part> parts)
    {
        if (parts == null || parts.Count == 0)
            return;

        var key = new CacheKey(drawing, sourceBox.Width, sourceBox.Length, spacing);

        if (_cache.ContainsKey(key))
            return;

        var offset = new Vector(-sourceBox.X, -sourceBox.Y);
        var normalized = new List<Part>(parts.Count);

        foreach (var part in parts)
            normalized.Add(part.CloneAtOffset(offset));

        _cache.TryAdd(key, normalized);
    }

    public static void Clear() => _cache.Clear();

    public static int Count => _cache.Count;

    private readonly struct CacheKey : System.IEquatable<CacheKey>
    {
        public readonly Drawing Drawing;
        public readonly double Width;
        public readonly double Height;
        public readonly double Spacing;

        public CacheKey(Drawing drawing, double width, double height, double spacing)
        {
            Drawing = drawing;
            Width = System.Math.Round(width, 2);
            Height = System.Math.Round(height, 2);
            Spacing = spacing;
        }

        public bool Equals(CacheKey other) =>
            ReferenceEquals(Drawing, other.Drawing) &&
            Width == other.Width && Height == other.Height &&
            Spacing == other.Spacing;

        public override bool Equals(object obj) => obj is CacheKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = RuntimeHelpers.GetHashCode(Drawing);
                hash = hash * 397 ^ Width.GetHashCode();
                hash = hash * 397 ^ Height.GetHashCode();
                hash = hash * 397 ^ Spacing.GetHashCode();
                return hash;
            }
        }
    }
}
