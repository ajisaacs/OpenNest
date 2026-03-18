using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using System;
using System.Linq;

namespace OpenNest
{
    public static class Timing
    {
        public static TimingInfo GetTimingInfo(Program pgm)
        {
            var entities = ConvertProgram.ToGeometry(pgm);
            var shapes = ShapeBuilder.GetShapes(entities.Where(entity => entity.Layer != SpecialLayers.Rapid));
            var info = new TimingInfo { PierceCount = shapes.Count };

            var last = entities[0];
            ProcessEntity(info, last, null);

            for (int i = 1; i < entities.Count; i++)
            {
                var entity = entities[i];
                ProcessEntity(info, entity, last);
                last = entity;
            }

            return info;
        }

        public static TimingInfo GetTimingInfo(Plate plate)
        {
            var info = new TimingInfo();
            var pos = new Vector(0, 0);

            foreach (var part in plate.Parts)
            {
                var endpt = part.Program.EndPoint() + part.Location;
                info += GetTimingInfo(part.Program);
                info.TravelDistance += pos.DistanceTo(endpt);
                pos = endpt;
            }

            info *= plate.Quantity;

            return info;
        }

        public static TimingInfo GetTimingInfo(Nest nest)
        {
            var info = new TimingInfo();
            return nest.Plates.Aggregate(info, (current, plate) => current + GetTimingInfo(plate));
        }

        private static void ProcessEntity(TimingInfo info, Entity entity, Entity lastEntity)
        {
            if (entity.Layer == SpecialLayers.Cut)
            {
                info.CutDistance += entity.Length;

                if (entity.Type == EntityType.Line &&
                    lastEntity != null &&
                    lastEntity.Type == EntityType.Line &&
                    lastEntity.Layer == SpecialLayers.Cut)
                    info.IntersectionCount++;
            }
            else if (entity.Layer == SpecialLayers.Rapid)
                info.TravelDistance += entity.Length;
        }

        public static TimeSpan CalculateTime(TimingInfo info, CutParameters cutParams)
        {
            var time = new TimeSpan();

            switch (cutParams.Units)
            {
                case Units.Inches:
                    time += TimeSpan.FromMinutes(info.CutDistance / cutParams.Feedrate);
                    time += TimeSpan.FromMinutes(info.TravelDistance / cutParams.RapidTravelRate);
                    break;

                case Units.Millimeters:
                    time += TimeSpan.FromSeconds(info.CutDistance / cutParams.Feedrate);
                    time += TimeSpan.FromSeconds(info.TravelDistance / cutParams.RapidTravelRate);
                    break;
            }

            time += TimeSpan.FromTicks(info.PierceCount * cutParams.PierceTime.Ticks);

            return time;
        }
    }
}
