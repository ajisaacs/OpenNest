using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.Data;

namespace OpenNest.Desktop;

/// <summary>
/// The per-user settings File > New reads and advances: the local nest counter, the date it
/// last counted from (the counter restarts each year) and the Made By the user last typed.
/// </summary>
public interface INewNestUserSettings
{
    int NestNumber { get; set; }

    DateTime LastNestCreatedDate { get; set; }

    /// <summary>The Made By last typed in Nest Info; blank when none.</summary>
    string MadeBy { get; }

    /// <summary>Persists <see cref="NestNumber"/> and <see cref="LastNestCreatedDate"/>.</summary>
    void Save();
}

/// <summary>A new nest and the warning to show with it (unreadable nest defaults), or null.</summary>
public sealed record NewNest(Nest Nest, string Warning);

/// <summary>
/// Creates File > New nests: nest defaults, clamps, the default machine and its support grid,
/// then the year's next name (<see cref="NestNames"/>). In Database mode the nest server
/// allocates the number so every PC shares one sequence; when it cannot, the user's
/// <see cref="INewNestUserSettings.NestNumber"/> is the local fallback, kept above every number
/// the server handed out and reset each year.
/// </summary>
public sealed class NewNestSource
{
    private static readonly TimeSpan ServerTimeout = TimeSpan.FromSeconds(3);

    private readonly MachineSettingsSource machines;
    private readonly INewNestUserSettings user;
    private readonly Func<INestRepository> databaseRepository;
    private readonly Func<DateTime> now;

    /// <param name="databaseRepository">
    /// The nest server's repository in Database mode, or null in File mode; read on every new
    /// nest. Null itself means File mode always.
    /// </param>
    /// <param name="now">The clock naming the nest; defaults to <see cref="DateTime.Now"/>.</param>
    public NewNestSource(
        MachineSettingsSource machines,
        INewNestUserSettings user,
        Func<INestRepository> databaseRepository = null,
        Func<DateTime> now = null)
    {
        this.machines = machines ?? throw new ArgumentNullException(nameof(machines));
        this.user = user ?? throw new ArgumentNullException(nameof(user));
        this.databaseRepository = databaseRepository;
        this.now = now ?? (() => DateTime.Now);
    }

    /// <summary>
    /// The author a new nest starts with: the name the user last typed in Nest Info's Made By,
    /// else their user name. Only new nests get it; opening an existing nest's info leaves a
    /// blank Made By blank.
    /// </summary>
    public string DefaultMadeBy => MadeByOrUserName(user.MadeBy);

    /// <summary><paramref name="remembered"/> trimmed, or the user name when it is blank.</summary>
    public static string MadeByOrUserName(string remembered) =>
        string.IsNullOrWhiteSpace(remembered) ? Environment.UserName : remembered.Trim();

    /// <param name="release">
    /// The drawing set release group the nest is for: its units, with the saved plate defaults
    /// converted to them, its material and thickness, and the machine's spacing for those. Null
    /// uses the defaults as saved.
    /// </param>
    public async Task<NewNest> CreateAsync(SetReleaseGroup release = null)
    {
        var nest = CreateDefaultNest(release, out var warning);
        var created = now();

        nest.DateCreated = created;
        nest.DateLastModified = created;
        nest.MadeBy = DefaultMadeBy;
        nest.Name = NestNames.Format(created.Year, await AllocateNumberAsync(created).ConfigureAwait(false));
        return new NewNest(nest, warning);
    }

    private Nest CreateDefaultNest(SetReleaseGroup release, out string warning)
    {
        var nest = new Nest();
        var defaults = machines.LoadNestDefaults(out warning);
        (release != null ? defaults.ConvertedTo(release.Units) : defaults).ApplyTo(nest);
        // After the defaults so the rail and grid are converted to the new nest's units.
        var machine = MachineSettingsSource.DefaultMachine(defaults, machines.LoadMachines());
        MachineSettingsSource.LoadClampSettings(machine).ApplyToNewNest(nest);
        machines.AssignMachine(nest, machine);
        if (release != null)
        {
            nest.Material = new Material(release.MaterialName, release.MaterialGrade);
            nest.Thickness = release.Thickness;
            MachineCuttingDefaults.ApplySpacingTo(nest, machine);
        }
        return nest;
    }

    private async Task<int> AllocateNumberAsync(DateTime created)
    {
        if (created.Year != user.LastNestCreatedDate.Year)
            user.NestNumber = 1;
        user.LastNestCreatedDate = created.Date;

        try
        {
            var repository = databaseRepository?.Invoke();
            if (repository != null)
            {
                using var timeout = new CancellationTokenSource(ServerTimeout);
                var number = await repository.AllocateNestNumberAsync(created.Year, timeout.Token).ConfigureAwait(false);
                user.NestNumber = System.Math.Max(user.NestNumber, number + 1);
                user.Save();
                return number;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException
            or OperationCanceledException or NotSupportedException or ArgumentException)
        {
            // Offline, an invalid server URL or an older server: fall back to this PC's counter.
        }

        var local = user.NestNumber;
        user.NestNumber = local + 1;
        user.Save();
        return local;
    }
}

/// <summary>
/// <see cref="INewNestUserSettings"/> kept in the desktop user-settings file
/// (<see cref="UserSettingsFile"/>) under the WinForms names and text, so both apps count from
/// one sequence: NestNumber as an invariant integer (1 when missing or unreadable),
/// LastNestCreatedDate as System.Configuration's invariant date text, and MadeBy as text. Each
/// read goes to the file and each change is written at once, so <see cref="Save"/> has nothing
/// left to do.
/// </summary>
public sealed class NewNestUserSettingsFile(string path) : INewNestUserSettings
{
    public const string NestNumberName = "NestNumber";
    public const string LastNestCreatedDateName = "LastNestCreatedDate";
    public const string MadeByName = "MadeBy";

    /// <summary>The WinForms default, which never matches this year so the counter restarts.</summary>
    private static readonly DateTime DefaultDate = new(2015, 4, 8, 19, 30, 0);

    public int NestNumber
    {
        get => UserSettingsFile.Read(path).TryGetValue(NestNumberName, out var text)
            && int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number
                : 1;
        set => UserSettingsFile.Update(path, values =>
            values[NestNumberName] = value.ToString(CultureInfo.InvariantCulture));
    }

    public DateTime LastNestCreatedDate
    {
        get => UserSettingsFile.Read(path).TryGetValue(LastNestCreatedDateName, out var text)
            && DateTime.TryParse(text?.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : DefaultDate;
        set => UserSettingsFile.Update(path, values => values[LastNestCreatedDateName] = FormatDate(value));
    }

    public string MadeBy => UserSettingText.ReadString(UserSettingsFile.Read(path), MadeByName, "");

    public void Save()
    {
    }

    /// <summary>As System.Configuration's invariant DateTimeConverter writes it.</summary>
    public static string FormatDate(DateTime value) =>
        value.TimeOfDay == TimeSpan.Zero
            ? value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : value.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
}
