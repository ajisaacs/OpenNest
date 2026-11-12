using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace OpenNest.Reporting;

/// <summary>
/// Renders a detached <see cref="NestReportSnapshot"/> to PDF: MigraDoc owns text, tables and
/// pagination; PDFsharp draws vector thumbnails and sheet diagrams into reserved layout areas.
/// </summary>
public static class NestPdfWriter
{
    private const double Margin = 36;
    private const double BottomMargin = 50;
    private const double HeaderDistance = 18;
    private const double HeaderFontSize = 7.5;
    private const int MaxHeaderLines = 3;
    private const double BodyFontSize = 9;
    private const double CellPadding = 3;
    private const int CardsPerRow = 4;
    private const double CardGap = 8;
    private const double CardWidth = (PortraitWidth - (CardsPerRow - 1) * CardGap) / CardsPerRow;
    private const double CardThumbnailHeight = 66;
    private const double CardFontSize = 8;
    private const double PortraitWidth = 540;
    private const double LandscapeWidth = 720;
    private const double OverviewHeight = 320;

    private static readonly XColor Fill = XColor.FromArgb(222, 222, 222);
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // PDFsharp/MigraDoc layout and font state is process-wide and not safe for concurrent
    // documents: parallel exports laid out text differently. Reports are rare, so serialize them.
    // Shared with PartPrintoutWriter for the same reason.
    internal static readonly object RenderLock = new();

    /// <summary>Thread-safe; concurrent calls are serialized.</summary>
    public static void Write(NestReportSnapshot snapshot, string destination) =>
        Write(snapshot, destination, null);

    /// <param name="wrapOutput">Test seam for injecting late write failures.</param>
    internal static void Write(NestReportSnapshot snapshot, string destination, Func<Stream, Stream>? wrapOutput)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        lock (RenderLock)
            WriteSerialized(snapshot, destination, wrapOutput);
    }

    private static void WriteSerialized(NestReportSnapshot snapshot, string destination, Func<Stream, Stream>? wrapOutput)
    {
        ValidateText(snapshot);
        ReportFonts.Initialize();

        // Plan every diagram before layout; nothing below touches the destination until Save.
        var plans = snapshot.Plates.Select(plate =>
            NestReportDiagram.Plan(plate, LandscapeWidth, OverviewHeight)).ToList();

        var document = new Document();
        document.Info.Title = snapshot.Name;
        var normal = document.Styles[StyleNames.Normal]
            ?? throw new InvalidOperationException("MigraDoc normal style is unavailable.");
        normal.Font.Name = ReportFonts.Family;
        normal.Font.Size = BodyFontSize;
        var areas = new List<(Table Table, Action<XGraphics, XRect>[] Layers)>();
        AddSummary(document, snapshot);
        for (var index = 0; index < snapshot.Plates.Length; index++)
            AddPlate(document, snapshot, snapshot.Plates[index], plans[index], areas);

        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        // PDFsharp 6.2.4 disallows reading the page count after Save.
        var pageCount = pdf.PageCount;
        foreach (var (page, area, layers) in LocateAreas(renderer, pageCount, areas))
        {
            // Each layer is its own content stream, so the diagram's stream holds only the sheet
            // and what is on it, never the rulers around it.
            foreach (var draw in layers)
            {
                using var gfx = XGraphics.FromPdfPage(pdf.Pages[page - 1]);
                var state = gfx.Save();
                gfx.TranslateTransform(area.X, area.Y);
                gfx.IntersectClip(new XRect(0, 0, area.Width, area.Height));
                draw(gfx, area);
                gfx.Restore(state);
            }
        }

        pdf.Info.Title = snapshot.Name;
        pdf.Info.Creator = "OpenNest";
        pdf.Info.CreationDate = snapshot.GeneratedAt.UtcDateTime;
        AtomicReportFile.Write(destination, stream => pdf.Save(wrapOutput?.Invoke(stream) ?? stream, false));
    }

    private static void ValidateText(NestReportSnapshot snapshot)
    {
        ReportFonts.ValidateText(snapshot.Name, "Nest name");
        ReportFonts.ValidateText(snapshot.Customer, "Customer");
        ReportFonts.ValidateText(snapshot.Notes, "Notes");
        ReportFonts.ValidateText(snapshot.Material, "Material");
        ReportFonts.ValidateText(snapshot.Grade, "Material grade");
        foreach (var drawing in snapshot.Drawings)
            ReportFonts.ValidateText(drawing.Name, $"Drawing {drawing.Id} name");
    }

    private static void AddSummary(Document document, NestReportSnapshot snapshot)
    {
        var section = AddSection(document, snapshot, Orientation.Portrait, PortraitWidth, $"Nest report: {snapshot.Name}");
        var title = section.AddParagraph("Nest Report");
        title.Format.Font.Size = 14;
        title.Format.Font.Bold = true;
        title.Format.SpaceAfter = Unit.FromPoint(6);

        // Two key/value columns keep the header short so more part cards fit on the first page:
        // the job and material on the left, the report's totals on the right.
        var left = new List<(string Key, string Value)> { ("Nest:", snapshot.Name) };
        if (snapshot.Customer.Length > 0)
            left.Add(("Customer:", snapshot.Customer));
        left.Add(("Material:", Material(snapshot)));
        // No units row: every size, thickness and weight carries its own unit.
        left.Add(("Thickness:", Thickness(snapshot)));
        var right = new List<(string Key, string Value)>
        {
            ("Distinct layouts:", snapshot.Plates.Length.ToString(Invariant)),
            ("Total physical sheets:", snapshot.TotalSheets.ToString(Invariant)),
        };
        if (snapshot.TotalCutTime is { } total)
            right.Add(("Estimated cut time:", Duration(total) + " (all sheets)"));

        var info = AddTable(section, 72, 182, 136, 150);
        info.Borders.Visible = false;
        // The left column always has at least as many rows as the right.
        for (var index = 0; index < left.Count; index++)
        {
            string[] cells = index < right.Count
                ? [left[index].Key, left[index].Value, right[index].Key, right[index].Value]
                : [left[index].Key, left[index].Value];
            AddInfoRow(info, cells);
        }
        if (snapshot.Notes.Length > 0)
        {
            // Free paragraphs split across pages; a table row taller than a page would be clipped.
            AddHeading(section, "Notes");
            AddLines(section.AddParagraph(), ReportText.Wrap(snapshot.Notes, BodyFont(false), PortraitWidth));
        }

        AddHeading(section, "Plates");
        if (snapshot.Plates.Length == 0)
            section.AddParagraph("No plates in this job.");
        else
            AddPlatesTable(section, snapshot);

        AddHeading(section, "Parts");
        if (snapshot.Drawings.Length == 0)
        {
            section.AddParagraph("No parts in this job.");
            return;
        }
        if (snapshot.Drawings.Any(drawing => drawing.Weight == null))
        {
            var note = section.AddParagraph(WeightNote(snapshot));
            note.Format.Font.Size = 7.5;
            note.Format.SpaceAfter = Unit.FromPoint(4);
        }
        AddPartCards(section, snapshot);
    }

    /// <summary>
    /// Sheet weight and cut time columns appear only when the report has them; the stock size
    /// column takes whatever width they leave.
    /// </summary>
    private static void AddPlatesTable(Section section, NestReportSnapshot snapshot)
    {
        var hasWeights = snapshot.Plates.Any(plate => plate.Weight != null);
        var widths = new List<double> { 40, 0, 56, 92, 68 };
        var headings = new List<string> { "Plate", "Stock size", "Quantity", "Parts per plate", "Utilization" };
        if (hasWeights)
        {
            widths.Add(92);
            headings.Add("Sheet weight");
        }
        if (snapshot.HasCutTimes)
        {
            widths.Add(92);
            headings.Add("Est. cut time");
        }
        widths[1] = PortraitWidth - widths.Sum();

        var plates = AddTable(section, widths.ToArray());
        AddHeader(plates, headings.ToArray());
        foreach (var plate in snapshot.Plates)
        {
            var cells = new List<string>
            {
                plate.Number.ToString(Invariant), SheetSize(snapshot, plate), plate.Copies.ToString(Invariant),
                plate.Parts.Length.ToString(Invariant), Percent(plate.Utilization),
            };
            if (hasWeights)
                cells.Add(SheetWeight(snapshot, plate));
            if (snapshot.HasCutTimes)
                cells.Add(PlateCutTime(plate));
            AddRow(plates, $"Plate {plate.Number}", false, cells.ToArray());
        }
    }

    /// <summary>One sheet, then all its copies on a second line.</summary>
    private static string SheetWeight(NestReportSnapshot snapshot, ReportPlate plate) =>
        plate.Weight is not { } each ? "-"
        : plate.Copies == 1 ? $"{Weight(each)} {snapshot.WeightUnits}"
        : $"{Weight(each)} {snapshot.WeightUnits} each\n{Weight(each * plate.Copies)} {snapshot.WeightUnits} total";

    /// <summary>
    /// One card per drawing, <see cref="CardsPerRow"/> to a row. Cards are table cells with
    /// unbordered spacer columns between them; MigraDoc never splits a row across pages.
    /// </summary>
    private static void AddPartCards(Section section, NestReportSnapshot snapshot)
    {
        var columns = new List<double>();
        for (var index = 0; index < CardsPerRow; index++)
        {
            if (index > 0)
                columns.Add(CardGap);
            columns.Add(CardWidth);
        }
        var table = AddTable(section, columns.ToArray());
        table.Borders.Visible = false;
        table.LeftPadding = table.RightPadding = Unit.FromPoint(CellPadding);
        table.TopPadding = table.BottomPadding = Unit.FromPoint(CellPadding);
        table.Format.Font.Size = CardFontSize;

        Row? row = null;
        for (var index = 0; index < snapshot.Drawings.Length; index++)
        {
            if (index % CardsPerRow == 0)
            {
                if (row != null)
                {
                    var gap = table.AddRow();
                    gap.HeightRule = RowHeightRule.Exactly;
                    gap.Height = Unit.FromPoint(CardGap);
                    gap.TopPadding = gap.BottomPadding = Unit.Zero;
                }
                row = table.AddRow();
                row.VerticalAlignment = VerticalAlignment.Top;
            }
            AddPartCard(row!.Cells[index % CardsPerRow * 2], snapshot, snapshot.Drawings[index]);
        }
    }

    private static void AddPartCard(Cell cell, NestReportSnapshot snapshot, ReportDrawing drawing)
    {
        var context = $"Drawing {drawing.Id}";
        var inner = CardWidth - 2 * CellPadding;
        cell.Borders.Visible = true;
        cell.Borders.Width = 0.5;
        cell.Borders.Color = Colors.Gray;

        // ID on the left; a shortage or extra on the right. Complete parts show no status.
        var title = cell.AddParagraph();
        title.Format.Font.Bold = true;
        title.Format.Font.Size = BodyFontSize;
        title.Format.TabStops.AddTabStop(Unit.FromPoint(inner - 1), TabAlignment.Right);
        title.AddText(drawing.Id);
        if (drawing.Shortage > 0 || drawing.Extra > 0)
        {
            title.AddTab();
            var status = title.AddFormattedText(drawing.Shortage > 0
                ? $"SHORT {drawing.Shortage.ToString(Invariant)}"
                : $"EXTRA {drawing.Extra.ToString(Invariant)}");
            status.Color = drawing.Shortage > 0 ? Colors.DarkRed : Colors.Black;
            status.Font.Size = CardFontSize;
        }

        var picture = cell.AddParagraph();
        picture.Format.Alignment = ParagraphAlignment.Center;
        picture.Format.SpaceBefore = picture.Format.SpaceAfter = Unit.FromPoint(2);
        var image = picture.AddImage(Thumbnail(drawing.Geometry, inner, CardThumbnailHeight));
        image.Width = Unit.FromPoint(inner);
        image.Height = Unit.FromPoint(CardThumbnailHeight);

        var name = cell.AddParagraph();
        name.Format.Font.Bold = true;
        AddLines(name, ReportText.Cell(drawing.Name, CardFont(true), inner, context));

        var details = cell.AddParagraph();
        details.Format.SpaceBefore = Unit.FromPoint(1);
        string[] lines =
        [
            $"Size: {PartSize(snapshot, drawing.Geometry.Bounds)}",
            $"Weight: {PartWeight(snapshot, drawing)}",
            .. TotalWeight(snapshot, drawing),
            $"Required {drawing.Required.ToString(Invariant)}, nested {drawing.Nested.ToString(Invariant)}",
            $"Plates: {ReportText.Ranges(drawing.Plates)}",
        ];
        AddLines(details, lines.SelectMany(line => ReportText.Cell(line, CardFont(false), inner, context)).ToList());
    }

    private static string PartSize(NestReportSnapshot snapshot, ReportBounds bounds) =>
        $"{Dimension(bounds.Width)} x {Dimension(bounds.Length)} {snapshot.Units}";

    /// <summary>Bounding-box sizes to shop precision; sheet sizes keep <see cref="Number"/>.</summary>
    private static string Dimension(double value) => value.ToString("0.###", Invariant);

    private static string PartWeight(NestReportSnapshot snapshot, ReportDrawing drawing) =>
        drawing.Weight is { } each ? $"{Weight(each)} {snapshot.WeightUnits} ea" : "-";

    /// <summary>All nested copies; omitted when it would repeat the single-part weight.</summary>
    private static string[] TotalWeight(NestReportSnapshot snapshot, ReportDrawing drawing) =>
        drawing.Weight is { } each && drawing.Nested > 1
            ? [$"Total weight: {Weight(each * drawing.Nested)} {snapshot.WeightUnits}"]
            : [];

    private static string WeightNote(NestReportSnapshot snapshot) => snapshot.Thickness > 0
        ? "Weights need a material density (lb/in\u00B3); parts without one show \"-\"."
        : "Weights need the nest thickness and a material density (lb/in\u00B3); parts without them show \"-\".";

    /// <summary>Three significant figures for light parts, whole units from 1000 up.</summary>
    private static string Weight(double value) => value switch
    {
        < 10 => value.ToString("0.00", Invariant),
        < 1000 => value.ToString("0.0", Invariant),
        _ => value.ToString("#,0", Invariant),
    };

    private static void AddPlate(Document document, NestReportSnapshot snapshot, ReportPlate plate,
        DiagramView plan, List<(Table, Action<XGraphics, XRect>[])> areas)
    {
        var heading = $"Plate {plate.Number} of {snapshot.Plates.Length}";
        var header = new List<string> { $"Nest report: {snapshot.Name}", heading, SheetSize(snapshot, plate) };
        // Unset material/thickness are stated in the page body, not repeated in every header.
        if (snapshot.Material.Length + snapshot.Grade.Length > 0)
            header.Add(Material(snapshot));
        if (snapshot.Thickness > 0)
            header.Add(Thickness(snapshot));
        var section = AddSection(document, snapshot, Orientation.Landscape, LandscapeWidth, string.Join("  |  ", header));
        var title = section.AddParagraph(heading);
        title.Format.Font.Size = 13;
        title.Format.Font.Bold = true;

        var info = AddTable(section, 100, 260, 90, 270);
        info.Borders.Visible = false;
        AddInfoRow(info, "Nest:", snapshot.Name, "Sheet size:", SheetSize(snapshot, plate));
        AddInfoRow(info, "Material:", Material(snapshot), "Quantity:", plate.Copies.ToString(Invariant));
        AddInfoRow(info, "Thickness:", Thickness(snapshot), "Part spacing:",
            $"{Number(plate.PartSpacing)} {snapshot.Units}");
        AddInfoRow(info, "Parts per plate:", plate.Parts.Length.ToString(Invariant), "Utilization:",
            Percent(plate.Utilization) + " (net part area / full sheet)");
        if (plate.CutTime is { } cutTime)
            AddInfoRow(info, "Est. cut time:", Duration(cutTime) + " per plate", "Total cut time:",
                Duration(cutTime * plate.Copies) + (plate.Copies == 1 ? " (1 plate)" : $" ({plate.Copies} plates)"));

        var overview = AddReservedArea(section, plan.Size.Height);
        areas.Add((overview, [(gfx, _) => DrawRulers(gfx, plate, plan, snapshot.Units), (gfx, _) => DrawView(gfx, plate, plan)]));

        var notes = new List<string>();
        if (plate.Clamps.Length > 0)
            notes.Add(plate.Clamps.Any(clamp => clamp.Removed)
                ? "Hatched rectangles are machine clamps. A dashed rectangle crossed out is a clamp to remove before cutting."
                : "Hatched rectangles are machine clamps.");
        notes.Add($"Rulers show sheet coordinates in {UnitsName(snapshot)}.");
        for (var i = 0; i < notes.Count; i++)
        {
            var note = section.AddParagraph(notes[i]);
            note.Format.Font.Size = 7.5;
            if (i == 0)
                note.Format.SpaceBefore = Unit.FromPoint(4);
            if (i == notes.Count - 1)
                note.Format.SpaceAfter = Unit.FromPoint(4);
        }

        var names2 = snapshot.Drawings.ToDictionary(drawing => drawing.Id, drawing => drawing.Name, StringComparer.Ordinal);
        var perSheet = plate.Parts.GroupBy(part => part.ReportId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (long)group.Count(), StringComparer.Ordinal);
        var parts = AddTable(section, 60, 400, 110, 150);
        AddHeader(parts, "ID", "Drawing", "Parts per plate", $"Total ({plate.Copies} plates)");
        foreach (var drawing in snapshot.Drawings.Where(drawing => perSheet.ContainsKey(drawing.Id)))
        {
            var count = perSheet[drawing.Id];
            AddRow(parts, $"Plate {plate.Number}, drawing {drawing.Id}", false, drawing.Id, names2[drawing.Id],
                count.ToString(Invariant), checked(count * plate.Copies).ToString(Invariant));
        }
    }

    /// <summary>A fixed-height, never-split table row reserves a vector drawing area in MigraDoc's flow.</summary>
    private static Table AddReservedArea(Section section, double height)
    {
        var table = AddTable(section, LandscapeWidth);
        table.Borders.Visible = false;
        table.LeftPadding = table.RightPadding = Unit.Zero;
        table.TopPadding = table.BottomPadding = Unit.Zero;
        table.Format.SpaceBefore = Unit.FromPoint(4);
        var reserved = table.AddRow();
        reserved.HeightRule = RowHeightRule.Exactly;
        reserved.Height = Unit.FromPoint(height);
        return table;
    }

    private static IEnumerable<(int Page, XRect Area, Action<XGraphics, XRect>[] Layers)> LocateAreas(
        PdfDocumentRenderer renderer, int pageCount, List<(Table Table, Action<XGraphics, XRect>[] Layers)> areas)
    {
        var found = new (int Page, XRect Area)?[areas.Count];
        for (var page = 1; page <= pageCount; page++)
        {
            var renderInfos = renderer.DocumentRenderer?.GetRenderInfoFromPage(page)
                ?? throw new InvalidOperationException("MigraDoc render information is unavailable.");
            foreach (var info in renderInfos)
            {
                var index = areas.FindIndex(area => ReferenceEquals(area.Table, info.DocumentObject));
                if (index < 0)
                    continue;
                var table = areas[index].Table;
                // Only the page holding the reserved (last) row receives the drawing.
                if (info.FormatInfo is not TableFormatInfo format || format.EndRow != table.Rows.Count - 1)
                    continue;
                if (found[index] != null)
                    throw new InvalidOperationException($"Report drawing area {index + 1} was laid out twice.");
                var content = info.LayoutInfo.ContentArea;
                var height = table.Rows[table.Rows.Count - 1].Height.Point;
                found[index] = (page, new XRect(content.X.Point, content.Y.Point + content.Height.Point - height,
                    content.Width.Point, height));
            }
        }
        for (var index = 0; index < areas.Count; index++)
        {
            var (page, area) = found[index]
                ?? throw new InvalidOperationException($"Report drawing area {index + 1} was not laid out.");
            yield return (page, area, areas[index].Layers);
        }
    }

    private static void DrawView(XGraphics gfx, ReportPlate plate, DiagramView view)
    {
        var fit = view.Fit;
        gfx.DrawRectangle(new XPen(XColors.Black, 1), fit.Rect(plate.Bounds));
        // Active clamps lie under the parts; a removed clamp's area may hold parts, so its
        // cross goes on top where the operator sees it.
        foreach (var clamp in plate.Clamps.Where(clamp => !clamp.Removed))
            DrawClamp(gfx, fit.Rect(clamp.Bounds));
        var outline = new XPen(XColors.Black, 0.6);
        foreach (var part in plate.Parts)
            DrawGeometry(gfx, part.Geometry, fit, outline);
        var removed = new XPen(XColors.Black, 0.6) { DashPattern = [5, 3] };
        var cross = new XPen(XColors.Black, 0.4);
        foreach (var clamp in plate.Clamps.Where(clamp => clamp.Removed))
        {
            var box = fit.Rect(clamp.Bounds);
            gfx.DrawRectangle(removed, box);
            gfx.DrawLine(cross, box.TopLeft, box.BottomRight);
            gfx.DrawLine(cross, box.TopRight, box.BottomLeft);
        }
        // PDFsharp multiplies dash patterns by the pen width; the preset styles are so short at
        // these thin widths that they print as solid lines.
        var cutoff = new XPen(XColors.Black, 0.75) { DashPattern = [9, 3] };
        foreach (var geometry in plate.Cutoffs)
            StrokeContours(gfx, geometry, fit, cutoff);
        var groupPen = new XPen(XColors.Black, 0.6) { DashPattern = [1, 3] };
        foreach (var group in view.Groups)
            gfx.DrawRectangle(groupPen, group.Boundary);
        var font = NestReportDiagram.LabelFont();
        foreach (var label in view.Labels.Concat(view.Groups.Select(group => group.Label)))
        {
            gfx.DrawRectangle(XBrushes.White, label.Box);
            gfx.DrawString(label.Id, font, XBrushes.Black, label.Box, XStringFormats.Center);
        }
    }

    /// <summary>A machine clamp: white, hatched with diagonal lines, outlined.</summary>
    private static void DrawClamp(XGraphics gfx, XRect box)
    {
        const double spacing = 2.5;
        gfx.DrawRectangle(XBrushes.White, box);
        var state = gfx.Save();
        gfx.IntersectClip(box);
        var hatch = new XPen(XColors.Black, 0.35);
        for (var offset = -box.Height; offset < box.Width; offset += spacing)
            gfx.DrawLine(hatch, box.X + offset, box.Bottom, box.X + offset + box.Height, box.Y);
        gfx.Restore(state);
        gfx.DrawRectangle(new XPen(XColors.Black, 0.75), box);
    }

    /// <summary>
    /// A horizontal ruler under the drawing and a vertical one left of it, each spanning the
    /// sheet and reading its plate coordinates, with the units in the corner between them.
    /// </summary>
    private static void DrawRulers(XGraphics gfx, ReportPlate plate, DiagramView view, string units)
    {
        var fit = view.Fit;
        var rulers = view.Rulers;
        var sheet = plate.Bounds;
        var pen = new XPen(XColors.Black, 0.5);
        var font = NestReportDiagram.RulerFont();
        var textHeight = font.GetHeight();
        var low = fit.Point(sheet.Left, sheet.Top);
        var high = fit.Point(sheet.Right, sheet.Bottom);
        const double textBox = 100;

        var baseY = rulers.Horizontal.Top;
        var textY = baseY + NestReportDiagram.MajorTick + NestReportDiagram.RulerTextGap;
        gfx.DrawLine(pen, low.X, baseY, high.X, baseY);
        foreach (var value in rulers.Minors(sheet.Left, sheet.Right))
        {
            var x = fit.Point(value, 0).X;
            gfx.DrawLine(pen, x, baseY, x, baseY + NestReportDiagram.MinorTick);
        }
        foreach (var value in rulers.Majors(sheet.Left, sheet.Right))
        {
            var x = fit.Point(value, 0).X;
            gfx.DrawLine(pen, x, baseY, x, baseY + NestReportDiagram.MajorTick);
            gfx.DrawString(rulers.Format(value), font, XBrushes.Black,
                new XRect(x - textBox / 2, textY, textBox, textHeight), XStringFormats.TopCenter);
        }

        var baseX = rulers.Vertical.Right;
        var textRight = baseX - NestReportDiagram.MajorTick - NestReportDiagram.RulerTextGap;
        gfx.DrawLine(pen, baseX, low.Y, baseX, high.Y);
        foreach (var value in rulers.Minors(sheet.Bottom, sheet.Top))
        {
            var y = fit.Point(0, value).Y;
            gfx.DrawLine(pen, baseX, y, baseX - NestReportDiagram.MinorTick, y);
        }
        foreach (var value in rulers.Majors(sheet.Bottom, sheet.Top))
        {
            var y = fit.Point(0, value).Y;
            gfx.DrawLine(pen, baseX, y, baseX - NestReportDiagram.MajorTick, y);
            gfx.DrawString(rulers.Format(value), font, XBrushes.Black,
                new XRect(textRight - textBox, y - textHeight / 2, textBox, textHeight), XStringFormats.CenterRight);
        }
        gfx.DrawString(units, font, XBrushes.Black, new XRect(textRight - textBox, textY, textBox, textHeight),
            XStringFormats.TopRight);
    }

    private static string Thumbnail(ReportGeometry geometry, double width, double height)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(width);
        page.Height = XUnit.FromPoint(height);
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            var fit = Fit.Create(geometry.Bounds, new XRect(2, 2, width - 4, height - 4));
            DrawGeometry(gfx, geometry, fit, new XPen(XColors.Black, 0.5));
        }
        using var stream = new MemoryStream();
        document.Save(stream, false);
        // MigraDoc embeds a PDF image source as a vector form XObject, not a raster image.
        return "base64:" + Convert.ToBase64String(stream.ToArray());
    }

    private static void DrawGeometry(XGraphics gfx, ReportGeometry geometry, Fit fit, XPen pen)
    {
        if (geometry.StrokeOnly)
        {
            StrokeContours(gfx, geometry, fit, pen);
            return;
        }
        // Every contour is closed here, so CloseFigure separates the subpaths.
        var path = new XGraphicsPath();
        foreach (var contour in geometry.Contours)
        {
            AddContour(path, contour, fit);
            path.CloseFigure();
        }
        path.FillMode = XFillMode.Alternate; // Even-odd: holes stay unfilled.
        gfx.DrawPath(pen, new XSolidBrush(Fill), path);
    }

    /// <summary>
    /// PDFsharp's StartFigure does not emit a move-to after an open figure, which would draw a
    /// false segment across a tab gap to the next contour. Stroke each contour as its own path.
    /// </summary>
    private static void StrokeContours(XGraphics gfx, ReportGeometry geometry, Fit fit, XPen pen)
    {
        foreach (var contour in geometry.Contours)
        {
            var path = new XGraphicsPath();
            AddContour(path, contour, fit);
            if (contour.Closed)
                path.CloseFigure();
            gfx.DrawPath(pen, path);
        }
    }

    internal static void AddContour(XGraphicsPath path, ReportContour contour, Fit fit)
    {
        foreach (var segment in contour.Segments)
        {
            if (segment.Center == null)
            {
                path.AddLine(fit.Point(segment.Start), fit.Point(segment.End));
                continue;
            }
            var center = fit.Point(segment.Center);
            var radius = segment.Radius * fit.Scale;
            var box = new XRect(center.X - radius, center.Y - radius, 2 * radius, 2 * radius);
            // Page Y points down, so model angles and sweeps change sign.
            path.AddArc(box, -Degrees(segment.StartAngle), -Degrees(segment.SweepAngle));
        }
    }

    private static double Degrees(double radians) => radians * 180 / System.Math.PI;

    private static XFont BodyFont(bool bold) =>
        new(ReportFonts.Family, BodyFontSize, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);

    private static XFont CardFont(bool bold) =>
        new(ReportFonts.Family, CardFontSize, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);

    private static Section AddSection(Document document, NestReportSnapshot snapshot, Orientation orientation,
        double width, string header)
    {
        var section = document.AddSection();
        var setup = section.PageSetup;
        setup.PageFormat = PageFormat.Letter;
        setup.Orientation = orientation;
        setup.LeftMargin = setup.RightMargin = Unit.FromPoint(Margin);
        setup.BottomMargin = Unit.FromPoint(BottomMargin);
        setup.FooterDistance = Unit.FromPoint(20);

        // Every page, including continuations, identifies its job and plate. MigraDoc does not
        // push the body below a tall header, so size the top margin from the wrapped header.
        var headerFont = new XFont(ReportFonts.Family, HeaderFontSize);
        var lines = ReportText.Wrap(header, headerFont, width);
        if (lines.Count > MaxHeaderLines)
            throw new NotSupportedException($"Page header (nest name): text needs {lines.Count} lines; this report supports at most {MaxHeaderLines}.");
        setup.HeaderDistance = Unit.FromPoint(HeaderDistance);
        setup.TopMargin = Unit.FromPoint(HeaderDistance + lines.Count * headerFont.GetHeight() + 8);
        var top = section.Headers.Primary.AddParagraph();
        top.Format.Font.Size = HeaderFontSize;
        top.Format.Font.Color = Colors.DimGray;
        AddLines(top, lines);

        // When it was generated on the left, in this computer's time zone; the page on the right.
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.TabStops.AddTabStop(Unit.FromPoint(width), TabAlignment.Right);
        footer.AddText("Generated " + snapshot.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", Invariant));
        footer.AddTab();
        footer.AddText("Page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();
        footer.Format.Font.Size = 7.5;
        return section;
    }

    private static void AddHeading(Section section, string text)
    {
        var heading = section.AddParagraph(text);
        heading.Format.Font.Bold = true;
        heading.Format.Font.Size = 11;
        heading.Format.SpaceBefore = Unit.FromPoint(10);
        heading.Format.SpaceAfter = Unit.FromPoint(4);
        heading.Format.KeepWithNext = true;
    }

    private static Table AddTable(Section section, params double[] widths)
    {
        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = Colors.Gray;
        table.LeftPadding = table.RightPadding = Unit.FromPoint(CellPadding);
        table.TopPadding = table.BottomPadding = Unit.FromPoint(1.5);
        foreach (var width in widths)
            table.AddColumn(Unit.FromPoint(width));
        return table;
    }

    /// <summary>Heading rows repeat at the top of every continuation page.</summary>
    private static void AddHeader(Table table, params string[] cells)
    {
        var row = AddRow(table, "Table heading", true, cells);
        row.HeadingFormat = true;
        row.Format.Font.Bold = true;
        row.Shading.Color = Colors.LightGray;
    }

    private static void AddInfoRow(Table table, params string[] cells)
    {
        var row = table.AddRow();
        row.VerticalAlignment = VerticalAlignment.Top;
        for (var index = 0; index < cells.Length; index++)
        {
            var bold = index % 2 == 0;
            FillCell(table, row, index, cells[index], bold, $"{cells[index - index % 2].TrimEnd(':')} field");
            if (bold)
                row.Cells[index].Format.Font.Bold = true;
        }
    }

    /// <summary>Adds wrapped text cells; a null cell is left empty for non-text content.</summary>
    private static Row AddRow(Table table, string context, bool bold, params string?[] cells)
    {
        var row = table.AddRow();
        row.VerticalAlignment = VerticalAlignment.Center;
        for (var index = 0; index < cells.Length; index++)
        {
            if (cells[index] != null)
                FillCell(table, row, index, cells[index]!, bold, context);
        }
        return row;
    }

    private static void FillCell(Table table, Row row, int index, string text, bool bold, string context)
    {
        var width = table.Columns[index].Width.Point - table.LeftPadding.Point - table.RightPadding.Point;
        AddLines(row.Cells[index].AddParagraph(), ReportText.Cell(text, BodyFont(bold), width, context));
    }

    private static void AddLines(Paragraph paragraph, IReadOnlyList<string> lines)
    {
        for (var line = 0; line < lines.Count; line++)
        {
            if (line > 0)
                paragraph.AddLineBreak();
            paragraph.AddText(lines[line]);
        }
    }

    private static string UnitsName(NestReportSnapshot snapshot) =>
        snapshot.Units == "mm" ? "millimeters (mm)" : "inches (in)";

    private static string Material(NestReportSnapshot snapshot)
    {
        var material = $"{snapshot.Material} {snapshot.Grade}".Trim();
        return material.Length == 0 ? "Not specified" : material;
    }

    // Zero thickness means "unset" in a new nest; never present it as a genuine value.
    // The machine config's name for the thickness leads: "16 ga - 0.0598 in".
    private static string Thickness(NestReportSnapshot snapshot) =>
        snapshot.Thickness <= 0 ? "Not specified"
        : snapshot.ThicknessName.Length > 0 ? $"{snapshot.ThicknessName} - {Number(snapshot.Thickness)} {snapshot.Units}"
        : $"{Number(snapshot.Thickness)} {snapshot.Units}";

    private static string SheetSize(NestReportSnapshot snapshot, ReportPlate plate) =>
        $"{Number(plate.Bounds.Width)} x {Number(plate.Bounds.Length)} {snapshot.Units}";

    private static string Number(double value) => value.ToString("0.####", Invariant);

    private static string PlateCutTime(ReportPlate plate) =>
        plate.CutTime is not { } time ? ""
        : plate.Copies == 1 ? Duration(time)
        : $"{Duration(time)} each\n{Duration(time * plate.Copies)} total";

    /// <summary>Whole seconds as "1h 02m 03s", "4m 05s" or "45s".</summary>
    private static string Duration(TimeSpan time)
    {
        var seconds = (long)System.Math.Round(time.TotalSeconds);
        var hours = seconds / 3600;
        var minutes = seconds / 60 % 60;
        var rest = seconds % 60;
        return hours > 0 ? $"{hours}h {minutes:00}m {rest:00}s"
            : minutes > 0 ? $"{minutes}m {rest:00}s"
            : $"{rest}s";
    }

    private static string Percent(double value) => (value * 100).ToString("0.0", Invariant) + "%";
}
