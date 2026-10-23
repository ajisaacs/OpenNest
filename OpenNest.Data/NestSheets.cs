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
