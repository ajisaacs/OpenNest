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

    public ThicknessConfig? GetParameters(string material, double thickness) =>
        GetParameters(material, null, thickness);

    public ThicknessConfig? GetParameters(string material, string? grade, double thickness)
    {
        var mat = grade is null ? GetMaterial(material) : GetMaterial(material, grade);
        return mat?.Thicknesses.FirstOrDefault(t => t.Value.IsEqualTo(thickness));
    }

    /// <summary>
    /// Returns the first material matching <paramref name="name"/>, regardless of
    /// grade. Use <see cref="GetMaterial(string, string)"/> when the material may
    /// have more than one grade on file.
    /// </summary>
    public MaterialConfig? GetMaterial(string name) =>
        Materials.FirstOrDefault(m => SharedListNames.Key(m.Name) == SharedListNames.Key(name));

    public MaterialConfig? GetMaterial(string name, string grade) =>
        Materials.FirstOrDefault(m =>
            SharedListNames.Key(m.Name) == SharedListNames.Key(name)
            && SharedListNames.Key(m.Grade) == SharedListNames.Key(grade)
        );

    /// <summary>
    /// Returns every grade on file for the given material name.
    /// </summary>
    public IEnumerable<MaterialConfig> GetGrades(string name) =>
        Materials.Where(m => SharedListNames.Key(m.Name) == SharedListNames.Key(name));
}
