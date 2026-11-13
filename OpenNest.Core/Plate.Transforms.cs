using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
{
    public partial class Plate
    {
        /// <summary>
        /// Rotates the plate clockwise or counter-clockwise along with all parts.
        /// </summary>
        /// <param name="rotationDirection"></param>
        /// <param name="keepSameQuadrant"></param>
        public void Rotate90(RotationType rotationDirection, bool keepSameQuadrant = true)
        {
            const double oneAndHalfPI = System.Math.PI * 1.5;

            var turned = Outline?.Rotated90(rotationDirection);
            Size = new Size(Size.Length, Size.Width);
            Outline = turned;

            // After Size swap above, new Size.Width = old Length (old X extent),
            // new Size.Length = old Width (old Y extent).
            // Convention: Length = X axis, Width = Y axis.
            if (rotationDirection == RotationType.CW)
            {
                Rotate(oneAndHalfPI);

                if (keepSameQuadrant)
                {
                    switch (Quadrant)
                    {
                        case 1:
                            Offset(0, Size.Width);
                            break;

                        case 2:
                            Offset(-Size.Length, 0);
                            break;

                        case 3:
                            Offset(0, -Size.Width);
                            break;

                        case 4:
                            Offset(Size.Length, 0);
                            break;

                        default:
                            return;
                    }
                }
                else
                {
                    Quadrant = Quadrant < 2 ? 4 : Quadrant - 1;
                }
            }
            else
            {
                Rotate(Angle.HalfPI);

                if (keepSameQuadrant)
                {
                    switch (Quadrant)
                    {
                        case 1:
                            Offset(Size.Length, 0);
                            break;

                        case 2:
                            Offset(0, Size.Width);
                            break;

                        case 3:
                            Offset(-Size.Length, 0);
                            break;

                        case 4:
                            Offset(0, -Size.Width);
                            break;

                        default:
                            return;
                    }
                }
                else
                {
                    Quadrant = Quadrant > 3 ? 1 : Quadrant + 1;
                }
            }
        }

        /// <summary>
        /// Rotates the plate 180 degrees along with all parts.
        /// </summary>
        /// <param name="keepSameQuadrant"></param>
        public void Rotate180(bool keepSameQuadrant = true)
        {
            if (keepSameQuadrant)
            {
                Vector centerpt;

                switch (Quadrant)
                {
                    case 1:
                        centerpt = new Vector(Size.Length * 0.5, Size.Width * 0.5);
                        break;

                    case 2:
                        centerpt = new Vector(-Size.Length * 0.5, Size.Width * 0.5);
                        break;

                    case 3:
                        centerpt = new Vector(-Size.Length * 0.5, -Size.Width * 0.5);
                        break;

                    case 4:
                        centerpt = new Vector(Size.Length * 0.5, -Size.Width * 0.5);
                        break;

                    default:
                        return;
                }

                Rotate(System.Math.PI, centerpt);
                Outline = Outline?.Rotated180();
            }
            else
            {
                Rotate(System.Math.PI);
                Outline = Outline?.Rotated180();
                Quadrant = (Quadrant + 2) % 4;

                if (Quadrant == 0)
                    Quadrant = 4;
            }
        }

        /// <summary>
        /// Rotates the parts on the plate.
        /// </summary>
        /// <param name="angle"></param>
        public void Rotate(double angle)
        {
            for (var i = Parts.Count - 1; i >= 0; i--)
            {
                if (Parts[i].BaseDrawing.IsCutOff)
                    Parts.RemoveAt(i);
            }

            for (var i = 0; i < Parts.Count; ++i)
            {
                var part = Parts[i];
                part.Rotate(angle);
            }

            foreach (var cutoff in CutOffs)
                cutoff.Position = cutoff.Position.Rotate(angle);
        }

        /// <summary>
        /// Rotates the parts on the plate around the specified origin.
        /// </summary>
        /// <param name="angle"></param>
        /// <param name="origin"></param>
        public void Rotate(double angle, Vector origin)
        {
            for (var i = Parts.Count - 1; i >= 0; i--)
            {
                if (Parts[i].BaseDrawing.IsCutOff)
                    Parts.RemoveAt(i);
            }

            for (var i = 0; i < Parts.Count; ++i)
            {
                var part = Parts[i];
                part.Rotate(angle, origin);
            }

            foreach (var cutoff in CutOffs)
            {
                var pos = cutoff.Position - origin;
                pos = pos.Rotate(angle);
                cutoff.Position = pos + origin;
            }
        }

        /// <summary>
        /// Offsets the parts on the plate.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public void Offset(double x, double y)
        {
            // Remove cut-off parts before transforming
            for (var i = Parts.Count - 1; i >= 0; i--)
            {
                if (Parts[i].BaseDrawing.IsCutOff)
                    Parts.RemoveAt(i);
            }

            for (var i = 0; i < Parts.Count; ++i)
            {
                var part = Parts[i];
                part.Offset(x, y);
            }

            // Transform cut-off positions
            foreach (var cutoff in CutOffs)
                cutoff.Position = new Vector(cutoff.Position.X + x, cutoff.Position.Y + y);
        }

        /// <summary>
        /// Offsets the parts on the plate.
        /// </summary>
        /// <param name="voffset"></param>
        public void Offset(Vector voffset)
        {
            for (var i = Parts.Count - 1; i >= 0; i--)
            {
                if (Parts[i].BaseDrawing.IsCutOff)
                    Parts.RemoveAt(i);
            }

            for (var i = 0; i < Parts.Count; ++i)
            {
                var part = Parts[i];
                part.Offset(voffset);
            }

            foreach (var cutoff in CutOffs)
                cutoff.Position = new Vector(
                    cutoff.Position.X + voffset.X,
                    cutoff.Position.Y + voffset.Y
                );
        }
    }
}
