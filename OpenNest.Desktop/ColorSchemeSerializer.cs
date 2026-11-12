using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace OpenNest
{
    public static class ColorSchemeSerializer
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public static string Serialize(ColorScheme scheme)
        {
            var dto = new ColorSchemeDto
            {
                Name = scheme.Name,
                BackgroundColor = ToHex(scheme.BackgroundColor),
                LayoutOutlineColor = ToHex(scheme.LayoutOutlineColor),
                LayoutFillColor = ToHex(scheme.LayoutFillColor),
                BoundingBoxColor = ToHex(scheme.BoundingBoxColor),
                RapidColor = ToHex(scheme.RapidColor),
                OriginColor = ToHex(scheme.OriginColor),
                EdgeSpacingColor = ToHex(scheme.EdgeSpacingColor),
                PreviewPartColor = ToHex(scheme.PreviewPartColor),
                PartOutlineColor = scheme.PartOutlineColor.IsEmpty ? null : ToHex(scheme.PartOutlineColor),
                SelectedPartColor = scheme.SelectedPartColor.IsEmpty ? null : ToHex(scheme.SelectedPartColor),
                EtchColor = ToHex(scheme.EtchColor),
                LibraryBackgroundColor = ToHex(scheme.LibraryBackgroundColor),
                LibraryFillColor = ToHex(scheme.LibraryFillColor),
                LibraryOutlineColor = ToHex(scheme.LibraryOutlineColor),
                LibraryEtchColor = ToHex(scheme.LibraryEtchColor),
                LibraryBendColor = ToHex(scheme.LibraryBendColor),
                PartColors = scheme.PartColors.Select(ToHex).ToArray(),
                UseGoldenAngleColors = scheme.UseGoldenAngleColors,
            };
            return JsonSerializer.Serialize(dto, JsonOptions);
        }

        public static ColorScheme Deserialize(string json)
        {
            var dto =
                JsonSerializer.Deserialize<ColorSchemeDto>(json, JsonOptions)
                ?? throw new JsonException("ColorScheme JSON was null");

            var scheme = new ColorScheme
            {
                Name = dto.Name ?? "Unnamed",
                BackgroundColor = FromHex(dto.BackgroundColor),
                LayoutOutlineColor = FromHex(dto.LayoutOutlineColor),
                LayoutFillColor = FromHex(dto.LayoutFillColor),
                BoundingBoxColor = FromHex(dto.BoundingBoxColor),
                RapidColor = FromHex(dto.RapidColor),
                OriginColor = FromHex(dto.OriginColor),
                EdgeSpacingColor = FromHex(dto.EdgeSpacingColor),
                PreviewPartColor = FromHex(dto.PreviewPartColor),
                PartOutlineColor = string.IsNullOrWhiteSpace(dto.PartOutlineColor) ? Color.Empty : FromHex(dto.PartOutlineColor),
                SelectedPartColor = string.IsNullOrWhiteSpace(dto.SelectedPartColor) ? Color.Empty : FromHex(dto.SelectedPartColor),
                EtchColor = string.IsNullOrWhiteSpace(dto.EtchColor) ? Color.Lime : FromHex(dto.EtchColor),
                PartColors = (dto.PartColors ?? new string[0]).Select(FromHex).ToArray(),
                UseGoldenAngleColors = dto.UseGoldenAngleColors,
            };

            // Schemes saved before the library style keep its defaults.
            scheme.LibraryBackgroundColor = FromHexOr(dto.LibraryBackgroundColor, scheme.LibraryBackgroundColor);
            scheme.LibraryFillColor = FromHexOr(dto.LibraryFillColor, scheme.LibraryFillColor);
            scheme.LibraryOutlineColor = FromHexOr(dto.LibraryOutlineColor, scheme.LibraryOutlineColor);
            scheme.LibraryEtchColor = FromHexOr(dto.LibraryEtchColor, scheme.LibraryEtchColor);
            scheme.LibraryBendColor = FromHexOr(dto.LibraryBendColor, scheme.LibraryBendColor);
            return scheme;
        }

        private static Color FromHexOr(string hex, Color fallback) =>
            string.IsNullOrWhiteSpace(hex) ? fallback : FromHex(hex);

        private static string ToHex(Color c) =>
            "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");

        private static Color FromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return Color.Black;
            var h = hex.TrimStart('#');
            if (h.Length < 6)
                return Color.Black;
            var r = byte.Parse(
                h.Substring(0, 2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture
            );
            var g = byte.Parse(
                h.Substring(2, 2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture
            );
            var b = byte.Parse(
                h.Substring(4, 2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture
            );
            return Color.FromArgb(r, g, b);
        }

        private class ColorSchemeDto
        {
            public string Name { get; set; }
            public string BackgroundColor { get; set; }
            public string LayoutOutlineColor { get; set; }
            public string LayoutFillColor { get; set; }
            public string BoundingBoxColor { get; set; }
            public string RapidColor { get; set; }
            public string OriginColor { get; set; }
            public string EdgeSpacingColor { get; set; }
            public string PreviewPartColor { get; set; }
            public string PartOutlineColor { get; set; }
            public string SelectedPartColor { get; set; }
            public string EtchColor { get; set; }
            public string LibraryBackgroundColor { get; set; }
            public string LibraryFillColor { get; set; }
            public string LibraryOutlineColor { get; set; }
            public string LibraryEtchColor { get; set; }
            public string LibraryBendColor { get; set; }
            public string[] PartColors { get; set; }
            public bool UseGoldenAngleColors { get; set; }
        }
    }
}
