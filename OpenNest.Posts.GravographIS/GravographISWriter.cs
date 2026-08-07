using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenNest.Geometry;

namespace OpenNest.Posts.GravographIS
{
    /// <summary>
    /// One tool pass: a run of polylines cut at a single feed/depth, optionally
    /// preceded by an operator pause (to swap or adjust the tool). The Gravograph
    /// post builds one pass for engrave and one for cut.
    /// </summary>
    public sealed class GravographPass
    {
        public IEnumerable<IReadOnlyList<Vector>> Polylines { get; set; }

        public int FeedMmPerSec { get; set; }

        public double DepthInches { get; set; }

        /// <summary>When true, park to origin and prompt the operator before this pass.</summary>
        public bool PauseBefore { get; set; }

        public string PauseMessage { get; set; } = "";
    }

    /// <summary>
    /// Encodes polylines (in inches) into the Gravograph IS8000 native "binary HPGL"
    /// wire format. The byte stream is byte-exact against captures from GravoStyle'98.
    ///
    /// Scale: 80 steps/mm = 2032 steps/inch. Y (and Z) are negated on the wire.
    /// Deltas are signed big-endian int16 (max ±32767 steps ≈ ±16 inches per move).
    /// </summary>
    public sealed class GravographISWriter
    {
        // 93-byte preamble — captured from GravoStyle'98 with the trailing
        // job-specific travel block stripped. The VS, VZ and DZ operands are
        // patched by the writer to reflect feed and depth options.
        //
        // The original capture ended with a DR command (FF FD 44 52 00 00)
        // followed by three 8-byte int16 records — same format as PU/PD —
        // that carried a chunked travel from the head's parked position to
        // the original job's first vertex (cumulative ΔX ≈ 1", ΔY ≈ 47").
        // Those frozen deltas have nothing to do with our job geometry, so
        // replaying them sends the head to a fixed point regardless of where
        // the operator set zero. Stripped for the same reason as the captured
        // fixed return-to-home block.
        private static readonly byte[] PreambleTemplate = new byte[]
        {
            0x21, 0x41, 0x53, 0x20, 0x33, 0x38, 0x3b, 0x01, 0x90, 0x01,
            0xf4, 0x01, 0x90, 0x01, 0xf4, 0x01, 0x90, 0x01, 0xf4, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x09, 0x00, 0x00, 0x03, 0xe8, 0x05, 0x06, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0xff, 0xfd, 0x32, 0x44, 0x00,
            0x00, 0xff, 0xfd, 0x4d, 0x43, 0x00, 0x01, 0xff, 0xfd, 0x4f,
            0x55, 0xff, 0xfb, 0xff, 0xfd, 0x4f, 0x55, 0xff, 0xfa, 0xff,
            0xfd, 0x50, 0x5a, 0x00, 0x00, 0xff, 0xfd, 0x56, 0x53, 0x00,
            0x23, 0xff, 0xfd, 0x56, 0x5a, 0x00, 0x23, 0xff, 0xfd, 0x44,
            0x5a, 0x01, 0xfc,
        };

        // Stripped 36-byte postamble: lift, aux off, motor off, operator beep,
        // job-finish. The 24-byte return-to-home block that appears in GravoStyle's
        // captured postamble between MC and OP is intentionally OMITTED — those
        // three 8-byte int16 records carry chunked job-specific return deltas
        // (each record is [word1:int16][param:int16][ΔX:int16][ΔY:int16], same
        // format as PU/PD records; the original capture chunked the long Y return
        // across three records because each delta has to fit in int16). Reusing
        // GravoStyle's frozen deltas on different geometry overshoots the X-axis
        // limit. We emit calculated return deltas for the current job instead.
        // The writer now replaces the captured fixed return block with a calculated
        // lift + PU travel to the operator-set origin before these final commands.
        private static readonly byte[] EndJobBytes = new byte[]
        {
            0xff, 0xfd, 0x4f, 0x55, 0xff, 0xfa, // OU 0xFFFA  aux off
            0xff, 0xfd, 0x4f, 0x55, 0xff, 0xfb, // OU 0xFFFB  aux off
            0xff, 0xfd, 0x4d, 0x43, 0x00, 0x00, // MC 0x0000  motor off
            0xff, 0xfd, 0x4f, 0x50, 0x00, 0x00, // OP 0x0000  operator beep
            0xff, 0xfd, 0x4a, 0x46, 0x00, 0x00, // JF 0x0000  job finish
        };

        // 80 steps/mm × 25.4 mm/in
        internal const int StepsPerInch = 2032;

        public GravographISWriterOptions Options { get; }

        public GravographISWriter()
            : this(new GravographISWriterOptions())
        {
        }

        public GravographISWriter(GravographISWriterOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Writes the full byte stream (preamble + geometry + postamble) for the given
        /// polylines. Polyline coordinates are in inches, relative to the operator-set
        /// work origin. The writer emits a leading DR travel to the first polyline
        /// start before lowering for the first cut.
        /// </summary>
        public void Write(IEnumerable<IReadOnlyList<Vector>> polylines, Stream output)
        {
            if (polylines == null) throw new ArgumentNullException(nameof(polylines));

            // A single pass at the configured feed/depth — byte-identical to the
            // original single-group output (no transitions, no pause).
            Write(new[]
            {
                new GravographPass
                {
                    Polylines = polylines,
                    FeedMmPerSec = Options.FeedMmPerSec,
                    DepthInches = Options.DepthInches,
                    PauseBefore = false,
                    PauseMessage = "",
                },
            }, output);
        }

        /// <summary>
        /// Writes the full byte stream for an ordered list of tool passes. The preamble
        /// carries the first pass's feed/depth; each later pass emits an inline feed
        /// (and depth, if changed) and, when <see cref="GravographPass.PauseBefore"/> is
        /// set, parks to the operator origin and emits an operator pause before cutting.
        /// </summary>
        public void Write(IReadOnlyList<GravographPass> passes, Stream output)
        {
            if (passes == null) throw new ArgumentNullException(nameof(passes));
            if (output == null) throw new ArgumentNullException(nameof(output));

            var firstFeed = passes.Count > 0 ? passes[0].FeedMmPerSec : Options.FeedMmPerSec;
            var firstDepth = passes.Count > 0 ? passes[0].DepthInches : Options.DepthInches;

            var preamble = (byte[])PreambleTemplate.Clone();
            PatchOperand(preamble, (byte)'V', (byte)'S', (short)firstFeed);
            PatchOperand(preamble, (byte)'V', (byte)'Z', (short)firstFeed);
            PatchOperand(preamble, (byte)'D', (byte)'Z', DepthInStepsAsInt16(firstDepth));
            output.Write(preamble, 0, preamble.Length);

            // Cumulative head position from the operator-set upper-left origin, in
            // wire steps. The first polyline gets a leading DR travel from this
            // origin before PD lowers for cutting. Used by the envelope guard to
            // catch bad records before they ship to the engraver.
            var headX = 0;
            var headY = 0;
            var envelopeXSteps = (int)System.Math.Round(Options.WorkEnvelopeXMm * StepsPerMm,
                MidpointRounding.AwayFromZero);
            var envelopeYSteps = (int)System.Math.Round(Options.WorkEnvelopeYMm * StepsPerMm,
                MidpointRounding.AwayFromZero);

            var firstPolyline = true;
            var polyIndex = 0;
            var currentFeed = firstFeed;
            var currentDepth = firstDepth;

            for (var p = 0; p < passes.Count; p++)
            {
                var pass = passes[p];

                if (p > 0)
                {
                    if (pass.PauseBefore)
                    {
                        // Park: lift Z, then rapid (pen-up) back to the operator origin so
                        // the head is clear of the work while the tool is swapped.
                        WriteLiftOnly(output);
                        WriteTravel(output, (byte)'P', (byte)'U',
                            checked(-headX), checked(-headY),
                            ref headX, ref headY, envelopeXSteps, envelopeYSteps, polyIndex);
                        WritePauseCore(output, pass.PauseMessage);
                    }

                    if (pass.FeedMmPerSec != currentFeed)
                    {
                        WriteCommand(output, (byte)'V', (byte)'S', (short)pass.FeedMmPerSec);
                        WriteCommand(output, (byte)'V', (byte)'Z', (short)pass.FeedMmPerSec);
                        currentFeed = pass.FeedMmPerSec;
                    }

                    if (pass.DepthInches != currentDepth)
                    {
                        WriteCommand(output, (byte)'D', (byte)'Z', DepthInStepsAsInt16(pass.DepthInches));
                        currentDepth = pass.DepthInches;
                    }
                }

                if (pass.Polylines == null) continue;

                foreach (var poly in pass.Polylines)
                {
                    polyIndex++;
                    if (poly == null || poly.Count < 2)
                        continue;

                    WritePolyline(output, poly, ref firstPolyline, ref headX, ref headY,
                        envelopeXSteps, envelopeYSteps, polyIndex);
                }
            }

            WriteLiftOnly(output);
            if (Options.ReturnToOriginAtEnd && !firstPolyline)
            {
                WriteTravel(output, (byte)'P', (byte)'U',
                    checked(-headX), checked(-headY),
                    ref headX, ref headY, envelopeXSteps, envelopeYSteps, polyIndex);
            }
            output.Write(EndJobBytes, 0, EndJobBytes.Length);
        }

        private void WritePolyline(Stream output, IReadOnlyList<Vector> poly,
            ref bool firstPolyline, ref int headX, ref int headY,
            int envelopeXSteps, int envelopeYSteps, int polyIndex)
        {
            var (startX, startY) = ToWire(poly[0]);
            WriteTravel(output,
                firstPolyline ? (byte)'D' : (byte)'P',
                firstPolyline ? (byte)'R' : (byte)'U',
                checked(startX - headX), checked(startY - headY),
                ref headX, ref headY, envelopeXSteps, envelopeYSteps, polyIndex);

            // PD command + single records-follow flag, then one record per segment.
            output.WriteByte(0xFF);
            output.WriteByte(0xFD);
            output.WriteByte((byte)'P');
            output.WriteByte((byte)'D');
            output.WriteByte(0x00);
            output.WriteByte(0x00);

            var prevX = startX;
            var prevY = startY;
            for (int i = 1; i < poly.Count; i++)
            {
                var (cx, cy) = ToWire(poly[i]);
                var dx = checked(cx - prevX);
                var dy = checked(cy - prevY);
                EnsureEnvelope(headX + dx, headY + dy, envelopeXSteps, envelopeYSteps,
                    polyIndex, segment: i, isTravel: false);
                WriteRecord(output, dx, dy);
                prevX = cx;
                prevY = cy;
                headX += dx;
                headY += dy;
            }

            firstPolyline = false;
        }

        // The operator pause, minus the leading lift/park which the caller emits.
        // Stops the spindle (MC off), turns off aux, writes the console message, then
        // restarts the spindle (MC on) so the job resumes when the operator presses start.
        private static void WritePauseCore(Stream s, string message)
        {
            WriteCommandRaw(s, (byte)'M', (byte)'C', 0x00, 0x00); // motor off
            WriteCommandRaw(s, (byte)'O', (byte)'U', 0xFF, 0xFB); // aux off
            WriteCommandRaw(s, (byte)'O', (byte)'U', 0xFF, 0xFA); // aux off
            WriteCommandRaw(s, (byte)'L', (byte)'B', 0x00, 0x00); // begin message
            WriteMessagePackets(s, message);
            WriteCommandRaw(s, (byte)'N', (byte)'R', 0x00, 0x01); // line terminator
            WriteCommandRaw(s, (byte)'L', (byte)'B', 0x00, 0x01); // end message
            WriteCommandRaw(s, (byte)'M', (byte)'C', 0x00, 0x01); // motor on
        }

        // Console label packets carry two ASCII chars each; an odd-length message is
        // space-padded to a whole number of packets so widths stay 2 bytes.
        private static void WriteMessagePackets(Stream s, string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            var chars = Encoding.ASCII.GetBytes(message);
            for (var i = 0; i < chars.Length; i += 2)
            {
                var c0 = chars[i];
                var c1 = (i + 1 < chars.Length) ? chars[i + 1] : (byte)0x20;
                WriteCommandRaw(s, (byte)'L', (byte)'B', c0, c1);
            }
        }

        private static void WriteCommand(Stream s, byte c0, byte c1, short value)
        {
            WriteCommandRaw(s, c0, c1, (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF));
        }

        private static void WriteCommandRaw(Stream s, byte c0, byte c1, byte hi, byte lo)
        {
            s.WriteByte(0xFF);
            s.WriteByte(0xFD);
            s.WriteByte(c0);
            s.WriteByte(c1);
            s.WriteByte(hi);
            s.WriteByte(lo);
        }

        private const double StepsPerMm = 80.0;

        private void EnsureEnvelope(int wireX, int wireY,
            int envXSteps, int envYSteps,
            int polyIndex, int segment, bool isTravel)
        {
            if (!Options.EnvelopeGuardEnabled) return;

            // Wire frame: X is identity to input; Y is negated. With the operator
            // origin set at the upper-left of the work envelope and an OpenNest
            // quadrant-4 plate, valid part coordinates are +X/right and -Y/down:
            //   wireX ∈ [0, +envXSteps]
            //   wireY ∈ [0, +envYSteps]
            if (wireX >= 0 && wireX <= envXSteps && wireY >= 0 && wireY <= envYSteps)
                return;

            var inputX = wireX / (double)StepsPerInch;
            var inputY = -wireY / (double)StepsPerInch;
            var kind = isTravel ? "pen-up travel" : "cut segment";
            throw new InvalidOperationException(
                $"Polyline {polyIndex} {kind} (segment {segment}) would place the head at " +
                $"({inputX:F3}\", {inputY:F3}\"), outside the {Options.WorkEnvelopeXMm}×{Options.WorkEnvelopeYMm} mm " +
                $"work envelope from upper-left origin. Refusing to emit the record.");
        }

        private static short DepthInStepsAsInt16(double depthInches)
        {
            var steps = (long)System.Math.Round(depthInches * StepsPerInch, MidpointRounding.AwayFromZero);
            if (steps < short.MinValue || steps > short.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(depthInches), $"Depth {depthInches} in. → {steps} steps overflows int16.");
            return (short)steps;
        }

        private static (int x, int y) ToWire(Vector v)
        {
            // Inches -> steps. With upper-left origin in OpenNest quadrant 4,
            // negative input Y is down; Y is negated on the wire.
            var x = (int)System.Math.Round(v.X * StepsPerInch, MidpointRounding.AwayFromZero);
            var y = (int)System.Math.Round(-v.Y * StepsPerInch, MidpointRounding.AwayFromZero);
            return (x, y);
        }

        private void WriteTravel(Stream s, byte c0, byte c1, int dx, int dy,
            ref int headX, ref int headY,
            int envelopeXSteps, int envelopeYSteps,
            int polyIndex)
        {
            if (dx == 0 && dy == 0)
                return;

            s.WriteByte(0xFF);
            s.WriteByte(0xFD);
            s.WriteByte(c0);
            s.WriteByte(c1);
            s.WriteByte(0x00);
            s.WriteByte(0x00);

            var chunks = System.Math.Max(
                (int)System.Math.Ceiling(System.Math.Abs(dx) / (double)short.MaxValue),
                (int)System.Math.Ceiling(System.Math.Abs(dy) / (double)short.MaxValue));
            if (chunks < 1) chunks = 1;

            var emittedX = 0;
            var emittedY = 0;
            for (var i = 1; i <= chunks; i++)
            {
                var targetX = (int)System.Math.Round(dx * (i / (double)chunks), MidpointRounding.AwayFromZero);
                var targetY = (int)System.Math.Round(dy * (i / (double)chunks), MidpointRounding.AwayFromZero);
                var chunkX = checked(targetX - emittedX);
                var chunkY = checked(targetY - emittedY);

                EnsureEnvelope(headX + chunkX, headY + chunkY, envelopeXSteps, envelopeYSteps,
                    polyIndex, segment: 0, isTravel: true);
                WriteRecord(s, chunkX, chunkY);

                emittedX = targetX;
                emittedY = targetY;
                headX += chunkX;
                headY += chunkY;
            }
        }

        private static void WriteLiftOnly(Stream s)
        {
            s.WriteByte(0xFF);
            s.WriteByte(0xFD);
            s.WriteByte((byte)'P');
            s.WriteByte((byte)'U');
            s.WriteByte(0x00);
            s.WriteByte(0x01);
        }

        private static void WriteCommandWithRecord(Stream s, byte c0, byte c1, int dx, int dy)
        {
            s.WriteByte(0xFF);
            s.WriteByte(0xFD);
            s.WriteByte(c0);
            s.WriteByte(c1);
            // Records-follow flag (0x0000) emitted once per PU/PD packet.
            s.WriteByte(0x00);
            s.WriteByte(0x00);
            WriteRecord(s, dx, dy);
        }

        private static void WriteRecord(Stream s, int dx, int dy)
        {
            if (dx < short.MinValue || dx > short.MaxValue ||
                dy < short.MinValue || dy > short.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Move delta ({dx}, {dy}) steps overflows signed int16 — split moves upstream.");
            }

            int word1;
            int param;

            var absDx = (double)System.Math.Abs(dx);
            var absDy = (double)System.Math.Abs(dy);
            var len = System.Math.Sqrt(absDx * absDx + absDy * absDy);

            if (len < 1.0)
            {
                // Zero-length lift (PU 00 01) is the dedicated form; for a record-carrying
                // packet a true zero-length move shouldn't occur, but stay numerically safe.
                word1 = 16384;
                param = 1;
            }
            else
            {
                var maxAbs = System.Math.Max(absDx, absDy);
                word1 = (int)System.Math.Round(16384.0 * maxAbs / len, MidpointRounding.AwayFromZero);
                param = (int)System.Math.Round(len / 22.4, MidpointRounding.AwayFromZero);
                if (param < 1) param = 1;
                if (param > 180) param = 180;
                if (word1 > 16384) word1 = 16384;
            }

            WriteBigEndianInt16(s, (short)word1);
            WriteBigEndianInt16(s, (short)param);
            WriteBigEndianInt16(s, (short)dx);
            WriteBigEndianInt16(s, (short)dy);
        }

        private static void WriteBigEndianInt16(Stream s, short value)
        {
            s.WriteByte((byte)((value >> 8) & 0xFF));
            s.WriteByte((byte)(value & 0xFF));
        }

        // Locates the operand of a command (FF FD <c0> <c1> <hi> <lo>) and overwrites it.
        // Throws if the command isn't present — that would mean the preamble was mis-edited.
        private static void PatchOperand(byte[] buffer, byte c0, byte c1, short value)
        {
            for (int i = 0; i <= buffer.Length - 6; i++)
            {
                if (buffer[i] == 0xFF && buffer[i + 1] == 0xFD &&
                    buffer[i + 2] == c0 && buffer[i + 3] == c1)
                {
                    buffer[i + 4] = (byte)((value >> 8) & 0xFF);
                    buffer[i + 5] = (byte)(value & 0xFF);
                    return;
                }
            }

            throw new InvalidOperationException(
                $"Command '{(char)c0}{(char)c1}' not found in preamble template.");
        }
    }
}
