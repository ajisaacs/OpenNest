using System;
using OpenNest.Math;

namespace OpenNest
{
    public struct Vector
    {
        public static readonly Vector Invalid = new Vector(double.NaN, double.NaN);
        public static readonly Vector Zero = new Vector(0, 0);

        public double X;
        public double Y;

        public Vector(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double DistanceTo(Vector pt)
        {
            var vx = pt.X - X;
            var vy = pt.Y - Y;

            return System.Math.Sqrt(vx * vx + vy * vy);
        }

        public double DistanceTo(double x, double y)
        {
            var vx = x - X;
            var vy = y - Y;

            return System.Math.Sqrt(vx * vx + vy * vy);
        }

        public double DotProduct(Vector pt)
        {
            return X * pt.X + Y * pt.Y;
        }

        public double Angle()
        {
            return OpenNest.Math.Angle.NormalizeRad(System.Math.Atan2(Y, X));
        }

        /// <summary>
        /// Returns the angle to the given point when the origin is this point.
        /// </summary>
        /// <param name="pt"></param>
        /// <returns></returns>
        public double AngleTo(Vector pt)
        {
            return (pt - this).Angle();
        }

        /// <summary>
        /// Returns the angle when the origin is set at the given point.
        /// </summary>
        /// <param name="pt"></param>
        /// <returns></returns>
        public double AngleFrom(Vector pt)
        {
            return (this - pt).Angle();
        }

        /// <summary>
        /// Returns the angle between this point and the given point.
        /// Source: http://math.stackexchange.com/questions/878785/how-to-find-an-angle-in-range0-360-between-2-vectors
        /// </summary>
        /// <param name="pt"></param>
        /// <returns></returns>
        public double AngleBetween(Vector pt)
        {
            var v1 = Normalize();
            var v2 = pt.Normalize();
            var dot = v1.X * v2.X + v1.Y + v2.Y;
            var det = v1.X * v2.X - v1.Y + v2.Y;

            return System.Math.Atan2(det, dot);
        }

        public static Vector operator +(Vector pt1, Vector pt2)
        {
            return new Vector(pt1.X + pt2.X, pt1.Y + pt2.Y);
        }

        public static Vector operator -(Vector pt1, Vector pt2)
        {
            return new Vector(pt1.X - pt2.X, pt1.Y - pt2.Y);
        }

        public static Vector operator -(Vector pt)
        {
            return new Vector(-pt.X, -pt.Y);
        }

        public static Vector operator *(Vector pt, double factor)
        {
            return new Vector(pt.X * factor, pt.Y * factor);
        }

        public static Vector operator *(double factor, Vector pt)
        {
            return new Vector(pt.X * factor, pt.Y * factor);
        }

        public static Vector operator *(Vector pt, Vector factor)
        {
            return new Vector(pt.X * factor.X, pt.Y * factor.Y);
        }

        public static Vector operator /(Vector pt, double divisor)
        {
            return new Vector(pt.X / divisor, pt.Y / divisor);
        }

        public static bool operator ==(Vector pt1, Vector pt2)
        {
            return pt1.X.IsEqualTo(pt2.X) && pt1.Y.IsEqualTo(pt2.Y);
        }

        public static bool operator !=(Vector pt1, Vector pt2)
        {
            return !(pt1 == pt2);
        }

        /// <summary>
        /// Returns the unit vector equivalent to this point.
        /// </summary>
        /// <returns></returns>
        public Vector Normalize()
        {
            var d = DistanceTo(Vector.Zero);
            return new Vector(X / d, Y / d);
        }

        public Vector Rotate(double angle)
        {
            var v = new Vector();

            var cos = System.Math.Cos(angle);
            var sin = System.Math.Sin(angle);

            v.X = X * cos - Y * sin;
            v.Y = X * sin + Y * cos;

            return v;
        }

        public Vector Rotate(double angle, Vector origin)
        {
            var v = new Vector();
            var pt = this - origin;

            var cos = System.Math.Cos(angle);
            var sin = System.Math.Sin(angle);

            v.X = pt.X * cos - pt.Y * sin + origin.X;
            v.Y = pt.X * sin + pt.Y * cos + origin.Y;

            return v;
        }

        public Vector Offset(double x, double y)
        {
            return new Vector(X + x, Y + y);
        }

        public Vector Offset(Vector voffset)
        {
            return this + voffset;
        }

        public Vector Scale(double factor)
        {
            return new Vector(X * factor, Y * factor);
        }

        public Vector Scale(double factor, Vector origin)
        {
            return (this - origin) * factor + origin;
        }

        public Vector Clone()
        {
            return new Vector(X, Y);
        }

        public override bool Equals(object obj)
        {
            if (!(obj is Vector))
                return false;

            var pt = (Vector)obj;

            return (X.IsEqualTo(pt.X)) && (Y.IsEqualTo(pt.Y));
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public override string ToString()
        {
            return string.Format("[Vector: X:{0}, Y:{1}]", X, Y);
        }

        public bool IsValid()
        {
            return !double.IsNaN(X) && !double.IsNaN(Y);
        }
    }
}
