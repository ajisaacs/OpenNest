using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Engine.RapidPlanning;

namespace OpenNest.Engine
{
    public class PlateResult
    {
        public List<ProcessedPart> Parts { get; init; }
    }

    public readonly struct ProcessedPart
    {
        public Part Part { get; init; }
        public Program ProcessedProgram { get; init; }
        public RapidPath RapidPath { get; init; }
    }
}
