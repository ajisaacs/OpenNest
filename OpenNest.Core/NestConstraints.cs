using System;

namespace OpenNest
{
    public class NestConstraints
    {
        /// <summary>
        /// The rotation step in radians.
        /// </summary>
        public double StepAngle { get; set; }

        /// <summary>
        /// The rotation start angle in radians.
        /// </summary>
        public double StartAngle { get; set; }

        /// <summary>
        /// The rotation end angle in radians.
        /// </summary>
        public double EndAngle { get; set; }

        public bool Allow180Equivalent { get; set; }

        /// <summary>
        /// Sets the StartAngle and EndAngle to allow 360 degree rotation.
        /// </summary>
        public void AllowAnyRotation()
        {
            StartAngle = 0;
            EndAngle = Angle.TwoPI;
        }

        public bool HasLimitedRotation()
        {
            var diff = EndAngle - StartAngle;

            if (diff.IsEqualTo(Angle.TwoPI))
                return false;

            if ((diff > Math.PI || diff.IsEqualTo(Math.PI)) && Allow180Equivalent)
                return false;

            return true;
        }
    }
}
