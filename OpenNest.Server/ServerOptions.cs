namespace OpenNest.Server;

/// <summary>Startup settings read from the command line and environment.</summary>
public static class ServerOptions
{
    public const string DatabaseVariable = "OPENNEST_DB";
    public const string UpdatesVariable = "OPENNEST_UPDATES";
    public const string JobRetentionVariable = "OPENNEST_JOB_RETENTION_DAYS";

    /// <summary>
    /// How long finished server nest jobs are kept: <c>OPENNEST_JOB_RETENTION_DAYS</c> (a positive
    /// number of days, fractions allowed), or <see cref="Data.NestJobRules.Retention"/> when unset.
    /// Anything else fails startup rather than deleting results sooner than intended.
    /// </summary>
    public static TimeSpan JobRetention(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Data.NestJobRules.Retention;
        if (!double.TryParse(value.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var days)
            || !(days > 0) || days > 36500)
            throw new ArgumentException($"{JobRetentionVariable} must be a positive number of days.", nameof(value));
        return TimeSpan.FromDays(days);
    }

    /// <summary>
    /// True unless <c>OPENNEST_UPDATES</c> is <c>off</c>: the server mirrors the desktop
    /// app's update feed from GitHub (see <see cref="UpdateMirror"/>).
    /// </summary>
    public static bool UpdatesEnabled(string? value) =>
        !string.Equals(value?.Trim(), "off", StringComparison.OrdinalIgnoreCase);

    /// <summary>The mirrored update feed lives in an <c>updates</c> directory beside the database.</summary>
    public static string UpdatesPath(string databasePath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(databasePath))!, "updates");

    /// <summary>
    /// The database path: <c>--database=&lt;path&gt;</c> or <c>--database &lt;path&gt;</c>, then
    /// <c>OPENNEST_DB</c>, then <c>./data/nests.db</c>. A <c>--database</c> without a path,
    /// or given twice, fails startup instead of silently storing nests elsewhere.
    /// </summary>
    public static string DatabasePath(IReadOnlyList<string> args, string? environmentPath)
    {
        string? path = null;
        for (var i = 0; i < args.Count; i++)
        {
            string value;
            if (args[i] == "--database")
            {
                if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("--database requires a path.", nameof(args));
                value = args[++i];
            }
            else if (args[i].StartsWith("--database=", StringComparison.Ordinal))
            {
                value = args[i]["--database=".Length..];
            }
            else
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("--database requires a path.", nameof(args));
            if (path is not null)
                throw new ArgumentException("--database was given more than once.", nameof(args));
            path = value;
        }

        return path ?? environmentPath ?? Path.Combine("data", "nests.db");
    }
}
