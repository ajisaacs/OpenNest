using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenNest.Data;
using OpenNest.Geometry;
using OpenNest.IO;
using CncProgram = OpenNest.CNC.Program;

namespace OpenNest.Server.Tests;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static async Task<int> Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            var options = Options.Parse(args);
            using var watchdog = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            using var handler = new WireContractHandler();
            using var http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10),
                // Only the limits mode downloads its near-cap synthetic archive.
                MaxResponseContentBufferSize = (options.Mode == "limits" ? 32 : 16) * 1024 * 1024,
            };
            using var repository = new RemoteNestRepository(http, options.Url);
            switch (options.Mode)
            {
                case "seed":
                    await Seed(repository, options, watchdog.Token);
                    break;
                case "verify":
                    await Verify(repository, options, watchdog.Token);
                    break;
                case "reject":
                    await Reject(repository, http, options.Url, watchdog.Token);
                    break;
                case "limits":
                    await Limits(repository, http, options.Url, watchdog.Token);
                    break;
            }
            Console.WriteLine($"PASS {options.Mode}; raw camelCase/string-enum responses checked: {handler.CheckedResponses}");
            return 0;
        }
        catch (SmokeAssertionException ex)
        {
            Console.Error.WriteLine($"FAIL: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            // Do not print URLs, response bodies, or metadata from an accidental target.
            Console.Error.WriteLine($"FAIL: {ex.GetType().Name}; smoke did not complete.");
            return 1;
        }
    }

    private static async Task Seed(RemoteNestRepository repository, Options options, CancellationToken ct)
    {
        var existing = await repository.ListAsync(ct);
        Check(existing.Count == 0 || options.AllowNonempty,
            "Seed refuses a nonempty server; use only an owned disposable server.");
        var baseline = await Snapshot(repository, ct);
        var nest = SyntheticNest();
        var session = new NestSaveSession();
        Check(session.RemoteId == Guid.Empty && session.ServerUrl == "", "New session must be unbound.");
        var archive = Archive(nest);
        var created = await session.SaveAsync(repository, options.Url, nest, archive, cancellationToken: ct);
        Check(created.Id != Guid.Empty, "Create must return a nonempty id.");
        CheckBinding(session, created.Id, options.Url);
        await ReadSaved(repository, nest, archive, created, ct);
        await CheckMembership(repository, baseline.Select(s => s.Metadata.Id).Append(created.Id), ct);

        nest.Name = "Synthetic container smoke updated";
        nest.Customer = "Synthetic test customer updated";
        nest.Notes = "Updated neutral smoke metadata";
        nest.MadeBy = "Synthetic smoke editor";
        nest.Material = new Material("Synthetic alloy updated", "test-grade", 0.3);
        nest.Thickness = 0.375;
        nest.Status = NestStatus.ToBeCut;
        nest.Plates[0].Parts[0].Location = new Vector(4, 5);
        archive = Archive(nest);
        var updated = await session.SaveAsync(repository, options.Url, nest, archive, cancellationToken: ct);
        Check(updated.Id == created.Id, "Update must retain the create id.");
        CheckBinding(session, created.Id, options.Url);
        var updateState = await ReadSaved(repository, nest, archive, updated, ct);
        await CheckMembership(repository, baseline.Select(s => s.Metadata.Id).Append(created.Id), ct);

        nest.Name = "Synthetic container smoke copy";
        nest.Status = NestStatus.HasBeenCut;
        archive = Archive(nest);
        var copy = await session.SaveAsync(repository, options.Url, nest, archive, saveCopy: true,
            cancellationToken: ct);
        Check(copy.Id != Guid.Empty && copy.Id != created.Id, "Save copy must create a different id.");
        CheckBinding(session, copy.Id, options.Url);
        var copyState = await ReadSaved(repository, nest, archive, copy, ct);
        var entries = baseline.Concat(new[] { updateState, copyState }).ToList();
        await CheckSnapshot(repository, entries, ct);
        var state = new SmokeState { Version = 1, Records = entries };
        await File.WriteAllTextAsync(options.State!, JsonSerializer.Serialize(state, JsonOptions), ct);
        Console.WriteLine($"Seed stored {entries.Count} records; create/update id={updated.Id}; copy id={copy.Id}");
        foreach (var entry in new[] { updateState, copyState })
            Console.WriteLine($"Archive id={entry.Metadata.Id}; bytes={entry.Metadata.FileSize}; sha256={entry.Sha256}");
    }

    private static async Task Verify(RemoteNestRepository repository, Options options, CancellationToken ct)
    {
        Check(File.Exists(options.State), "Verify state is missing.");
        var state = JsonSerializer.Deserialize<SmokeState>(await File.ReadAllTextAsync(options.State!, ct), JsonOptions);
        Check(state is { Version: 1, Records.Count: >= 2 }, "Verify state is invalid.");
        var entries = state!.Records;
        Check(entries.All(e => e is not null && e.Metadata is not null && e.Metadata.Id != Guid.Empty
            && e.Metadata.SavedAt != default && e.Metadata.FileSize > 0
            && e.Sha256 is not null && e.Sha256.Length == 64 && e.Sha256.All(Uri.IsHexDigit)),
            "Verify state contains invalid metadata or hashes.");
        Check(entries.Select(e => e.Metadata.Id).Distinct().Count() == entries.Count,
            "Verify state contains duplicate ids.");
        await CheckSnapshot(repository, entries, ct);
        Console.WriteLine($"Verified {entries.Count} persisted records, all metadata including SavedAt, and archive hashes.");
    }

    private static async Task Reject(RemoteNestRepository repository, HttpClient http, string url, CancellationToken ct)
    {
        var before = await Snapshot(repository, ct);
        Check(before.Count > 0, "Rejection checks require seeded records on an owned server.");
        var metadata = JsonSerializer.Serialize(new NestRecord { Name = "Synthetic rejected upload" }, JsonOptions);
        var cases = new (string Name, string? Metadata, byte[]? File)[]
        {
            ("missing metadata", null, new byte[] { 1 }),
            ("invalid metadata JSON", "{not-json", new byte[] { 1 }),
            ("null metadata", "null", new byte[] { 1 }),
            ("missing file", metadata, null),
            ("empty file", metadata, Array.Empty<byte>()),
        };
        // Exercise both shared multipart callers, including mutation of an existing record.
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put })
        {
            var path = method == HttpMethod.Post ? "api/nests" : $"api/nests/{before[0].Metadata.Id}/file";
            foreach (var test in cases)
            {
                using var content = new MultipartFormDataContent();
                if (test.Metadata is not null)
                    content.Add(new StringContent(test.Metadata), "metadata");
                if (test.File is not null)
                    content.Add(new ByteArrayContent(test.File), "file", "synthetic.nest");
                using var request = new HttpRequestMessage(method, new Uri(new Uri(url + "/"), path)) { Content = content };
                using var response = await http.SendAsync(request, ct);
                Check(response.StatusCode == HttpStatusCode.BadRequest, $"{method} {test.Name} must return 400.");
                await CheckSnapshot(repository, before, ct);
                Console.WriteLine($"Rejected {method} {test.Name}: 400; all metadata and hashes unchanged.");
            }
        }
        var unknown = Guid.NewGuid();
        Check(before.All(e => e.Metadata.Id != unknown), "Unknown-id fixture collided.");
        Check(await repository.GetMetadataAsync(unknown, ct) is null, "Unknown metadata id must return 404.");
        Check(await repository.GetFileAsync(unknown, ct) is null, "Unknown file id must return 404.");
        await CheckSnapshot(repository, before, ct);
        Console.WriteLine("Unknown metadata/file ids: 404; all metadata and hashes unchanged.");
    }

    // Kestrel's default MaxRequestBodySize (30,000,000 bytes) bounds a whole multipart request.
    private const int RequestBodyLimit = 30_000_000;

    private static async Task Limits(RemoteNestRepository repository, HttpClient http, string url, CancellationToken ct)
    {
        var before = await Snapshot(repository, ct);
        Check(before.Count > 0, "Limit checks require seeded records on an owned server.");

        // Opaque synthetic bytes (the server does not parse archives) above the 64 KiB form
        // memory threshold, so the non-root runtime must buffer the part to its temp directory.
        var accepted = new byte[29_000_000];
        RandomNumberGenerator.Fill(accepted);
        var created = await repository.UploadAsync(accepted, new NestRecord
        {
            Name = "Synthetic upload-limit probe",
            Comments = "Random bytes; deleted by the same smoke stage",
        }, ct);
        Check(created.Id != Guid.Empty && created.FileSize == accepted.LongLength,
            "A near-limit upload must be stored with its exact size.");
        var downloaded = await repository.GetFileAsync(created.Id, ct);
        Check(downloaded is not null && downloaded.AsSpan().SequenceEqual(accepted),
            "A near-limit archive must download byte-for-byte.");
        await CheckMembership(repository, before.Select(s => s.Metadata.Id).Append(created.Id), ct);
        await repository.DeleteAsync(created.Id, ct);
        await CheckSnapshot(repository, before, ct);
        Console.WriteLine($"Accepted, downloaded, and deleted a {accepted.Length}-byte archive; all other records unchanged.");

        // Kestrel rejects an oversized declared length before the app reads the form. With
        // Expect: 100-continue the client waits for, and must receive, that 413 response.
        var oversized = new byte[RequestBodyLimit];
        var oversizedRecord = new NestRecord { Name = "Synthetic oversized upload" };
        var metadata = JsonSerializer.Serialize(oversizedRecord, JsonOptions);
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put })
        {
            var path = method == HttpMethod.Post ? "api/nests" : $"api/nests/{before[0].Metadata.Id}/file";
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(metadata), "metadata");
            content.Add(new ByteArrayContent(oversized), "file", "synthetic.nest");
            using var request = new HttpRequestMessage(method, new Uri(new Uri(url + "/"), path)) { Content = content };
            request.Headers.ExpectContinue = true;
            using var response = await http.SendAsync(request, ct);
            Check(response.StatusCode == HttpStatusCode.RequestEntityTooLarge,
                $"{method} of a request over {RequestBodyLimit} bytes must return 413.");
            await CheckSnapshot(repository, before, ct);
            Console.WriteLine($"Rejected {method} over-limit request: 413; all metadata and hashes unchanged.");
        }

        // RemoteNestRepository streams without Expect: 100-continue, so the server may close the
        // connection before the client reads the 413. The real client must still report failure.
        var saves = new (string Name, Func<Task> Save)[]
        {
            ("upload", () => repository.UploadAsync(oversized, oversizedRecord, ct)),
            ("file update", () => repository.UpdateFileAsync(before[0].Metadata.Id, oversized, oversizedRecord, ct)),
        };
        foreach (var (name, save) in saves)
        {
            var outcome = "succeeded";
            try
            {
                await save();
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException)
            {
                outcome = ex.GetType().Name;
            }
            Check(outcome != "succeeded", $"Real-client oversized {name} must fail.");
            await CheckSnapshot(repository, before, ct);
            Console.WriteLine($"Real-client oversized {name} failed ({outcome}); all metadata and hashes unchanged.");
        }
    }

    private static async Task<SavedRecord> ReadSaved(RemoteNestRepository repository, Nest nest,
        byte[] archive, NestRecord returned, CancellationToken ct)
    {
        Check(returned.SavedAt != default, "Server must assign SavedAt.");
        var expected = NestRecordFactory.FromNest(nest, returned.Id, archive.LongLength);
        expected.SavedAt = returned.SavedAt;
        CheckMetadata(expected, returned);
        var readback = await repository.GetMetadataAsync(returned.Id, ct);
        Check(readback is not null, "Saved metadata must be readable.");
        CheckMetadata(expected, readback!);
        var downloaded = await repository.GetFileAsync(returned.Id, ct);
        Check(downloaded is not null && downloaded.SequenceEqual(archive), "Downloaded archive bytes must match upload.");
        var geometry = Geometry(nest);
        CheckArchive(downloaded!, readback!, geometry);
        return new SavedRecord { Metadata = readback!, Sha256 = Hash(downloaded!), Geometry = geometry };
    }

    private static async Task<List<SavedRecord>> Snapshot(RemoteNestRepository repository, CancellationToken ct)
    {
        var records = await repository.ListAsync(ct);
        Check(records.Count <= 100, "Smoke snapshot refuses more than 100 records.");
        Check(records.Select(r => r.Id).Distinct().Count() == records.Count, "List ids must be unique.");
        var snapshot = new List<SavedRecord>();
        foreach (var record in records.OrderBy(r => r.Id))
        {
            var readback = await repository.GetMetadataAsync(record.Id, ct);
            Check(readback is not null, "Snapshot metadata must be readable.");
            CheckMetadata(record, readback!);
            var file = await repository.GetFileAsync(record.Id, ct);
            Check(file is not null && file.LongLength == record.FileSize, "Snapshot archive size must match metadata.");
            snapshot.Add(new SavedRecord { Metadata = readback!, Sha256 = Hash(file!) });
        }
        return snapshot;
    }

    private static async Task CheckSnapshot(RemoteNestRepository repository,
        IReadOnlyList<SavedRecord> expected, CancellationToken ct)
    {
        await CheckMembership(repository, expected.Select(e => e.Metadata.Id), ct);
        var actual = await Snapshot(repository, ct);
        foreach (var entry in expected)
        {
            var readback = actual.Single(e => e.Metadata.Id == entry.Metadata.Id);
            CheckMetadata(entry.Metadata, readback.Metadata);
            Check(entry.Sha256 == readback.Sha256, "Persisted archive SHA256 must match state.");
            if (entry.Geometry is not null)
            {
                var archive = await repository.GetFileAsync(entry.Metadata.Id, ct);
                Check(archive is not null, "Persisted archive must be readable.");
                CheckArchive(archive!, entry.Metadata, entry.Geometry);
            }
        }
    }

    private static async Task CheckMembership(RemoteNestRepository repository,
        IEnumerable<Guid> ids, CancellationToken ct)
    {
        var expected = ids.Order().ToArray();
        var actual = (await repository.ListAsync(ct)).Select(r => r.Id).Order().ToArray();
        Check(expected.SequenceEqual(actual), "List must contain exactly the expected ids.");
    }

    private static void CheckMetadata(NestRecord expected, NestRecord actual) =>
        Check(JsonSerializer.Serialize(expected, JsonOptions) == JsonSerializer.Serialize(actual, JsonOptions),
            "All metadata fields must round-trip exactly.");

    private static void CheckBinding(NestSaveSession session, Guid id, string url) =>
        Check(session.RemoteId == id && session.ServerUrl == url, "Session must bind to the saved id and server URL.");

    private static Nest SyntheticNest()
    {
        var program = new CncProgram();
        program.MoveTo(0, 0);
        program.LineTo(6, 0);
        program.LineTo(6, 3);
        program.LineTo(0, 3);
        program.LineTo(0, 0);
        var drawing = new Drawing("Synthetic rectangle", program);
        drawing.Quantity.Required = 1;
        var nest = new Nest("Synthetic container smoke")
        {
            Customer = "Synthetic test customer",
            DateCreated = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Unspecified),
            Material = new Material("Synthetic alloy", "test-grade", 0.25),
            Thickness = 0.25,
            Status = NestStatus.Quote,
            Notes = "Neutral generated fixture; no customer data",
            MadeBy = "Synthetic smoke author",
            Units = Units.Inches,
        };
        nest.Drawings.Add(drawing);
        var plate = new Plate(new Size(20, 30)) { Quantity = 1 };
        plate.Parts.Add(new Part(drawing, new Vector(2, 3)));
        nest.Plates.Add(plate);
        Check(drawing.Area == 18, "Synthetic rectangle must have closed area 18.");
        return nest;
    }

    private static byte[] Archive(Nest nest)
    {
        using var stream = new MemoryStream();
        Check(new NestWriter(nest).Write(stream), "NestWriter must succeed.");
        return stream.ToArray();
    }

    private static GeometryState Geometry(Nest nest) => new()
    {
        PlateWidth = nest.Plates[0].Size.Width,
        PlateLength = nest.Plates[0].Size.Length,
        PartX = nest.Plates[0].Parts[0].Location.X,
        PartY = nest.Plates[0].Parts[0].Location.Y,
        DrawingArea = nest.Drawings.Single().Area,
    };

    private static void CheckArchive(byte[] archive, NestRecord metadata, GeometryState geometry)
    {
        using var stream = new MemoryStream(archive, writable: false);
        var reader = new NestReader(stream);
        var nest = reader.Read();
        Check(reader.Warnings.Count == 0, "NestReader must parse without warnings.");
        var parsed = NestRecordFactory.FromNest(nest, metadata.Id, archive.LongLength);
        parsed.SavedAt = metadata.SavedAt;
        CheckMetadata(metadata, parsed);
        Check(nest.Drawings.Count == 1 && nest.Plates.Count == 1 && nest.Plates[0].Parts.Count == 1,
            "Parsed archive must contain one drawing, plate, and part.");
        Check(nest.Drawings.Single().Quantity.Required == 1 && nest.Drawings.Single().Quantity.Nested == 1,
            "Parsed drawing quantities must match.");
        Check(nest.Plates[0].Size.Width == geometry.PlateWidth && nest.Plates[0].Size.Length == geometry.PlateLength
            && nest.Plates[0].Parts[0].Location.X == geometry.PartX && nest.Plates[0].Parts[0].Location.Y == geometry.PartY
            && nest.Drawings.Single().Area == geometry.DrawingArea && nest.Drawings.Single().Program.Codes.Count == 5,
            "Parsed plate/rectangle geometry must match.");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new SmokeAssertionException(message);
    }

    private sealed class SmokeAssertionException(string message) : Exception(message);

    private sealed record Options(string Mode, string Url, string? State, bool AllowNonempty)
    {
        public static Options Parse(string[] args)
        {
            Check(args.Length > 0 && args[0] is "seed" or "verify" or "reject" or "limits",
                "Mode must be seed, verify, reject, or limits.");
            string? url = null;
            string? state = null;
            var allowNonempty = false;
            for (var i = 1; i < args.Length; i++)
            {
                if (args[i] == "--url" && url is null && i + 1 < args.Length)
                    url = args[++i];
                else if (args[i] == "--state" && state is null && i + 1 < args.Length)
                    state = args[++i];
                else if (args[i] == "--test-allow-nonempty" && !allowNonempty)
                    allowNonempty = true;
                else
                    throw new SmokeAssertionException("Invalid or duplicate smoke argument.");
            }
            Check(Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp
                && uri.Host == "127.0.0.1" && uri.Port > 0 && uri.AbsolutePath == "/"
                && uri.UserInfo == "" && uri.Query == "" && uri.Fragment == "",
                "Smoke requires a plain http://127.0.0.1:<port> URL without credentials, path, query, or fragment.");
            Check(args[0] is "reject" or "limits" ? state is null && !allowNonempty : !string.IsNullOrWhiteSpace(state),
                "Seed/verify require --state; reject/limits do not accept state or an override.");
            Check(args[0] == "seed" || !allowNonempty, "Nonempty override is TEST-only and seed-only.");
            return new Options(args[0], uri!.GetLeftPart(UriPartial.Authority), state, allowNonempty);
        }
    }

    private sealed class SmokeState
    {
        public int Version { get; set; }
        public List<SavedRecord> Records { get; set; } = new();
    }

    private sealed class SavedRecord
    {
        public NestRecord Metadata { get; set; } = new();
        public string Sha256 { get; set; } = "";
        public GeometryState? Geometry { get; set; }
    }

    private sealed class GeometryState
    {
        public double PlateWidth { get; set; }
        public double PlateLength { get; set; }
        public double PartX { get; set; }
        public double PartY { get; set; }
        public double DrawingArea { get; set; }
    }

    // Observe the real repository's responses; do not reimplement its HTTP endpoints.
    private sealed class WireContractHandler : DelegatingHandler
    {
        private static readonly string[] Fields = typeof(NestRecord).GetProperties()
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).Order().ToArray();
        public int CheckedResponses { get; private set; }

        public WireContractHandler() : base(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            try
            {
                if (response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NoContent
                    && !(request.Method == HttpMethod.Get
                        && request.RequestUri!.AbsolutePath.EndsWith("/file", StringComparison.Ordinal)))
                {
                    using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                    if (json.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var record in json.RootElement.EnumerateArray())
                            CheckWireRecord(record);
                    }
                    else
                        CheckWireRecord(json.RootElement);
                    CheckedResponses++;
                }
                return response;
            }
            catch
            {
                response.Dispose();
                throw;
            }
        }

        private static void CheckWireRecord(JsonElement record)
        {
            Check(record.ValueKind == JsonValueKind.Object
                && record.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(Fields),
                "Raw JSON must contain exactly all camelCase metadata fields.");
            var status = record.GetProperty("status");
            Check(status.ValueKind == JsonValueKind.String
                && status.GetString() is "quote" or "toBeCut" or "hasBeenCut", "Raw JSON status must be a camelCase string enum.");
        }
    }
}
