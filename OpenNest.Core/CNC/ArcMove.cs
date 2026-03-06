using OpenNest.Geometry;

namespace OpenNest.CNC
{
    public class ArcMove : Motion
    {
        public ArcMove()
        {
        }

        public ArcMove(double x, double y, double i, double j, RotationType rotation = RotationType.CCW)
            : this(new Vector(x, y), new Vector(i, j), rotation)
        {
        }

        public ArcMove(Vector endPoint, Vector centerPoint, RotationType rotation = RotationType.CCW)
        {
            EndPoint = endPoint;
            CenterPoint = centerPoint;
            Rotation = rotation;
            Layer = LayerType.Cut;
        }

        public LayerType Layer { get; set; }

        public RotationType Rotation { get; set; }

        public Vector CenterPoint { get; set; }

        public double Radius
        {
            get { return CenterPoint.DistanceTo(EndPoint); }
        }

        public override void Rotate(double angle)
        {
            base.Rotate(angle);
            CenterPoint = CenterPoint.Rotate(angle);
        }

        public override void Rotate(double angle, Vector origin)
        {
            base.Rotate(angle, origin);
            CenterPoint = CenterPoint.Rotate(angle, origin);
        }

        public override void Offset(double x, double y)
        {
            base.Offset(x, y);
            CenterPoint = new Vector(CenterPoint.X + x, CenterPoint.Y + y);
        }

        public override void Offset(Vector voffset)
        {
            base.Offset(voffset);
            CenterPoint += voffset;
        }

        public override CodeType Type
        {
            get { return CodeType.ArcMove; }
        }

        public override ICode Clone()
        {
            return new ArcMove(EndPoint, CenterPoint, Rotation)
            {
                Layer = Layer
            };
        }

        public override string ToString()
        {
            return ToString(DefaultDecimalPlaces);
        }

        public override string ToString(int decimalPlaces)
        {
            var dp = "N" + decimalPlaces;
            var x = EndPoint.X.ToString(dp);
            var y = EndPoint.Y.ToString(dp);
            var i = CenterPoint.X.ToString(dp);
            var j = CenterPoint.Y.ToString(dp);

            return Rotation == RotationType.CW ?
                string.Format("G02 X{0} Y{1} I{2} J{3}", x, y, i, j) :
                string.Format("G03 X{0} Y{1} I{2} J{3}", x, y, i, j);
        }
    }
}
