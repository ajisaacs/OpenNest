
namespace OpenNest.CNC
{
    public abstract class Motion : ICode
    {
        protected const int DefaultDecimalPlaces = 4;

        public Vector EndPoint { get; set; }

        public bool UseExactStop { get; set; }

        public int Feedrate { get; set; }

        protected Motion()
        {
            Feedrate = CNC.Feedrate.UseDefault;
        }

        public virtual void Rotate(double angle)
        {
            EndPoint = EndPoint.Rotate(angle);
        }

        public virtual void Rotate(double angle, Vector origin)
        {
            EndPoint = EndPoint.Rotate(angle, origin);
        }

        public virtual void Offset(double x, double y)
        {
            EndPoint = new Vector(EndPoint.X + x, EndPoint.Y + y);
        }

        public virtual void Offset(Vector voffset)
        {
            EndPoint += voffset;
        }

        public abstract CodeType Type { get; }

        public abstract ICode Clone();

        public abstract string ToString(int decimalPlaces);
    }
}
