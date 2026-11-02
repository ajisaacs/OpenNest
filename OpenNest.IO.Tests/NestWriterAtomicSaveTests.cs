namespace OpenNest.IO.Tests;

/// <summary>
/// Saving over a nest file must never leave it truncated: a write that fails part way keeps
/// the previously saved nest.
/// </summary>
public class NestWriterAtomicSaveTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "opennest-atomic-save-" + Guid.NewGuid());

    public NestWriterAtomicSaveTests() => Directory.CreateDirectory(directory);

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void FailedWrite_KeepsThePreviousFile()
    {
        var path = Path.Combine(directory, "job.nest");
        var nest = new Nest("Good");
        var drawing = TestDrawings.Square();
        nest.Drawings.Add(drawing);
        var plate = new Plate(48, 96);
        plate.Parts.Add(new Part(drawing));
        nest.Plates.Add(plate);
        Assert.True(new NestWriter(nest).Write(path));
        var saved = File.ReadAllBytes(path);

        // JSON has no NaN, so serializing this plate throws part way through the archive.
        nest.Plates[0].PartSpacing = double.NaN;
        Assert.ThrowsAny<Exception>(() => new NestWriter(nest).Write(path));

        Assert.Equal(saved, File.ReadAllBytes(path));
        Assert.Equal(new[] { path }, Directory.GetFiles(directory));
    }

    [Fact]
    public void Write_ReplacesAnExistingFile()
    {
        var path = Path.Combine(directory, "job.nest");
        File.WriteAllText(path, "old");

        Assert.True(new NestWriter(new Nest("New")).Write(path));

        using var stream = File.OpenRead(path);
        Assert.Equal("New", new NestReader(stream).Read().Name);
        Assert.Equal(new[] { path }, Directory.GetFiles(directory));
    }
}
