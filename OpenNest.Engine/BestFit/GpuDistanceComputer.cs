using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.Engine.BestFit
{
    public class GpuDistanceComputer : IDistanceComputer
    {
        private readonly ISlideComputer _slideComputer;

        public GpuDistanceComputer(ISlideComputer slideComputer)
        {
            _slideComputer = slideComputer;
        }

        public double[] ComputeDistances(
            List<Line> stationaryLines,
            List<Line> movingTemplateLines,
            SlideOffset[] offsets)
        {
            var stationarySegments = SpatialQuery.FlattenLines(stationaryLines);
            var movingSegments = SpatialQuery.FlattenLines(movingTemplateLines);
            var count = offsets.Length;
            var flatOffsets = new double[count * 2];
            var directions = new int[count];

            for (var i = 0; i < count; i++)
            {
                flatOffsets[i * 2] = offsets[i].Dx;
                flatOffsets[i * 2 + 1] = offsets[i].Dy;
                directions[i] = DirectionVectorToInt(offsets[i].DirX, offsets[i].DirY);
            }

            return _slideComputer.ComputeBatchMultiDir(
                stationarySegments, stationaryLines.Count,
                movingSegments, movingTemplateLines.Count,
                flatOffsets, count, directions);
        }

        /// <summary>
        /// Maps a unit direction vector to a PushDirection int for the GPU interface.
        /// Left=0, Down=1, Right=2, Up=3.
        /// </summary>
        private static int DirectionVectorToInt(double dirX, double dirY)
        {
            if (dirX < -0.5) return (int)PushDirection.Left;
            if (dirX > 0.5) return (int)PushDirection.Right;
            if (dirY < -0.5) return (int)PushDirection.Down;
            return (int)PushDirection.Up;
        }
    }
}
