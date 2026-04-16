using OpenNest.Forms;
using OpenNest.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace OpenNest
{
    public static class ColorSchemeRegistry
    {
        private static readonly Dictionary<string, ColorScheme> builtIns =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Classic"] = BuildClassic(),
                ["Pastel"] = BuildPastel(),
                ["Dark"] = BuildDark()
            };

        private static List<ColorScheme> diskCache;

        public static IEnumerable<ColorScheme> AllSchemes
        {
            get
            {
                diskCache ??= LoadDiskSchemes().ToList();
                return builtIns.Values.Concat(diskCache);
            }
        }

        public static void Refresh() => diskCache = null;

        public static ColorScheme Get(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return builtIns["Classic"];

            var hit = AllSchemes.FirstOrDefault(
                s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            return hit ?? builtIns["Classic"];
        }

        public static void ApplyActiveFromSettings()
        {
            var name = Settings.Default.ActiveColorScheme;
            var scheme = Get(name);
            Apply(scheme);
        }

        public static void Apply(ColorScheme scheme)
        {
            var d = ColorScheme.Default;
            d.Name = scheme.Name;
            d.BackgroundColor = scheme.BackgroundColor;
            d.LayoutOutlineColor = scheme.LayoutOutlineColor;
            d.LayoutFillColor = scheme.LayoutFillColor;
            d.BoundingBoxColor = scheme.BoundingBoxColor;
            d.RapidColor = scheme.RapidColor;
            d.OriginColor = scheme.OriginColor;
            d.EdgeSpacingColor = scheme.EdgeSpacingColor;
            d.PreviewPartColor = scheme.PreviewPartColor;
            d.PartColors = scheme.PartColors;

            Drawing.PartColors = scheme.PartColors;

            RecolorOpenNests(scheme.PartColors);
        }

        private static void RecolorOpenNests(Color[] palette)
        {
            foreach (Form f in Application.OpenForms)
            {
                if (f is not EditNestForm enf)
                    continue;

                var i = 0;
                foreach (var drawing in enf.Nest.Drawings)
                {
                    if (drawing.IsCutOff)
                        continue;
                    drawing.Color = palette[i % palette.Length];
                    i++;
                }
            }
        }

        private static IEnumerable<ColorScheme> LoadDiskSchemes()
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Schemes");
            if (!Directory.Exists(dir))
                yield break;

            foreach (var path in Directory.GetFiles(dir, "*.json"))
            {
                ColorScheme scheme;
                try
                {
                    scheme = ColorSchemeSerializer.Deserialize(File.ReadAllText(path));
                }
                catch
                {
                    continue;
                }
                if (!builtIns.ContainsKey(scheme.Name))
                    yield return scheme;
            }
        }

        private static ColorScheme BuildClassic() => new ColorScheme
        {
            Name = "Classic",
            BackgroundColor = Color.DarkGray,
            LayoutOutlineColor = Color.Gray,
            LayoutFillColor = Color.WhiteSmoke,
            BoundingBoxColor = Color.FromArgb(128, 128, 255),
            RapidColor = Color.DodgerBlue,
            OriginColor = Color.Gray,
            EdgeSpacingColor = Color.FromArgb(180, 180, 180),
            PreviewPartColor = Color.FromArgb(255, 140, 0),
            PartColors = new[]
            {
                Color.FromArgb(205, 92, 92),
                Color.FromArgb(148, 103, 189),
                Color.FromArgb(75, 180, 175),
                Color.FromArgb(210, 190, 75),
                Color.FromArgb(190, 85, 175),
                Color.FromArgb(185, 115, 85),
                Color.FromArgb(120, 100, 190),
                Color.FromArgb(200, 100, 140),
                Color.FromArgb(80, 175, 155),
                Color.FromArgb(195, 160, 85),
                Color.FromArgb(175, 95, 160),
                Color.FromArgb(215, 130, 130),
            }
        };

        private static ColorScheme BuildPastel() => new ColorScheme
        {
            Name = "Pastel",
            BackgroundColor = Color.FromArgb(70, 75, 85),
            LayoutOutlineColor = Color.FromArgb(180, 180, 190),
            LayoutFillColor = Color.FromArgb(245, 245, 248),
            BoundingBoxColor = Color.FromArgb(128, 128, 255),
            RapidColor = Color.DodgerBlue,
            OriginColor = Color.FromArgb(160, 160, 160),
            EdgeSpacingColor = Color.FromArgb(200, 200, 210),
            PreviewPartColor = Color.FromArgb(255, 140, 0),
            PartColors = new[]
            {
                Color.FromArgb(122, 179, 209), Color.FromArgb(254, 229, 174),
                Color.FromArgb(143, 177, 229), Color.FromArgb(167, 172, 227),
                Color.FromArgb(216, 249, 195), Color.FromArgb(209, 168, 216),
                Color.FromArgb(222, 157, 190), Color.FromArgb(176, 255, 240),
                Color.FromArgb(235, 205, 153), Color.FromArgb(177, 225, 180),
                Color.FromArgb(125, 202, 241), Color.FromArgb(187, 206, 151),
                Color.FromArgb(251, 175, 190), Color.FromArgb(129, 226, 227),
                Color.FromArgb(255, 253, 207), Color.FromArgb(235, 205, 255),
                Color.FromArgb(255, 197, 168), Color.FromArgb(116, 213, 234),
                Color.FromArgb(190, 169, 122), Color.FromArgb(213, 159, 135),
                Color.FromArgb(124, 184, 155), Color.FromArgb(255, 189, 214),
                Color.FromArgb(146, 222, 255), Color.FromArgb(177, 173, 125),
                Color.FromArgb(177, 166, 202), Color.FromArgb(197, 208, 255),
                Color.FromArgb(255, 209, 243), Color.FromArgb(210, 255, 237),
                Color.FromArgb(255, 237, 204), Color.FromArgb(167, 233, 255),
                Color.FromArgb(182, 220, 255), Color.FromArgb(159, 177, 142),
                Color.FromArgb(190, 248, 255), Color.FromArgb(187, 169, 136),
                Color.FromArgb(199, 162, 168), Color.FromArgb(250, 255, 239),
                Color.FromArgb(222, 233, 255), Color.FromArgb(255, 234, 225),
                Color.FromArgb(240, 249, 255), Color.FromArgb(152, 176, 176),
            }
        };

        private static ColorScheme BuildDark() => new ColorScheme
        {
            Name = "Dark",
            BackgroundColor = Color.FromArgb(30, 30, 34),
            LayoutOutlineColor = Color.FromArgb(90, 90, 95),
            LayoutFillColor = Color.FromArgb(50, 50, 55),
            BoundingBoxColor = Color.FromArgb(100, 160, 220),
            RapidColor = Color.FromArgb(255, 200, 50),
            OriginColor = Color.FromArgb(120, 120, 130),
            EdgeSpacingColor = Color.FromArgb(90, 90, 100),
            PreviewPartColor = Color.FromArgb(255, 170, 60),
            PartColors = new[]
            {
                Color.FromArgb(255, 85, 85),   // Neon Red
                Color.FromArgb(80, 220, 255),  // Electric Cyan
                Color.FromArgb(255, 200, 50),  // Amber
                Color.FromArgb(130, 255, 130), // Lime Green
                Color.FromArgb(255, 130, 220), // Hot Pink
                Color.FromArgb(255, 165, 70),  // Tangerine
                Color.FromArgb(100, 180, 255), // Sky Blue
                Color.FromArgb(200, 160, 255), // Lavender
                Color.FromArgb(50, 230, 180),  // Mint
                Color.FromArgb(255, 255, 100), // Lemon
                Color.FromArgb(255, 120, 120), // Salmon
                Color.FromArgb(140, 230, 255), // Ice Blue
            }
        };
    }
}
