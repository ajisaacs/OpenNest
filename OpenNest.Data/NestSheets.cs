namespace OpenNest.Data;

/// <summary>
/// The sheets a saved nest needs and the parts it places, returned by
/// <c>GET /api/nests/{id}/sheets</c>. Read from the stored archive at <see cref="Revision"/>.
/// </summary>
public sealed class NestSheets
{
    public Guid NestId { get; init; }

    public int Revision { get; init; }

    /// <summary>One entry per distinct sheet size, ordered by width, then length.</summary>
    public IReadOnlyList<NestSheetSize> Sheets { get; init; } = Array.Empty<NestSheetSize>();

    /// <summary>One entry per drawing, including drawings with nothing nested, ordered by name.</summary>
    public IReadOnlyList<NestPartCount> Parts { get; init; } = Array.Empty<NestPartCount>();

    /// <summary>One entry per plate in nest order, the same plates <see cref="Sheets"/> groups.</summary>
    public IReadOnlyList<NestPlateSheet> Plates { get; init; } = Array.Empty<NestPlateSheet>();
}

/// <summary>
/// One plate of the nest: its size as stored with <see cref="Width"/> the shorter side, how many
/// times it is cut, and the inventory item each of those sheets is cut from.
/// </summary>
public sealed class NestPlateSheet
{
    /// <summary>The plate's 0-based position in the nest.</summary>
    public int Index { get; init; }

    public double Width { get; init; }

    public double Length { get; init; }

    public int Quantity { get; init; }

    /// <summary>
    /// Entry i is the inventory item code of sheet i; sheets past the end are uncoded, and a
    /// bundle used for several sheets repeats its code. Empty when no sheet is coded.
    /// </summary>
    public IReadOnlyList<string> StockItemCodes { get; init; } = Array.Empty<string>();
}

/// <summary>A sheet size as stored, with <see cref="Width"/> the shorter side.</summary>
public sealed class NestSheetSize
{
    public double Width { get; init; }

    public double Length { get; init; }

    /// <summary>Sheets of this size to cut: the sum of each such plate's quantity.</summary>
    public int Quantity { get; init; }
}

/// <summary>How many of one drawing the nest requires and how many it places.</summary>
public sealed class NestPartCount
{
    public string Name { get; init; } = "";

    public int Required { get; init; }

    /// <summary>Parts placed across all plates, each plate counted by its quantity.</summary>
    public int Nested { get; init; }
}
