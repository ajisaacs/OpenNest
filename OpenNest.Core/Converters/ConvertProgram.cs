using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Converters
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

        private static void AddProgram(
            Program program,
            ref Mode mode,
            ref Vector curpos,
            ref List<Entity> geometry
        )
        {
            // Capture the frame origin at entry. Sub-program Offsets are relative
            // to this fixed origin, not to the current tool position.
            var frameOrigin = curpos;
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
                        var subpgm = (SubProgramCall)code;
                        var savedMode = mode;

                        // The sub-program's frame origin in this program's frame is
                        // frameOrigin + Offset — independent of current tool position.
                        curpos = new Vector(
                            frameOrigin.X + subpgm.Offset.X,
                            frameOrigin.Y + subpgm.Offset.Y
                        );

                        AddProgram(subpgm.Program, ref mode, ref curpos, ref geometry);
                        mode = savedMode;
                        break;
                }
            }
        }

        private static void AddLinearMove(
            LinearMove linearMove,
            ref Mode mode,
            ref Vector curpos,
            ref List<Entity> geometry
        )
        {
            var pt = linearMove.EndPoint;

            if (mode == Mode.Incremental)
                pt += curpos;

            var layer = ConvertLayer(linearMove.Layer);
            var line = new Line(curpos, pt) { Layer = layer, Color = layer.Color };
            geometry.Add(line);
            curpos = pt;
        }

        private static void AddRapidMove(
            RapidMove rapidMove,
            ref Mode mode,
            ref Vector curpos,
            ref List<Entity> geometry
        )
        {
            var pt = rapidMove.EndPoint;

            if (mode == Mode.Incremental)
                pt += curpos;

            var line = new Line(curpos, pt)
            {
                Layer = SpecialLayers.Rapid,
                Color = SpecialLayers.Rapid.Color,
            };
            geometry.Add(line);
            curpos = pt;
        }

        private static void AddArcMove(
            ArcMove arcMove,
            ref Mode mode,
            ref Vector curpos,
            ref List<Entity> geometry
        )
        {
            var center = arcMove.CenterPoint;
            var endpt = arcMove.EndPoint;

            if (mode == Mode.Incremental)
            {
                endpt += curpos;
                center += curpos;
            }

            center = FitCenterToEndpoints(center, curpos, endpt);

            var startAngle = center.AngleTo(curpos);
            var endAngle = center.AngleTo(endpt);

            var dx = endpt.X - center.X;
            var dy = endpt.Y - center.Y;

            var radius = System.Math.Sqrt(dx * dx + dy * dy);
            var layer = ConvertLayer(arcMove.Layer);

            if (startAngle.IsEqualTo(endAngle))
                geometry.Add(
                    new Circle(center, radius)
                    {
                        Layer = layer,
                        Color = layer.Color,
                        Rotation = arcMove.Rotation,
                    }
                );
            else
                geometry.Add(
                    new Arc(
                        center,
                        radius,
                        startAngle,
                        endAngle,
                        arcMove.Rotation == RotationType.CW
                    )
                    {
                        Layer = layer,
                        Color = layer.Color,
                    }
                );

            curpos = endpt;
        }

        /// <summary>
        /// Programs can carry arc centers that are not quite equidistant from the
        /// start and end points (e.g. I0.03 on a 0.0598 chord). Building the arc from
        /// the end radius alone then leaves its start point off the previous move's
        /// end, which breaks contour chaining. Project the center onto the chord's
        /// perpendicular bisector so the arc passes through both endpoints exactly.
        /// </summary>
        private static Vector FitCenterToEndpoints(Vector center, Vector start, Vector end)
        {
            var startRadius = center.DistanceTo(start);
            var endRadius = center.DistanceTo(end);

            if (startRadius.IsEqualTo(endRadius))
                return center;

            var chord = end - start;
            var chordLengthSq = chord.X * chord.X + chord.Y * chord.Y;

            // Full circle (start == end): no chord to fit against.
            if (chordLengthSq < Tolerance.Epsilon * Tolerance.Epsilon)
                return center;

            var mid = new Vector((start.X + end.X) * 0.5, (start.Y + end.Y) * 0.5);
            var normal = new Vector(-chord.Y, chord.X);
            var t = ((center.X - mid.X) * normal.X + (center.Y - mid.Y) * normal.Y) / chordLengthSq;

            return new Vector(mid.X + normal.X * t, mid.Y + normal.Y * t);
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
