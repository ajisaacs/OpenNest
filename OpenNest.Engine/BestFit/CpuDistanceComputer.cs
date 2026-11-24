using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Engine.BestFit
{
    /// <summary>
    /// Slides one boundary against another from every offset of a batch. The boundaries are
    /// prepared once (<see cref="SlideBatch"/>); each slide then returns what
    /// <see cref="SlideResolver.FirstBlocking"/> returns for that offset, bit for bit.
    /// </summary>
    public class CpuDistanceComputer : IPreparedDistanceComputer
    {
        public double[] ComputeDistances(
            List<Line> stationaryLines,
            List<Line> movingTemplateLines,
            SlideOffset[] offsets
        )
        {
            return Slide(SlideBatch.ForLines(movingTemplateLines, stationaryLines), offsets);
        }

        public double[] ComputeDistances(
            List<Entity> stationaryEntities,
            List<Entity> movingEntities,
            SlideOffset[] offsets
        )
        {
            return Slide(SlideBatch.ForEntities(movingEntities, stationaryEntities), offsets);
        }

        public IPreparedDistances Prepare(List<Entity> stationaryEntities, List<Entity> movingEntities)
        {
            return new Prepared(SlideBatch.ForEntities(movingEntities, stationaryEntities));
        }

        private static double[] Slide(SlideBatch batch, SlideOffset[] offsets)
        {
            var results = new double[offsets.Length];

            for (var i = 0; i < offsets.Length; i++)
                batch.Cover(offsets[i].Dx, offsets[i].Dy, offsets[i].DirX, offsets[i].DirY);
            batch.Index();

            // All vertices participate: a leading-half filter can miss the next contact
            // after sliding past an initial touch on a concave boundary.
            System.Threading.Tasks.Parallel.For(0, offsets.Length, i =>
            {
                var offset = offsets[i];
                results[i] = batch.FirstBlocking(offset.Dx, offset.Dy, offset.DirX, offset.DirY);
            });
            return results;
        }

        private sealed class Prepared : IPreparedDistances
        {
            private readonly SlideBatch batch;

            public Prepared(SlideBatch batch)
            {
                this.batch = batch;
            }

            public double[] ComputeDistances(SlideOffset[] offsets) => Slide(batch, offsets);
        }
    }
}
