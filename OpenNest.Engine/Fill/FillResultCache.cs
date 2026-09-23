using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OpenNest.Geometry;

namespace OpenNest.Engine.Fill;

/// <summary>
/// Caches fill results by drawing and box dimensions so repeated fills
/// of the same size don't recompute. Parts are stored normalized to origin
/// and offset to the actual location on retrieval.
///
/// Entries are keyed weakly by the source drawing, so every canonical copy of one drawing shares
/// them. Canonical and non-canonical callers are kept apart because cached parts are in the frame
/// of the drawing they were computed for. An entry is dropped when the drawing's
/// <see cref="Drawing.Program"/> instance or canonical angle changes.
/// </summary>
public static class FillResultCache
{
    private static readonly ConditionalWeakTable<Drawing, Entry> _entries = new();
    private static readonly object _entriesLock = new();

    /// <summary>
    /// Returns a cached fill result for the given drawing and box dimensions,
    /// offset to the target location. Returns null on cache miss.
    /// </summary>
    public static List<Part> Get(Drawing drawing, Box targetBox, double spacing)
    {
        var source = CanonicalFrame.SourceOf(drawing);
        if (!_entries.TryGetValue(source, out var entry) || !entry.IsCurrentFor(source))
            return null;

        var key = new CacheKey(drawing, source, targetBox.Width, targetBox.Length, spacing);

        if (!entry.Results.TryGetValue(key, out var cached) || cached.Count == 0)
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

        var source = CanonicalFrame.SourceOf(drawing);
        var entry = GetEntry(source);
        var key = new CacheKey(drawing, source, sourceBox.Width, sourceBox.Length, spacing);

        if (entry.Results.ContainsKey(key))
            return;

        var offset = new Vector(-sourceBox.X, -sourceBox.Y);
        var normalized = new List<Part>(parts.Count);

        foreach (var part in parts)
            normalized.Add(part.CloneAtOffset(offset));

        entry.Results.TryAdd(key, normalized);
    }

    public static void Clear()
    {
        lock (_entriesLock)
            _entries.Clear();
    }

    public static int Count
    {
        get
        {
            var count = 0;
            foreach (var kvp in _entries)
                count += kvp.Value.Results.Count;
            return count;
        }
    }

    private static Entry GetEntry(Drawing source)
    {
        if (_entries.TryGetValue(source, out var entry) && entry.IsCurrentFor(source))
            return entry;

        lock (_entriesLock)
        {
            if (_entries.TryGetValue(source, out entry) && entry.IsCurrentFor(source))
                return entry;

            entry = new Entry(source);
            _entries.AddOrUpdate(source, entry);
            return entry;
        }
    }

    private sealed class Entry
    {
        private readonly CNC.Program program;
        private readonly double sourceAngle;

        public readonly ConcurrentDictionary<CacheKey, List<Part>> Results = new();

        public Entry(Drawing source)
        {
            program = source.Program;
            sourceAngle = source.Source?.Angle ?? 0.0;
        }

        public bool IsCurrentFor(Drawing source) =>
            ReferenceEquals(program, source.Program)
            && sourceAngle == (source.Source?.Angle ?? 0.0);
    }

    private readonly record struct CacheKey(bool IsCanonical, double Width, double Height, double Spacing)
    {
        public CacheKey(Drawing drawing, Drawing source, double width, double height, double spacing)
            : this(
                !ReferenceEquals(drawing, source),
                System.Math.Round(width, 2),
                System.Math.Round(height, 2),
                spacing
            ) { }
    }
}
