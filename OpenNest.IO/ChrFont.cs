using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenNest.Geometry;

namespace OpenNest.IO
{
    public class ChrFont
    {
        private readonly Dictionary<int, ChrGlyph> glyphs = new();

        public string Name { get; internal set; }
        public string Version { get; internal set; }
        public double CapHeight { get; internal set; } = 5000;

        internal void AddGlyph(int charCode, ChrGlyph glyph)
        {
            glyphs[charCode] = glyph;
        }

        public bool HasGlyph(int charCode) => glyphs.ContainsKey(charCode);

        public ChrGlyph GetGlyph(int charCode) =>
            glyphs.TryGetValue(charCode, out var g) ? g : null;

        public double MeasureTextWidth(string text, double height)
        {
            var scale = height / CapHeight;
            double width = 0;
            foreach (var ch in text)
            {
                var glyph = GetGlyph(ch);
                if (glyph == null)
                {
                    var space = GetGlyph(' ');
                    width += (space?.AdvanceWidth ?? CapHeight * 0.6) * scale;
                    continue;
                }
                width += glyph.AdvanceWidth * scale;
            }
            return width;
        }

        public List<Entity> RenderText(string text, double height, Vector position, Layer layer = null)
        {
            var scale = height / CapHeight;
            var entities = new List<Entity>();
            var cursorX = position.X;

            foreach (var ch in text)
            {
                var glyph = GetGlyph(ch);
                if (glyph == null)
                {
                    var space = GetGlyph(' ');
                    cursorX += (space?.AdvanceWidth ?? CapHeight * 0.6) * scale;
                    continue;
                }

                var glyphEntities = glyph.ToEntities(scale, cursorX, position.Y, layer);
                entities.AddRange(glyphEntities);
                cursorX += glyph.AdvanceWidth * scale;
            }

            return entities;
        }

        public static ChrFont Read(string path, byte? xorKey = null)
        {
            var raw = File.ReadAllBytes(path);

            // The whole file is obfuscated with a single-byte XOR. Different
            // GravoStyle versions use different keys (0x2F in older releases,
            // 0xCF in 7000-series). The font name at offset 0 is ASCII stored
            // as UTF-16LE, so the high byte of its first character is 0x00 in
            // plaintext — which means raw[1] is exactly the XOR key. Detect it
            // from the file unless the caller forces a specific key.
            var key = xorKey ?? (raw.Length > 1 ? raw[1] : (byte)0x2F);

            var data = new byte[raw.Length];
            for (var i = 0; i < raw.Length; i++)
                data[i] = (byte)(raw[i] ^ key);

            return Parse(data);
        }

        private static ChrFont Parse(byte[] data)
        {
            var font = new ChrFont();

            font.Name = Encoding.Unicode.GetString(data, 0, 26).TrimEnd('\0').Trim();
            font.Version = Encoding.ASCII.GetString(data, 26, 12).TrimEnd('\0').Trim();

            var charTable = new List<(int charCode, int offset)>();
            var i = 0x40;
            while (i + 5 < data.Length)
            {
                var charCode = data[i] | (data[i + 1] << 8);
                var offset = data[i + 2] | (data[i + 3] << 8) | (data[i + 4] << 16) | (data[i + 5] << 24);

                if (charCode < 0x20 || offset == 0 || offset >= data.Length)
                    break;

                charTable.Add((charCode, offset));
                i += 6;
            }

            for (var c = 0; c < charTable.Count; c++)
            {
                var (charCode, offset) = charTable[c];

                var nextOffset = c + 1 < charTable.Count
                    ? FindNextOffset(charTable, offset, data.Length)
                    : data.Length;

                var glyph = ParseGlyph(data, offset, nextOffset);
                if (glyph != null)
                    font.AddGlyph(charCode, glyph);
            }

            if (font.glyphs.Count > 0)
            {
                foreach (var g in font.glyphs.Values)
                {
                    if (g.CapHeight > 0)
                    {
                        font.CapHeight = g.CapHeight;
                        break;
                    }
                }
            }

            return font;
        }

        private static int FindNextOffset(List<(int charCode, int offset)> table, int currentOffset, int fileLength)
        {
            var best = fileLength;
            foreach (var (_, off) in table)
            {
                if (off > currentOffset && off < best)
                    best = off;
            }
            return best;
        }

        private static ChrGlyph ParseGlyph(byte[] data, int offset, int endOffset)
        {
            if (offset + 92 > data.Length)
                return null;

            var glyph = new ChrGlyph();
            glyph.CapHeight = ReadBE16(data, offset + 15 * 2);
            var bearing = System.Math.Abs(ReadBE16(data, offset + 18 * 2));
            glyph.AdvanceWidth = ReadBE16(data, offset + 22 * 2) + bearing;

            var strokeStart = offset + 92;
            var pos = strokeStart;
            var currentStroke = new List<ChrStrokePoint>();

            while (pos + 5 < endOffset)
            {
                var cmd = ReadBE16(data, pos);
                var x = ReadBE16(data, pos + 2);
                var y = ReadBE16(data, pos + 4);
                pos += 6;

                if (System.Math.Abs(x) > 15000 || System.Math.Abs(y) > 15000)
                    break;

                if (cmd < -1000)
                    break;

                var type = cmd switch
                {
                    1 => ChrPointType.Vertex,
                    4 => ChrPointType.Control,
                    5 => ChrPointType.EndPoint,
                    _ => ChrPointType.Vertex,
                };

                currentStroke.Add(new ChrStrokePoint(type, x, y));

                if (type == ChrPointType.EndPoint)
                {
                    if (currentStroke.Count > 0)
                        glyph.Strokes.Add(currentStroke);
                    currentStroke = new List<ChrStrokePoint>();
                }
            }

            if (currentStroke.Count > 0)
                glyph.Strokes.Add(currentStroke);

            return glyph;
        }

        private static int ReadBE16(byte[] data, int offset)
        {
            var val = (data[offset] << 8) | data[offset + 1];
            if (val > 32767) val -= 65536;
            return val;
        }
    }

    internal enum ChrPointType
    {
        Vertex,
        Control,
        EndPoint,
    }

    internal struct ChrStrokePoint
    {
        public ChrPointType Type;
        public double X;
        public double Y;

        public ChrStrokePoint(ChrPointType type, double x, double y)
        {
            Type = type;
            X = x;
            Y = y;
        }
    }

    public class ChrGlyph
    {
        internal readonly List<List<ChrStrokePoint>> Strokes = new();

        public double AdvanceWidth { get; internal set; }
        public double CapHeight { get; internal set; }

        private const int ArcSamples = 16;

        public List<Entity> ToEntities(double scale, double offsetX, double offsetY, Layer layer = null)
        {
            var entities = new List<Entity>();
            layer ??= Layer.Default;

            foreach (var stroke in Strokes)
            {
                if (stroke.Count < 2) continue;

                var segments = BuildSegments(stroke);
                foreach (var seg in segments)
                {
                    if (seg.Points.Count < 2) continue;

                    var scaled = new List<Vector>(seg.Points.Count);
                    foreach (var pt in seg.Points)
                        scaled.Add(new Vector(pt.X * scale + offsetX, pt.Y * scale + offsetY));

                    var converted = PointsToLines(scaled);

                    foreach (var e in converted)
                    {
                        e.Layer = layer;
                        entities.Add(e);
                    }
                }
            }

            return entities;
        }

        private static List<Entity> PointsToLines(List<Vector> points)
        {
            var entities = new List<Entity>();
            for (var i = 0; i < points.Count - 1; i++)
            {
                if (points[i].DistanceTo(points[i + 1]) < 0.001)
                    continue;
                entities.Add(new Line(points[i], points[i + 1]));
            }
            return entities;
        }

        private static List<StrokeSegment> BuildSegments(List<ChrStrokePoint> stroke)
        {
            var segments = new List<StrokeSegment>();
            var current = new StrokeSegment();

            var i = 0;
            while (i < stroke.Count)
            {
                var pt = stroke[i];

                if (pt.Type == ChrPointType.Vertex || pt.Type == ChrPointType.EndPoint)
                {
                    if (i + 1 < stroke.Count && stroke[i + 1].Type == ChrPointType.Control)
                    {
                        var p0 = new Vector(pt.X, pt.Y);
                        var pMid = new Vector(stroke[i + 1].X, stroke[i + 1].Y);
                        var p2End = i + 2 < stroke.Count ? stroke[i + 2] : stroke[i + 1];
                        var p1 = new Vector(p2End.X, p2End.Y);

                        SampleCircularArc(current.Points, p0, pMid, p1, ArcSamples);
                        current.HasCurves = true;
                        i += 2;
                    }
                    else
                    {
                        current.Points.Add(new Vector(pt.X, pt.Y));
                        i++;
                    }
                }
                else
                {
                    i++;
                }
            }

            if (current.Points.Count >= 2)
                segments.Add(current);

            return segments;
        }

        private class StrokeSegment
        {
            public readonly List<Vector> Points = new();
            public bool HasCurves;
        }

        private static void SampleCircularArc(List<Vector> output, Vector p0, Vector pMid, Vector p1, int samples)
        {
            if (output.Count == 0 || output[^1].DistanceTo(p0) > 0.01)
                output.Add(p0);

            double ax = p0.X, ay = p0.Y;
            double bx = pMid.X, by = pMid.Y;
            double cx = p1.X, cy = p1.Y;

            var d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));

            if (System.Math.Abs(d) < 1e-6)
            {
                output.Add(pMid);
                output.Add(p1);
                return;
            }

            var ux = ((ax * ax + ay * ay) * (by - cy) + (bx * bx + by * by) * (cy - ay) + (cx * cx + cy * cy) * (ay - by)) / d;
            var uy = ((ax * ax + ay * ay) * (cx - bx) + (bx * bx + by * by) * (ax - cx) + (cx * cx + cy * cy) * (bx - ax)) / d;
            var radius = System.Math.Sqrt((ax - ux) * (ax - ux) + (ay - uy) * (ay - uy));

            var a0 = System.Math.Atan2(ay - uy, ax - ux);
            var am = System.Math.Atan2(by - uy, bx - ux);
            var a1 = System.Math.Atan2(cy - uy, cx - ux);

            var ccwSweep = a1 - a0;
            while (ccwSweep <= 0) ccwSweep += 2 * System.Math.PI;

            var midRel = am - a0;
            while (midRel < 0) midRel += 2 * System.Math.PI;

            var sweep = midRel < ccwSweep ? ccwSweep : ccwSweep - 2 * System.Math.PI;

            for (var i = 1; i <= samples; i++)
            {
                var t = (double)i / samples;
                var angle = a0 + sweep * t;
                output.Add(new Vector(ux + radius * System.Math.Cos(angle), uy + radius * System.Math.Sin(angle)));
            }
        }
    }
}
