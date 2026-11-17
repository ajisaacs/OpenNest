namespace OpenNest.Bending
{
    public static class BendMath
    {
        /// <summary>
        /// The length of the neutral axis through a bend: the arc at the inside radius plus
        /// <paramref name="kFactor"/> of the thickness.
        /// </summary>
        /// <param name="angle">The bend angle in radians.</param>
        public static double Allowance(double angle, double insideRadius, double thickness, double kFactor) =>
            angle * (insideRadius + kFactor * thickness);
    }
}
