using OpenNest.Geometry;

namespace OpenNest.CNC
{
    public class LinearMove : Motion
    {
        public LinearMove()
            : this(new Vector())
        {
        }

        public LinearMove(double x, double y)
            : this(new Vector(x, y))
        {
        }

        public LinearMove(Vector endPoint)
        {
            EndPoint = endPoint;
            Layer = LayerType.Cut;
        }

        public LayerType Layer { get; set; }

        public override CodeType Type
        {
            get { return CodeType.LinearMove; }
        }

        public override ICode Clone()
        {
            return new LinearMove(EndPoint)
            {
                Layer = Layer,
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
            return string.Format("G01 X{0} Y{1}", EndPoint.X.ToString(dp), EndPoint.Y.ToString(dp));
        }
    }
}
