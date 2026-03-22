using System;
using System.Collections.Concurrent;
using OpenNest.Geometry;

namespace OpenNest.Engine.Fill;

/// <summary>
/// Tracks evaluated grid configurations so duplicate pattern/direction/workArea
/// combinations can be skipped across fill strategies.
/// </summary>
public class GridDedup
{
    public const string SharedStateKey = "GridDedup";

    private readonly ConcurrentDictionary<GridKey, byte> _seen = new();

    /// <summary>
    /// Returns true if this configuration has NOT been seen before (i.e., should be evaluated).
    /// Returns false if it's a duplicate.
    /// </summary>
    public bool TryAdd(Box patternBox, Box workArea, NestDirection dir)
    {
        var key = new GridKey(patternBox, workArea, dir);
        return _seen.TryAdd(key, 0);
    }

    public int Count => _seen.Count;

    /// <summary>
    /// Gets or creates a GridDedup from FillContext.SharedState.
    /// </summary>
    public static GridDedup GetOrCreate(System.Collections.Generic.Dictionary<string, object> sharedState)
    {
        if (sharedState.TryGetValue(SharedStateKey, out var existing))
            return (GridDedup)existing;

        var dedup = new GridDedup();
        sharedState[SharedStateKey] = dedup;
        return dedup;
    }

    private readonly struct GridKey : IEquatable<GridKey>
    {
        private readonly int _patternW, _patternL, _workW, _workL, _dir;

        public GridKey(Box patternBox, Box workArea, NestDirection dir)
        {
            _patternW = (int)System.Math.Round(patternBox.Width * 10);
            _patternL = (int)System.Math.Round(patternBox.Length * 10);
            _workW = (int)System.Math.Round(workArea.Width * 10);
            _workL = (int)System.Math.Round(workArea.Length * 10);
            _dir = (int)dir;
        }

        public bool Equals(GridKey other) =>
            _patternW == other._patternW && _patternL == other._patternL &&
            _workW == other._workW && _workL == other._workL &&
            _dir == other._dir;

        public override bool Equals(object obj) => obj is GridKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = _patternW;
                hash = hash * 397 ^ _patternL;
                hash = hash * 397 ^ _workW;
                hash = hash * 397 ^ _workL;
                hash = hash * 397 ^ _dir;
                return hash;
            }
        }
    }
}
