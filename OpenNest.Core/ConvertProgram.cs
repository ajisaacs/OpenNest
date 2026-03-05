using System;
using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest
{
    public static class ConvertProgram
    {
        public static List<Entity> ToGeometry(Program pgm)
        {
            var geometry = new List<Entity>();
            var curpos = new Vector();
            var mode = Mode.Absolute;

            AddProgram(pgm, ref mode, ref curpos, ref geometry);

            return geometry;
        }

        private static void AddProgram(Program program, ref Mode mode, ref Vector curpos, ref List<Entity> geometry)
        {
            mode = program.Mode;

            for (int i = 0; i < program.Length; ++i)
            {
                var code = program[i];

                switch (code.Type)
                {
                    case CodeType.ArcMove:
                        AddArcMove((ArcMove)code, ref mode, ref curpos, ref geometry);
                        break;

                    case CodeType.LinearMove:
                        AddLinearMove((LinearMove)code, ref mode, ref curpos, ref geometry);
                        break;

                    case CodeType.RapidMove:
                        AddRapidMove((RapidMove)code, ref mode, ref curpos, ref geometry);
                        break;

                    case CodeType.SubProgramCall:
                        var tmpmode = mode;
                        var subpgm = (SubProgramCall)code;
                        var geoProgram = new Shape();
                        AddProgram(subpgm.Program, ref mode, ref curpos, ref geoProgram.Entities);
                        geometry.Add(geoProgram);
                        mode = tmpmode;
                        break;
                }
            }
        }

        private static void AddLinearMove(LinearMove linearMove, ref Mode mode, ref Vector curpos, ref List<Entity> geometry)
        {
            var pt = linearMove.EndPoint;

            if (mode == Mode.Incremental)
                pt += curpos;

            var line = new Line(curpos, pt)
            {
                Layer = ConvertLayer(linearMove.Layer)
            };
            geometry.Add(line);
            curpos = pt;
        }

        private static void AddRapidMove(RapidMove rapidMove, ref Mode mode, ref Vector curpos, ref List<Entity> geometry)
        {
            var pt = rapidMove.EndPoint;

            if (mode == Mode.Incremental)
                pt += curpos;

            var line = new Line(curpos, pt)
            {
                Layer = SpecialLayers.Rapid
            };
            geometry.Add(line);
            curpos = pt;
        }

        private static void AddArcMove(ArcMove arcMove, ref Mode mode, ref Vector curpos, ref List<Entity> geometry)
        {
            var center = arcMove.CenterPoint;
            var endpt = arcMove.EndPoint;

            if (mode == Mode.Incremental)
            {
                endpt += curpos;
                center += curpos;
            }

            var startAngle = center.AngleTo(curpos);
            var endAngle = center.AngleTo(endpt);

            var dx = endpt.X - center.X;
            var dy = endpt.Y - center.Y;

            var radius = Math.Sqrt(dx * dx + dy * dy);
            var layer = ConvertLayer(arcMove.Layer);

            if (startAngle.IsEqualTo(endAngle))
                geometry.Add(new Circle(center, radius) { Layer = layer });
            else
                geometry.Add(new Arc(center, radius, startAngle, endAngle, arcMove.Rotation == RotationType.CW) { Layer = layer });

            curpos = endpt;
        }

        private static Layer ConvertLayer(LayerType layer)
        {
            switch (layer)
            {
                case LayerType.Cut:
                    return SpecialLayers.Cut;

                case LayerType.Display:
                    return SpecialLayers.Display;

                case LayerType.Leadin:
                    return SpecialLayers.Leadin;

                case LayerType.Leadout:
                    return SpecialLayers.Leadout;

                case LayerType.Scribe:
                    return SpecialLayers.Scribe;

                default:
                    return new Layer(layer.ToString());
            }
        }
    }
}
