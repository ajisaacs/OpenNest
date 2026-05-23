using System.Collections.Generic;
using System.IO;
using OpenNest.Geometry;
using OpenNest.Posts.GravographIS;

namespace OpenNest.Tests.GravographIS;

public class EnvelopeGuardTests
{
    // 0.610 m / 0.0125 mm/step = 48 800 steps = 24.0157 inches
    // 1.220 m / 0.0125 mm/step = 97 600 steps = 48.0315 inches

    [Fact]
    public void NegativeX_FromOrigin_Throws()
    {
        // Operator origin is upper-left; quadrant 4 walks right/down. A cut that walks
        // left of origin in -X must be refused.
        var polylines = new List<IReadOnlyList<Vector>>
        {
            new[] { new Vector(0, 0), new Vector(-1, 0) },
        };

        var ex = Assert.Throws<System.InvalidOperationException>(() =>
        {
            using var ms = new MemoryStream();
            new GravographISWriter().Write(polylines, ms);
        });

        Assert.Contains("Polyline 1", ex.Message);
        Assert.Contains("cut segment", ex.Message);
        Assert.Contains("segment 1", ex.Message);
    }

    [Fact]
    public void PositiveY_FromOrigin_Throws()
    {
        // Positive input-Y is above the upper-left origin in quadrant 4.
        var polylines = new List<IReadOnlyList<Vector>>
        {
            new[] { new Vector(0, 0), new Vector(0, 1) },
        };

        var ex = Assert.Throws<System.InvalidOperationException>(() =>
        {
            using var ms = new MemoryStream();
            new GravographISWriter().Write(polylines, ms);
        });

        Assert.Contains("Polyline 1", ex.Message);
    }

    [Fact]
    public void XExceedsEnvelope_Throws_AndNamesSegment()
    {
        // 25" in X is past the 0.610 m (~24.02") envelope.
        var polylines = new List<IReadOnlyList<Vector>>
        {
            new[] { new Vector(0, 0), new Vector(10, 0), new Vector(25, 0) },
        };

        var ex = Assert.Throws<System.InvalidOperationException>(() =>
        {
            using var ms = new MemoryStream();
            new GravographISWriter().Write(polylines, ms);
        });

        Assert.Contains("Polyline 1", ex.Message);
        Assert.Contains("segment 2", ex.Message); // 0→10 ok; 10→25 trips
        Assert.Contains("25.000\"", ex.Message);
    }

    [Fact]
    public void YExceedsEnvelope_Throws()
    {
        // -49" in Y is past the 1.220 m (~48.03") envelope.
        var polylines = new List<IReadOnlyList<Vector>>
        {
            new[] { new Vector(0, 0), new Vector(0, -49) },
        };

        Assert.Throws<System.InvalidOperationException>(() =>
        {
            using var ms = new MemoryStream();
            new GravographISWriter().Write(polylines, ms);
        });
    }

    [Fact]
    public void PenUpTravel_OutsideEnvelope_AlsoThrows_AndIsLabeledTravel()
    {
        // Polyline 1 ends in-envelope; the PU travel to polyline 2 leaves it.
        var polylines = new List<IReadOnlyList<Vector>>
        {
            new[] { new Vector(0, 0), new Vector(1, 0) },
            new[] { new Vector(30, 0), new Vector(30, -1) },
        };

        var ex = Assert.Throws<System.InvalidOperationException>(() =>
        {
            using var ms = new MemoryStream();
            new GravographISWriter().Write(polylines, ms);
        });

        Assert.Contains("Polyline 2", ex.Message);
        Assert.Contains("pen-up travel", ex.Message);
    }

    [Fact]
    public void RightAtEnvelopeCorner_IsAllowed()
    {
        // Walk to (24", -48") in int16-sized hops (each delta < 16.1"). The
        // catalog envelope is 24.02" × 48.03", so this lands just inside.
        var polylines = new List<IReadOnlyList<Vector>>
        {
            new[]
            {
                new Vector(0, 0),
                new Vector(8, -16),
                new Vector(16, -32),
                new Vector(24, -48),
            },
        };

        using var ms = new MemoryStream();
        new GravographISWriter().Write(polylines, ms); // no throw
        Assert.True(ms.Length > 0);
    }

    [Fact]
    public void EnvelopeGuard_CanBeDisabled_ForOffMachineEncoding()
    {
        var polylines = new List<IReadOnlyList<Vector>>
        {
            new[] { new Vector(0, 0), new Vector(-5, 0) },
        };

        var opts = new GravographISWriterOptions { EnvelopeGuardEnabled = false };

        using var ms = new MemoryStream();
        new GravographISWriter(opts).Write(polylines, ms); // no throw
        Assert.True(ms.Length > 0);
    }

    [Fact]
    public void CustomEnvelope_TightensTheCheck()
    {
        // Restrict to 1" × 1" — a 2" line in -Y now overshoots.
        var polylines = new List<IReadOnlyList<Vector>>
        {
            new[] { new Vector(0, 0), new Vector(0, -2) },
        };

        var opts = new GravographISWriterOptions
        {
            WorkEnvelopeXMm = 25.4,
            WorkEnvelopeYMm = 25.4,
        };

        Assert.Throws<System.InvalidOperationException>(() =>
        {
            using var ms = new MemoryStream();
            new GravographISWriter(opts).Write(polylines, ms);
        });
    }
}
