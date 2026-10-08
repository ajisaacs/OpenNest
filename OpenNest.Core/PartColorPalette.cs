using System;
using System.Drawing;

namespace OpenNest
{
    /// <summary>
    /// Drawing colors: golden-angle hue walk (skipping the etch-green band)
    /// cycled through eight saturation/lightness tiers for wide separation.
    /// </summary>
    public static class PartColorPalette
    {
        // (lightness, saturation) tiers cycled on an irrational stride so any
        // window of neighboring parts spans clearly different tones.
        private static readonly (double Lightness, double Saturation)[] Tiers =
        {
            (0.52, 0.68), (0.64, 0.46), (0.75, 0.54), (0.61, 0.87),
            (0.79, 0.97), (0.43, 0.82), (0.65, 0.76), (0.48, 0.97),
        };

        // Plastic-number stride: tier(i) never falls into a short repeating phase with the hue walk.
        private const double TierStride = 0.7548776662466927;

        // Hues land in [0,95) + [170,295) mapped onto the golden-angle cycle,
        // keeping fills out of the bright-green etch band.
        private const double HueSpan = 295.0;
        private const double BandStart = 95.0;
        private const double BandWidth = 75.0;

        public static Color GoldenAngle(int index)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            var hue = index * 137.508 % HueSpan;
            if (hue >= BandStart)
                hue += BandWidth;
            var (lightness, saturation) = Tiers[
                unchecked((int)(Tiers.Length * (index * TierStride % 1.0)) % Tiers.Length)];

            var q = lightness < 0.5
                ? lightness * (1 + saturation)
                : lightness + saturation - lightness * saturation;
            var p = 2 * lightness - q;

            int Channel(double t)
            {
                t = (t % 1 + 1) % 1;
                var value = t < 1 / 6.0 ? p + (q - p) * 6 * t
                    : t < 0.5 ? q
                    : t < 2 / 3.0 ? p + (q - p) * (2 / 3.0 - t) * 6
                    : p;
                return (int)System.Math.Round(value * 255);
            }

            var h = hue / 360.0;
            return Color.FromArgb(Channel(h + 1 / 3.0), Channel(h), Channel(h - 1 / 3.0));
        }
    }
}
