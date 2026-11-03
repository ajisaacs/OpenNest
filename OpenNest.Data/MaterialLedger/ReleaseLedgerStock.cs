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
    public IReadOnlyList<PlateOption> Options => Rows
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
/// once per run) and codes nests earlier in the same run took: those are not on the server
/// until the release uploads them. Nothing here throws for an unusable ledger; the offer
/// says so.
/// </summary>
public sealed class ReleaseLedgerStock
{
    /// <summary>Reads the free stock for a material, grade and thickness in inches.</summary>
    public delegate Task<LedgerStockResult> StockReader(string material, string grade,
        double thicknessIn, CancellationToken cancellationToken);

    private readonly StockReader _readStock;
    private readonly Func<CancellationToken, Task<LedgerCodesInUse>> _readCodesInUse;
    private readonly HashSet<string> _taken = new(StringComparer.Ordinal);
    private LedgerCodesInUse? _codesInUse;

    public ReleaseLedgerStock(StockReader readStock,
        Func<CancellationToken, Task<LedgerCodesInUse>> readCodesInUse)
    {
        _readStock = readStock ?? throw new ArgumentNullException(nameof(readStock));
        _readCodesInUse = readCodesInUse
            ?? throw new ArgumentNullException(nameof(readCodesInUse));
    }

    /// <summary>The codes nests of this run have taken so far.</summary>
    public IReadOnlyCollection<string> Taken => _taken;

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
        switch (result)
        {
            case LedgerStockResult.Ok ok:
                var mapping = LedgerStockMapper.Map(ok.Items, nest.Units, PartEnvelopes(nest),
                    ok.Specs);
                _codesInUse ??= await _readCodesInUse(cancellationToken);
                if (_codesInUse.IsKnown)
                    mapping = LedgerCodeFilter.Exclude(mapping, _codesInUse.Codes);
                var rows = LedgerCodeFilter.Exclude(mapping.Rows, _taken, out _);
                if (rows.Count == 0)
                    return ReleaseLedgerOffer.None(
                        $"no free stock of {Describe(nest, material, grade)}");
                return new ReleaseLedgerOffer(rows, null)
                {
                    OtherNestsUnchecked = _codesInUse.Unavailable,
                };
            case LedgerStockResult.Unmapped:
                return ReleaseLedgerOffer.None($"no mapping for {Describe(nest, material, grade)}");
            case LedgerStockResult.Unauthorized:
                return ReleaseLedgerOffer.None("it rejected the API key (Tools > Options)");
            case LedgerStockResult.Unreachable unreachable:
                return ReleaseLedgerOffer.None($"unreachable: {unreachable.Reason}");
            default:
                return ReleaseLedgerOffer.None("not set up on this PC (Tools > Options)");
        }
    }

    /// <summary>
    /// Gives the plates the release committed on <paramref name="offer"/>'s rows their item
    /// codes, as Auto Nest does, keeps those codes from later nests of this run, and returns
    /// the note for the release: how many sheets came from the ledger, or why it was not used.
    /// </summary>
    /// <param name="committed">
    /// Each committed plate with the engine stock id it was solved on.
    /// </param>
    public string Assign(Nest nest, ReleaseLedgerOffer offer,
        IReadOnlyList<(Plate Plate, string StockId)> committed)
    {
        ArgumentNullException.ThrowIfNull(nest);
        ArgumentNullException.ThrowIfNull(offer);
        ArgumentNullException.ThrowIfNull(committed);
        if (offer.NotUsed != null)
            return $"MaterialLedger not used: {offer.NotUsed}.";

        LedgerStockAssignment.Assign(nest.Plates, committed, offer.Rows);
        var sheets = 0;
        foreach (var (plate, stockId) in committed)
        {
            _taken.UnionWith(plate.StockItemCodes);
            if (LedgerStockMapper.IsLedgerId(stockId))
                sheets += plate.Quantity;
        }

        var note = sheets == 0
            ? "No MaterialLedger stock used."
            : string.Format(CultureInfo.CurrentCulture, "{0} sheet{1} from MaterialLedger stock.",
                sheets, sheets == 1 ? "" : "s");
        return offer.OtherNestsUnchecked == null
            ? note
            : $"{note} Codes on other nests were not checked: {offer.OtherNestsUnchecked}.";
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
