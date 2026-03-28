using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenNest.Data;

public class LocalJsonProvider : IDataProvider
{
    private readonly string _directory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public LocalJsonProvider(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public IReadOnlyList<MachineSummary> GetMachines()
    {
        var summaries = new List<MachineSummary>();
        foreach (var file in Directory.GetFiles(_directory, "*.json"))
        {
            var machine = ReadFile(file);
            if (machine is not null)
                summaries.Add(new MachineSummary(machine.Id, machine.Name));
        }
        return summaries;
    }

    public MachineConfig? GetMachine(Guid id)
    {
        var path = GetPath(id);
        return File.Exists(path) ? ReadFile(path) : null;
    }

    public void SaveMachine(MachineConfig machine)
    {
        var json = JsonSerializer.Serialize(machine, JsonOptions);
        var path = GetPath(machine.Id);
        WriteWithRetry(path, json);
    }

    public void DeleteMachine(Guid id)
    {
        var path = GetPath(id);
        if (File.Exists(path))
            File.Delete(path);
    }

    private string GetPath(Guid id) => Path.Combine(_directory, $"{id}.json");

    private static MachineConfig? ReadFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<MachineConfig>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void WriteWithRetry(string path, string json, int maxRetries = 3)
    {
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                File.WriteAllText(path, json);
                return;
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                Thread.Sleep(100);
            }
        }
    }
}
