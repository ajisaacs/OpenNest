using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.Data;
using OpenNest.Data.MaterialLedger;
using OpenNest.Desktop;
using OpenNest.Engine.Jobs;

namespace OpenNest.Mcp.Database;

/// <summary>
/// The nest server the Database-mode tools talk to, and what they need to make nests as the
/// desktop does: File &gt; New nests (<see cref="NewNestSource"/>), the saved Auto Nest engine and
/// this PC's MaterialLedger connection. <see cref="Load"/> reads the desktop's own settings
/// (storage.json, defaults, machines, user-settings.json); tests build one around an
/// in-process server.
/// </summary>
public sealed class NestServerContext : IDisposable
{
    private static readonly HttpClient LedgerHttp = new();

    // Distinguishes OpenNest's ledger key from other DPAPI blobs of the same user; matches the desktop.
    private static readonly byte[] LedgerEntropy = Encoding.UTF8.GetBytes("OpenNest.MaterialLedger.ApiKey");

    private readonly Func<SetReleaseGroup, Task<NewNest>> _createNest;
    private readonly Func<string> _engineName;
    private readonly Func<Guid, ReleaseLedgerStock> _createLedger;
    private readonly IDisposable[] _owned;

    /// <param name="createNest">Makes a group's empty nest as File &gt; New would.</param>
    /// <param name="engineName">The Auto Nest engine to use when a call names none.</param>
    /// <param name="createLedger">
    /// MaterialLedger stock for one run, leaving out codes on every open nest but the given one;
    /// null when the ledger is not set up, so nests use sheet sizes only.
    /// </param>
    public NestServerContext(string serverUrl, RemoteDrawingLibrary library, IDrawingSetStore sets,
        INestRepository nests, Func<SetReleaseGroup, Task<NewNest>> createNest, Func<string> engineName,
        Func<Guid, ReleaseLedgerStock> createLedger, string userName, params IDisposable[] owned)
    {
        ServerUrl = serverUrl ?? throw new ArgumentNullException(nameof(serverUrl));
        Library = library ?? throw new ArgumentNullException(nameof(library));
        Sets = sets ?? throw new ArgumentNullException(nameof(sets));
        Nests = nests ?? throw new ArgumentNullException(nameof(nests));
        _createNest = createNest ?? throw new ArgumentNullException(nameof(createNest));
        _engineName = engineName ?? throw new ArgumentNullException(nameof(engineName));
        _createLedger = createLedger;
        UserName = string.IsNullOrWhiteSpace(userName) ? Environment.UserName : userName.Trim();
        _owned = owned ?? Array.Empty<IDisposable>();
    }

    private NestServerContext(string error)
    {
        Error = error;
        _owned = Array.Empty<IDisposable>();
    }

    /// <summary>Why the tools cannot reach a nest server (File mode, no URL); null when they can.</summary>
    public string Error { get; }

    public string ServerUrl { get; }

    public RemoteDrawingLibrary Library { get; }

    public IDrawingSetStore Sets { get; }

    public INestRepository Nests { get; }

    /// <summary>Recorded as SavedBy, ReleasedBy and UndoneBy.</summary>
    public string UserName { get; }

    /// <summary>True when MaterialLedger stock is offered before the sheet sizes.</summary>
    public bool HasLedger => _createLedger != null;

    public Task<NewNest> CreateNestAsync(SetReleaseGroup group) => _createNest(group);

    /// <summary>The ledger stock for one run, or null when the ledger is not set up.</summary>
    public ReleaseLedgerStock CreateLedger(Guid excludeNestId) => _createLedger?.Invoke(excludeNestId);

    /// <summary>
    /// <paramref name="requested"/> as the registry spells it, else the engine the desktop last
    /// used (engine-selection.json), else Default.
    /// </summary>
    public string ResolveEngine(string requested, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(requested))
            return _engineName();
        var name = NestingEngineRegistry.ResolveName(requested.Trim());
        if (name != null)
            return name;
        error = $"Unknown engine '{requested}'. Available: "
            + string.Join(", ", NestingEngineRegistry.AvailableEngines.Select(e => e.Name)) + ".";
        return null;
    }

    /// <summary>
    /// The nest server from storage.json in Database mode, with File &gt; New nests, the engine
    /// and MaterialLedger set up from the desktop's settings files.
    /// </summary>
    public static NestServerContext Load() => FromStorage(NestStorageSettings.Load(NestStorageSettings.DefaultPath));

    /// <summary><see cref="Load"/> for the given storage settings; File mode gives a context with <see cref="Error"/>.</summary>
    public static NestServerContext FromStorage(NestStorageSettings storage)
    {
        ArgumentNullException.ThrowIfNull(storage);
        if (!storage.IsDatabaseMode || string.IsNullOrWhiteSpace(storage.ServerUrl))
            return new NestServerContext(
                "OpenNest is not in Database mode on this PC (storage.json has no nest server), so the nest server tools are unavailable.");

        var url = storage.ServerUrl;
        RemoteDrawingLibrary library;
        RemoteDrawingSetStore sets;
        RemoteNestRepository nests;
        try
        {
            library = new RemoteDrawingLibrary(url);
            sets = new RemoteDrawingSetStore(url);
            nests = new RemoteNestRepository(url);
        }
        catch (ArgumentException ex)
        {
            return new NestServerContext($"The nest server URL '{url}' in storage.json is not valid: {ex.Message}");
        }

        var shared = new SharedSettingsSource(() => storage);
        var machines = new MachineSettingsSource(shared, new MachineUserSettingsFile(UserSettingsFile.DefaultPath));
        var newNests = new NewNestSource(machines, new NewNestUserSettingsFile(UserSettingsFile.DefaultPath), () => nests);

        var ledgerSettings = MaterialLedgerSettings.Load(MaterialLedgerSettings.DefaultPath, UnprotectLedgerKey);
        Func<Guid, ReleaseLedgerStock> ledger = !ledgerSettings.IsConfigured
            ? null
            : exclude => new ReleaseLedgerStock(
                (material, grade, thicknessIn, token) => new MaterialLedgerClient(LedgerHttp, ledgerSettings)
                    .GetStockAsync(material, grade, thicknessIn, token),
                token => LedgerCodesInUse.ReadAsync(nests, exclude, token));

        return new NestServerContext(url, library, sets, nests, group => newNests.CreateAsync(group),
            SavedEngine, ledger, Environment.UserName, library, sets, nests);
    }

    /// <summary>The engine the desktop last used, resolved against the loaded engines.</summary>
    public static string SavedEngine() =>
        EngineSelectionSettings.Load(EngineSelectionSettings.DefaultPath)
            .Resolve(NestingEngineRegistry.AvailableEngines.Select(e => e.Name), out _, NestingEngineRegistry.ResolveName);

    private static string UnprotectLedgerKey(string protectedText)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("The MaterialLedger key is protected for Windows.");
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedText), LedgerEntropy,
            DataProtectionScope.CurrentUser));
    }

    public void Dispose()
    {
        foreach (var owned in _owned)
            owned?.Dispose();
    }
}
