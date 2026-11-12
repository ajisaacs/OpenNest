using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace OpenNest.Reporting;

/// <summary>
/// Renders part printouts: one Letter portrait page per part, bookmarked by part name, with a
/// part name header, customer subtitle, the part drawn to fit, a details table and the generated date.
/// </summary>
public static class PartPrintoutWriter
{
    private const double PageWidth = 612; // Letter, points
    private const double PageHeight = 792;
    private const double Margin = 36;
    private const double ContentWidth = PageWidth - 2 * Margin;
    private const double RowHeight = 22;
    private const double LabelWidth = 100;
    private const double ValueWidth = ContentWidth / 2 - LabelWidth;
    private const double DrawingPadding = 14;
    private const double FooterTop = PageHeight - Margin - 10;

    private static readonly XColor Ink = XColor.FromArgb(0x1a, 0x1a, 0x1a);
    private static readonly XColor Muted = XColor.FromArgb(0x64, 0x74, 0x8b);
    private static readonly XColor Line = XColor.FromArgb(0xcb, 0xd5, 0xe1);
    private static readonly XColor LabelFill = XColor.FromArgb(0xf8, 0xfa, 0xfc);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>Thread-safe; renders are serialized with nest reports.</summary>
    public static void Write(PartPrintoutSnapshot snapshot, string destination)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        if (snapshot.Parts.IsDefaultOrEmpty)
            throw new InvalidOperationException("A printout needs at least one part.");
        ValidateText(snapshot);

        lock (NestPdfWriter.RenderLock)
        {
            ReportFonts.Initialize();
            using var pdf = new PdfDocument();
            pdf.Info.Title = snapshot.Title;
            pdf.Info.Creator = "OpenNest";
            pdf.Info.CreationDate = snapshot.GeneratedAt.UtcDateTime;
            var stamp = snapshot.GeneratedAt.ToString("M/d/yyyy h:mm tt", Invariant);

            foreach (var part in snapshot.Parts)
            {
                var page = pdf.AddPage();
                page.Width = XUnit.FromPoint(PageWidth);
                page.Height = XUnit.FromPoint(PageHeight);
                using (var gfx = XGraphics.FromPdfPage(page))
                    DrawPage(gfx, snapshot, part, stamp);
                pdf.Outlines.Add(part.Name.Length == 0 ? "(unnamed)" : part.Name, page, true);
            }

            AtomicReportFile.Write(destination, stream => pdf.Save(stream, false));
        }
    }

    private static void ValidateText(PartPrintoutSnapshot snapshot)
    {
        ReportFonts.ValidateText(snapshot.Title, "Printout title");
        ReportFonts.ValidateText(snapshot.Material, "Material");
        foreach (var part in snapshot.Parts)
        {
            ReportFonts.ValidateText(part.Name, $"Drawing '{part.Name}' name");
            ReportFonts.ValidateText(part.Customer, $"Drawing '{part.Name}' customer");
        }
    }

    private static void DrawPage(XGraphics gfx, PartPrintoutSnapshot snapshot, PrintoutPart part, string stamp)
    {
        var title = Font(24, true);
        var subtitle = Font(12, false);
        var label = Font(8, true);
        var value = Font(10, true);
        var small = Font(8, false);
        var ink = new XSolidBrush(Ink);
        var muted = new XSolidBrush(Muted);
        var linePen = new XPen(Line, 0.75);

        // Header: part name, then the customer.
        var y = Margin;
        gfx.DrawString(Elide(gfx, Dash(part.Name), title, ContentWidth), title, ink,
            new XRect(Margin, y, ContentWidth, 30), XStringFormats.TopLeft);
        y += 32;
        if (part.Customer.Length > 0)
        {
            gfx.DrawString(Elide(gfx, part.Customer, subtitle, ContentWidth), subtitle, muted,
                new XRect(Margin, y, ContentWidth, 16), XStringFormats.TopLeft);
            y += 18;
        }
        y += 6;
        gfx.DrawLine(linePen, Margin, y, PageWidth - Margin, y);
        y += 12;

        var units = snapshot.Units;
        var rows = new (string, string, string, string)[]
        {
            ("DIMENSIONS", $"{Length(part.Width, units)} x {Length(part.Length, units)}", "MATERIAL", Dash(snapshot.Material)),
            ("THICKNESS", snapshot.Thickness > 0 ? Length(snapshot.Thickness, units, "0.####") : "-",
                "CUT LENGTH", Length(part.CutLength, units)),
            ("PIERCE COUNT", part.PierceCount.ToString(Invariant), "WEIGHT",
                part.Weight is { } weight ? $"{Number(weight)} {snapshot.WeightUnits}" : "-"),
            ("AREA", $"{Number(part.Area)} sq {units}", "QUANTITY",
                part.Quantity > 0 ? part.Quantity.ToString(Invariant) : "-"),
        };
        var tableTop = FooterTop - 12 - rows.Length * RowHeight;
        var drawing = new XRect(Margin, y, ContentWidth, tableTop - 14 - y);

        gfx.DrawRoundedRectangle(new XPen(Line, 1.5), drawing, new XSize(10, 10));
        DrawGeometry(gfx, part.Geometry, Inset(drawing, DrawingPadding));

        var rowTop = tableTop;
        foreach (var (l1, v1, l2, v2) in rows)
        {
            DrawCell(gfx, Margin, rowTop, LabelWidth, l1, label, muted, linePen, labelCell: true);
            DrawCell(gfx, Margin + LabelWidth, rowTop, ValueWidth, v1, value, ink, linePen, labelCell: false);
            DrawCell(gfx, Margin + LabelWidth + ValueWidth, rowTop, LabelWidth, l2, label, muted, linePen, labelCell: true);
            DrawCell(gfx, Margin + 2 * LabelWidth + ValueWidth, rowTop, ValueWidth, v2, value, ink, linePen, labelCell: false);
            rowTop += RowHeight;
        }

        gfx.DrawString($"Generated {stamp}", small, muted,
            new XRect(Margin, FooterTop, ContentWidth, 10), XStringFormats.TopRight);
    }

    private static void DrawCell(XGraphics gfx, double x, double y, double width, string text,
        XFont font, XBrush brush, XPen pen, bool labelCell)
    {
        var rect = new XRect(x, y, width, RowHeight);
        if (labelCell)
            gfx.DrawRectangle(pen, new XSolidBrush(LabelFill), rect);
        else
            gfx.DrawRectangle(pen, rect);
        var inner = new XRect(x + 8, y, width - 16, RowHeight);
        gfx.DrawString(Elide(gfx, text, font, inner.Width), font, brush, inner, XStringFormats.CenterLeft);
    }

    /// <summary>
    /// Strokes every contour, scaled uniformly and centered in the area. Each contour is its own
    /// path so a tab gap never gets a false joining segment.
    /// </summary>
    private static void DrawGeometry(XGraphics gfx, ReportGeometry geometry, XRect area)
    {
        var fit = Fit.Create(geometry.Bounds, area);
        var pen = new XPen(Ink, 0.75) { LineCap = XLineCap.Round, LineJoin = XLineJoin.Round };
        foreach (var contour in geometry.Contours)
        {
            var path = new XGraphicsPath();
            NestPdfWriter.AddContour(path, contour, fit);
            if (contour.Closed)
                path.CloseFigure();
            gfx.DrawPath(pen, path);
        }
    }

    private static XFont Font(double size, bool bold) =>
        new(ReportFonts.Family, size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);

    /// <summary>Overlong text is elided visibly rather than overflowing its box.</summary>
    private static string Elide(XGraphics gfx, string text, XFont font, double width)
    {
        if (gfx.MeasureString(text, font).Width <= width)
            return text;
        var shortened = text;
        while (shortened.Length > 1 && gfx.MeasureString(shortened + "...", font).Width > width)
            shortened = shortened[..^1];
        return shortened + "...";
    }

    private static XRect Inset(XRect rect, double by) =>
        new(rect.X + by, rect.Y + by, rect.Width - 2 * by, rect.Height - 2 * by);

    /// <summary>Inches get an inch mark, as on CutSend's printout; millimeters spell the unit.</summary>
    private static string Length(double value, string units, string format = "0.00") =>
        units == "mm" ? $"{value.ToString(format, Invariant)} mm" : $"{value.ToString(format, Invariant)}\"";

    private static string Number(double value) => value.ToString("0.00", Invariant);

    private static string Dash(string text) => string.IsNullOrWhiteSpace(text) ? "-" : text.Trim();
}
