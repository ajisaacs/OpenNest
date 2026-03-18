using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenNest.Engine.Strategies
{
    public class FillContext
    {
        public NestItem Item { get; init; }
        public Box WorkArea { get; init; }
        public Plate Plate { get; init; }
        public int PlateNumber { get; init; }
        public CancellationToken Token { get; init; }
        public IProgress<NestProgress> Progress { get; init; }

        public List<Part> CurrentBest { get; set; }
        public FillScore CurrentBestScore { get; set; }
        public NestPhase WinnerPhase { get; set; }
        public List<PhaseResult> PhaseResults { get; } = new();
        public List<AngleResult> AngleResults { get; } = new();

        public Dictionary<string, object> SharedState { get; } = new();
    }
}
