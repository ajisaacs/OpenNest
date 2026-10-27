namespace OpenNest.Data;

/// <summary>
/// Moves the single legacy clamps.json (or its shared copy) onto a machine once. The legacy
/// rail is copied only while no machine has clamps of its own; after that it is never read.
/// </summary>
public static class LegacyClampMigration
{
    /// <summary>
    /// Whether <paramref name="legacy"/> (null when there is no legacy file) still has to be
    /// copied: it lists at least one clamp and none of <paramref name="machines"/> has clamps yet.
    /// </summary>
    public static bool IsNeeded(ClampSettings? legacy, IReadOnlyCollection<MachineConfig> machines) =>
        legacy is { ClampCount: > 0 } && machines.Count > 0 && machines.All(m => m.Clamps == null);

    /// <summary>
    /// The machine that takes the legacy rail without asking: the nest defaults' machine when it
    /// is on file, otherwise the only machine. Null when there are several and none is the
    /// default, so the user has to choose.
    /// </summary>
    public static MachineConfig? Target(IReadOnlyCollection<MachineConfig> machines, Guid? defaultMachineId) =>
        machines.FirstOrDefault(m => m.Id == defaultMachineId)
        ?? (machines.Count == 1 ? machines.First() : null);

    /// <summary>Gives <paramref name="machine"/> the legacy rail, converted to the machine's units.</summary>
    public static void Apply(ClampSettings legacy, MachineConfig machine)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        ArgumentNullException.ThrowIfNull(machine);
        machine.Clamps = MachineClamps.FromSettings(legacy, machine.Units);
    }
}
