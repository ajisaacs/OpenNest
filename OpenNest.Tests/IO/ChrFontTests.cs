using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.IO;

public class ChrFontTests
{
    private ChrFont LoadFont()
    {
        var path = TestConfig.GetExistingPath("ChrFontPath");
        Skip.If(path == null, "ChrFontPath not configured in test-config.json or file not found");
        return ChrFont.Read(path);
    }

    [SkippableFact]
    public void Read_ParsesFontName()
    {
        var font = LoadFont();
        Assert.Equal("US BLOCK 1L", font.Name);
    }

    [SkippableFact]
    public void Read_ParsesVersion()
    {
        var font = LoadFont();
        Assert.StartsWith("C1.", font.Version);
    }

    [SkippableFact]
    public void Read_HasAsciiGlyphs()
    {
        var font = LoadFont();
        Assert.True(font.HasGlyph('A'));
        Assert.True(font.HasGlyph('Z'));
        Assert.True(font.HasGlyph('0'));
        Assert.True(font.HasGlyph(' '));
    }

    [SkippableFact]
    public void Read_HasExtendedGlyphs()
    {
        var font = LoadFont();
        Assert.True(font.HasGlyph(0xC7)); // C-cedilla
    }

    [SkippableFact]
    public void Glyph_L_ProducesLines()
    {
        var font = LoadFont();
        var glyph = font.GetGlyph('L');
        Assert.NotNull(glyph);

        var entities = glyph.ToEntities(1.0, 0, 0);
        Assert.True(entities.Count >= 2, $"Expected at least 2 entities for 'L', got {entities.Count}");
        Assert.All(entities, e => Assert.Equal(EntityType.Line, e.Type));
    }

    [SkippableFact]
    public void Glyph_O_ProducesEntities()
    {
        var font = LoadFont();
        var glyph = font.GetGlyph('O');
        Assert.NotNull(glyph);

        var entities = glyph.ToEntities(1.0, 0, 0);
        Assert.True(entities.Count > 0);
    }

    [SkippableFact]
    public void RenderText_ProducesEntities()
    {
        var font = LoadFont();
        var entities = font.RenderText("HELLO", 1.0, new Vector(0, 0));
        Assert.True(entities.Count > 0, "RenderText should produce entities");
    }

    [SkippableFact]
    public void RenderText_ScalesCorrectly()
    {
        var font = LoadFont();

        var small = font.RenderText("A", 0.5, Vector.Zero);
        var large = font.RenderText("A", 2.0, Vector.Zero);

        var smallBox = small.GetBoundingBox();
        var largeBox = large.GetBoundingBox();

        Assert.True(largeBox.Width > smallBox.Width);
        Assert.True(largeBox.Length > smallBox.Length);
    }

    [SkippableFact]
    public void RenderText_AdvancesCursor()
    {
        var font = LoadFont();
        var abEntities = font.RenderText("AB", 1.0, Vector.Zero);
        var aEntities = font.RenderText("A", 1.0, Vector.Zero);

        var abBox = abEntities.GetBoundingBox();
        var aBox = aEntities.GetBoundingBox();

        Assert.True(abBox.Length > aBox.Length * 1.5,
            $"AB width ({abBox.Length:F1}) should be significantly wider than A width ({aBox.Length:F1})");
    }

    [SkippableFact]
    public void RenderText_MatchesGravographReference()
    {
        var font = LoadFont();

        var height = 5.08;
        var centerX = 50.8;
        var centerY = 34.925;

        var entities = font.RenderText("Text", height, Vector.Zero);
        var rawBox = entities.GetBoundingBox();
        var shiftX = centerX - (rawBox.Left + rawBox.Right) / 2;
        var shiftY = centerY - (rawBox.Top + rawBox.Bottom) / 2;
        foreach (var e in entities)
            e.Offset(new Vector(shiftX, shiftY));
        Assert.True(entities.Count > 0, "Should produce entities for 'Text'");

        var box = entities.GetBoundingBox();

        var refLeft = 43.53;
        var refRight = 58.07;
        var refBottom = 32.39;
        var refTop = 37.47;

        var tolerance = 0.5;

        Assert.True(System.Math.Abs(box.Left - refLeft) < tolerance,
            $"Left: ours={box.Left:F2}, ref={refLeft:F2}, diff={System.Math.Abs(box.Left - refLeft):F2}");
        Assert.True(System.Math.Abs(box.Right - refRight) < tolerance,
            $"Right: ours={box.Right:F2}, ref={refRight:F2}, diff={System.Math.Abs(box.Right - refRight):F2}");
        Assert.True(System.Math.Abs(box.Bottom - refBottom) < tolerance,
            $"Bottom: ours={box.Bottom:F2}, ref={refBottom:F2}, diff={System.Math.Abs(box.Bottom - refBottom):F2}");
        Assert.True(System.Math.Abs(box.Top - refTop) < tolerance,
            $"Top: ours={box.Top:F2}, ref={refTop:F2}, diff={System.Math.Abs(box.Top - refTop):F2}");

        var actualCapHeight = box.Top - box.Bottom;
        Assert.True(System.Math.Abs(actualCapHeight - height) < 0.5,
            $"Cap height: ours={actualCapHeight:F2}, expected={height:F2}");
    }

    [SkippableFact]
    public void MeasureTextWidth_IsConsistent()
    {
        var font = LoadFont();
        var height = 5.08;
        var measuredWidth = font.MeasureTextWidth("Text", height);
        var entities = font.RenderText("Text", height, Vector.Zero);
        var box = entities.GetBoundingBox();

        Assert.True(measuredWidth >= box.Length,
            $"Measured={measuredWidth:F2} should be >= rendered={box.Length:F2}");
        Assert.True(measuredWidth - box.Length < 2.0,
            $"Measured={measuredWidth:F2}, rendered={box.Length:F2}, diff={measuredWidth - box.Length:F2}");
    }

    [SkippableFact]
    public void Glyph_t_HasCurveAtBottom()
    {
        var font = LoadFont();
        var glyph = font.GetGlyph('t');
        Assert.NotNull(glyph);

        var entities = glyph.ToEntities(1.0, 0, 0);
        var lines = entities.Cast<Line>().ToList();

        Assert.True(lines.Count >= 10, $"Expected at least 10 entities for 't', got {lines.Count}");

        var curveLines = lines.Skip(1).Take(lines.Count - 3).ToList();
        Assert.True(curveLines.Count >= 14, $"Expected at least 14 curve segments, got {curveLines.Count}");

        var lastCurve = curveLines[^1];
        Assert.True(lastCurve.EndPoint.X > curveLines[0].StartPoint.X,
            $"Curve should end to the right of where it starts: start X={curveLines[0].StartPoint.X:F1}, end X={lastCurve.EndPoint.X:F1}");
    }
}
