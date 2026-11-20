namespace OpenNest.Engine.NestingEngines.Irregular;

/// <summary>Direction the packing front sweeps across the sheet (the free strip is left behind it).</summary>
internal enum PackAxis
{
    /// <summary>Front moves in +X; parts settle toward low X, then low Y.</summary>
    X,

    /// <summary>Front moves in +Y; parts settle toward low Y, then low X.</summary>
    Y,
}
