using PdfSharp.Fonts;

namespace OpenNest.Reporting;

/// <summary>Process-wide, embedded fonts; never consult platform font directories.</summary>
internal static class ReportFonts
{
    internal const string Family = "OpenNest Report Sans";
    private static readonly object Sync = new();
    private static readonly BundledResolver Resolver = new();

    internal static void Initialize()
    {
        lock (Sync)
        {
            if (GlobalFontSettings.FontResolver == null)
                GlobalFontSettings.FontResolver = Resolver;
            else if (!ReferenceEquals(GlobalFontSettings.FontResolver, Resolver))
                throw new InvalidOperationException("Nest reports require their bundled font resolver before PDF fonts are created. Another resolver is already installed.");
            // MigraDoc otherwise asks the resolver for a platform "Courier New" error font.
            MigraDoc.PredefinedFontsAndChars.ErrorFontName = Family;
        }
    }

    internal static void ValidateText(string text, string field)
    {
        // Slice 1 deliberately supports printable Latin-1, not general Unicode shaping.
        // Reject unavailable text explicitly instead of substituting .notdef boxes or hiding it.
        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;
            if (value is '\r' or '\n' or '\t' || value is >= 0x20 and <= 0x7e
                || value is >= 0xa0 and <= 0xff && value != 0xad)
                continue;
            throw new InvalidOperationException($"{field}: unsupported report character U+{value:X4}. This report slice supports printable Latin-1 text only.");
        }
    }

    private sealed class BundledResolver : IFontResolver
    {
        public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            if (!string.Equals(familyName, Family, StringComparison.OrdinalIgnoreCase) || isItalic)
                return null;
            return new FontResolverInfo(isBold ? "DejaVuSans-Bold" : "DejaVuSans");
        }

        public byte[] GetFont(string faceName)
        {
            if (faceName is not ("DejaVuSans" or "DejaVuSans-Bold"))
                throw new InvalidOperationException($"Unknown report font face: {faceName}.");
            using var source = typeof(ReportFonts).Assembly.GetManifestResourceStream($"OpenNest.Reporting.Fonts.{faceName}.ttf")
                ?? throw new InvalidOperationException($"Bundled report font missing: {faceName}.");
            using var bytes = new MemoryStream();
            source.CopyTo(bytes);
            return bytes.ToArray();
        }
    }
}
