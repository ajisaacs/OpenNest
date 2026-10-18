using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Forms;
using OpenNest.Properties;

namespace OpenNest
{
    public static class ColorSchemeRegistry
    {
        private static readonly Dictionary<string, ColorScheme> builtIns = new(
            StringComparer.OrdinalIgnoreCase
        )
        {
            ["Classic"] = BuildClassic(),
            ["Pastel"] = BuildPastel(),
            ["Dark"] = BuildDark(),
            ["Workshop"] = BuildWorkshop(),
            ["Halloween"] = BuildHalloween(),
            ["Christmas"] = BuildChristmas(),
            ["4th of July"] = BuildFourthOfJuly(),
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

            var hit = AllSchemes.FirstOrDefault(s =>
                string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)
            );
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
            d.UseGoldenAngleColors = scheme.UseGoldenAngleColors;
            d.PartOutlineColor = scheme.PartOutlineColor;
            d.SelectedPartColor = scheme.SelectedPartColor;
            d.EtchColor = scheme.EtchColor;

            Drawing.PartColors = scheme.PartColors;
            Drawing.UseGoldenAngleColors = scheme.UseGoldenAngleColors;

            RecolorOpenNests();
        }

        private static void RecolorOpenNests()
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
                    drawing.Color = Drawing.GetPartColor(i);
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

        private static ColorScheme BuildWorkshop() =>
            new ColorScheme
            {
                Name = "Workshop",
                BackgroundColor = Color.FromArgb(205, 208, 212),
                LayoutOutlineColor = Color.FromArgb(145, 150, 156),
                LayoutFillColor = Color.FromArgb(242, 242, 242),
                BoundingBoxColor = Color.FromArgb(95, 125, 155),
                RapidColor = Color.FromArgb(65, 115, 165),
                OriginColor = Color.FromArgb(110, 115, 120),
                EdgeSpacingColor = Color.FromArgb(185, 190, 196),
                PreviewPartColor = Color.FromArgb(100, 145, 180),
                PartOutlineColor = Color.FromArgb(48, 48, 48),
                EtchColor = Color.Lime,
                UseGoldenAngleColors = true,
                // Samples for previews/legacy consumers; assignment uses the full index.
                PartColors = Enumerable.Range(0, 12).Select(PartColorPalette.GoldenAngle).ToArray(),
            };

        // Festive schemes theme the background, sheet and accents; their part palettes
        // stay varied and light enough for black part IDs, and avoid the etch green.
        private static ColorScheme BuildHalloween() =>
            new ColorScheme
            {
                Name = "Halloween",
                BackgroundColor = Color.FromArgb(28, 18, 36),
                LayoutOutlineColor = Color.FromArgb(110, 80, 130),
                LayoutFillColor = Color.FromArgb(52, 48, 58),
                BoundingBoxColor = Color.FromArgb(170, 120, 230),
                RapidColor = Color.FromArgb(255, 140, 0),
                OriginColor = Color.FromArgb(130, 110, 140),
                EdgeSpacingColor = Color.FromArgb(80, 70, 92),
                PreviewPartColor = Color.FromArgb(160, 255, 80),
                PartColors = new[]
                {
                    Color.FromArgb(255, 130, 30), // Pumpkin
                    Color.FromArgb(170, 110, 230), // Witch Purple
                    Color.FromArgb(255, 205, 60), // Candy Corn
                    Color.FromArgb(235, 235, 245), // Ghost
                    Color.FromArgb(220, 60, 60), // Blood Red
                    Color.FromArgb(130, 170, 230), // Moonlight
                    Color.FromArgb(225, 210, 170), // Bone
                    Color.FromArgb(230, 90, 200), // Potion
                    Color.FromArgb(60, 200, 180), // Cauldron Teal
                    Color.FromArgb(200, 120, 60), // Caramel Apple
                    Color.FromArgb(200, 170, 240), // Phantom Lavender
                    Color.FromArgb(255, 90, 40), // Ember
                },
            };

        private static ColorScheme BuildChristmas() =>
            new ColorScheme
            {
                Name = "Christmas",
                BackgroundColor = Color.FromArgb(18, 58, 38),
                LayoutOutlineColor = Color.FromArgb(190, 160, 80),
                LayoutFillColor = Color.FromArgb(248, 248, 244),
                BoundingBoxColor = Color.FromArgb(200, 40, 50),
                RapidColor = Color.FromArgb(200, 150, 30),
                OriginColor = Color.FromArgb(150, 130, 90),
                EdgeSpacingColor = Color.FromArgb(215, 220, 215),
                PreviewPartColor = Color.FromArgb(230, 60, 60),
                PartColors = new[]
                {
                    Color.FromArgb(215, 60, 60), // Holly Red
                    Color.FromArgb(80, 160, 90), // Pine
                    Color.FromArgb(230, 190, 70), // Gold
                    Color.FromArgb(150, 200, 235), // Ice
                    Color.FromArgb(190, 195, 205), // Silver
                    Color.FromArgb(240, 150, 160), // Candy Cane
                    Color.FromArgb(175, 120, 200), // Sugarplum
                    Color.FromArgb(150, 215, 170), // Mint
                    Color.FromArgb(190, 110, 70), // Cinnamon
                    Color.FromArgb(180, 50, 80), // Cranberry
                    Color.FromArgb(245, 225, 160), // Eggnog
                    Color.FromArgb(100, 150, 200), // Winter Sky
                },
            };

        private static ColorScheme BuildFourthOfJuly() =>
            new ColorScheme
            {
                Name = "4th of July",
                BackgroundColor = Color.FromArgb(18, 28, 60),
                LayoutOutlineColor = Color.FromArgb(110, 120, 150),
                LayoutFillColor = Color.FromArgb(250, 250, 252),
                BoundingBoxColor = Color.FromArgb(120, 150, 230),
                RapidColor = Color.FromArgb(210, 40, 50),
                OriginColor = Color.FromArgb(120, 130, 160),
                EdgeSpacingColor = Color.FromArgb(205, 210, 225),
                PreviewPartColor = Color.FromArgb(255, 190, 40),
                PartColors = new[]
                {
                    Color.FromArgb(220, 60, 60), // Old Glory Red
                    Color.FromArgb(110, 160, 230), // Liberty Blue
                    Color.FromArgb(250, 195, 60), // Firework Gold
                    Color.FromArgb(200, 205, 215), // Star Silver
                    Color.FromArgb(220, 90, 180), // Magenta Burst
                    Color.FromArgb(70, 190, 180), // Teal Burst
                    Color.FromArgb(240, 120, 110), // Coral
                    Color.FromArgb(150, 200, 240), // Sky
                    Color.FromArgb(250, 150, 60), // Sparkler
                    Color.FromArgb(170, 150, 230), // Twilight
                    Color.FromArgb(235, 150, 170), // Rose
                    Color.FromArgb(90, 120, 210), // Royal Blue
                },
            };

        private static ColorScheme BuildClassic() =>
            new ColorScheme
            {
                Name = "Classic",
                BackgroundColor = Color.FromArgb(104, 108, 114),
                LayoutOutlineColor = Color.Gray,
                LayoutFillColor = Color.WhiteSmoke,
                BoundingBoxColor = Color.FromArgb(128, 128, 255),
                RapidColor = Color.DodgerBlue,
                OriginColor = Color.Gray,
                EdgeSpacingColor = Color.FromArgb(180, 180, 180),
                PreviewPartColor = Color.FromArgb(255, 140, 0),
                // Twelve fills at least ~35 CIE76 apart from each other (the old set had near-duplicate
                // teals and purples under 10), each readable with black part IDs and clear of etch green.
                // Ordered so neighboring drawings contrast.
                PartColors = new[]
                {
                    Color.FromArgb(206, 153, 85),
                    Color.FromArgb(196, 66, 221),
                    Color.FromArgb(196, 220, 67),
                    Color.FromArgb(104, 113, 230),
                    Color.FromArgb(62, 223, 145),
                    Color.FromArgb(223, 67, 118),
                    Color.FromArgb(135, 231, 224),
                    Color.FromArgb(219, 91, 64),
                    Color.FromArgb(82, 166, 223),
                    Color.FromArgb(194, 217, 148),
                    Color.FromArgb(212, 147, 220),
                    Color.FromArgb(204, 132, 135),
                },
            };

        private static ColorScheme BuildPastel() =>
            new ColorScheme
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
                    Color.FromArgb(122, 179, 209),
                    Color.FromArgb(254, 229, 174),
                    Color.FromArgb(143, 177, 229),
                    Color.FromArgb(167, 172, 227),
                    Color.FromArgb(216, 249, 195),
                    Color.FromArgb(209, 168, 216),
                    Color.FromArgb(222, 157, 190),
                    Color.FromArgb(176, 255, 240),
                    Color.FromArgb(235, 205, 153),
                    Color.FromArgb(177, 225, 180),
                    Color.FromArgb(125, 202, 241),
                    Color.FromArgb(187, 206, 151),
                    Color.FromArgb(251, 175, 190),
                    Color.FromArgb(129, 226, 227),
                    Color.FromArgb(255, 253, 207),
                    Color.FromArgb(235, 205, 255),
                    Color.FromArgb(255, 197, 168),
                    Color.FromArgb(116, 213, 234),
                    Color.FromArgb(190, 169, 122),
                    Color.FromArgb(213, 159, 135),
                    Color.FromArgb(124, 184, 155),
                    Color.FromArgb(255, 189, 214),
                    Color.FromArgb(146, 222, 255),
                    Color.FromArgb(177, 173, 125),
                    Color.FromArgb(177, 166, 202),
                    Color.FromArgb(197, 208, 255),
                    Color.FromArgb(255, 209, 243),
                    Color.FromArgb(210, 255, 237),
                    Color.FromArgb(255, 237, 204),
                    Color.FromArgb(167, 233, 255),
                    Color.FromArgb(182, 220, 255),
                    Color.FromArgb(159, 177, 142),
                    Color.FromArgb(190, 248, 255),
                    Color.FromArgb(187, 169, 136),
                    Color.FromArgb(199, 162, 168),
                    Color.FromArgb(250, 255, 239),
                    Color.FromArgb(222, 233, 255),
                    Color.FromArgb(255, 234, 225),
                    Color.FromArgb(240, 249, 255),
                    Color.FromArgb(152, 176, 176),
                },
            };

        private static ColorScheme BuildDark() =>
            new ColorScheme
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
                    Color.FromArgb(255, 85, 85), // Neon Red
                    Color.FromArgb(80, 220, 255), // Electric Cyan
                    Color.FromArgb(255, 200, 50), // Amber
                    Color.FromArgb(130, 255, 130), // Lime Green
                    Color.FromArgb(255, 130, 220), // Hot Pink
                    Color.FromArgb(255, 165, 70), // Tangerine
                    Color.FromArgb(100, 180, 255), // Sky Blue
                    Color.FromArgb(200, 160, 255), // Lavender
                    Color.FromArgb(50, 230, 180), // Mint
                    Color.FromArgb(255, 255, 100), // Lemon
                    Color.FromArgb(255, 120, 120), // Salmon
                    Color.FromArgb(140, 230, 255), // Ice Blue
                },
            };
    }
}
