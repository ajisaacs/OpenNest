using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest.Engine.BestFit
{
    public class CpuDistanceComputer : IDistanceComputer
    {
        public double[] ComputeDistances(
            List<Line> stationaryLines,
            List<Line> movingTemplateLines,
            SlideOffset[] offsets
        )
        {
            var results = new double[offsets.Length];
            var movingVertices = Vertices(movingTemplateLines);
            var stationaryVertices = Vertices(stationaryLines);
            var contacts = SlideContactClassifier.FromLines(
                movingTemplateLines, Vector.Zero, stationaryLines, Vector.Zero).Prepare();

            System.Threading.Tasks.Parallel.For(0, offsets.Length, i =>
            {
                var offset = offsets[i];
                var source = new LineSlideEvents(
                    movingTemplateLines, movingVertices, offset.Dx, offset.Dy,
                    stationaryLines, stationaryVertices, offset.DirX, offset.DirY);
                results[i] = SlideResolver.FirstBlocking(ref source,
                    contacts.At(new Vector(offset.Dx, offset.Dy), Vector.Zero), offset.DirX, offset.DirY);
            });
            return results;
        }

        public double[] ComputeDistances(
            List<Entity> stationaryEntities,
            List<Entity> movingEntities,
            SlideOffset[] offsets
        )
        {
            var results = new double[offsets.Length];
            var movingVertices = SpatialQuery.ExtractEntityVertices(movingEntities);
            var stationaryVertices = SpatialQuery.ExtractEntityVertices(stationaryEntities);
            var contacts = new SlideContactClassifier(movingEntities, stationaryEntities).Prepare();

            // All vertices participate: a leading-half filter can miss the next contact
            // after sliding past an initial touch on a concave boundary.
            System.Threading.Tasks.Parallel.For(0, offsets.Length, i =>
            {
                var offset = offsets[i];
                var source = new EntitySlideEvents(
                    movingEntities, movingVertices, offset.Dx, offset.Dy,
                    stationaryEntities, stationaryVertices, offset.DirX, offset.DirY, arcToLine: true);
                results[i] = SlideResolver.FirstBlocking(ref source,
                    contacts.At(new Vector(offset.Dx, offset.Dy), Vector.Zero), offset.DirX, offset.DirY);
            });
            return results;
        }

        private static Vector[] Vertices(List<Line> lines) =>
            lines.SelectMany(line => new[] { line.StartPoint, line.EndPoint }).Distinct().ToArray();
    }
}
