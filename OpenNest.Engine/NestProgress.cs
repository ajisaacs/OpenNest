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

    public class NestProgress
    {
        public NestPhase Phase { get; set; }
        public int PlateNumber { get; set; }
        public int BestPartCount { get; set; }
        public double BestDensity { get; set; }
        public double NestedWidth { get; set; }
        public double NestedLength { get; set; }
        public double NestedArea { get; set; }
        public List<Part> BestParts { get; set; }
        public string Description { get; set; }
        public Box ActiveWorkArea { get; set; }
        public bool IsOverallBest { get; set; }
    }
}
