using OpenNest.Math;

namespace OpenNest.Data;

public class MachineConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    public MachineType Type { get; set; } = MachineType.Laser;
    public UnitSystem Units { get; set; } = UnitSystem.Inches;
    public List<MaterialConfig> Materials { get; set; } = new();

    public ThicknessConfig? GetParameters(string material, double thickness)
    {
        var mat = GetMaterial(material);
        if (mat is null) return null;
        return mat.Thicknesses.FirstOrDefault(t => t.Value.IsEqualTo(thickness));
    }

    public MaterialConfig? GetMaterial(string name)
    {
        return Materials.FirstOrDefault(m =>
            string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
    }
}
