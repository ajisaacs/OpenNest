namespace OpenNest.Mcp.Database;

/// <summary>A drawing set and how many kits of it.</summary>
public sealed class ReleaseSetInput
{
    /// <summary>The set's id, or its exact name.</summary>
    public string Set { get; set; }

    public int Quantity { get; set; } = 1;
}

/// <summary>A library drawing (released on its own, or a set member) and how many.</summary>
public sealed class DrawingQuantityInput
{
    /// <summary>The drawing's id, or its exact name.</summary>
    public string Drawing { get; set; }

    public int Quantity { get; set; } = 1;
}

/// <summary>Merges one material group of a release into an existing Draft nest.</summary>
public sealed class MergeInput
{
    /// <summary>The group key plan_release prints.</summary>
    public string Group { get; set; }

    /// <summary>The draft nest's id, or its exact name.</summary>
    public string Draft { get; set; }
}
