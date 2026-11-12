using OpenNest.Data;

namespace OpenNest.Desktop.Tests;

/// <summary>
/// Which copy of the machines, nest defaults and post settings the desktop uses: the nest
/// server's once it holds one in Database mode, otherwise this PC's; the last copy read,
/// read-only, while the server cannot be reached.
/// </summary>
public sealed class SharedSettingsSourceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "OpenNestDesktopTests", Guid.NewGuid().ToString("N"));
    private readonly FakeSettingsServer server = new();
    private readonly MemoryUserSettings user = new();

    public SharedSettingsSourceTests() => Directory.CreateDirectory(directory);

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
    }

    private string CachePath => Path.Combine(directory, "shared.json");

    private string MachinesDirectory => Path.Combine(directory, "Machines");

    private string DefaultsPath => Path.Combine(directory, "defaults.json");

    private MachineSettingsSource Machines(SharedSettingsSource shared) =>
        new(shared, user, MachinesDirectory, DefaultsPath, Path.Combine(directory, "clamps.json"), Path.Combine(directory, "grid.json"));

    private static SharedSettingsSource FileMode() =>
        new(() => new NestStorageSettings { Mode = NestStorageMode.File, ServerUrl = FakeSettingsServer.Url },
            _ => throw new InvalidOperationException("File mode never reaches the server."));

    private MachineConfig SaveLocalMachine(string name)
    {
        var machine = new MachineConfig { Name = name };
        new LocalJsonProvider(MachinesDirectory).SaveMachine(machine);
        return machine;
    }

    private MachineConfig HoldServerMachine(string name)
    {
        var machine = new MachineConfig { Name = name };
        server.Hold(SharedSettingKinds.Machine, machine.Id.ToString("D"), SharedMachineProvider.Serialize(machine));
        return machine;
    }

    private void HoldServerDefaults(double partSpacing) =>
        server.Hold(SharedSettingKinds.NestDefaults, SharedSettingKinds.DefaultKey,
            new NestDefaults { PartSpacing = partSpacing }.ToJson());

    [Fact]
    public void FileMode_UsesThisPcsMachinesAndDefaults()
    {
        SaveLocalMachine("Local Laser");
        new NestDefaults { PartSpacing = 2 }.Save(DefaultsPath);
        var shared = FileMode();
        var machines = Machines(shared);

        Assert.Null(shared.Current(refresh: true));
        Assert.Equal(["Local Laser"], machines.LoadMachines().Select(m => m.Name));
        Assert.Equal(2, machines.LoadSavedNestDefaults(out _).PartSpacing);
        Assert.Equal("", shared.MachineSource(machines.CreateMachineProvider()));
        Assert.Equal(("Saved on this PC.", false), shared.NestDefaultsSource());
    }

    [Fact]
    public void DatabaseMode_TheServersMachinesReplaceThisPcsOnceItHoldsOne()
    {
        SaveLocalMachine("Local Laser");
        HoldServerMachine("Shared Laser");
        var shared = server.Source(CachePath);
        var machines = Machines(shared);

        Assert.Equal(["Shared Laser"], machines.LoadMachines().Select(m => m.Name));
        Assert.Equal(["Local Laser"], machines.LoadLocalMachines().Select(m => m.Name));
        Assert.IsType<SharedMachineProvider>(machines.CreateMachineProvider());
        Assert.Equal("shared on the nest server", shared.MachineSource(machines.CreateMachineProvider()));
    }

    [Fact]
    public void DatabaseMode_UsesThisPcsMachinesUntilTheServerHoldsOne()
    {
        SaveLocalMachine("Local Laser");
        HoldServerDefaults(3);
        var shared = server.Source(CachePath);
        var machines = Machines(shared);

        Assert.Equal(["Local Laser"], machines.LoadMachines().Select(m => m.Name));
        Assert.IsType<LocalJsonProvider>(machines.CreateMachineProvider());
        Assert.Equal("this PC only: not shared on the server yet", shared.MachineSource(machines.CreateMachineProvider()));
    }

    [Fact]
    public void DatabaseMode_TheServersNestDefaultsWinAndSavesGoBackToIt()
    {
        new NestDefaults { PartSpacing = 2 }.Save(DefaultsPath);
        HoldServerDefaults(3);
        var shared = server.Source(CachePath);
        var machines = Machines(shared);

        Assert.Equal(3, machines.LoadSavedNestDefaults(out _).PartSpacing);
        Assert.Equal(2, machines.LoadLocalNestDefaults().PartSpacing);
        Assert.Equal(("Shared with every workstation on the nest server.", false), shared.NestDefaultsSource());

        machines.SaveNestDefaults(new NestDefaults { PartSpacing = 4 });

        var stored = server.Records.Single(r => r.Kind == SharedSettingKinds.NestDefaults);
        Assert.Equal(4, NestDefaults.Parse(stored.Json, Units.Inches, out _).PartSpacing);
        Assert.Equal(2, NestDefaults.Load(DefaultsPath).PartSpacing);
    }

    [Fact]
    public void DatabaseMode_NestDefaultsTheServerDoesNotHoldSaveOnThisPc()
    {
        var shared = server.Source(CachePath);
        var machines = Machines(shared);

        Assert.Equal(("Saved on this PC only: not shared on the server yet.", false), shared.NestDefaultsSource());
        machines.SaveNestDefaults(new NestDefaults { PartSpacing = 5 });

        Assert.Equal(5, NestDefaults.Load(DefaultsPath).PartSpacing);
        Assert.DoesNotContain(server.Records, r => r.Kind == SharedSettingKinds.NestDefaults);
    }

    [Fact]
    public void Offline_UsesTheLastCopyReadReadOnlyAndSaysSoOnce()
    {
        SaveLocalMachine("Local Laser");
        HoldServerMachine("Shared Laser");
        HoldServerDefaults(3);
        // An earlier session read the server, leaving its copy on this PC.
        Assert.NotNull(server.Source(CachePath).Current(refresh: true));
        server.Online = false;

        var shared = server.Source(CachePath);
        var machines = Machines(shared);

        Assert.Equal(["Shared Laser"], machines.LoadMachines().Select(m => m.Name));
        Assert.Equal(3, machines.LoadSavedNestDefaults(out _).PartSpacing);
        Assert.True(shared.IsOffline);
        Assert.Equal("shared, read-only: the server cannot be reached", shared.MachineSource(machines.CreateMachineProvider()));
        Assert.Equal(("Shared, read-only: the server cannot be reached.", true), shared.NestDefaultsSource());
        Assert.Throws<IOException>(() => machines.SaveNestDefaults(new NestDefaults { PartSpacing = 4 }));
        Assert.False(File.Exists(DefaultsPath));

        var warning = shared.TakeOfflineWarning();
        Assert.Contains("the copy read", warning);
        Assert.Contains("cannot be changed", warning);
        Assert.Null(shared.TakeOfflineWarning());
    }

    [Fact]
    public void Offline_WithNoCopyReadYet_UsesThisPcsOwn()
    {
        SaveLocalMachine("Local Laser");
        server.Online = false;
        var shared = server.Source(CachePath);
        var machines = Machines(shared);

        Assert.Equal(["Local Laser"], machines.LoadMachines().Select(m => m.Name));
        Assert.Contains("this PC's own settings", shared.TakeOfflineWarning());
    }

    [Fact]
    public void TheServersCopyIsReReadWhenOlderThanTwoMinutesAndOfflineRetriedOnceAMinute()
    {
        // The store stamps each read with the real clock; the source sees this much time since it.
        var later = TimeSpan.Zero;
        var shared = new SharedSettingsSource(
            () => new NestStorageSettings { Mode = NestStorageMode.Database, ServerUrl = FakeSettingsServer.Url },
            url => new SharedSettingsStore(new RemoteSharedSettings(new HttpClient(server, false), url), CachePath, "tester"),
            () => DateTime.Now + later);

        shared.Current();
        later = TimeSpan.FromSeconds(110);
        shared.Current();
        Assert.Equal(1, server.Reads);

        later = TimeSpan.FromSeconds(130);
        shared.Current();
        Assert.Equal(2, server.Reads);

        later = TimeSpan.Zero;
        shared.Current(refresh: true);
        Assert.Equal(3, server.Reads);

        // Offline: retried after a minute rather than two.
        server.Online = false;
        shared.Current(refresh: true);
        server.Online = true;
        later = TimeSpan.FromSeconds(50);
        shared.Current();
        Assert.Equal(3, server.Reads);
        later = TimeSpan.FromSeconds(70);
        shared.Current();
        Assert.Equal(4, server.Reads);
    }

    [Fact]
    public void PendingUploads_ListWhatTheServerLacksAndNeverReplaceWhatItHolds()
    {
        SaveLocalMachine("Local Laser");
        new NestDefaults { PartSpacing = 2 }.Save(DefaultsPath);
        var post = new JsonPost();
        var shared = server.Source(CachePath);
        var machines = Machines(shared);

        var uploads = machines.PendingUploads(shared.Current(refresh: true), [post]);

        Assert.Equal(["Machine: Local Laser", "Post settings: Json Post", "Nest defaults"], uploads.Select(u => u.Label));
        Assert.Contains("• Machine: Local Laser", SharedSettingsSource.DescribeUploads(uploads));
        Assert.Null(SharedSettingsSource.Upload(shared.Current(), uploads));
        Assert.Equal(3, server.Records.Count);
        Assert.IsType<SharedMachineProvider>(machines.CreateMachineProvider());
        Assert.Empty(machines.PendingUploads(shared.Current(refresh: true), [post]));
    }

    [Fact]
    public void PendingUploads_SkipMachinesOnceTheServerHoldsAnyAndDefaultsThisPcHasNot()
    {
        SaveLocalMachine("Local Laser");
        HoldServerMachine("Shared Laser");
        var shared = server.Source(CachePath);
        var machines = Machines(shared);

        Assert.Empty(machines.PendingUploads(shared.Current(refresh: true), []));
    }

    [Fact]
    public void PostSettings_TheServersCopyIsLoadedAndAnUnreadableOneStopsThePost()
    {
        var post = new JsonPost();
        server.Hold(SharedSettingKinds.Post, SharedSettingsSource.PostKey(post), "{\"feed\":7}");
        var shared = server.Source(CachePath);

        Assert.Null(shared.LoadPostSettings(post));
        Assert.Equal("{\"feed\":7}", post.Loaded);

        post.Refuse = true;
        Assert.Contains("could not be read, so nothing was posted", shared.LoadPostSettings(post));
    }

    [Fact]
    public void PostSettings_SaveWhereTheyAreReadAndNotWhenUnchanged()
    {
        var post = new JsonPost { Json = "{\"feed\":1}" };
        var shared = server.Source(CachePath);

        shared.SavePostSettings(post, "{\"feed\":1}");
        Assert.Equal(0, post.FileSaves);

        shared.SavePostSettings(post, "{\"feed\":0}");
        Assert.Equal(1, post.FileSaves);
        Assert.Empty(server.Records);

        server.Hold(SharedSettingKinds.Post, SharedSettingsSource.PostKey(post), "{\"feed\":0}");
        shared.Current(refresh: true);
        shared.SavePostSettings(post, "{\"feed\":0}");
        Assert.Equal(1, post.FileSaves);
        Assert.Equal("{\"feed\":1}", server.Records.Single().Json);
    }

    internal sealed class JsonPost : IJsonConfigPostProcessor
    {
        public string Json { get; set; } = "{}";

        public string? Loaded { get; private set; }

        public bool Refuse { get; set; }

        public int FileSaves { get; private set; }

        public string Name => "Json Post";

        public string Author => "Tests";

        public string Description => "A post sharing its settings as JSON.";

        public object Config => Json;

        public void Post(Nest nest, Stream outputStream) { }

        public void Post(Nest nest, string outputFile) { }

        public void SaveConfig() => FileSaves++;

        public string SerializeConfig() => Json;

        public void LoadConfig(string json)
        {
            if (Refuse)
                throw new System.Text.Json.JsonException("Not this post's settings.");
            Loaded = json;
        }
    }
}
