using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;

namespace OpenNest.CNC
{
    public class Program
    {
        public List<ICode> Codes;

        private Mode mode;

        public Program(Mode mode = Mode.Absolute)
        {
            Codes = new List<ICode>();
            Mode = mode;
        }

        public Mode Mode
        {
            get { return mode; }
            set
            {
                if (value == Mode.Absolute)
                    SetModeAbs();
                else
                    SetModeInc();
            }
        }

        public double Rotation { get; protected set; }

        private void SetModeInc()
        {
            if (mode == Mode.Incremental)
                return;

            ConvertMode.ToIncremental(this);

            mode = Mode.Incremental;
        }

        private void SetModeAbs()
        {
            if (mode == Mode.Absolute)
                return;

            ConvertMode.ToAbsolute(this);

            mode = Mode.Absolute;
        }

        public virtual void Rotate(double angle) => Rotate(angle, new Vector(0, 0));

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(mode == Mode.Absolute ? "G90" : "G91");
            foreach (var code in Codes)
            {
                if (code is Motion m)
                {
                    var cmd = m is RapidMove ? "G00" : (m is ArcMove am ? (am.Rotation == RotationType.CW ? "G02" : "G03") : "G01");
                    sb.Append($"{cmd}X{m.EndPoint.X:F4}Y{m.EndPoint.Y:F4}");
                    if (m is ArcMove arc) sb.Append($"I{arc.CenterPoint.X:F4}J{arc.CenterPoint.Y:F4}");
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }

        public virtual void Rotate(double angle, Vector origin)
        {
            var mode = Mode;

            SetModeAbs();

            for (int i = 0; i < Codes.Count; ++i)
            {
                var code = Codes[i];

                if (code.Type == CodeType.SubProgramCall)
                {
                    var subpgm = (SubProgramCall)code;

                    if (subpgm.Program != null)
                        subpgm.Program.Rotate(angle, origin);
                }

                if (code is Motion == false)
                    continue;

                var code2 = (Motion)code;

                code2.Rotate(angle, origin);
            }

            if (mode == Mode.Incremental)
                SetModeInc();

            Rotation = Angle.NormalizeRad(Rotation + angle);
        }

        public virtual void Offset(double x, double y)
        {
            var mode = Mode;

            SetModeAbs();

            for (int i = 0; i < Codes.Count; ++i)
            {
                var code = Codes[i];

                if (code is Motion == false)
                    continue;

                var code2 = (Motion)code;

                code2.Offset(x, y);
            }

            if (mode == Mode.Incremental)
                SetModeInc();
        }

        public virtual void Offset(Vector voffset)
        {
            var mode = Mode;

            SetModeAbs();

            for (int i = 0; i < Codes.Count; ++i)
            {
                var code = Codes[i];

                if (code is Motion == false)
                    continue;

                var code2 = (Motion)code;

                code2.Offset(voffset);
            }

            if (mode == Mode.Incremental)
                SetModeInc();
        }

        public void LineTo(double x, double y)
        {
            Codes.Add(new LinearMove(x, y));
        }

        public void LineTo(Vector pt)
        {
            Codes.Add(new LinearMove(pt));
        }

        public void MoveTo(double x, double y)
        {
            Codes.Add(new RapidMove(x, y));
        }

        public void MoveTo(Vector pt)
        {
            Codes.Add(new RapidMove(pt));
        }

        public void ArcTo(double x, double y, double i, double j, RotationType rotation)
        {
            Codes.Add(new ArcMove(x, y, i, j, rotation));
        }

        public void ArcTo(Vector endpt, Vector center, RotationType rotation)
        {
            Codes.Add(new ArcMove(endpt, center, rotation));
        }

        public void AddSubProgram(Program program)
        {
            Codes.Add(new SubProgramCall(program, program.Rotation));
        }

        public ICode this[int index]
        {
            get { return Codes[index]; }
            set { Codes[index] = value; }
        }

        public int Length
        {
            get { return Codes.Count; }
        }

        public void Merge(Program pgm)
        {
            // Set the program to be merged to the same Mode as the current.
            pgm.Mode = this.Mode;

            if (Mode == Mode.Absolute)
            {
                bool isRapid = false;

                // Check if the first motion code is a rapid move
                foreach (var code in pgm.Codes)
                {
                    if (code is Motion == false)
                        continue;

                    var motion = (Motion)code;

                    isRapid = motion.GetType() == typeof(RapidMove);
                    break;
                }

                // If the first motion code is not a rapid, move to the origin.
                if (!isRapid)
                    MoveTo(0, 0);

                Codes.AddRange(pgm.Codes);
            }
            else
            {
                Codes.AddRange(pgm.Codes);
            }
        }

        public Vector EndPoint()
        {
            switch (Mode)
            {
                case Mode.Absolute:
                    {
                        for (int i = Codes.Count; i >= 0; --i)
                        {
                            var code = Codes[i];
                            var motion = code as Motion;

                            if (motion == null) continue;

                            return motion.EndPoint;
                        }
                        break;
                    }

                case Mode.Incremental:
                    {
                        var pos = new Vector(0, 0);

                        for (int i = 0; i < Codes.Count; ++i)
                        {
                            var code = Codes[i];
                            var motion = code as Motion;

                            if (motion == null) continue;

                            pos += motion.EndPoint;
                        }

                        return pos;
                    }
            }

            return new Vector(0, 0);
        }

        public Box BoundingBox()
        {
            var origin = new Vector(0, 0);
            return BoundingBox(ref origin);
        }

        private Box BoundingBox(ref Vector pos)
        {
            double minX = 0.0;
            double minY = 0.0;
            double maxX = 0.0;
            double maxY = 0.0;

            for (int i = 0; i < Codes.Count; ++i)
            {
                var code = Codes[i];

                switch (code.Type)
                {
                    case CodeType.LinearMove:
                        {
                            var line = (LinearMove)code;
                            var pt = Mode == Mode.Absolute ?
                                line.EndPoint :
                                line.EndPoint + pos;

                            if (pt.X > maxX)
                                maxX = pt.X;
                            else if (pt.X < minX)
                                minX = pt.X;

                            if (pt.Y > maxY)
                                maxY = pt.Y;
                            else if (pt.Y < minY)
                                minY = pt.Y;

                            pos = pt;

                            break;
                        }

                    case CodeType.RapidMove:
                        {
                            var line = (RapidMove)code;
                            var pt = Mode == Mode.Absolute
                                ? line.EndPoint
                                : line.EndPoint + pos;

                            if (pt.X > maxX)
                                maxX = pt.X;
                            else if (pt.X < minX)
                                minX = pt.X;

                            if (pt.Y > maxY)
                                maxY = pt.Y;
                            else if (pt.Y < minY)
                                minY = pt.Y;

                            pos = pt;

                            break;
                        }

                    case CodeType.ArcMove:
                        {
                            var arc = (ArcMove)code;
                            var radius = arc.CenterPoint.DistanceTo(arc.EndPoint);

                            Vector endpt;
                            Vector centerpt;

                            if (Mode == Mode.Incremental)
                            {
                                endpt = arc.EndPoint + pos;
                                centerpt = arc.CenterPoint + pos;
                            }
                            else
                            {
                                endpt = arc.EndPoint;
                                centerpt = arc.CenterPoint;
                            }

                            double minX1;
                            double minY1;
                            double maxX1;
                            double maxY1;

                            if (pos.X < endpt.X)
                            {
                                minX1 = pos.X;
                                maxX1 = endpt.X;
                            }
                            else
                            {
                                minX1 = endpt.X;
                                maxX1 = pos.X;
                            }

                            if (pos.Y < endpt.Y)
                            {
                                minY1 = pos.Y;
                                maxY1 = endpt.Y;
                            }
                            else
                            {
                                minY1 = endpt.Y;
                                maxY1 = pos.Y;
                            }

                            var startAngle = pos.AngleFrom(centerpt);
                            var endAngle = endpt.AngleFrom(centerpt);

                            // switch the angle to counter clockwise.
                            if (arc.Rotation == RotationType.CW)
                                Generic.Swap(ref startAngle, ref endAngle);

                            startAngle = Angle.NormalizeRad(startAngle);
                            endAngle = Angle.NormalizeRad(endAngle);

                            if (Angle.IsBetweenRad(Angle.HalfPI, startAngle, endAngle))
                                maxY1 = centerpt.Y + radius;

                            if (Angle.IsBetweenRad(System.Math.PI, startAngle, endAngle))
                                minX1 = centerpt.X - radius;

                            const double oneHalfPI = System.Math.PI * 1.5;

                            if (Angle.IsBetweenRad(oneHalfPI, startAngle, endAngle))
                                minY1 = centerpt.Y - radius;

                            if (Angle.IsBetweenRad(Angle.TwoPI, startAngle, endAngle))
                                maxX1 = centerpt.X + radius;

                            if (maxX1 > maxX)
                                maxX = maxX1;

                            if (minX1 < minX)
                                minX = minX1;

                            if (maxY1 > maxY)
                                maxY = maxY1;

                            if (minY1 < minY)
                                minY = minY1;

                            pos = endpt;

                            break;
                        }

                    case CodeType.SubProgramCall:
                        {
                            var subpgm = (SubProgramCall)code;
                            var box = subpgm.Program.BoundingBox(ref pos);

                            if (box.Left < minX)
                                minX = box.Left;

                            if (box.Right > maxX)
                                maxX = box.Right;

                            if (box.Bottom < minY)
                                minY = box.Bottom;

                            if (box.Top > maxY)
                                maxY = box.Top;

                            break;
                        }
                }
            }

            return new Box(minX, minY, maxX - minX, maxY - minY);
        }

        public object Clone()
        {
            var pgm = new Program()
            {
                mode = this.mode,
                Rotation = this.Rotation
            };

            var codes = new ICode[Length];

            for (int i = 0; i < Length; ++i)
                codes[i] = this.Codes[i].Clone();

            pgm.Codes.AddRange(codes);

            return pgm;
        }

        public List<Entity> ToGeometry()
        {
            return ConvertProgram.ToGeometry(this);
        }
    }
}
