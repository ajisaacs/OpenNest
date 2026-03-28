namespace OpenNest.Data;

public interface IDataProvider
{
    IReadOnlyList<MachineSummary> GetMachines();
    MachineConfig? GetMachine(Guid id);
    void SaveMachine(MachineConfig machine);
    void DeleteMachine(Guid id);
}
