using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.CNC
{
    public abstract class Motion : ICode
    {
        protected const int DefaultDecimalPlaces = 4;

        public Vector EndPoint { get; set; }

        public bool UseExactStop { get; set; }

        public int Feedrate { get; set; }

        public bool Suppressed { get; set; }

        public Dictionary<string, string> VariableRefs { get; set; }

        protected Motion()
        {
            Feedrate = CNC.Feedrate.UseDefault;
        }

        public virtual void Rotate(double angle)
        {
            EndPoint = EndPoint.Rotate(angle);
            VariableRefs = null;
        }

        public virtual void Rotate(double angle, Vector origin)
        {
            EndPoint = EndPoint.Rotate(angle, origin);
            VariableRefs = null;
        }

        public virtual void Offset(double x, double y)
        {
            EndPoint = new Vector(EndPoint.X + x, EndPoint.Y + y);
            VariableRefs = null;
        }

        public virtual void Offset(Vector voffset)
        {
            EndPoint += voffset;
            VariableRefs = null;
        }

        public abstract CodeType Type { get; }

        public abstract ICode Clone();

        public abstract string ToString(int decimalPlaces);
    }
}
