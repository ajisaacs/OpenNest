using OpenNest.Data;
using OpenNest.Geometry;

namespace OpenNest.Desktop.Tests;

/// <summary>
/// File > New in both desktop apps: the nest starts from the nest defaults and the default
/// machine, and takes the year's next name from the nest server in Database mode or this PC's
/// counter, which the WinForms and Avalonia apps share in user-settings.json.
/// </summary>
public sealed class NewNestSourceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 13, 9, 30, 0);

    private readonly string directory = Path.Combine(Path.GetTempPath(), "OpenNestDesktopTests", Guid.NewGuid().ToString("N"));
    private readonly MemoryNewNestSettings user = new();

    public NewNestSourceTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
    }

    private string MachinesDirectory => Path.Combine(directory, "Machines");

    private string DefaultsPath => Path.Combine(directory, "defaults.json");

    private string UserSettingsPath => Path.Combine(directory, "user-settings.json");

    private MachineSettingsSource Machines() =>
        new(new SharedSettingsSource(() => new NestStorageSettings(), _ => throw new InvalidOperationException("File mode")),
            new MemoryUserSettings(), MachinesDirectory, DefaultsPath,
            Path.Combine(directory, "clamps.json"), Path.Combine(directory, "grid.json"));

    private NewNestSource Source(INewNestUserSettings? settings = null, Func<INestRepository?>? repository = null) =>
        new(Machines(), settings ?? user, repository!, () => Now);

    [Fact]
    public async Task FileMode_NamesFromThisPcsCounterAndAdvancesIt()
    {
        user.NestNumber = 5;
        user.LastNestCreatedDate = new DateTime(2026, 1, 2);

        var created = await Source().CreateAsync();

        Assert.Equal(NestNames.Format(2026, 5), created.Nest.Name);
        Assert.Equal(6, user.NestNumber);
        Assert.Equal(Now.Date, user.LastNestCreatedDate);
        Assert.True(user.Saves > 0);
        Assert.Equal(Now, created.Nest.DateCreated);
        Assert.Null(created.Warning);
    }

    [Fact]
    public async Task FileMode_RestartsTheCounterInANewYear()
    {
        user.NestNumber = 40;
        user.LastNestCreatedDate = new DateTime(2025, 12, 31);

        var created = await Source().CreateAsync();

        Assert.Equal(NestNames.Format(2026, 1), created.Nest.Name);
        Assert.Equal(2, user.NestNumber);
    }

    [Fact]
    public async Task DatabaseMode_TheServerNumbersItAndTheCounterStaysAbove()
    {
        user.NestNumber = 3;
        user.LastNestCreatedDate = Now.Date;
        var repository = new NumberingRepository { NextNumber = 17 };

        var created = await Source(repository: () => repository).CreateAsync();

        Assert.Equal(NestNames.Format(2026, 17), created.Nest.Name);
        Assert.Equal(18, user.NestNumber);
    }

    [Fact]
    public async Task DatabaseMode_AnUnreachableServerFallsBackToTheCounter()
    {
        user.NestNumber = 3;
        user.LastNestCreatedDate = Now.Date;
        var repository = new NumberingRepository { Failure = new HttpRequestException("offline") };

        var created = await Source(repository: () => repository).CreateAsync();

        Assert.Equal(NestNames.Format(2026, 3), created.Nest.Name);
        Assert.Equal(4, user.NestNumber);
    }

    [Fact]
    public async Task StartsFromTheNestDefaultsAndTheDefaultMachine()
    {
        var machine = new MachineConfig { Name = "Fiber" };
        new LocalJsonProvider(MachinesDirectory).SaveMachine(machine);
        new NestDefaults { Units = Units.Millimeters, Size = new Size(3000, 1500), PartSpacing = 4, MachineId = machine.Id }
            .Save(DefaultsPath);

        var nest = (await Source().CreateAsync()).Nest;

        Assert.Equal(Units.Millimeters, nest.Units);
        Assert.Equal(new Size(3000, 1500), nest.PlateDefaults.Size);
        Assert.Equal(4, nest.PlateDefaults.PartSpacing);
        Assert.Equal(machine.Id, nest.Machine?.Id);
        Assert.Empty(nest.Plates);
    }

    [Fact]
    public async Task UnreadableNestDefaults_AreReportedWithTheNest()
    {
        File.WriteAllText(DefaultsPath, "{ not json");

        var created = await Source().CreateAsync();

        Assert.NotNull(created.Nest);
        Assert.Contains("could not be read", created.Warning);
    }

    [Fact]
    public async Task MadeBy_IsTheRememberedNameElseTheUserName()
    {
        user.MadeBy = "  Synthetic operator ";
        Assert.Equal("Synthetic operator", (await Source().CreateAsync()).Nest.MadeBy);

        user.MadeBy = " ";
        Assert.Equal(Environment.UserName, (await Source().CreateAsync()).Nest.MadeBy);
    }

    [Fact]
    public async Task SettingsFile_SharesTheWinFormsNamesAndText()
    {
        // As System.Configuration writes them; MadeBy is only read.
        UserSettingsFile.Write(UserSettingsPath, new Dictionary<string, string>
        {
            ["NestNumber"] = "9",
            ["LastNestCreatedDate"] = "2026-03-04",
            ["MadeBy"] = "Synthetic operator",
            ["DefaultUnit"] = "Millimeters",
        });

        var created = await Source(new NewNestUserSettingsFile(UserSettingsPath)).CreateAsync();

        Assert.Equal(NestNames.Format(2026, 9), created.Nest.Name);
        Assert.Equal("Synthetic operator", created.Nest.MadeBy);
        var stored = UserSettingsFile.Read(UserSettingsPath);
        Assert.Equal("10", stored["NestNumber"]);
        Assert.Equal("2026-11-13", stored["LastNestCreatedDate"]);
        Assert.Equal("Millimeters", stored["DefaultUnit"]);
    }

    [Fact]
    public void SettingsFile_ReadsTheWinFormsDefaultDateAndFallsBackWhenMissing()
    {
        var settings = new NewNestUserSettingsFile(UserSettingsPath);
        Assert.Equal(1, settings.NestNumber);
        Assert.Equal(2015, settings.LastNestCreatedDate.Year);

        UserSettingsFile.Write(UserSettingsPath, new Dictionary<string, string>
        {
            ["NestNumber"] = "not a number",
            ["LastNestCreatedDate"] = "04/08/2024 19:30:00",
        });

        Assert.Equal(1, settings.NestNumber);
        Assert.Equal(new DateTime(2024, 4, 8, 19, 30, 0), settings.LastNestCreatedDate);
        Assert.Equal("04/08/2024 19:30:00", NewNestUserSettingsFile.FormatDate(settings.LastNestCreatedDate));
    }

    private sealed class MemoryNewNestSettings : INewNestUserSettings
    {
        public int NestNumber { get; set; } = 1;

        public DateTime LastNestCreatedDate { get; set; }

        public string MadeBy { get; set; } = "";

        public int Saves { get; private set; }

        public void Save() => Saves++;
    }

    private sealed class NumberingRepository : INestRepository
    {
        public int NextNumber { get; set; } = 1;
        public Exception? Failure { get; set; }

        public Task<int> AllocateNestNumberAsync(int year, CancellationToken cancellationToken = default) =>
            Failure != null ? Task.FromException<int>(Failure) : Task.FromResult(NextNumber++);

        public Task<NestPage> QueryAsync(NestQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<NestRecord>> ListAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NestRecord?> GetMetadataAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<byte[]?> GetFileAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NestRecord> UploadAsync(byte[] file, NestRecord record, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NestRecord> UpdateFileAsync(Guid id, byte[] file, NestRecord record, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NestRecord> UpdateMetadataAsync(Guid id, NestRecord record, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
