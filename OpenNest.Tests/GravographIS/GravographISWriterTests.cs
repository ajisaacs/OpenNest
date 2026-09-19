using System.Collections.Generic;
using System.IO;
using OpenNest.Geometry;
using OpenNest.Posts.GravographIS;

namespace OpenNest.Tests.GravographIS;

public class GravographISWriterTests
{
    // 93-byte preamble captured from GravoStyle'98 (VS/VZ=35, DZ=508 → matches defaults).
    // The original capture ended with a DR command (FF FD 44 52) followed by three
    // 8-byte int16 records carrying a chunked job-specific travel (~1" X, ~47" Y).
    // Stripped from the writer (see GravographISWriter.PreambleTemplate) because
    // those frozen deltas send the head to a fixed point regardless of the job. The
    // writer now emits a job-specific leading DR travel from operator zero instead.
    private const string PreambleHex =
        "21 41 53 20 33 38 3b 01 90 01 f4 01 90 01 f4 01 90 01 f4 00 00 00 00 00 00 00 00 00 00 " +
        "00 00 00 09 00 00 03 e8 05 06 00 00 00 00 00 00 ff fd 32 44 00 00 ff fd 4d 43 00 01 ff fd " +
        "4f 55 ff fb ff fd 4f 55 ff fa ff fd 50 5a 00 00 ff fd 56 53 00 23 ff fd 56 5a 00 23 ff fd " +
        "44 5a 01 fc";

    // Legacy 36-byte tail with lift, aux off, motor off, operator beep, job finish.
    // Byte-exact capture tests disable dynamic return-to-origin to preserve this form.
    private const string PostambleHex =
        "ff fd 50 55 00 01 ff fd 4f 55 ff fa ff fd 4f 55 ff fb ff fd 4d 43 00 00 " +
        "ff fd 4f 50 00 00 ff fd 4a 46 00 00";

    [Fact]
    public void TestA_SingleTwoInchVerticalLine_IsByteExact()
    {
        var polylines = new List<IReadOnlyList<Vector>>
        {
            new[] { new Vector(1, 1), new Vector(1, 3) },
        };

        var writer = new GravographISWriter(new GravographISWriterOptions
        {
            DepthInches = 0.25,
            FeedMmPerSec = 35,
            EnvelopeGuardEnabled = false,
            ReturnToOriginAtEnd = false,
        });

        using var ms = new MemoryStream();
        writer.Write(polylines, ms);

        const string GeomHex =
            "ff fd 44 52 00 00 2d 41 00 80 07 f0 f8 10 " +
            "ff fd 50 44 00 00 40 00 00 b4 00 00 f0 20";
        var expected = HexToBytes(PreambleHex + " " + GeomHex + " " + PostambleHex);

        Assert.Equal(expected, ms.ToArray());
    }

    [Fact]
    public void TestB_FourLines_IsByteExact()
    {
        var polylines = new List<IReadOnlyList<Vector>>
        {
            new[] { new Vector(1, 1), new Vector(1, 3) },
            new[] { new Vector(4, 1), new Vector(4, 3) },
            new[] { new Vector(4, 5), new Vector(4, 7) },
            new[] { new Vector(1, 5), new Vector(1, 7) },
        };

        var writer = new GravographISWriter(new GravographISWriterOptions
        {
            DepthInches = 0.25,
            FeedMmPerSec = 35,
            EnvelopeGuardEnabled = false,
            ReturnToOriginAtEnd = false,
        });

        using var ms = new MemoryStream();
        writer.Write(polylines, ms);

        const string GeomHex =
            "ff fd 44 52 00 00 2d 41 00 80 07 f0 f8 10 " +
            "ff fd 50 44 00 00 40 00 00 b4 00 00 f0 20 " +
            "ff fd 50 55 00 00 35 40 00 b4 17 d0 0f e0 " +
            "ff fd 50 44 00 00 40 00 00 b4 00 00 f0 20 " +
            "ff fd 50 55 00 00 40 00 00 b4 00 00 f0 20 " +
            "ff fd 50 44 00 00 40 00 00 b4 00 00 f0 20 " +
            "ff fd 50 55 00 00 35 40 00 b4 e8 30 0f e0 " +
            "ff fd 50 44 00 00 40 00 00 b4 00 00 f0 20";
        var expected = HexToBytes(PreambleHex + " " + GeomHex + " " + PostambleHex);

        Assert.Equal(expected, ms.ToArray());
    }

    [Fact]
    public void LeadingDR_TravelsToFirstPolylineStartBeforePD()
    {
        var polylines = new List<IReadOnlyList<Vector>>
        {
            new[] { new Vector(2, 2), new Vector(3, 2) },
        };

        using var ms = new MemoryStream();
        new GravographISWriter(new GravographISWriterOptions { EnvelopeGuardEnabled = false }).Write(polylines, ms);

        var bytes = ms.ToArray();
        // First command after the 93-byte preamble must be DR to the first point,
        // followed by PD for the first cut.
        Assert.Equal(0xFF, bytes[93]);
        Assert.Equal(0xFD, bytes[94]);
        Assert.Equal((byte)'D', bytes[95]);
        Assert.Equal((byte)'R', bytes[96]);

        Assert.Equal(0xFF, bytes[107]);
        Assert.Equal(0xFD, bytes[108]);
        Assert.Equal((byte)'P', bytes[109]);
        Assert.Equal((byte)'D', bytes[110]);
    }

    [Fact]
    public void LeadingDR_LongTravel_IsChunked()
    {
        var polylines = new List<IReadOnlyList<Vector>>
        {
            new[] { new Vector(1, 47), new Vector(2, 47) },
        };

        using var ms = new MemoryStream();
        new GravographISWriter(new GravographISWriterOptions { EnvelopeGuardEnabled = false }).Write(polylines, ms);

        var bytes = ms.ToArray();
        Assert.Equal((byte)'D', bytes[95]);
        Assert.Equal((byte)'R', bytes[96]);
        Assert.Equal((byte)'P', bytes[125]);
        Assert.Equal((byte)'D', bytes[126]);
    }

    [Fact]
    public void OptionsPatchVsVzDz()
    {
        var polylines = new List<IReadOnlyList<Vector>>
        {
            new[] { new Vector(0, 0), new Vector(0.5, 0) },
        };

        using var ms = new MemoryStream();
        new GravographISWriter(new GravographISWriterOptions
        {
            DepthInches = 0.125,   // 254 steps = 0x00FE
            FeedMmPerSec = 50,     // 0x0032
        }).Write(polylines, ms);

        var bytes = ms.ToArray();
        AssertOperand(bytes, (byte)'V', (byte)'S', 0x00, 0x32);
        AssertOperand(bytes, (byte)'V', (byte)'Z', 0x00, 0x32);
        AssertOperand(bytes, (byte)'D', (byte)'Z', 0x00, 0xFE);
    }

    [Fact]
    public void ReturnsToOriginAfterFinalLift_ByDefault()
    {
        var polylines = new List<IReadOnlyList<Vector>>
        {
            new[] { new Vector(0, 0), new Vector(1, 0), new Vector(1, -1) },
        };

        using var ms = new MemoryStream();
        new GravographISWriter().Write(polylines, ms);

        var bytes = ms.ToArray();
        var liftIndex = LastIndexOfCommand(bytes, (byte)'P', (byte)'U', 0x00, 0x01);
        Assert.True(liftIndex >= 0);

        Assert.Equal(0xFF, bytes[liftIndex + 6]);
        Assert.Equal(0xFD, bytes[liftIndex + 7]);
        Assert.Equal((byte)'P', bytes[liftIndex + 8]);
        Assert.Equal((byte)'U', bytes[liftIndex + 9]);

        var dx = ReadInt16(bytes, liftIndex + 16);
        var dy = ReadInt16(bytes, liftIndex + 18);
        Assert.Equal(-GravographISWriter.StepsPerInch, dx);
        Assert.Equal(-GravographISWriter.StepsPerInch, dy);
    }

    [Fact]
    public void Passes_PauseBeforeCut_EmitsPauseSequenceBetweenGroups()
    {
        var engrave = new List<IReadOnlyList<Vector>> { new[] { new Vector(0, 0), new Vector(1, 0) } };
        var cut = new List<IReadOnlyList<Vector>> { new[] { new Vector(0, 0), new Vector(0, -1) } };
        var passes = new List<GravographPass>
        {
            new GravographPass { Polylines = engrave, FeedMmPerSec = 10, DepthInches = 0.25 },
            new GravographPass { Polylines = cut, FeedMmPerSec = 3, DepthInches = 0.25, PauseBefore = true, PauseMessage = "Hi" },
        };

        using var ms = new MemoryStream();
        new GravographISWriter(new GravographISWriterOptions
        {
            EnvelopeGuardEnabled = false,
            ReturnToOriginAtEnd = false,
        }).Write(passes, ms);
        var bytes = ms.ToArray();

        var mcOff = IndexOf(bytes, 0, (byte)'M', (byte)'C', 0x00, 0x00);
        var ouFb = IndexOf(bytes, mcOff, (byte)'O', (byte)'U', 0xFF, 0xFB);
        var ouFa = IndexOf(bytes, ouFb, (byte)'O', (byte)'U', 0xFF, 0xFA);
        var lbBegin = IndexOf(bytes, ouFa, (byte)'L', (byte)'B', 0x00, 0x00);
        var lbMsg = IndexOf(bytes, lbBegin, (byte)'L', (byte)'B', (byte)'H', (byte)'i');
        var nr = IndexOf(bytes, lbMsg, (byte)'N', (byte)'R', 0x00, 0x01);
        var lbEnd = IndexOf(bytes, nr, (byte)'L', (byte)'B', 0x00, 0x01);
        var mcOn = IndexOf(bytes, lbEnd, (byte)'M', (byte)'C', 0x00, 0x01);

        Assert.True(mcOff >= 0, "motor-off (MC 0000) not found");
        Assert.True(mcOff < ouFb && ouFb < ouFa && ouFa < lbBegin && lbBegin < lbMsg
            && lbMsg < nr && nr < lbEnd && lbEnd < mcOn,
            "pause commands out of order");

        // Resume sets the cut feed inline (VS 0x0003) after the motor restarts.
        var vsCut = IndexOf(bytes, mcOn, (byte)'V', (byte)'S', 0x00, 0x03);
        Assert.True(vsCut > mcOn, "cut feed not set after resume");
    }

    [Fact]
    public void Passes_PauseOddMessage_SpacePadsLastPacket()
    {
        var passes = new List<GravographPass>
        {
            new GravographPass { Polylines = new List<IReadOnlyList<Vector>> { new[] { new Vector(0, 0), new Vector(1, 0) } }, FeedMmPerSec = 10 },
            new GravographPass { Polylines = new List<IReadOnlyList<Vector>> { new[] { new Vector(0, 0), new Vector(0, -1) } }, FeedMmPerSec = 3, PauseBefore = true, PauseMessage = "abc" },
        };

        using var ms = new MemoryStream();
        new GravographISWriter(new GravographISWriterOptions { EnvelopeGuardEnabled = false, ReturnToOriginAtEnd = false }).Write(passes, ms);
        var bytes = ms.ToArray();

        var lbAb = IndexOf(bytes, 0, (byte)'L', (byte)'B', (byte)'a', (byte)'b');
        var lbCPad = IndexOf(bytes, lbAb, (byte)'L', (byte)'B', (byte)'c', 0x20);
        Assert.True(lbAb >= 0, "first message packet 'ab' not found");
        Assert.True(lbCPad > lbAb, "odd packet not space-padded to 'c '");
    }

    [Fact]
    public void Passes_DifferentFeeds_NoPause_EmitsInlineFeedChangeNoMessage()
    {
        var passes = new List<GravographPass>
        {
            new GravographPass { Polylines = new List<IReadOnlyList<Vector>> { new[] { new Vector(0, 0), new Vector(1, 0) } }, FeedMmPerSec = 10 },
            new GravographPass { Polylines = new List<IReadOnlyList<Vector>> { new[] { new Vector(0, 0), new Vector(0, -1) } }, FeedMmPerSec = 3, PauseBefore = false },
        };

        using var ms = new MemoryStream();
        new GravographISWriter(new GravographISWriterOptions { EnvelopeGuardEnabled = false, ReturnToOriginAtEnd = false }).Write(passes, ms);
        var bytes = ms.ToArray();

        Assert.True(IndexOf(bytes, 0, (byte)'V', (byte)'S', 0x00, 0x03) >= 0, "inline cut feed change missing");
        Assert.True(IndexOfCmd(bytes, (byte)'L', (byte)'B') < 0, "no LB message expected without a pause");
    }

    private static int IndexOf(byte[] bytes, int from, byte c0, byte c1, byte hi, byte lo)
    {
        for (var i = System.Math.Max(0, from); i <= bytes.Length - 6; i++)
        {
            if (bytes[i] == 0xFF && bytes[i + 1] == 0xFD && bytes[i + 2] == c0 &&
                bytes[i + 3] == c1 && bytes[i + 4] == hi && bytes[i + 5] == lo)
                return i;
        }
        return -1;
    }

    private static int IndexOfCmd(byte[] bytes, byte c0, byte c1)
    {
        for (var i = 0; i <= bytes.Length - 4; i++)
        {
            if (bytes[i] == 0xFF && bytes[i + 1] == 0xFD && bytes[i + 2] == c0 && bytes[i + 3] == c1)
                return i;
        }
        return -1;
    }

    private static void AssertOperand(byte[] bytes, byte c0, byte c1, byte hi, byte lo)
    {
        for (var i = 0; i < bytes.Length - 5; i++)
        {
            if (bytes[i] == 0xFF && bytes[i + 1] == 0xFD && bytes[i + 2] == c0 && bytes[i + 3] == c1)
            {
                Assert.Equal(hi, bytes[i + 4]);
                Assert.Equal(lo, bytes[i + 5]);
                return;
            }
        }
        Assert.Fail($"Command {(char)c0}{(char)c1} not found in stream.");
    }

    private static int LastIndexOfCommand(byte[] bytes, byte c0, byte c1, byte hi, byte lo)
    {
        for (var i = bytes.Length - 6; i >= 0; i--)
        {
            if (bytes[i] == 0xFF && bytes[i + 1] == 0xFD &&
                bytes[i + 2] == c0 && bytes[i + 3] == c1 &&
                bytes[i + 4] == hi && bytes[i + 5] == lo)
            {
                return i;
            }
        }

        return -1;
    }

    private static short ReadInt16(byte[] bytes, int offset)
    {
        return unchecked((short)((bytes[offset] << 8) | bytes[offset + 1]));
    }

    internal static byte[] HexToBytes(string hex)
    {
        var clean = hex.Replace(" ", string.Empty).Replace("\n", string.Empty).Replace("\r", string.Empty);
        var bytes = new byte[clean.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = System.Convert.ToByte(clean.Substring(i * 2, 2), 16);
        return bytes;
    }
}
