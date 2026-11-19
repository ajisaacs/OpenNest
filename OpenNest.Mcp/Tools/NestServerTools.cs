using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Server;
using OpenNest.Data;
using OpenNest.Geometry;
using OpenNest.Mcp.Database;

namespace OpenNest.Mcp.Tools
{
    /// <summary>
    /// Database-mode tools: the nest server's drawing library, drawing sets, releases (Make
    /// Nests) and saved nests, through the same clients and release session the desktop uses.
    /// </summary>
    [McpServerToolType]
    public class NestServerTools
    {
        private const string SheetSizesHelp =
            "Optional candidate sheet sizes as WIDTHxLENGTH in the nest's units, e.g. [\"60x120\",\"72x144\"]. "
            + "Each size is tried alone and, for two or more, all together as plate options; the layout leaving the "
            + "fewest parts unplaced, then using the fewest sheets, then the least sheet area, is kept. "
            + "Omit to nest on the nest defaults' plate size and options, as the desktop does.";

        private readonly NestServerContext _server;

        public NestServerTools(NestServerContext server)
        {
            _server = server;
        }

        [McpServerTool(Name = "search_drawings")]
        [Description("Search the nest server's drawing library. Text search matches names; filters narrow by customer, material, grade and thickness. Returns ids for get_drawing, create_set and make_nests.")]
        public Task<string> SearchDrawings(
            [Description("Text to search for in drawing names (optional)")] string search = null,
            [Description("Customer (optional)")] string customer = null,
            [Description("Material name, e.g. 'Stainless' (optional)")] string material = null,
            [Description("Material grade, e.g. '304' (optional)")] string grade = null,
            [Description("Thickness in the drawing's units (optional)")] double? thickness = null,
            [Description("Include archived drawings (default false)")] bool includeArchived = false,
            [Description("Results to skip (default 0)")] int offset = 0,
            [Description("Most results to return (default 50, at most 500)")] int limit = 50,
            CancellationToken cancellationToken = default) =>
            RunAsync(async () =>
            {
                var page = await _server.Library.QueryAsync(new DrawingLibraryQuery
                {
                    Search = search ?? "",
                    Customer = customer ?? "",
                    MaterialName = material ?? "",
                    MaterialGrade = grade ?? "",
                    Thickness = thickness,
                    IncludeArchived = includeArchived,
                    Offset = System.Math.Max(0, offset),
                    Limit = System.Math.Clamp(limit, 1, DrawingLibraryQuery.MaxLimit),
                }, cancellationToken);
                var sb = new StringBuilder();
                sb.AppendLine($"{page.Items.Count} of {page.Total} drawings (offset {page.Offset}):");
                foreach (var drawing in page.Items)
                    sb.AppendLine("- " + DescribeDrawing(drawing));
                return sb.ToString();
            });

        [McpServerTool(Name = "get_drawing")]
        [Description("One library drawing by id or exact name: material, thickness, size, notes, revision and the sets that list it.")]
        public Task<string> GetDrawing(
            [Description("Drawing id, or its exact name")] string drawing,
            CancellationToken cancellationToken = default) =>
            RunAsync(async () =>
            {
                var (record, error) = await ResolveDrawingAsync(drawing, cancellationToken);
                if (record == null)
                    return Error(error);
                var sb = new StringBuilder();
                sb.AppendLine(DescribeDrawing(record));
                sb.AppendLine($"Saved {record.SavedAt:yyyy-MM-dd HH:mm} by {Blank(record.SavedBy)}");
                if (record.Notes.Length > 0)
                    sb.AppendLine($"Notes: {record.Notes}");
                var sets = await _server.Sets.GetSetsContainingAsync(record.Id, 0, 100, cancellationToken);
                if (sets == null || sets.Items.Count == 0)
                    sb.AppendLine("In no drawing set.");
                else
                {
                    sb.AppendLine($"In {sets.Total} set(s):");
                    foreach (var set in sets.Items)
                        sb.AppendLine($"- {set.Name} | id {set.Id} rev {set.Revision}{(set.Archived ? " | archived" : "")}");
                }
                return sb.ToString();
            });

        [McpServerTool(Name = "search_sets")]
        [Description("Search the nest server's drawing sets (assemblies/kits of library drawings) by name text and customer.")]
        public Task<string> SearchSets(
            [Description("Text to search for in set names (optional)")] string search = null,
            [Description("Customer (optional)")] string customer = null,
            [Description("Include archived sets (default false)")] bool includeArchived = false,
            [Description("Results to skip (default 0)")] int offset = 0,
            [Description("Most results to return (default 50, at most 500)")] int limit = 50,
            CancellationToken cancellationToken = default) =>
            RunAsync(async () =>
            {
                var page = await _server.Sets.QueryAsync(new DrawingSetQuery
                {
                    Search = search ?? "",
                    Customer = customer ?? "",
                    IncludeArchived = includeArchived,
                    Offset = System.Math.Max(0, offset),
                    Limit = System.Math.Clamp(limit, 1, DrawingSetQuery.MaxLimit),
                }, cancellationToken);
                var sb = new StringBuilder();
                sb.AppendLine($"{page.Items.Count} of {page.Total} sets (offset {page.Offset}):");
                foreach (var set in page.Items)
                    sb.AppendLine("- " + DescribeSet(set));
                return sb.ToString();
            });

        [McpServerTool(Name = "get_set")]
        [Description("One drawing set by id or exact name, with every member's drawing, quantity per kit, material and thickness.")]
        public Task<string> GetSet(
            [Description("Set id, or its exact name")] string set,
            CancellationToken cancellationToken = default) =>
            RunAsync(async () =>
            {
                var (record, error) = await ResolveSetAsync(set, cancellationToken);
                return record == null ? Error(error) : FormatSet(record);
            });

        [McpServerTool(Name = "create_set")]
        [Description("Create a drawing set on the nest server from library drawings and per-kit quantities. Refuses a name another set already has.")]
        public Task<string> CreateSet(
            [Description("Set name, e.g. '4348 A21 Lower element rack'")] string name,
            [Description("Members: [{\"drawing\": id or exact name, \"quantity\": per kit}]")] DrawingQuantityInput[] members,
            [Description("Customer (optional)")] string customer = null,
            [Description("Notes (optional)")] string notes = null,
            CancellationToken cancellationToken = default) =>
            RunAsync(async () =>
            {
                if (string.IsNullOrWhiteSpace(name))
                    return Error("A set needs a name.");
                var existing = await FindSetsByNameAsync(name.Trim(), cancellationToken);
                if (existing.Count > 0)
                    return Error($"A set named '{existing[0].Name}' already exists (id {existing[0].Id}). Use update_set to change it.");
                var (resolved, error) = await ResolveMembersAsync(members, cancellationToken);
                if (resolved == null)
                    return Error(error);
                var created = await _server.Sets.CreateAsync(new DrawingSetRecord
                {
                    Id = Guid.NewGuid(),
                    Name = name.Trim(),
                    Customer = customer?.Trim() ?? "",
                    Notes = notes ?? "",
                    SavedBy = _server.UserName,
                    Members = resolved,
                }, cancellationToken);
                var stored = await _server.Sets.GetAsync(created.Id, cancellationToken) ?? created;
                return "Created set.\n" + FormatSet(stored);
            });

        [McpServerTool(Name = "update_set")]
        [Description("Save a new revision of a drawing set: rename it or change its customer, notes or members. Omitted fields keep their current value; members, when given, replace the whole list.")]
        public Task<string> UpdateSet(
            [Description("Set id, or its exact name")] string set,
            [Description("New name (optional)")] string name = null,
            [Description("New customer (optional)")] string customer = null,
            [Description("New notes (optional)")] string notes = null,
            [Description("New member list, replacing the current one: [{\"drawing\": id or exact name, \"quantity\": per kit}] (optional)")] DrawingQuantityInput[] members = null,
            CancellationToken cancellationToken = default) =>
            RunAsync(async () =>
            {
                var (current, error) = await ResolveSetAsync(set, cancellationToken);
                if (current == null)
                    return Error(error);
                if (name != null && !string.Equals(name.Trim(), current.Name, StringComparison.OrdinalIgnoreCase)
                    && (await FindSetsByNameAsync(name.Trim(), cancellationToken)).FirstOrDefault(s => s.Id != current.Id) is { } clash)
                    return Error($"Another set is already named '{clash.Name}' (id {clash.Id}).");

                IReadOnlyList<DrawingSetMember> newMembers = current.Members
                    .Select(m => new DrawingSetMember { DrawingId = m.DrawingId, Quantity = m.Quantity }).ToList();
                if (members != null)
                {
                    var (resolved, memberError) = await ResolveMembersAsync(members, cancellationToken);
                    if (resolved == null)
                        return Error(memberError);
                    newMembers = resolved;
                }

                var saved = await _server.Sets.UpdateAsync(current.Id, new DrawingSetRecord
                {
                    Id = current.Id,
                    Name = name?.Trim() ?? current.Name,
                    Customer = customer?.Trim() ?? current.Customer,
                    Notes = notes ?? current.Notes,
                    Archived = current.Archived,
                    SavedBy = _server.UserName,
                    Members = newMembers,
                }, current.Revision, cancellationToken);
                var stored = await _server.Sets.GetAsync(saved.Id, cancellationToken) ?? saved;
                return $"Saved revision {stored.Revision}.\n" + FormatSet(stored);
            });

        [McpServerTool(Name = "archive_set")]
        [Description("Archive a drawing set so it no longer shows by default. Releases already made are kept.")]
        public Task<string> ArchiveSet(
            [Description("Set id, or its exact name")] string set,
            CancellationToken cancellationToken = default) =>
            SetArchivedAsync(set, archive: true, cancellationToken);

        [McpServerTool(Name = "restore_set")]
        [Description("Restore an archived drawing set.")]
        public Task<string> RestoreSet(
            [Description("Set id, or its exact name")] string set,
            CancellationToken cancellationToken = default) =>
            SetArchivedAsync(set, archive: false, cancellationToken);

        [McpServerTool(Name = "plan_release")]
        [Description("Read-only preview of a release (Make Nests): sets x kits plus drawings on their own x quantity, split into material groups (material, grade, thickness, units, cut type) with each group's parts and quantities, refusals, warnings and the Draft nests it could merge into. Nothing is created.")]
        public Task<string> PlanRelease(
            [Description("Sets to release: [{\"set\": id or exact name, \"quantity\": kits}] (optional)")] ReleaseSetInput[] sets = null,
            [Description("Library drawings to release on their own: [{\"drawing\": id or exact name, \"quantity\": n}] (optional)")] DrawingQuantityInput[] drawings = null,
            [Description("Include archived drawings (default false: they are skipped)")] bool includeArchived = false,
            CancellationToken cancellationToken = default) =>
            RunAsync(async () =>
            {
                var (plan, error) = await BuildPlanAsync(sets, drawings, includeArchived, cancellationToken);
                if (plan == null)
                    return Error(error);
                var sb = new StringBuilder();
                await AppendPlanAsync(sb, plan, cancellationToken);
                return sb.ToString();
            });

        [McpServerTool(Name = "make_nests")]
        [Description("Release sets and drawings as the desktop's Make Nests does: one Draft nest per material group, new (named by the server, set up as File > New) or merged into a named draft, nested after release with the desktop's engine (MaterialLedger stock first when set up on this PC), saved to the nest server and recorded as one release. Returns each group's nest name, id, revision, sheets, utilization and unplaced parts, and the release id (undo_release undoes it). Refuses everything when any group cannot be released, unless skipRefusedGroups.")]
        public Task<string> MakeNests(
            [Description("Sets to release: [{\"set\": id or exact name, \"quantity\": kits}] (optional)")] ReleaseSetInput[] sets = null,
            [Description("Library drawings to release on their own: [{\"drawing\": id or exact name, \"quantity\": n}] (optional)")] DrawingQuantityInput[] drawings = null,
            [Description("Release notes, e.g. job numbers (optional)")] string notes = null,
            [Description(SheetSizesHelp)] string[] sheetSizes = null,
            [Description("Groups to merge into existing Draft nests instead of new nests: [{\"group\": key from plan_release, \"draft\": nest id or exact name}] (optional)")] MergeInput[] merge = null,
            [Description("Include archived drawings (default false: they are skipped)")] bool includeArchived = false,
            [Description("Run Auto Nest on each nest before saving (default true)")] bool nestAfterRelease = true,
            [Description("Offer MaterialLedger stock first when it is set up on this PC (default true)")] bool useLedger = true,
            [Description("Release the groups that can be and leave out refused ones (default false: refuse all)")] bool skipRefusedGroups = false,
            [Description("Whole-job engine name (optional; default: the engine last chosen on the desktop)")] string engine = null,
            CancellationToken cancellationToken = default) =>
            RunAsync(async () =>
            {
                var engineName = _server.ResolveEngine(engine, out var engineError);
                if (engineName == null)
                    return Error(engineError);
                var sizes = SheetSizes.Parse(sheetSizes, out var sizeError);
                if (sizes == null)
                    return Error(sizeError);
                var (plan, error) = await BuildPlanAsync(sets, drawings, includeArchived, cancellationToken);
                if (plan == null)
                    return Error(error);
                var refused = plan.Groups.Where(g => !g.CanRelease).ToList();
                var chosen = plan.Groups.Where(g => g.CanRelease).ToList();
                if (chosen.Count == 0 || (refused.Count > 0 && !skipRefusedGroups))
                {
                    var why = new StringBuilder("Error: nothing was released. ");
                    why.AppendLine(chosen.Count == 0 ? "No group can be released." : "Some groups cannot be released (pass skipRefusedGroups to release the rest).");
                    foreach (var group in refused)
                        why.AppendLine($"- {group.Key}: {group.Refusal}");
                    return why.ToString();
                }

                var merges = new Dictionary<SetReleaseGroup, NestRecord>();
                foreach (var target in merge ?? Array.Empty<MergeInput>())
                {
                    var group = chosen.FirstOrDefault(g => string.Equals(g.Key, target?.Group?.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (group == null)
                        return Error($"No releasable group '{target?.Group}'. Groups: {string.Join(", ", chosen.Select(g => g.Key))}.");
                    if (merges.ContainsKey(group))
                        return Error($"Group '{group.Key}' is merged twice.");
                    var (draft, draftError) = await ResolveNestAsync(target.Draft, cancellationToken);
                    if (draft == null)
                        return Error(draftError);
                    merges[group] = draft;
                }

                var ledger = nestAfterRelease && useLedger ? _server.CreateLedger(Guid.Empty) : null;
                var trials = new Dictionary<Nest, SheetTrialOutcome>(ReferenceEqualityComparer.Instance);
                var session = new SetReleaseSession(_server.Sets, _server.Library, _server.Nests, _server.ServerUrl,
                    ReadPackage, SheetSizeTrials.Write,
                    async (group, _) =>
                    {
                        var made = await _server.CreateNestAsync(group);
                        if (made.Warning != null)
                            Warnings(made.Nest).Add(made.Warning);
                        return made.Nest;
                    },
                    SheetSizeTrials.Read,
                    !nestAfterRelease ? null : async (nest, token) =>
                    {
                        var outcome = await SheetSizeTrials.NestAsync(nest, sizes, ledger, engineName, token);
                        trials[nest] = outcome;
                        return new SetReleaseRenest(outcome.Nested, outcome.Note);
                    });

                var result = await session.RunAsync(plan, chosen, _server.UserName, notes ?? "", null, cancellationToken, merges);

                var sb = new StringBuilder();
                if (result.Release != null)
                    sb.AppendLine($"Release {result.Release.Id} recorded ({result.Created.Count} nest(s)).");
                else if (result.ReleaseError.Length > 0)
                    sb.AppendLine($"Release NOT recorded: {result.ReleaseError}");
                else
                    sb.AppendLine(result.Canceled ? "Canceled; the release was not recorded." : "The release was not recorded.");
                if (nestAfterRelease)
                    sb.AppendLine($"Engine: {engineName}. MaterialLedger: {(ledger != null ? "offered first" : useLedger ? "not set up on this PC, sheet sizes only" : "not used")}.");
                foreach (var group in result.Groups)
                {
                    sb.AppendLine();
                    sb.AppendLine($"Group {group.Group.Key}: {group.Outcome}");
                    if (group.Error.Length > 0)
                        sb.AppendLine($"  {group.Error}");
                    if (group.Record != null)
                        sb.AppendLine($"  Nest {group.Record.Name} | id {group.Record.Id} | revision {group.Record.Revision}"
                            + (group.MergedInto != null ? $" | merged into revision {group.MergedInto.Revision}" : " | new"));
                    if (group.Nest != null)
                    {
                        AppendLayout(sb, group.Nest, "  ");
                        if (trials.TryGetValue(group.Nest, out var outcome) && outcome.Trials.Count > 1)
                        {
                            sb.AppendLine($"  Sheet sizes tried (chose {outcome.Chosen.Name}):");
                            foreach (var trial in outcome.Trials)
                                sb.AppendLine("    " + trial.Describe());
                        }
                        foreach (var warning in Warnings(group.Nest))
                            sb.AppendLine($"  Warning: {warning}");
                    }
                    foreach (var warning in group.Warnings)
                        sb.AppendLine($"  Note: {warning}");
                }
                foreach (var group in refused)
                    sb.AppendLine($"\nGroup {group.Key}: not released: {group.Refusal}");
                return sb.ToString();
            });

        [McpServerTool(Name = "renest_nest")]
        [Description("Re-nest a Draft or Quote nest on the nest server from scratch (every plate removed, every drawing's required quantity nested again), optionally on the best of several sheet sizes, and save it as a new revision only when every part is placed and a layout was kept; the save is refused if someone saved the nest since it was read.")]
        public Task<string> RenestNest(
            [Description("Nest id, or its exact name (e.g. N26-2CM)")] string nest,
            [Description(SheetSizesHelp)] string[] sheetSizes = null,
            [Description("Save the result as a new revision (default true); false only reports what it would be")] bool save = true,
            [Description("Offer MaterialLedger stock first when it is set up on this PC (default true)")] bool useLedger = true,
            [Description("Whole-job engine name (optional; default: the engine last chosen on the desktop)")] string engine = null,
            CancellationToken cancellationToken = default) =>
            RunAsync(async () =>
            {
                var engineName = _server.ResolveEngine(engine, out var engineError);
                if (engineName == null)
                    return Error(engineError);
                var sizes = SheetSizes.Parse(sheetSizes, out var sizeError);
                if (sizes == null)
                    return Error(sizeError);
                var (record, error) = await ResolveNestAsync(nest, cancellationToken);
                if (record == null)
                    return Error(error);
                if (record.Status is not (NestStatus.Draft or NestStatus.Quote))
                    return Error($"'{record.Name}' is {record.Status}; only Draft and Quote nests are re-nested.");
                var archive = await _server.Nests.GetFileAsync(record.Id, cancellationToken);
                if (archive == null)
                    return Error($"'{record.Name}' has no archive on the server.");

                var ledger = useLedger ? _server.CreateLedger(record.Id) : null;
                var (renested, outcome) = await SheetSizeTrials.NestCopyAsync(archive,
                    copy => NestRecordFactory.ApplyMetadata(copy, record), sizes, ledger, engineName, cancellationToken);

                var sb = new StringBuilder();
                sb.AppendLine($"{record.Name} | id {record.Id} | revision {record.Revision} | engine {engineName}");
                if (outcome.Trials.Count > 1)
                {
                    sb.AppendLine($"Sheet sizes tried (chose {outcome.Chosen.Name}):");
                    foreach (var trial in outcome.Trials)
                        sb.AppendLine("  " + trial.Describe());
                }
                if (outcome.Note.Length > 0)
                    sb.AppendLine($"Auto Nest: {outcome.Note}");
                AppendLayout(sb, renested, "");

                if (!save)
                    return sb.AppendLine("Not saved (save=false).").ToString();
                if (!outcome.Nested)
                    return sb.AppendLine("Not saved: no layout was kept.").ToString();
                if (outcome.Chosen.Unplaced > 0)
                    return sb.AppendLine($"Not saved: {outcome.Chosen.Unplaced} part(s) unplaced.").ToString();

                var bytes = SheetSizeTrials.Write(renested);
                var saved = await _server.Nests.UpdateFileAsync(record.Id, bytes,
                    NestRecordFactory.FromNest(renested, record.Id, bytes.LongLength), record.Revision, cancellationToken);
                return sb.AppendLine($"Saved revision {saved.Revision} ({saved.PlateCount} plate(s), {saved.PartCount} part(s)).").ToString();
            });

        [McpServerTool(Name = "nest_summary")]
        [Description("A saved nest on the nest server: status, material, sheets with sizes and utilization, and each drawing's required and nested quantities.")]
        public Task<string> NestSummary(
            [Description("Nest id, or its exact name (e.g. N26-2CM)")] string nest,
            CancellationToken cancellationToken = default) =>
            RunAsync(async () =>
            {
                var (record, error) = await ResolveNestAsync(nest, cancellationToken);
                if (record == null)
                    return Error(error);
                var archive = await _server.Nests.GetFileAsync(record.Id, cancellationToken);
                if (archive == null)
                    return Error($"'{record.Name}' has no archive on the server.");
                var read = SheetSizeTrials.Read(archive);
                var sb = new StringBuilder();
                sb.AppendLine($"{record.Name} | id {record.Id} | revision {record.Revision} | {record.Status}");
                sb.AppendLine($"Material: {Material(record.Material, record.MaterialGrade, record.Thickness, read.Units)} | customer {Blank(record.Customer)} | made by {Blank(record.MadeBy)}");
                if (record.Comments.Length > 0)
                    sb.AppendLine($"Comments: {record.Comments}");
                sb.AppendLine($"Plate defaults: {SheetSizes.Format(read.PlateDefaults.Size)}"
                    + (read.PlateOptions.Count > 0 ? $"; options {string.Join(", ", read.PlateOptions.Select(o => SheetSizes.Format(new Size(o.Width, o.Length))))}" : ""));
                AppendLayout(sb, read, "");
                sb.AppendLine("Drawings (required / nested):");
                foreach (var drawing in read.Drawings.Where(d => !d.IsCutOff).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine($"  {drawing.Name}: {drawing.Quantity.Required} / {drawing.Quantity.Nested}");
                return sb.ToString();
            });

        [McpServerTool(Name = "get_release")]
        [Description("One release by id: its sets and drawings, notes, and each nest it created or merged into, with undo state.")]
        public Task<string> GetRelease(
            [Description("Release id (make_nests prints it)")] string release,
            CancellationToken cancellationToken = default) =>
            RunAsync(async () =>
            {
                if (!Guid.TryParse(release, out var id))
                    return Error($"'{release}' is not a release id.");
                var record = await _server.Sets.GetReleaseAsync(id, cancellationToken);
                return record == null ? Error($"Release {id} is not on the nest server.") : FormatRelease(record);
            });

        [McpServerTool(Name = "undo_release")]
        [Description("Undo a release, all or nothing: nests it created are deleted (recoverable on the server) and drafts it merged into are restored to their earlier revision. Refused when a nest changed since the release unless that nest is listed in skipNests, which leaves it as it is.")]
        public Task<string> UndoRelease(
            [Description("Release id (make_nests prints it)")] string release,
            [Description("Nest ids to leave as they are because they changed since the release (optional)")] string[] skipNests = null,
            CancellationToken cancellationToken = default) =>
            RunAsync(async () =>
            {
                if (!Guid.TryParse(release, out var id))
                    return Error($"'{release}' is not a release id.");
                var skip = new List<Guid>();
                foreach (var text in skipNests ?? Array.Empty<string>())
                {
                    if (!Guid.TryParse(text, out var nestId))
                        return Error($"'{text}' is not a nest id.");
                    skip.Add(nestId);
                }
                try
                {
                    var undone = await _server.Sets.UndoReleaseAsync(id,
                        new SetReleaseUndoRequest { UndoneBy = _server.UserName, SkipNests = skip }, cancellationToken);
                    return "Undone.\n" + FormatRelease(undone);
                }
                catch (SetReleaseUndoConflictException ex)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine(ex.Conflict.Reason == SetReleaseUndoRefusal.AlreadyUndone
                        ? "Error: the release is already undone."
                        : "Error: nothing was undone; these nests changed since the release (list them in skipNests to leave them as they are):");
                    foreach (var changed in ex.Conflict.ChangedNests)
                        sb.AppendLine($"- {changed.Name} | id {changed.Id} | now revision {changed.Revision}{(changed.Deleted ? " (deleted)" : "")}");
                    return sb.ToString();
                }
            });

        // --- Shared steps ---

        private Task<string> SetArchivedAsync(string set, bool archive, CancellationToken cancellationToken) =>
            RunAsync(async () =>
            {
                var (current, error) = await ResolveSetAsync(set, cancellationToken);
                if (current == null)
                    return Error(error);
                if (current.Archived == archive)
                    return $"'{current.Name}' is already {(archive ? "archived" : "not archived")} (revision {current.Revision}).";
                var saved = archive
                    ? await _server.Sets.ArchiveAsync(current.Id, current.Revision, cancellationToken)
                    : await _server.Sets.RestoreAsync(current.Id, current.Revision, cancellationToken);
                return $"{(archive ? "Archived" : "Restored")} '{saved.Name}' (id {saved.Id}, revision {saved.Revision}).";
            });

        private async Task<(SetReleasePlan Plan, string Error)> BuildPlanAsync(ReleaseSetInput[] sets,
            DrawingQuantityInput[] drawings, bool includeArchived, CancellationToken cancellationToken)
        {
            sets ??= Array.Empty<ReleaseSetInput>();
            drawings ??= Array.Empty<DrawingQuantityInput>();
            if (sets.Length == 0 && drawings.Length == 0)
                return (null, "Give at least one set or drawing to release.");
            var lines = new List<SetReleasePlanLine>();
            foreach (var line in sets)
            {
                var (set, error) = await ResolveSetAsync(line?.Set, cancellationToken);
                if (set == null)
                    return (null, error);
                lines.Add(new SetReleasePlanLine(set, line.Quantity));
            }
            var own = new List<SetReleaseDrawingLine>();
            foreach (var line in drawings)
            {
                var (drawing, error) = await ResolveDrawingAsync(line?.Drawing, cancellationToken);
                if (drawing == null)
                    return (null, error);
                own.Add(new SetReleaseDrawingLine(drawing, line.Quantity));
            }
            try
            {
                return (SetReleasePlan.Build(lines, own, includeArchived), null);
            }
            catch (ArgumentException ex)
            {
                return (null, ex.Message);
            }
        }

        private async Task AppendPlanAsync(StringBuilder sb, SetReleasePlan plan, CancellationToken cancellationToken)
        {
            sb.AppendLine($"Customer: {Blank(plan.Customer)}");
            foreach (var line in plan.Lines)
                sb.AppendLine($"Set {line.Set.Name} (rev {line.Set.Revision}) x {line.Quantity}");
            foreach (var line in plan.Drawings)
                sb.AppendLine($"Drawing {line.Drawing.Name} x {line.Quantity}");
            foreach (var skipped in plan.SkippedArchived)
                sb.AppendLine($"Skipped (archived): {skipped.Drawing?.Name ?? skipped.DrawingId.ToString()}");
            foreach (var group in plan.Groups)
            {
                sb.AppendLine();
                sb.AppendLine($"Group {group.Key}: {(group.CanRelease ? "can be released" : "REFUSED: " + group.Refusal)}");
                sb.AppendLine($"  {Material(group.MaterialName, group.MaterialGrade, group.Thickness, group.Units)} {group.CutType.ToString().ToLowerInvariant()}, {group.TotalQuantity} part(s)");
                foreach (var item in group.Items)
                    sb.AppendLine($"  - {item.Drawing.Name} x {item.Quantity} | id {item.Drawing.Id} rev {item.Drawing.Revision}"
                        + Size(item.Drawing.Geometry));
                foreach (var warning in group.Warnings)
                    sb.AppendLine($"  Warning: {warning}");
                if (!group.CanRelease)
                    continue;
                var drafts = await SetReleasePlan.FindDraftsAsync(_server.Nests, group, 20, cancellationToken);
                sb.AppendLine(drafts.Count == 0
                    ? "  Drafts to merge into: none (a new nest is made)"
                    : "  Drafts to merge into (optional): " + string.Join("; ",
                        drafts.Select(d => $"{d.Name} id {d.Id} rev {d.Revision}, {d.PlateCount} plate(s), {d.PartCount} part(s)")));
            }
        }

        private async Task<(List<DrawingSetMember> Members, string Error)> ResolveMembersAsync(
            DrawingQuantityInput[] members, CancellationToken cancellationToken)
        {
            if (members == null || members.Length == 0)
                return (null, "A set needs at least one member.");
            var resolved = new List<DrawingSetMember>();
            foreach (var member in members)
            {
                var (drawing, error) = await ResolveDrawingAsync(member?.Drawing, cancellationToken);
                if (drawing == null)
                    return (null, error);
                if (member.Quantity < 1 || member.Quantity > DrawingSetRules.MaxQuantity)
                    return (null, $"'{drawing.Name}' needs a quantity from 1 to {DrawingSetRules.MaxQuantity}.");
                if (resolved.Any(m => m.DrawingId == drawing.Id))
                    return (null, $"'{drawing.Name}' is listed twice. List it once with a larger quantity.");
                resolved.Add(new DrawingSetMember { DrawingId = drawing.Id, Quantity = member.Quantity });
            }
            return (resolved, null);
        }

        private async Task<(DrawingLibraryRecord Record, string Error)> ResolveDrawingAsync(string drawing,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(drawing))
                return (null, "A drawing id or name is required.");
            drawing = drawing.Trim();
            if (Guid.TryParse(drawing, out var id))
            {
                var record = await _server.Library.GetAsync(id, cancellationToken);
                return record == null ? (null, $"No library drawing has id {id}.") : (record, null);
            }
            var page = await _server.Library.QueryAsync(new DrawingLibraryQuery
            {
                Search = drawing,
                IncludeArchived = true,
                Limit = DrawingLibraryQuery.MaxLimit,
            }, cancellationToken);
            var exact = page.Items.Where(d => string.Equals(d.Name, drawing, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count > 1 && exact.Count(d => !d.Archived) == 1)
                exact = exact.Where(d => !d.Archived).ToList();
            return exact.Count switch
            {
                1 => (exact[0], null),
                0 => (null, $"No library drawing is named '{drawing}'." + Similar(page.Items.Select(d => d.Name))),
                _ => (null, $"{exact.Count} library drawings are named '{drawing}'; use an id: "
                    + string.Join("; ", exact.Select(DescribeDrawing))),
            };
        }

        private async Task<List<DrawingSetRecord>> FindSetsByNameAsync(string name, CancellationToken cancellationToken)
        {
            var page = await _server.Sets.QueryAsync(new DrawingSetQuery
            {
                Search = name,
                IncludeArchived = true,
                Limit = DrawingSetQuery.MaxLimit,
            }, cancellationToken);
            return page.Items.Where(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private async Task<(DrawingSetRecord Record, string Error)> ResolveSetAsync(string set, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(set))
                return (null, "A set id or name is required.");
            set = set.Trim();
            Guid id;
            if (!Guid.TryParse(set, out id))
            {
                var exact = await FindSetsByNameAsync(set, cancellationToken);
                if (exact.Count > 1 && exact.Count(s => !s.Archived) == 1)
                    exact = exact.Where(s => !s.Archived).ToList();
                if (exact.Count == 0)
                {
                    var near = await _server.Sets.QueryAsync(new DrawingSetQuery { Search = set, IncludeArchived = true, Limit = 10 }, cancellationToken);
                    return (null, $"No drawing set is named '{set}'." + Similar(near.Items.Select(s => s.Name)));
                }
                if (exact.Count > 1)
                    return (null, $"{exact.Count} sets are named '{set}'; use an id: " + string.Join("; ", exact.Select(DescribeSet)));
                id = exact[0].Id;
            }
            var record = await _server.Sets.GetAsync(id, cancellationToken);
            return record == null ? (null, $"No drawing set has id {id}.") : (record, null);
        }

        private async Task<(NestRecord Record, string Error)> ResolveNestAsync(string nest, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(nest))
                return (null, "A nest id or name is required.");
            nest = nest.Trim();
            if (Guid.TryParse(nest, out var id))
            {
                var record = await _server.Nests.GetMetadataAsync(id, cancellationToken);
                return record == null || record.Deleted ? (null, $"No nest has id {id}.") : (record, null);
            }
            var page = await _server.Nests.QueryAsync(new NestQuery { Search = nest, Limit = NestQuery.MaxLimit }, cancellationToken);
            var exact = page.Items.Where(n => !n.Deleted && string.Equals(n.Name, nest, StringComparison.OrdinalIgnoreCase)).ToList();
            return exact.Count switch
            {
                1 => (exact[0], null),
                0 => (null, $"No nest is named '{nest}'." + Similar(page.Items.Select(n => n.Name))),
                _ => (null, $"{exact.Count} nests are named '{nest}'; use an id: "
                    + string.Join("; ", exact.Select(n => $"{n.Name} id {n.Id} ({n.Status}, saved {n.SavedAt:yyyy-MM-dd})"))),
            };
        }

        private static LibraryPackage ReadPackage(byte[] bytes)
        {
            var contents = OpenNest.IO.DrawingPackage.Read(bytes);
            return new LibraryPackage(bytes, contents.Drawing, contents.Units, contents.Warnings);
        }

        // Warnings raised while making a nest (File > New's unreadable defaults), by nest.
        private readonly Dictionary<Nest, List<string>> _nestWarnings = new(ReferenceEqualityComparer.Instance);

        private List<string> Warnings(Nest nest)
        {
            if (!_nestWarnings.TryGetValue(nest, out var list))
                _nestWarnings[nest] = list = new List<string>();
            return list;
        }

        private async Task<string> RunAsync(Func<Task<string>> action)
        {
            if (_server.Error != null)
                return Error(_server.Error);
            try
            {
                return await action();
            }
            catch (DrawingSetSaveConflictException ex)
            {
                return Error($"Someone saved the set since it was read (now revision {ex.Current.Revision}); nothing was changed. Try again.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Error(ex.Message);
            }
            finally
            {
                _nestWarnings.Clear();
            }
        }

        // --- Formatting ---

        private static string Error(string message) => "Error: " + message;

        private static string Blank(string text) => string.IsNullOrWhiteSpace(text) ? "(none)" : text;

        private static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        private static string Material(string name, string grade, double thickness, Units units) =>
            string.Join(" ", new[] { name, grade, $"{Number(thickness)} {(units == Units.Millimeters ? "mm" : "in")}" }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        private static string Size(DrawingGeometrySummary geometry) =>
            geometry is { IsMeasured: true } ? $" | {Number(geometry.Length)} x {Number(geometry.Width)}" : "";

        private static string Similar(IEnumerable<string> names)
        {
            var list = names.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToList();
            return list.Count == 0 ? "" : " Similar: " + string.Join(", ", list) + ".";
        }

        private static string DescribeDrawing(DrawingLibraryRecord d) =>
            $"{d.Name} | id {d.Id} rev {d.Revision} | customer {Blank(d.Customer)} | "
            + $"{Material(d.MaterialName, d.MaterialGrade, d.Thickness, d.Units)} {d.CutType.ToString().ToLowerInvariant()}"
            + Size(d.Geometry) + (d.Archived ? " | archived" : "");

        private static string DescribeSet(DrawingSetRecord s) =>
            $"{s.Name} | id {s.Id} rev {s.Revision} | customer {Blank(s.Customer)} | {s.MemberCount} member(s)"
            + (s.Archived ? " | archived" : "");

        private static string FormatSet(DrawingSetRecord set)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{set.Name} | id {set.Id} | revision {set.Revision} | customer {Blank(set.Customer)}{(set.Archived ? " | archived" : "")}");
            sb.AppendLine($"Saved {set.SavedAt:yyyy-MM-dd HH:mm} by {Blank(set.SavedBy)}");
            if (set.Notes.Length > 0)
                sb.AppendLine($"Notes: {set.Notes}");
            sb.AppendLine($"Members ({set.Members.Count}), quantity per kit:");
            foreach (var member in set.Members)
                sb.AppendLine(member.Drawing == null
                    ? $"- x{member.Quantity} drawing {member.DrawingId} (not in the library)"
                    : $"- x{member.Quantity} {DescribeDrawing(member.Drawing)}");
            return sb.ToString();
        }

        private static string FormatRelease(SetReleaseRecord release)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Release {release.Id} | {release.ReleasedAt:yyyy-MM-dd HH:mm} by {Blank(release.ReleasedBy)}"
                + (release.IsUndone ? $" | undone {release.UndoneAt:yyyy-MM-dd HH:mm} by {Blank(release.UndoneBy)}" : ""));
            if (release.Notes.Length > 0)
                sb.AppendLine($"Notes: {release.Notes}");
            foreach (var line in release.Sets)
                sb.AppendLine($"Set {Blank(line.SetName)} (id {line.SetId} rev {line.SetRevision}) x {line.Quantity}");
            foreach (var line in release.Drawings)
                sb.AppendLine($"Drawing {Blank(line.DrawingName)} (id {line.DrawingId}) x {line.Quantity}");
            foreach (var nest in release.Nests)
                sb.AppendLine($"Nest {nest.Nest?.Name ?? "?"} | id {nest.NestId} | {nest.Kind} | revision {(nest.RevisionBefore is { } before ? before + " -> " : "")}{nest.RevisionAfter}"
                    + $" | {Material(nest.MaterialName, nest.MaterialGrade, nest.Thickness, nest.Units)}"
                    + (nest.Undo is { } undo ? $" | undo: {undo}" : ""));
            return sb.ToString();
        }

        private static void AppendLayout(StringBuilder sb, Nest nest, string indent)
        {
            var trial = SheetSizeTrials.Measure(nest, null, nest.Plates.Count > 0, "");
            if (nest.Plates.Count == 0)
                sb.AppendLine($"{indent}Not nested: no sheets.");
            else
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}Sheets: {1} physical, {2:0.##} sq total, {3:P1} used", indent, trial.Sheets, trial.SheetArea, trial.Utilization));
                for (var i = 0; i < nest.Plates.Count; i++)
                {
                    var plate = nest.Plates[i];
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0}  Plate {1}: {2} x{3}, {4} part(s) each, {5:P1} used{6}", indent, i + 1,
                        SheetSizes.Format(plate.Size), plate.Quantity, plate.Parts.Count(p => !p.BaseDrawing.IsCutOff),
                        plate.Utilization(),
                        plate.StockItemCodes.Count > 0 ? $", ledger {string.Join(",", plate.StockItemCodes)}" : ""));
                }
            }
            var drawings = nest.Drawings.Where(d => !d.IsCutOff).ToList();
            sb.AppendLine($"{indent}Parts: {drawings.Sum(d => d.Quantity.Required)} required, {drawings.Sum(d => System.Math.Min(d.Quantity.Nested, d.Quantity.Required))} nested");
            var unplaced = drawings.Where(d => d.Quantity.Remaining > 0).ToList();
            if (unplaced.Count > 0)
                sb.AppendLine($"{indent}Unplaced: " + string.Join(", ", unplaced.Select(d => $"{d.Name} x{d.Quantity.Remaining}")));
        }
    }
}
