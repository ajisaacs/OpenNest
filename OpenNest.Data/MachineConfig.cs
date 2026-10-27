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

    /// <summary>Acceleration limits; null until entered, when <see cref="GetMotionOrPlaceholder"/> stands in.</summary>
    public MotionConfig? Motion { get; set; }

    /// <summary>
    /// The support grid nests assigned this machine rest on; null until saved from
    /// Nest &gt; Support Grid, when the legacy shared support-grid.json stands in.
    /// </summary>
    public SupportGridSettings? SupportGrid { get; set; }

    /// <summary>
    /// The edge-clamp rail new nests assigned this machine receive, in <see cref="Units"/>; null
    /// until set (the machine then gives nests no clamps).
    /// </summary>
    public MachineClamps? Clamps { get; set; }

    public MotionConfig GetMotionOrPlaceholder() => Motion ?? MotionConfig.Placeholder(Units);

    /// <summary>The machine's clamps in its units; off, at the default clamp size, when it has none.</summary>
    public ClampSettings GetClampSettings() =>
        Clamps?.ToSettings(Units) ?? ClampSettings.FromRail(null, MachineClamps.UnitsOf(Units), enabled: false);

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
