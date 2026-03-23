namespace OpenNest.Posts.Cincinnati
{
    public sealed class SpeedClassifier
    {
        public double FastThreshold { get; set; } = 0.5;
        public double SlowThreshold { get; set; } = 0.1;

        public string Classify(double contourLength, double sheetDiagonal)
        {
            var ratio = contourLength / sheetDiagonal;
            if (ratio > FastThreshold) return "FAST";
            if (ratio <= SlowThreshold) return "SLOW";
            return "MEDIUM";
        }

        public string FormatCutDist(double contourLength, double sheetDiagonal)
        {
            return $"CutDist={FormatValue(contourLength)}/{FormatValue(sheetDiagonal)}";
        }

        private static string FormatValue(double value)
        {
            // Cincinnati convention: no leading zero for values < 1 (e.g., ".8702" not "0.8702")
            var rounded = System.Math.Round(value, 4);
            var str = rounded.ToString("0.####");
            if (rounded > 0 && rounded < 1 && str.StartsWith("0."))
                return str.Substring(1);
            return str;
        }
    }
}
