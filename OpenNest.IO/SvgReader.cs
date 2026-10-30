using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.IO
{
    /// <summary>
    /// Converts an SVG file's path geometry into entities: centerlines only, since stroke
    /// widths and fills do not describe a cut. Anything else is reported as a warning.
    /// </summary>
    internal sealed class SvgReader
    {
        private const double CssPixelsPerInch = 96;
        private const double MillimetersPerInch = 25.4;
        private const int MinCurveSegments = 8;
        private const int MaxCurveSegments = 512;

        // Bounded so a hostile file's entities cannot expand without limit.
        private const long MaxCharactersFromEntities = 1_000_000;

        private static readonly Regex Length = new Regex(
            @"^\s*([+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)\s*([a-zA-Z%]*)\s*$",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1)
        );

        private static readonly Regex HiddenStyle = new Regex(
            @"(?:^|;)\s*(?:display\s*:\s*none|visibility\s*:\s*(?:hidden|collapse))",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1)
        );

        private static readonly HashSet<string> IgnoredElements = new HashSet<string>
        {
            "clipPath", "defs", "desc", "filter", "linearGradient", "marker", "mask",
            "metadata", "pattern", "radialGradient", "script", "style", "symbol", "title",
        };

        private readonly XElement root;
        private readonly double tolerance;
        private readonly XNamespace svgNamespace;
        private readonly Dictionary<string, XElement> elementsById;
        private readonly Dictionary<string, Layer> layers = new Dictionary<string, Layer>(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, int> skipped = new Dictionary<string, int>();
        private readonly List<Line> lines = new List<Line>();
        private readonly List<Arc> arcs = new List<Arc>();
        private readonly List<Circle> circles = new List<Circle>();
        private readonly HashSet<XElement> activeUses = new HashSet<XElement>();

        private SvgReader(XElement root, double tolerance)
        {
            this.root = root;
            this.tolerance = tolerance;
            svgNamespace = root.Name.Namespace;
            elementsById = root
                .DescendantsAndSelf()
                .Where(e => e.Attribute("id") != null)
                .GroupBy(e => e.Attribute("id").Value)
                .ToDictionary(g => g.Key, g => g.First());
        }

        public static DxfImportResult Read(string path, CadImportOptions options)
        {
            var root = Load(path);
            var perInch = DrawingUnitsPerInch(options.DrawingUnits);
            var scale = DeclaredScale(root)
                ?? ExplicitScale(options.SvgUserUnitsPerInch)
                ?? throw new InvalidDataException(
                    $"\"{Path.GetFileName(path)}\" does not state its real-world size, "
                    + "so its scale would be a guess. Choose how many of its units make an inch."
                );

            var tolerance = options.DrawingUnits == Units.Millimeters ? 0.025 : 0.001;
            var reader = new SvgReader(root, tolerance);
            var transform = SvgTransform.Scale(scale.X * perInch, -scale.Y * perInch);
            return reader.Convert(transform);
        }

        public static (double Width, double Height)? UndeclaredSize(string path)
        {
            var root = Load(path);
            if (DeclaredScale(root) != null)
                return null;

            var entities = new SvgReader(root, 0.001)
                .Convert(SvgTransform.Scale(1, -1))
                .Entities;
            if (entities.Count == 0)
                return (0, 0);

            var bounds = entities.GetBoundingBox();
            return (bounds.Length, bounds.Width);
        }

        private static XElement Load(string path)
        {
            // Older Illustrator files declare their namespaces as DTD entities, so a DTD has to
            // parse; it just may not fetch anything or expand without limit.
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Parse,
                XmlResolver = null,
                MaxCharactersFromEntities = MaxCharactersFromEntities,
            };

            try
            {
                using var reader = XmlReader.Create(path, settings);
                var root = XDocument.Load(reader).Root;
                if (root?.Name.LocalName != "svg")
                    throw new InvalidDataException($"\"{Path.GetFileName(path)}\" is not an SVG file.");
                return root;
            }
            catch (XmlException ex)
            {
                throw new InvalidDataException(
                    $"\"{Path.GetFileName(path)}\" is not valid SVG: {ex.Message}",
                    ex
                );
            }
        }

        private static double DrawingUnitsPerInch(Units units) =>
            units == Units.Millimeters ? MillimetersPerInch : 1;

        private static Vector? ExplicitScale(double? userUnitsPerInch)
        {
            if (userUnitsPerInch == null)
                return null;
            if (!(userUnitsPerInch > 0) || double.IsInfinity(userUnitsPerInch.Value))
                throw new ArgumentOutOfRangeException(
                    nameof(userUnitsPerInch),
                    userUnitsPerInch,
                    "SVG units per inch must be positive."
                );
            return new Vector(1 / userUnitsPerInch.Value, 1 / userUnitsPerInch.Value);
        }

        /// <summary>
        /// Inches per user unit, from the root's width and height. Only absolute units count:
        /// "px" is left out because applications disagree on what it means (a CSS pixel is
        /// 1/96 in, but Illustrator writes px at 72 per inch).
        /// </summary>
        private static Vector? DeclaredScale(XElement root)
        {
            var width = PhysicalInches(root.Attribute("width")?.Value);
            var height = PhysicalInches(root.Attribute("height")?.Value);
            if (width == null && height == null)
                return null;

            var viewBox = ParseViewBox(root.Attribute("viewBox")?.Value);
            if (viewBox == null)
                return new Vector(1 / CssPixelsPerInch, 1 / CssPixelsPerInch);

            var scaleX = width / viewBox.Value.Width;
            var scaleY = height / viewBox.Value.Height;
            var aspect = root.Attribute("preserveAspectRatio")?.Value.Trim() ?? string.Empty;
            if (scaleX != null && scaleY != null && aspect.StartsWith("none", StringComparison.Ordinal))
                return new Vector(scaleX.Value, scaleY.Value);

            // The default aspect handling fits the view box uniformly inside the page.
            var scale = System.Math.Min(scaleX ?? double.MaxValue, scaleY ?? double.MaxValue);
            return new Vector(scale, scale);
        }

        private static double? PhysicalInches(string text)
        {
            if (text == null)
                return null;
            var match = Length.Match(text);
            if (!match.Success)
                return null;

            var value = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            if (value <= 0)
                return null;

            return match.Groups[2].Value switch
            {
                "in" => value,
                "cm" => value / 2.54,
                "mm" => value / MillimetersPerInch,
                "Q" => value / (MillimetersPerInch * 4),
                "pt" => value / 72,
                "pc" => value / 6,
                _ => null,
            };
        }

        private static (double Width, double Height)? ParseViewBox(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            var values = ParseNumbers(text);
            return values.Length == 4 && values[2] > 0 && values[3] > 0
                ? (values[2], values[3])
                : null;
        }

        internal static double[] ParseNumbers(string text)
        {
            var scanner = new PathScanner(text);
            var values = new List<double>();
            while (scanner.HasNumber)
                values.Add(scanner.ReadNumber());
            return values.ToArray();
        }

        private DxfImportResult Convert(SvgTransform transform)
        {
            WalkChildren(root, transform, Layer.Default);

            GeometryOptimizer.Optimize(lines);
            GeometryOptimizer.Optimize(arcs);
            GeometryOptimizer.Deduplicate(circles);
            GeometryOptimizer.Deduplicate(circles, arcs);

            var entities = new List<Entity>();
            entities.AddRange(circles);
            entities.AddRange(lines);
            entities.AddRange(arcs);

            return new DxfImportResult
            {
                Entities = entities,
                Warnings = skipped
                    .OrderBy(s => s.Key, StringComparer.Ordinal)
                    .Select(s => $"Skipped {s.Value} <{s.Key}> element(s); convert them to paths to import them.")
                    .ToList(),
            };
        }

        private void WalkChildren(XElement parent, SvgTransform transform, Layer layer)
        {
            foreach (var child in parent.Elements())
                Walk(child, transform, layer);
        }

        private void Walk(XElement element, SvgTransform parent, Layer parentLayer)
        {
            if (element.Name.Namespace != svgNamespace)
                return;

            var name = element.Name.LocalName;
            if (IgnoredElements.Contains(name) || IsHidden(element))
                return;

            var transform = parent.Then(SvgTransform.Parse(element.Attribute("transform")?.Value));
            switch (name)
            {
                case "g":
                case "a":
                case "switch":
                    var layer = LayerOf(element, parentLayer);
                    if (!IsSkippedLayer(layer.Name))
                        WalkChildren(element, transform, layer);
                    break;

                case "use":
                    WalkUse(element, transform, parentLayer);
                    break;

                case "path":
                    ReadPath(element.Attribute("d")?.Value, transform, parentLayer);
                    break;

                case "rect":
                    ReadShape(element, RectPath(element), transform, parentLayer);
                    break;

                case "line":
                    ReadShape(element, LinePath(element), transform, parentLayer);
                    break;

                case "polyline":
                    ReadPath(PointsPath(element, close: false), transform, parentLayer);
                    break;

                case "polygon":
                    ReadPath(PointsPath(element, close: true), transform, parentLayer);
                    break;

                case "circle":
                    ReadEllipse(element, Measure(element, "r"), Measure(element, "r"), transform, parentLayer);
                    break;

                case "ellipse":
                    ReadEllipse(element, Measure(element, "rx"), Measure(element, "ry"), transform, parentLayer);
                    break;

                default:
                    Skip(name);
                    break;
            }
        }

        private void Skip(string name)
        {
            skipped.TryGetValue(name, out var count);
            skipped[name] = count + 1;
        }

        private static bool IsHidden(XElement element)
        {
            var style = element.Attribute("style")?.Value;
            return element.Attribute("display")?.Value.Trim() == "none"
                || element.Attribute("visibility")?.Value.Trim() is "hidden" or "collapse"
                || (style != null && HiddenStyle.IsMatch(style));
        }

        /// <summary>
        /// A group names a layer through OpenNest's data-layer, an Inkscape layer label, or an
        /// id on a top-level group (how Illustrator writes its layers).
        /// </summary>
        private Layer LayerOf(XElement group, Layer parent)
        {
            var name = group.Attribute("data-layer")?.Value;
            name ??= group.Attributes().FirstOrDefault(a => a.Name.LocalName == "groupmode") != null
                ? group.Attributes().FirstOrDefault(a => a.Name.LocalName == "label")?.Value
                : null;
            if (name == null && group.Parent == root)
                name = group.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(name))
                return parent;

            if (!layers.TryGetValue(name, out var layer))
            {
                layer = new Layer(name) { Color = Color.White, IsVisible = true };
                layers.Add(name, layer);
            }

            return layer;
        }

        // Rapids and the plate outline are what OpenNest's own export adds around the part.
        private static bool IsSkippedLayer(string name) =>
            Dxf.IsNonCutLayer(name)
            || string.Equals(name, SpecialLayers.Rapid.Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, Svg.PlateLayerName, StringComparison.OrdinalIgnoreCase);

        private void WalkUse(XElement use, SvgTransform transform, Layer layer)
        {
            var href = use.Attributes().FirstOrDefault(a => a.Name.LocalName == "href")?.Value;
            if (href == null || !href.StartsWith('#')
                || !elementsById.TryGetValue(href[1..], out var target))
                return;

            if (target.Name.LocalName == "symbol" || !activeUses.Add(use))
            {
                Skip("use");
                return;
            }

            var placed = transform.Then(SvgTransform.Translate(
                Measure(use, "x") ?? 0,
                Measure(use, "y") ?? 0
            ));
            Walk(target, placed, layer);
            activeUses.Remove(use);
        }

        /// <summary>The attribute in user units, or null when missing or relative to the viewport.</summary>
        private static double? Measure(XElement element, string attribute)
        {
            var text = element.Attribute(attribute)?.Value;
            if (text == null)
                return null;
            var match = Length.Match(text);
            if (!match.Success)
                return null;

            var value = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            return match.Groups[2].Value switch
            {
                "" or "px" => value,
                "in" => value * CssPixelsPerInch,
                "cm" => value * CssPixelsPerInch / 2.54,
                "mm" => value * CssPixelsPerInch / MillimetersPerInch,
                "Q" => value * CssPixelsPerInch / (MillimetersPerInch * 4),
                "pt" => value * CssPixelsPerInch / 72,
                "pc" => value * CssPixelsPerInch / 6,
                _ => null,
            };
        }

        private static bool HasRelativeSize(XElement element, params string[] attributes) =>
            attributes.Any(a => element.Attribute(a) != null && Measure(element, a) == null);

        private void ReadShape(XElement element, string data, SvgTransform transform, Layer layer)
        {
            if (data == null)
                Skip(element.Name.LocalName);
            else
                ReadPath(data, transform, layer);
        }

        private static string RectPath(XElement rect)
        {
            if (HasRelativeSize(rect, "x", "y", "width", "height", "rx", "ry"))
                return null;

            var x = Measure(rect, "x") ?? 0;
            var y = Measure(rect, "y") ?? 0;
            var width = Measure(rect, "width") ?? 0;
            var height = Measure(rect, "height") ?? 0;
            if (width <= 0 || height <= 0)
                return string.Empty;

            var rx = Measure(rect, "rx") ?? Measure(rect, "ry") ?? 0;
            var ry = Measure(rect, "ry") ?? rx;
            rx = System.Math.Min(rx, width / 2);
            ry = System.Math.Min(ry, height / 2);

            var data = new StringBuilder();
            void Add(FormattableString text) => data.Append(FormattableString.Invariant(text));

            if (rx <= 0 || ry <= 0)
            {
                Add($"M{x:R} {y:R}H{x + width:R}V{y + height:R}H{x:R}Z");
                return data.ToString();
            }

            var corner = FormattableString.Invariant($"A{rx:R} {ry:R} 0 0 1 ");
            Add($"M{x + rx:R} {y:R}H{x + width - rx:R}");
            data.Append(corner);
            Add($"{x + width:R} {y + ry:R}V{y + height - ry:R}");
            data.Append(corner);
            Add($"{x + width - rx:R} {y + height:R}H{x + rx:R}");
            data.Append(corner);
            Add($"{x:R} {y + height - ry:R}V{y + ry:R}");
            data.Append(corner);
            Add($"{x + rx:R} {y:R}Z");
            return data.ToString();
        }

        private static string LinePath(XElement line)
        {
            if (HasRelativeSize(line, "x1", "y1", "x2", "y2"))
                return null;

            var x1 = Measure(line, "x1") ?? 0;
            var y1 = Measure(line, "y1") ?? 0;
            var x2 = Measure(line, "x2") ?? 0;
            var y2 = Measure(line, "y2") ?? 0;
            return FormattableString.Invariant($"M{x1:R} {y1:R}L{x2:R} {y2:R}");
        }

        private static string PointsPath(XElement element, bool close)
        {
            var points = element.Attribute("points")?.Value;
            if (string.IsNullOrWhiteSpace(points))
                return null;

            // Point lists use the same number grammar as path data.
            return "M" + points + (close ? "Z" : string.Empty);
        }

        private void ReadEllipse(
            XElement element,
            double? rx,
            double? ry,
            SvgTransform transform,
            Layer layer
        )
        {
            if (rx == null || ry == null || HasRelativeSize(element, "cx", "cy"))
            {
                Skip(element.Name.LocalName);
                return;
            }

            if (rx <= 0 || ry <= 0)
                return;

            var center = new Vector(Measure(element, "cx") ?? 0, Measure(element, "cy") ?? 0);
            if (rx.Value.IsEqualTo(ry.Value) && transform.IsSimilarity)
                AddCircle(transform.Apply(center), rx.Value * transform.ScaleFactor, layer);
            else
                AddEllipticalArc(center, rx.Value, ry.Value, 0, 0, Angle.TwoPI, transform, layer);
        }

        private void AddCircle(Vector center, double radius, Layer layer) =>
            circles.Add(Tag(new Circle(center, radius), layer));

        private void ReadPath(string data, SvgTransform t, Layer layer)
        {
            if (string.IsNullOrWhiteSpace(data))
                return;

            var scanner = new PathScanner(data);
            var current = Vector.Zero;
            var start = Vector.Zero;
            var control = Vector.Zero;
            var previous = '\0';
            var command = '\0';

            while (true)
            {
                if (!scanner.HasCommand && !scanner.HasNumber)
                    break;

                if (scanner.HasCommand)
                    command = scanner.ReadCommand();
                else if (command is '\0' or 'Z' or 'z')
                    throw scanner.Error();
                else if (command == 'M')
                    command = 'L';
                else if (command == 'm')
                    command = 'l';

                if (previous == '\0' && command is not ('M' or 'm'))
                    throw scanner.Error();

                var relative = char.IsLower(command);
                var origin = relative ? current : Vector.Zero;

                Vector Next() => scanner.ReadPoint() + origin;

                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                        current = start = Next();
                        break;

                    case 'Z':
                        AddLine(current, start, t, layer);
                        current = start;
                        break;

                    case 'L':
                        {
                            var end = Next();
                            AddLine(current, end, t, layer);
                            current = end;
                            break;
                        }

                    case 'H':
                        {
                            var end = new Vector(scanner.ReadNumber() + origin.X, current.Y);
                            AddLine(current, end, t, layer);
                            current = end;
                            break;
                        }

                    case 'V':
                        {
                            var end = new Vector(current.X, scanner.ReadNumber() + origin.Y);
                            AddLine(current, end, t, layer);
                            current = end;
                            break;
                        }

                    case 'C':
                        {
                            var c1 = Next();
                            control = Next();
                            var end = Next();
                            AddCubic(current, c1, control, end, t, layer);
                            current = end;
                            break;
                        }

                    case 'S':
                        {
                            var c1 = previous is 'C' or 'c' or 'S' or 's'
                                ? current * 2 - control
                                : current;
                            control = Next();
                            var end = Next();
                            AddCubic(current, c1, control, end, t, layer);
                            current = end;
                            break;
                        }

                    case 'Q':
                        {
                            control = Next();
                            var end = Next();
                            AddQuadratic(current, control, end, t, layer);
                            current = end;
                            break;
                        }

                    case 'T':
                        {
                            control = previous is 'Q' or 'q' or 'T' or 't'
                                ? current * 2 - control
                                : current;
                            var end = Next();
                            AddQuadratic(current, control, end, t, layer);
                            current = end;
                            break;
                        }

                    case 'A':
                        {
                            var rx = System.Math.Abs(scanner.ReadNumber());
                            var ry = System.Math.Abs(scanner.ReadNumber());
                            var rotation = scanner.ReadNumber() * System.Math.PI / 180;
                            var large = scanner.ReadFlag();
                            var sweep = scanner.ReadFlag();
                            var end = Next();
                            AddSvgArc(current, rx, ry, rotation, large, sweep, end, t, layer);
                            current = end;
                            break;
                        }

                    default:
                        throw scanner.Error();
                }

                previous = command;
            }
        }

        private void AddLine(Vector from, Vector to, SvgTransform t, Layer layer)
        {
            var start = t.Apply(from);
            var end = t.Apply(to);
            if (start.DistanceTo(end) >= Tolerance.Epsilon)
                lines.Add(Tag(new Line(start, end), layer));
        }

        private void AddQuadratic(Vector p0, Vector c, Vector p1, SvgTransform t, Layer layer) =>
            AddCubic(p0, p0 + (c - p0) * (2.0 / 3), p1 + (c - p1) * (2.0 / 3), p1, t, layer);

        /// <summary>
        /// Samples the curve within half the tolerance and fits arcs and lines through the
        /// samples within the other half, so the result stays within the tolerance.
        /// </summary>
        private void AddCubic(Vector p0, Vector c1, Vector c2, Vector p1, SvgTransform t, Layer layer)
        {
            var a = t.Apply(p0);
            var b = t.Apply(c1);
            var c = t.Apply(c2);
            var d = t.Apply(p1);

            var bend = System.Math.Max(Length2(a - b * 2 + c), Length2(b - c * 2 + d));
            if (bend < Tolerance.Epsilon)
            {
                AddLine(p0, p1, t, layer);
                return;
            }

            // A cubic's chord error over n segments is at most 6 * bend / (8 * n^2).
            var segments = (int)System.Math.Ceiling(System.Math.Sqrt(6 * bend / (8 * (tolerance / 2))));
            segments = System.Math.Clamp(segments, MinCurveSegments, MaxCurveSegments);

            var points = new List<Vector>(segments + 1) { a };
            for (var i = 1; i < segments; i++)
            {
                var u = (double)i / segments;
                var v = 1 - u;
                points.Add(
                    a * (v * v * v) + b * (3 * v * v * u) + c * (3 * v * u * u) + d * (u * u * u)
                );
            }
            points.Add(d);

            AddFitted(SplineConverter.Convert(points, false, tolerance / 2), layer);
        }

        private static double Length2(Vector v) => System.Math.Sqrt(v.X * v.X + v.Y * v.Y);

        /// <summary>The SVG endpoint-to-center arc conversion (SVG 2 appendix B.2.4).</summary>
        private void AddSvgArc(
            Vector from,
            double rx,
            double ry,
            double rotation,
            bool large,
            bool sweep,
            Vector to,
            SvgTransform t,
            Layer layer
        )
        {
            if (from.DistanceTo(to) < Tolerance.Epsilon)
                return;

            if (rx < Tolerance.Epsilon || ry < Tolerance.Epsilon)
            {
                AddLine(from, to, t, layer);
                return;
            }

            var cos = System.Math.Cos(rotation);
            var sin = System.Math.Sin(rotation);
            var halfX = (from.X - to.X) / 2;
            var halfY = (from.Y - to.Y) / 2;
            var x = cos * halfX + sin * halfY;
            var y = -sin * halfX + cos * halfY;

            // Radii too small to reach both endpoints scale up until they just do.
            var fit = x * x / (rx * rx) + y * y / (ry * ry);
            if (fit > 1)
            {
                rx *= System.Math.Sqrt(fit);
                ry *= System.Math.Sqrt(fit);
            }

            var numerator = rx * rx * ry * ry - rx * rx * y * y - ry * ry * x * x;
            var denominator = rx * rx * y * y + ry * ry * x * x;
            var factor = System.Math.Sqrt(System.Math.Max(0, numerator / denominator));
            if (large == sweep)
                factor = -factor;

            var centerX = factor * rx * y / ry;
            var centerY = -factor * ry * x / rx;
            var center = new Vector(
                cos * centerX - sin * centerY + (from.X + to.X) / 2,
                sin * centerX + cos * centerY + (from.Y + to.Y) / 2
            );

            var startAngle = System.Math.Atan2((y - centerY) / ry, (x - centerX) / rx);
            var endAngle = System.Math.Atan2((-y - centerY) / ry, (-x - centerX) / rx);
            var delta = endAngle - startAngle;
            if (sweep && delta < 0)
                delta += Angle.TwoPI;
            else if (!sweep && delta > 0)
                delta -= Angle.TwoPI;

            AddEllipticalArc(center, rx, ry, rotation, startAngle, delta, t, layer);
        }

        /// <summary>
        /// Adds part of an ellipse after the transform, which can change its axes: the output
        /// axes are where the transformed ellipse is widest and narrowest, then the parameter
        /// is shifted to them and mirrored when the transform flipped the ellipse's direction.
        /// </summary>
        private void AddEllipticalArc(
            Vector center,
            double rx,
            double ry,
            double rotation,
            double start,
            double delta,
            SvgTransform t,
            Layer layer
        )
        {
            var cos = System.Math.Cos(rotation);
            var sin = System.Math.Sin(rotation);
            var axisX = t.ApplyLinear(new Vector(rx * cos, rx * sin));
            var axisY = t.ApplyLinear(new Vector(-ry * sin, ry * cos));

            var offset = 0.5 * System.Math.Atan2(
                2 * (axisX.X * axisY.X + axisX.Y * axisY.Y),
                axisX.X * axisX.X + axisX.Y * axisX.Y - axisY.X * axisY.X - axisY.Y * axisY.Y
            );
            var major = axisX * System.Math.Cos(offset) + axisY * System.Math.Sin(offset);
            var minor = axisY * System.Math.Cos(offset) - axisX * System.Math.Sin(offset);

            var semiMajor = Length2(major);
            var semiMinor = Length2(minor);
            if (semiMajor < Tolerance.Epsilon || semiMinor < Tolerance.Epsilon)
                return;

            var direction = major.X * minor.Y - major.Y * minor.X >= 0 ? 1 : -1;
            var from = direction * (start - offset);
            var to = direction * (start + delta - offset);

            AddFitted(
                EllipseConverter.Convert(
                    t.Apply(center),
                    semiMajor,
                    semiMinor,
                    System.Math.Atan2(major.Y, major.X),
                    System.Math.Min(from, to),
                    System.Math.Max(from, to),
                    tolerance
                ),
                layer
            );
        }

        private void AddFitted(List<Entity> entities, Layer layer)
        {
            foreach (var entity in entities)
            {
                if (entity is Line line)
                    lines.Add(Tag(line, layer));
                else if (entity is Arc arc)
                    arcs.Add(Tag(arc, layer));
            }
        }

        private static T Tag<T>(T entity, Layer layer)
            where T : Entity
        {
            entity.Layer = layer;
            entity.Color = layer.Color;
            return entity;
        }

        /// <summary>Reads SVG's number grammar, where separators are optional ("1-2.5.5").</summary>
        private sealed class PathScanner
        {
            private readonly string text;
            private int position;

            public PathScanner(string text)
            {
                this.text = text;
            }

            public bool HasCommand
            {
                get
                {
                    SkipSeparators();
                    return position < text.Length && char.IsAsciiLetter(text[position]);
                }
            }

            public bool HasNumber
            {
                get
                {
                    SkipSeparators();
                    return position < text.Length
                        && (char.IsAsciiDigit(text[position]) || text[position] is '+' or '-' or '.');
                }
            }

            public char ReadCommand() => text[position++];

            public InvalidDataException Error() =>
                new InvalidDataException($"Malformed SVG path data at position {position}.");

            public Vector ReadPoint()
            {
                var x = ReadNumber();
                var y = ReadNumber();
                return new Vector(x, y);
            }

            public bool ReadFlag()
            {
                SkipSeparators();
                if (position >= text.Length || text[position] is not ('0' or '1'))
                    throw Error();
                return text[position++] == '1';
            }

            public double ReadNumber()
            {
                SkipSeparators();
                var begin = position;

                if (position < text.Length && text[position] is '+' or '-')
                    position++;
                var digits = SkipDigits();
                if (position < text.Length && text[position] == '.')
                {
                    position++;
                    digits += SkipDigits();
                }
                if (digits == 0)
                    throw Error();

                if (position < text.Length && text[position] is 'e' or 'E')
                {
                    var mark = position++;
                    if (position < text.Length && text[position] is '+' or '-')
                        position++;
                    if (SkipDigits() == 0)
                        position = mark;
                }

                return double.Parse(
                    text.AsSpan(begin, position - begin),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture
                );
            }

            private int SkipDigits()
            {
                var begin = position;
                while (position < text.Length && char.IsAsciiDigit(text[position]))
                    position++;
                return position - begin;
            }

            private void SkipSeparators()
            {
                while (
                    position < text.Length
                    && (char.IsWhiteSpace(text[position]) || text[position] == ',')
                )
                    position++;
            }
        }
    }
}
