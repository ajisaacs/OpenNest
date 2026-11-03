using System.Globalization;
using OpenNest.Geometry;

namespace OpenNest.Data.MaterialLedger;

/// <summary>
/// The MaterialLedger stock one released nest is offered: the rows Auto Nest's ledger load
/// would tick (substitutes included). An empty offer nests on the plate defaults alone, and
/// <see cref="NotUsed"/> says why the ledger was not used.
/// </summary>
public sealed record ReleaseLedgerOffer(IReadOnlyList<LedgerStockRow> Rows, string? NotUsed)
{
    public static ReleaseLedgerOffer None(string reason) =>
        new(Array.Empty<LedgerStockRow>(), reason);

    /// <summary>Why codes on other nests were not left out of the rows, or null.</summary>
    public string? OtherNestsUnchecked { get; init; }

    /// <summary>
    /// The rows as engine stock: finite, under their <c>ledger:{n}</c> ids, no costs.
    /// </summary>
    public IReadOnlyList<PlateOption> Options { get; } =
        (Rows ?? throw new ArgumentNullException(nameof(Rows)))
        .Select(row => new PlateOption
        {
            Width = row.Size.Width,
            Length = row.Size.Length,
            Quantity = row.Quantity,
            Id = row.Id,
        })
        .ToList();
}

/// <summary>
/// MaterialLedger stock for one Make Nests run. Each nest is offered the free sheets and
/// drops of its material, grade and thickness, minus codes on other open server nests (read
/// once per run) and the sheets nests earlier in the same run took: those are not on the
/// server until the release uploads them. Nothing here throws for an unusable ledger; the
/// offer says so.
/// </summary>
public sealed class ReleaseLedgerStock
{
    /// <summary>Reads the free stock for a material, grade and thickness in inches.</summary>
    public delegate Task<LedgerStockResult> StockReader(string material, string grade,
        double thicknessIn, CancellationToken cancellationToken);

    private readonly StockReader _readStock;
    private readonly Func<CancellationToken, Task<LedgerCodesInUse>> _readCodesInUse;
    // Sheets taken per code: a bundle repeats its one code per sheet.
    private readonly Dictionary<string, int> _taken = new(StringComparer.Ordinal);
    private LedgerCodesInUse? _codesInUse;

    public ReleaseLedgerStock(StockReader readStock,
        Func<CancellationToken, Task<LedgerCodesInUse>> readCodesInUse)
    {
        _readStock = readStock ?? throw new ArgumentNullException(nameof(readStock));
        _readCodesInUse = readCodesInUse
            ?? throw new ArgumentNullException(nameof(readCodesInUse));
    }

    /// <summary>The sheets nests of this run have taken so far, per code.</summary>
    public IReadOnlyDictionary<string, int> Taken =>
        new Dictionary<string, int>(_taken, StringComparer.Ordinal);

    /// <summary>
    /// The stock to offer <paramref name="nest"/>, whose drawings carry their release
    /// quantities.
    /// </summary>
    public async Task<ReleaseLedgerOffer> OfferAsync(Nest nest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nest);
        var material = nest.Material?.Name?.Trim() ?? "";
        if (material.Length == 0 || !(nest.Thickness > 0))
            return ReleaseLedgerOffer.None("the nest has no material or thickness");

        var grade = nest.Material?.Grade?.Trim() ?? "";
        var thicknessIn = LedgerStockMapper.ThicknessInches(nest.Thickness, nest.Units);
        var result = await _readStock(material, grade, thicknessIn, cancellationToken);
        var described = Describe(nest, material, grade);
        switch (result)
        {
            case LedgerStockResult.Ok ok:
                var mapping = LedgerStockMapper.Map(ok.Items, nest.Units, PartEnvelopes(nest),
                    ok.Specs);
                _codesInUse ??= await _readCodesInUse(cancellationToken);
                if (_codesInUse.IsKnown)
                    mapping = LedgerCodeFilter.Exclude(mapping, _codesInUse.Codes);
                var rows = LedgerCodeFilter.ExcludeCopies(mapping.Rows, _taken, out var taken);
                if (rows.Count == 0)
                    return ReleaseLedgerOffer.None(Unusable(described, mapping, taken));
                return new ReleaseLedgerOffer(rows, null)
                {
                    OtherNestsUnchecked = _codesInUse.Unavailable,
                };
            case LedgerStockResult.Unmapped:
                return ReleaseLedgerOffer.None($"no mapping for {described}");
            case LedgerStockResult.Unauthorized:
                return ReleaseLedgerOffer.None("it rejected the API key (Tools > Options)");
            case LedgerStockResult.Unreachable unreachable:
                return ReleaseLedgerOffer.None($"unreachable: {unreachable.Reason.TrimEnd('.')}");
            default:
                return ReleaseLedgerOffer.None("not set up on this PC (Tools > Options)");
        }
    }

    /// <summary>
    /// Gives the plates just committed on <paramref name="offer"/>'s rows their item codes, as
    /// Auto Nest does, and keeps those sheets from later nests of this run. Call it inside the
    /// commit's undo step. Returns each assigned code's ledger location, for display.
    /// </summary>
    /// <param name="committed">
    /// Each plate the commit attached, with the engine stock id it was solved on.
    /// </param>
    public IReadOnlyDictionary<string, string> AssignCodes(Nest nest, ReleaseLedgerOffer offer,
        IReadOnlyList<(Plate Plate, string StockId)> committed)
    {
        ArgumentNullException.ThrowIfNull(nest);
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(committed);
        var locations = new Dictionary<string, string>(StringComparer.Ordinal);
        LedgerStockAssignment.Assign(nest.Plates, committed, offer.Rows);
        foreach (var (plate, _) in committed)
        {
            foreach (var code in plate.StockItemCodes)
            {
                _taken[code] = _taken.GetValueOrDefault(code) + 1;
                var row = offer.Rows.FirstOrDefault(r => r.ItemLocations.ContainsKey(code));
                if (row != null)
                    locations[code] = row.ItemLocations[code];
            }
        }
        return locations;
    }

    /// <summary>
    /// The release note for a nest nested on <paramref name="offer"/>: how many sheets came
    /// from the ledger, or why it was not used.
    /// </summary>
    /// <param name="sheets">The ledger codes <see cref="AssignCodes"/> gave the nest.</param>
    /// <param name="layoutNotKept">
    /// True when Auto Nest could not keep a layout on the offer.
    /// </param>
    public static string Note(ReleaseLedgerOffer offer, int sheets, bool layoutNotKept)
    {
        ArgumentNullException.ThrowIfNull(offer);
        if (offer.NotUsed != null)
            return $"MaterialLedger not used: {offer.NotUsed}.";

        var note = layoutNotKept
            ? "MaterialLedger not used: Auto Nest could not keep a layout on its stock."
            : sheets == 0
                ? "No MaterialLedger stock used: Auto Nest placed nothing on it."
                : string.Format(CultureInfo.CurrentCulture,
                    "{0} sheet{1} from MaterialLedger stock.", sheets, sheets == 1 ? "" : "s");
        return offer.OtherNestsUnchecked == null
            ? note
            : $"{note} Codes on other nests were not checked: {offer.OtherNestsUnchecked}.";
    }

    // Why no row is left, in the counts Auto Nest's ledger summary uses.
    private static string Unusable(string described, LedgerStockMapping mapping, int taken)
    {
        var reasons = new List<string>();
        if (mapping.OnOtherNests > 0)
            reasons.Add($"{mapping.OnOtherNests} on other nests");
        if (taken > 0)
            reasons.Add($"{taken} taken by earlier nests of this release");
        if (mapping.SkippedTooSmall > 0)
            reasons.Add($"{mapping.SkippedTooSmall} too small for any part");
        if (mapping.SkippedUnknownShape > 0)
            reasons.Add($"{mapping.SkippedUnknownShape} of unknown shape");
        return reasons.Count == 0
            ? $"no free stock of {described}"
            : $"no usable stock of {described} ({string.Join(", ", reasons)})";
    }

    private static List<Size> PartEnvelopes(Nest nest)
    {
        var envelopes = new List<Size>();
        var drawings = nest.Drawings
            .Where(d => !d.IsCutOff && d.Quantity.Required > 0 && d.Program != null);
        foreach (var drawing in drawings)
        {
            var box = drawing.Program.BoundingBox();
            if (double.IsFinite(box.Width) && double.IsFinite(box.Length)
                && box.Width > 0 && box.Length > 0)
                envelopes.Add(new Size(box.Width, box.Length));
        }
        return envelopes;
    }

    private static string Describe(Nest nest, string material, string grade)
    {
        var unit = nest.Units == Units.Millimeters ? "mm" : "in";
        return FormattableString.Invariant(
            $"{material}{(grade.Length == 0 ? "" : " " + grade)} {nest.Thickness:G} {unit}");
    }
}
