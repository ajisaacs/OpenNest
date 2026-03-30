using OpenNest.Geometry;

namespace OpenNest.CNC
{
    public class RapidMove : Motion
    {
        public RapidMove()
        {
            Feedrate = CNC.Feedrate.UseMax;
        }

        public RapidMove(Vector endPoint)
        {
            EndPoint = endPoint;
        }

        public RapidMove(double x, double y)
        {
            EndPoint = new Vector(x, y);
        }

        public override CodeType Type
        {
            get { return CodeType.RapidMove; }
        }

        public override ICode Clone()
        {
            return new RapidMove(EndPoint)
            {
                Suppressed = Suppressed
            };
        }

        public override string ToString()
        {
            return ToString(DefaultDecimalPlaces);
        }

        public override string ToString(int decimalPlaces)
        {
            var dp = "N" + decimalPlaces;
            return string.Format("G00 X{0} Y{1}", EndPoint.X.ToString(dp), EndPoint.Y.ToString(dp));
        }
    }
}
