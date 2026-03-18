
namespace OpenNest
{
    public enum Units
    {
        Inches,
        Millimeters
    }

    public static class UnitsHelper
    {
        public static string GetShortString(Units units)
        {
            switch (units)
            {
                case Units.Inches:
                    return "in";

                case Units.Millimeters:
                    return "mm";

                default:
                    return string.Empty;
            }
        }

        public static string GetLongString(Units units)
        {
            switch (units)
            {
                case Units.Inches:
                    return "inches";

                case Units.Millimeters:
                    return "millimeters";

                default:
                    return string.Empty;
            }
        }

        public static string GetShortTimeUnit(Units units)
        {
            switch (units)
            {
                case Units.Inches:
                    return "min";

                case Units.Millimeters:
                    return "sec";

                default:
                    return string.Empty;
            }
        }

        public static string GetLongTimeUnit(Units units)
        {
            switch (units)
            {
                case Units.Inches:
                    return "minute";

                case Units.Millimeters:
                    return "second";

                default:
                    return string.Empty;
            }
        }

        public static string GetShortTimeUnitPair(Units units)
        {
            return GetShortString(units) + "/" + GetShortTimeUnit(units);
        }

        public static string GetLongTimeUnitPair(Units units)
        {
            return GetLongString(units) + "/" + GetLongTimeUnit(units);
        }
    }
}
