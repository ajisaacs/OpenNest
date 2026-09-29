using OpenNest.Reporting;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace OpenNest.Tests.Reporting;

public class ReportFontsTests
{
    [Fact]
    public void BundledFontsEmbedWithoutSystemFontResolutionAcrossRepeatedExports()
    {
        ReportFonts.Initialize();
        var resolver = GlobalFontSettings.FontResolver;
        for (var i = 0; i < 2; i++)
        {
            ReportFonts.Initialize();
            Assert.Same(resolver, GlobalFontSettings.FontResolver);
            using var doc = new PdfDocument();
            var page = doc.AddPage();
            using (var gfx = XGraphics.FromPdfPage(page))
            {
                gfx.DrawString("Résumé Ø TEST DATA", new XFont(ReportFonts.Family, 10), XBrushes.Black, 20, 20);
                gfx.DrawString("Bold", new XFont(ReportFonts.Family, 10, XFontStyleEx.Bold), XBrushes.Black, 20, 40);
            }
            using var stream = new MemoryStream();
            doc.Save(stream, false);
            Assert.True(stream.Length > 1000);
        }
    }

    [Theory]
    [InlineData("ASCII, 0.25 in; R001\nRésumé Ø Æ")]
    [InlineData("")]
    public void SupportedTextPasses(string text) => ReportFonts.ValidateText(text, "part R001");

    [Theory]
    [InlineData("中文")]
    [InlineData("bad\u0000text")]
    [InlineData("hidden\u00ADhyphen")]
    public void UnsupportedTextHasAnIdentifiedErrorInsteadOfMissingGlyphs(string text)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ReportFonts.ValidateText(text, "part R001"));
        Assert.Contains("part R001", error.Message);
        Assert.Contains("U+", error.Message);
    }
}
