using System.Text.Json;

namespace OpenNest.Data;

/// <summary>
/// The machine configurations shared through the nest server, in the same JSON as
/// <see cref="LocalJsonProvider"/>'s files. Reads come from the store's last-read copy; saves
/// and deletes go to the server and throw <see cref="IOException"/> when it cannot take them.
/// </summary>
public sealed class SharedMachineProvider(SharedSettingsStore store) : IDataProvider
{
    public IReadOnlyList<MachineSummary> GetMachines() =>
        store.List(SharedSettingKinds.Machine)
            .Select(Read)
            .OfType<MachineConfig>()
            .Select(machine => new MachineSummary(machine.Id, machine.Name))
            .ToList();

    public MachineConfig? GetMachine(Guid id) =>
        store.Get(SharedSettingKinds.Machine, id.ToString("D")) is { } record ? Read(record) : null;

    public void SaveMachine(MachineConfig machine) =>
        store.Save(SharedSettingKinds.Machine, machine.Id.ToString("D"), Serialize(machine));

    public void DeleteMachine(Guid id) => store.Delete(SharedSettingKinds.Machine, id.ToString("D"));

    /// <summary>A machine's shared document, the same JSON its local file holds.</summary>
    public static string Serialize(MachineConfig machine) => JsonSerializer.Serialize(machine, LocalJsonProvider.JsonOptions);

    private static MachineConfig? Read(SharedSettingRecord record)
    {
        try
        {
            return JsonSerializer.Deserialize<MachineConfig>(record.Json, LocalJsonProvider.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
