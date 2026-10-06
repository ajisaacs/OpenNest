using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.WinForms.Tests.Forms;

/// <summary>Private, synthetic files only; removed after each acceptance case.</summary>
internal sealed class WindowsAcceptanceFiles : IDisposable
{
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), "opennest-acceptance-" + Guid.NewGuid().ToString("N"));

    public WindowsAcceptanceFiles()
    {
        Directory.CreateDirectory(Folder);
    }

    public string WriteSquare(string name)
    {
        var shape = new Shape();
        shape.Entities.Add(new Line(new Vector(0, 0), new Vector(2, 0)));
        shape.Entities.Add(new Line(new Vector(2, 0), new Vector(2, 2)));
        shape.Entities.Add(new Line(new Vector(2, 2), new Vector(0, 2)));
        shape.Entities.Add(new Line(new Vector(0, 2), new Vector(0, 0)));
        var path = Path.Combine(Folder, name + ".dxf");
        Dxf.ExportProgram(ConvertGeometry.ToProgram(shape), path);
        Assert.True(File.Exists(path));
        return path;
    }

    public Nest RoundTrip(Nest nest)
    {
        var path = Path.Combine(Folder, "acceptance.nest");
        Assert.True(new NestWriter(nest).Write(path));
        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 0);
        using var stream = File.OpenRead(path);
        var reader = new NestReader(stream);
        var restored = reader.Read();
        Assert.Empty(reader.Warnings);
        Assert.NotSame(nest, restored);
        return restored;
    }

    public void Dispose()
    {
        Directory.Delete(Folder, recursive: true);
    }
}
