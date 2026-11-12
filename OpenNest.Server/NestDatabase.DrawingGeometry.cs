using System.Globalization;
using Microsoft.Data.Sqlite;
using OpenNest.Data;

namespace OpenNest.Server;

/// <summary>
/// Measured library package sizes. The server never parses packages, so the worker lists the
/// packages of current drawings without a summary at its version, measures each and stores it
/// here keyed by package hash: identical geometry is measured once, and a metadata-only
/// revision keeps its summary. Queries join it to filter and sort by size and holes.
/// </summary>
public sealed partial class NestDatabase
{
    // v19: one summary per package; error is nonblank (and the sizes 0) when it could not be measured.
    private const string DrawingGeometrySchema = """
        CREATE TABLE IF NOT EXISTS drawing_package_geometry (
            sha256     TEXT NOT NULL PRIMARY KEY REFERENCES drawing_packages(sha256),
            version    INTEGER NOT NULL,
            length     REAL NOT NULL,
            width      REAL NOT NULL,
            area       REAL NOT NULL,
            holes      INTEGER NOT NULL,
            error      TEXT NOT NULL DEFAULT '',
            measuredAt TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_drawing_package_geometry_length ON drawing_package_geometry(length);
        PRAGMA user_version=19;
        """;

    // Lengths and thickness compare in inches; the slack absorbs rounding and millimeter conversion.
    private const double RangeSlack = 1e-6;

    private const string GeometryJoin = "LEFT JOIN drawing_package_geometry g ON g.sha256 = drawings.packageSha256";

    private const string GeometryColumns = "g.version, g.length, g.width, g.area, g.holes, g.error";

    private const string ThicknessInches = "(CASE WHEN units = 'Millimeters' THEN thickness / 25.4 ELSE thickness END)";

    /// <summary>
    /// Up to <paramref name="limit"/> distinct package hashes of current drawings (archived ones
    /// included) with no summary at <paramref name="version"/> or later, in hash order.
    /// </summary>
    public IReadOnlyList<string> GetUnmeasuredPackages(int version, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = """
                SELECT DISTINCT d.packageSha256 FROM drawings d
                LEFT JOIN drawing_package_geometry g ON g.sha256 = d.packageSha256
                WHERE g.sha256 IS NULL OR g.version < $version
                ORDER BY d.packageSha256 LIMIT $limit
                """;
            command.Parameters.AddWithValue("$version", version);
            command.Parameters.AddWithValue("$limit", limit);
            var hashes = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                hashes.Add(reader.GetString(0));
            return hashes;
        }
    }

    /// <summary>The package stored under a hash, or null.</summary>
    public byte[]? GetPackageBySha(string sha256)
    {
        lock (_sync)
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT package FROM drawing_packages WHERE sha256=$sha";
            command.Parameters.AddWithValue("$sha", sha256);
            return command.ExecuteScalar() as byte[];
        }
    }

    /// <summary>
    /// Stores a package's summary; false when the package is unknown. A summary from an older
    /// version than the stored one is ignored, so an outdated worker cannot undo a newer one.
    /// </summary>
    public bool SetPackageGeometry(string sha256, DrawingGeometrySummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var error = summary.GetValidationError();
        if (error is not null)
            throw new ArgumentException(error, nameof(summary));

        lock (_sync)
        {
            using var exists = _connection.CreateCommand();
            exists.CommandText = "SELECT COUNT(*) FROM drawing_packages WHERE sha256=$sha";
            exists.Parameters.AddWithValue("$sha", sha256);
            if ((long)exists.ExecuteScalar()! == 0)
                return false;

            using var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO drawing_package_geometry (sha256, version, length, width, area, holes, error, measuredAt)
                VALUES ($sha, $version, $length, $width, $area, $holes, $error, $measuredAt)
                ON CONFLICT(sha256) DO UPDATE SET
                    version = excluded.version, length = excluded.length, width = excluded.width,
                    area = excluded.area, holes = excluded.holes, error = excluded.error, measuredAt = excluded.measuredAt
                WHERE excluded.version >= drawing_package_geometry.version
                """;
            var measured = summary.IsMeasured;
            command.Parameters.AddWithValue("$sha", sha256);
            command.Parameters.AddWithValue("$version", summary.Version);
            command.Parameters.AddWithValue("$length", measured ? summary.Length : 0);
            command.Parameters.AddWithValue("$width", measured ? summary.Width : 0);
            command.Parameters.AddWithValue("$area", measured ? summary.Area : 0);
            command.Parameters.AddWithValue("$holes", measured ? summary.Holes : 0);
            command.Parameters.AddWithValue("$error", summary.Error);
            command.Parameters.AddWithValue("$measuredAt", DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
            return true;
        }
    }

    // The filter's ranges as WHERE conditions over drawings joined to their geometry (GeometryJoin).
    private static void AddRangeConditions(List<string> conditions, DrawingLibraryFilter filter)
    {
        if (filter.MinThickness is not null || filter.MaxThickness is not null)
        {
            var range = new List<string> { "thickness > 0" };
            if (filter.MinThickness is not null) range.Add($"{ThicknessInches} >= $minThickness");
            if (filter.MaxThickness is not null) range.Add($"{ThicknessInches} <= $maxThickness");
            var inRange = "(" + string.Join(" AND ", range) + ")";
            // A drawing saved without a thickness suits any, as with the exact thickness filter.
            conditions.Add(filter.IncludeBlankThickness ? $"({inRange} OR thickness = 0)" : inRange);
        }
        else if (!filter.IncludeBlankThickness)
        {
            conditions.Add("thickness > 0");
        }

        if (filter.NeedsGeometry)
            conditions.Add("g.error = ''");
        if (filter.MinLength is not null) conditions.Add("g.length >= $minLength");
        if (filter.MaxLength is not null) conditions.Add("g.length <= $maxLength");
        if (filter.MinWidth is not null) conditions.Add("g.width >= $minWidth");
        if (filter.MaxWidth is not null) conditions.Add("g.width <= $maxWidth");
        if (filter.MinHoles is not null) conditions.Add("g.holes >= $minHoles");
        if (filter.MaxHoles is not null) conditions.Add("g.holes <= $maxHoles");
        // savedAt is the server's local ISO time, so its first ten characters are its local date.
        if (filter.SavedFrom is not null) conditions.Add("substr(savedAt, 1, 10) >= $savedFrom");
        if (filter.SavedTo is not null) conditions.Add("substr(savedAt, 1, 10) <= $savedTo");
    }

    private static void AddRangeParameters(SqliteCommand command, DrawingLibraryFilter filter)
    {
        AddLower(command, "$minThickness", filter.MinThickness);
        AddUpper(command, "$maxThickness", filter.MaxThickness);
        AddLower(command, "$minLength", filter.MinLength);
        AddUpper(command, "$maxLength", filter.MaxLength);
        AddLower(command, "$minWidth", filter.MinWidth);
        AddUpper(command, "$maxWidth", filter.MaxWidth);
        if (filter.MinHoles is { } minHoles) command.Parameters.AddWithValue("$minHoles", minHoles);
        if (filter.MaxHoles is { } maxHoles) command.Parameters.AddWithValue("$maxHoles", maxHoles);
        if (filter.SavedFrom is { } from) command.Parameters.AddWithValue("$savedFrom", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        if (filter.SavedTo is { } to) command.Parameters.AddWithValue("$savedTo", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static void AddLower(SqliteCommand command, string name, double? value)
    {
        if (value is { } number)
            command.Parameters.AddWithValue(name, number - RangeSlack);
    }

    private static void AddUpper(SqliteCommand command, string name, double? value)
    {
        if (value is { } number)
            command.Parameters.AddWithValue(name, number + RangeSlack);
    }

    // Reads GeometryColumns from column `first` on; null when the package has no summary yet.
    private static DrawingGeometrySummary? ReadGeometry(SqliteDataReader reader, int first) =>
        reader.IsDBNull(first) ? null : new DrawingGeometrySummary
        {
            Version = reader.GetInt32(first),
            Length = reader.GetDouble(first + 1),
            Width = reader.GetDouble(first + 2),
            Area = reader.GetDouble(first + 3),
            Holes = reader.GetInt32(first + 4),
            Error = reader.GetString(first + 5),
        };
}
