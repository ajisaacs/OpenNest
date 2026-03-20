using OpenNest.Math;

namespace OpenNest.Geometry
{
    public class Box
    {
        public static readonly Box Empty = new Box();

        public Box()
            : this(0, 0, 0, 0)
        {
        }

        public Box(double x, double y, double w, double h)
        {
            Location = new Vector(x, y);
            Width = w;
            Length = h;
        }

        public Vector Location;

        public Vector Center
        {
            get { return new Vector(X + Width * 0.5, Y + Length * 0.5); }
        }

        public Size Size;

        public double X
        {
            get { return Location.X; }
            set { Location.X = value; }
        }

        public double Y
        {
            get { return Location.Y; }
            set { Location.Y = value; }
        }

        public double Width
        {
            get { return Size.Width; }
            set { Size.Width = value; }
        }

        public double Length
        {
            get { return Size.Length; }
            set { Size.Length = value; }
        }

        public void MoveTo(double x, double y)
        {
            X = x;
            Y = y;
        }

        public void MoveTo(Vector pt)
        {
            X = pt.X;
            Y = pt.Y;
        }

        public void Offset(double x, double y)
        {
            X += x;
            Y += y;
        }

        public void Offset(Vector voffset)
        {
            Location += voffset;
        }

        public Box Translate(double x, double y)
        {
            return new Box(X + x, Y + y, Width, Length);
        }

        public Box Translate(Vector offset)
        {
            return new Box(X + offset.X, Y + offset.Y, Width, Length);
        }

        public double Left
        {
            get { return X; }
        }

        public double Right
        {
            get { return X + Width; }
        }

        public double Top
        {
            get { return Y + Length; }
        }

        public double Bottom
        {
            get { return Y; }
        }

        public double Area()
        {
            return Width * Length;
        }

        public double Perimeter()
        {
            return Width * 2 + Length * 2;
        }

        public bool Intersects(Box box)
        {
            if (Left >= box.Right) return false;
            if (Right <= box.Left) return false;
            if (Top <= box.Bottom) return false;
            if (Bottom >= box.Top) return false;

            return true;
        }

        public bool Intersects(Box box, out Box intersectingBox)
        {
            if (!Intersects(box))
            {
                intersectingBox = Box.Empty;
                return false;
            }

            var left = Left < box.Left ? box.Left : Left;
            var right = Right < box.Right ? Right : box.Right;

            var bottom = Bottom < box.Bottom ? box.Bottom : Bottom;
            var top = Top < box.Top ? Top : box.Top;

            intersectingBox = new Box(left, bottom, right - left, top - bottom);

            return true;
        }

        public bool Contains(Box box)
        {
            if (box.Top > Top) return false;
            if (box.Left < Left) return false;
            if (box.Right > Right) return false;
            if (box.Bottom < Bottom) return false;

            return true;
        }

        public bool Contains(Vector pt)
        {
            return pt.X >= Left - Tolerance.Epsilon && pt.X <= Right + Tolerance.Epsilon
                && pt.Y >= Bottom - Tolerance.Epsilon && pt.Y <= Top + Tolerance.Epsilon;
        }

        public bool IsHorizontalTo(Box box)
        {
            return Bottom > box.Top || Top < box.Bottom;
        }

        public bool IsHorizontalTo(Box box, out RelativePosition pos)
        {
            if (Bottom >= box.Top || Top <= box.Bottom)
            {
                pos = RelativePosition.None;
                return false;
            }

            if (Left >= box.Right)
                pos = RelativePosition.Right;
            else if (Right <= box.Left)
                pos = RelativePosition.Left;
            else
                pos = RelativePosition.Intersecting;

            return true;
        }

        public bool IsVerticalTo(Box box)
        {
            return Left > box.Right || Right < box.Left;
        }

        public bool IsVerticalTo(Box box, out RelativePosition pos)
        {
            if (Left >= box.Right || Right <= box.Left)
            {
                pos = RelativePosition.None;
                return false;
            }

            if (Bottom >= box.Top)
                pos = RelativePosition.Top;
            else if (Top <= box.Bottom)
                pos = RelativePosition.Bottom;
            else
                pos = RelativePosition.Intersecting;

            return true;
        }

        public Box Offset(double d)
        {
            return new Box(X - d, Y - d, Width + d * 2, Length + d * 2);
        }

        public override string ToString()
        {
            return string.Format("[Box: X={0}, Y={1}, Width={2}, Length={3}]", X, Y, Width, Length);
        }
    }
}
