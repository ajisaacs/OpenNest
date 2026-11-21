using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Controls;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Forms
{
    // Separating a file that draws several parts (a sheet of ornaments, say) into one drawing per part.
    public partial class CadConverterForm
    {
        private PartBodies currentBodies;
        private FileListItem bodiesItem;
        private int bodiesVersion;

        /// <summary>The parts found in the shown item, once the background search has finished; null before.</summary>
        internal PartBodies CurrentBodies => bodiesItem == CurrentItem ? currentBodies : null;

        /// <summary>Completes when the parts search started by the last item load or filter change has finished.</summary>
        internal Task BodiesSearch { get; private set; } = Task.CompletedTask;

        // The outlines that make parts: shown entities that cut, on material layers.
        private static List<Entity> Outlines(FileListItem item) =>
            item.Entities
                .Where(e => e.Layer is { IsVisible: true } && e.IsVisible
                    && SpecialLayers.IsMaterial(e.Layer)
                    && EntityRoles.Of(e, item.EntityRoles) == EntityRole.Cut)
                .ToList();

        // Finds the item's parts off the UI thread, as the Simplify check does, and shows the
        // Separate button when there are two or more.
        private void RefreshSeparable(FileListItem item)
        {
            var version = ++bodiesVersion;
            currentBodies = null;
            bodiesItem = null;
            ShowSeparable(0);
            if (item == null)
            {
                BodiesSearch = Task.CompletedTask;
                return;
            }

            var outlines = Outlines(item);
            BodiesSearch = Task.Run(() => PartBodies.Find(outlines))
                .ContinueWith(
                    t =>
                    {
                        if (version != bodiesVersion || item != CurrentItem || !t.IsCompletedSuccessfully)
                            return;
                        currentBodies = t.Result;
                        bodiesItem = item;
                        ShowSeparable(t.Result.Count);
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void ShowSeparable(int count)
        {
            var separable = count >= 2;
            btnSeparate.Visible = separable;
            btnSeparate.Text = separable ? $"Separate ({count})" : "Separate...";
            entityView1.SeparablePartCount = separable ? count : 0;
        }

        private void OnSeparateClicked(object sender, EventArgs e) => SeparateParts(null);

        /// <summary>
        /// Replaces the shown item with one item per part. With <paramref name="chosen"/>, only those
        /// parts leave; the rest stay together under the item's name. Returns false when nothing changed.
        /// </summary>
        internal bool SeparateParts(IReadOnlyCollection<int> chosen)
        {
            var item = CurrentItem;
            var bodies = CurrentBodies;
            if (item == null || bodies == null || bodies.Count < 2)
                return false;

            var spanning = SpanningFeature(item, bodies);
            if (spanning != null)
            {
                ShowSeparateMessage($"A {spanning} joins more than one of these parts, so they cannot be separated. "
                    + "Remove it first.");
                return false;
            }
            if (programEditor.IsDirty && !ConfirmSeparate(
                    "Separating rebuilds each part's program, so your edits to this program will be lost. Separate anyway?"))
                return false;

            var leaving = (chosen ?? Enumerable.Range(0, bodies.Count)).Distinct().OrderBy(i => i).ToList();
            var staying = Enumerable.Range(0, bodies.Count).Except(leaving).ToList();
            var taken = new HashSet<string>(
                fileList.Items.Where(i => i != item).Select(i => i.Name), StringComparer.OrdinalIgnoreCase);

            var replacements = new List<FileListItem>();
            foreach (var index in leaving)
            {
                var name = UniqueName(bodies.NameOf(index, item.Labels) ?? $"{item.Name}-{index + 1}", taken);
                replacements.Add(Slice(item, bodies, new HashSet<int> { index }, name));
            }
            if (staying.Count > 0)
                replacements.Add(Slice(item, bodies, staying.ToHashSet(), UniqueName(item.Name, taken)));

            programEditor.Clear();
            fileList.ReplaceItem(fileList.SelectedIndex, replacements);
            return true;
        }

        internal virtual void ShowSeparateMessage(string message) =>
            MessageBox.Show(this, message, "Separate Parts", MessageBoxButtons.OK, MessageBoxIcon.Information);

        internal virtual bool ConfirmSeparate(string message) =>
            MessageBox.Show(this, message, "Separate Parts", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning)
                == DialogResult.OK;

        private void SeparateSelectedParts(IReadOnlyList<Entity> selection)
        {
            var bodies = CurrentBodies;
            if (bodies == null)
                return;
            var chosen = selection.Select(bodies.IndexOf).Where(i => i >= 0).ToHashSet();
            if (chosen.Count > 0)
                SeparateParts(chosen);
        }

        // A breakoff or tabbed cutout whose edges lie in more than one part cannot follow either.
        private static string SpanningFeature(FileListItem item, PartBodies bodies)
        {
            var owners = new Dictionary<Guid, int>();
            foreach (var entity in item.Entities)
                owners.TryAdd(entity.Id, bodies.IndexOf(entity));
            bool Spans(IEnumerable<Guid> ids) =>
                ids.Where(owners.ContainsKey).Select(id => owners[id]).Distinct().Count() > 1;
            if (item.Breakoffs.Any(b => Spans(b.EdgeIds)))
                return "breakoff";
            if (item.TabbedCutouts.Any(c => Spans(c.EdgeIds)))
                return "tabbed cutout";
            return null;
        }

        // The part of an item that lies in the given parts: its entities keep their IDs, so roles,
        // exclusions, suppression, breakoffs, tabbed cutouts and dimensions follow them; bends and
        // text go by position.
        private static FileListItem Slice(FileListItem item, PartBodies bodies, HashSet<int> parts, string name)
        {
            bool Owns(Entity e) => parts.Contains(bodies.IndexOf(e));
            bool OwnsPoint(Vector p) => parts.Contains(bodies.IndexOf(p));

            var entities = item.Entities.Where(Owns).ToList();
            var ids = entities.Select(e => e.Id).ToHashSet();
            bool OwnsIds(IEnumerable<Guid> edgeIds) => edgeIds.Any(ids.Contains);

            return new FileListItem
            {
                Name = name,
                Customer = item.Customer,
                Quantity = item.Quantity,
                Path = item.Path,
                Entities = entities,
                OriginalEntities = item.OriginalEntities?.Where(Owns).ToList(),
                RawEntities = item.RawEntities?.Where(Owns).ToList(),
                Bends = item.Bends?
                    .Where(b => b.SourceEntity != null
                        ? Owns(b.SourceEntity)
                        : OwnsPoint(new Vector((b.StartPoint.X + b.EndPoint.X) / 2, (b.StartPoint.Y + b.EndPoint.Y) / 2)))
                    .ToList() ?? new List<Bending.Bend>(),
                Breakoffs = item.Breakoffs.Where(b => OwnsIds(b.EdgeIds)).ToList(),
                TabbedCutouts = item.TabbedCutouts.Where(c => OwnsIds(c.EdgeIds)).ToList(),
                Dimensions = item.Dimensions.Where(d => ids.Contains(d.EntityId)).ToList(),
                SuppressedEntityIds = item.SuppressedEntityIds?.Where(ids.Contains).ToHashSet(),
                ExcludedEntityIds = item.ExcludedEntityIds.Where(ids.Contains).ToHashSet(),
                EntityRoles = item.EntityRoles.Where(r => ids.Contains(r.Key)).ToDictionary(r => r.Key, r => r.Value),
                Bounds = entities.GetBoundingBox(),
                EntityCount = entities.Count,
                Texts = item.Texts?.Where(t => OwnsPoint(t.Position)).ToList() ?? new List<CadText>(),
                Labels = item.Labels,
            };
        }

        private static string UniqueName(string name, HashSet<string> taken)
        {
            var unique = name;
            for (var n = 2; taken.Contains(unique); n++)
                unique = $"{name} ({n})";
            taken.Add(unique);
            return unique;
        }
    }
}
