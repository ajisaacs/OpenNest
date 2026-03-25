using ACadSharp;
using OpenNest.Bending;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.IO.Bending
{
    public static class BendDetectorRegistry
    {
        private static readonly List<IBendDetector> detectors = new();

        static BendDetectorRegistry()
        {
            Register(new SolidWorksBendDetector());
        }

        public static void Register(IBendDetector detector)
        {
            detectors.Add(detector);
        }

        public static IReadOnlyList<IBendDetector> Detectors => detectors;

        public static IBendDetector GetByName(string name)
        {
            return detectors.FirstOrDefault(d => d.Name == name);
        }

        public static List<Bend> AutoDetect(CadDocument document)
        {
            foreach (var detector in detectors)
            {
                var bends = detector.DetectBends(document);
                if (bends.Count > 0)
                    return bends;
            }
            return new List<Bend>();
        }
    }
}
