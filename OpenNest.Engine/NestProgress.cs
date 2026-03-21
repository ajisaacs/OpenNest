using OpenNest.Geometry;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;

namespace OpenNest
{
    [AttributeUsage(AttributeTargets.Field)]
    internal class ShortNameAttribute(string name) : Attribute
    {
        public string Name { get; } = name;
    }

    public enum NestPhase
    {
        [Description("Trying rotations..."), ShortName("Linear")] Linear,
        [Description("Trying best fit..."), ShortName("BestFit")] RectBestFit,
        [Description("Trying pairs..."), ShortName("Pairs")] Pairs,
        [Description("Trying NFP..."), ShortName("NFP")] Nfp,
        [Description("Trying extents..."), ShortName("Extents")] Extents,
        [Description("Custom"), ShortName("Custom")] Custom
    }

    public static class NestPhaseExtensions
    {
        private static readonly ConcurrentDictionary<NestPhase, string> DisplayNames = new();
        private static readonly ConcurrentDictionary<NestPhase, string> ShortNames = new();

        public static string DisplayName(this NestPhase phase)
        {
            return DisplayNames.GetOrAdd(phase, p =>
            {
                var field = typeof(NestPhase).GetField(p.ToString());
                var attr = field?.GetCustomAttribute<DescriptionAttribute>();
                return attr?.Description ?? p.ToString();
            });
        }

        public static string ShortName(this NestPhase phase)
        {
            return ShortNames.GetOrAdd(phase, p =>
            {
                var field = typeof(NestPhase).GetField(p.ToString());
                var attr = field?.GetCustomAttribute<ShortNameAttribute>();
                return attr?.Name ?? p.ToString();
            });
        }
    }

    public class PhaseResult
    {
        public NestPhase Phase { get; set; }
        public int PartCount { get; set; }
        public long TimeMs { get; set; }

        public PhaseResult(NestPhase phase, int partCount, long timeMs)
        {
            Phase = phase;
            PartCount = partCount;
            TimeMs = timeMs;
        }
    }

    public class AngleResult
    {
        public double AngleDeg { get; set; }
        public NestDirection Direction { get; set; }
        public int PartCount { get; set; }
    }

    internal readonly struct ProgressReport
    {
        public NestPhase Phase { get; init; }
        public int PlateNumber { get; init; }
        public List<Part> Parts { get; init; }
        public Box WorkArea { get; init; }
        public string Description { get; init; }
        public bool IsOverallBest { get; init; }
    }

    public class NestProgress
    {
        public NestPhase Phase { get; set; }
        public int PlateNumber { get; set; }

        private List<Part> bestParts;
        public List<Part> BestParts
        {
            get => bestParts;
            set { bestParts = value; cachedParts = null; }
        }

        public string Description { get; set; }
        public Box ActiveWorkArea { get; set; }
        public bool IsOverallBest { get; set; }

        public int BestPartCount => BestParts?.Count ?? 0;

        private List<Part> cachedParts;
        private Box cachedBounds;
        private double cachedPartArea;

        private void EnsureCache()
        {
            if (cachedParts == bestParts) return;
            cachedParts = bestParts;
            if (bestParts == null || bestParts.Count == 0)
            {
                cachedBounds = default;
                cachedPartArea = 0;
                return;
            }
            cachedBounds = bestParts.GetBoundingBox();
            cachedPartArea = 0;
            foreach (var p in bestParts)
                cachedPartArea += p.BaseDrawing.Area;
        }

        public double BestDensity
        {
            get
            {
                if (BestParts == null || BestParts.Count == 0) return 0;
                EnsureCache();
                var bboxArea = cachedBounds.Width * cachedBounds.Length;
                return bboxArea > 0 ? cachedPartArea / bboxArea : 0;
            }
        }

        public double NestedWidth
        {
            get
            {
                if (BestParts == null || BestParts.Count == 0) return 0;
                EnsureCache();
                return cachedBounds.Width;
            }
        }

        public double NestedLength
        {
            get
            {
                if (BestParts == null || BestParts.Count == 0) return 0;
                EnsureCache();
                return cachedBounds.Length;
            }
        }

        public double NestedArea
        {
            get
            {
                if (BestParts == null || BestParts.Count == 0) return 0;
                EnsureCache();
                return cachedPartArea;
            }
        }
    }
}
