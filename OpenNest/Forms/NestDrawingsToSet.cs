using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenNest.Data;

namespace OpenNest.Forms
{
    /// <summary>
    /// Drawing list &gt; Add Library Drawings to Set: chooses a set (or a new one), appends the
    /// nest's library drawings with their required quantities through <see cref="NestSetMerge"/>,
    /// reports anything kept, skipped or worth checking, then opens the set editor so the
    /// operator reviews and saves. Nothing is stored until the editor saves.
    /// </summary>
    internal sealed class NestDrawingsToSet
    {
        private const string Caption = "Add Library Drawings to Set";

        private readonly IDrawingSetStore sets;
        private readonly IDrawingLibraryStore library;
        private readonly string serverUrl;

        /// <summary>Shows a message and returns the operator's answer; replaced by tests.</summary>
        internal Func<string, MessageBoxButtons, MessageBoxIcon, DialogResult> ShowMessage { get; set; }

        /// <summary>Chooses the set: Ok false when canceled, otherwise the listed set or null for a new one; replaced by tests.</summary>
        internal Func<(bool Ok, DrawingSetRecord Listed)> ChooseSet { get; set; }

        /// <summary>Shows the set editor and returns the saved record, or null; replaced by tests.</summary>
        internal Func<DrawingSetEditSession, DrawingSetRecord> EditSet { get; set; }

        public NestDrawingsToSet(IWin32Window owner, IDrawingSetStore sets, IDrawingLibraryStore library, string serverUrl)
        {
            this.sets = sets ?? throw new ArgumentNullException(nameof(sets));
            this.library = library ?? throw new ArgumentNullException(nameof(library));
            this.serverUrl = serverUrl ?? "";
            ShowMessage = (text, buttons, icon) => MessageBox.Show(owner, text, Caption, buttons, icon);
            ChooseSet = () =>
            {
                using var form = new ChooseDrawingSetForm(sets, library, this.serverUrl);
                return form.ShowDialog(owner) == DialogResult.OK ? (true, form.PickedSet) : (false, null);
            };
            EditSet = edit =>
            {
                using var form = new EditDrawingSetForm(edit, library, this.serverUrl);
                return form.ShowDialog(owner) == DialogResult.OK ? form.Saved : null;
            };
        }

        /// <summary>
        /// Runs the command for <paramref name="nest"/>, planned before the first prompt so later
        /// changes to the nest do not reach the set. Returns the saved set, or null.
        /// </summary>
        public async Task<DrawingSetRecord> RunAsync(Nest nest)
        {
            ArgumentNullException.ThrowIfNull(nest);
            var merge = NestSetMerge.Plan(nest, serverUrl);
            if (merge.Candidates.Count == 0)
            {
                var text = "None of this nest's drawings can be added to a set. Save them to the drawing library first.";
                if (merge.Skipped.Count > 0)
                    text += Environment.NewLine + Environment.NewLine + "Skipped:" + Environment.NewLine + merge.DescribeSkipped();
                ShowMessage(text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }

            var (ok, listed) = ChooseSet();
            if (!ok)
                return null;

            DrawingSetEditSession edit;
            NestSetMergeResult result;
            try
            {
                DrawingSetRecord current = null;
                if (listed != null)
                {
                    current = await sets.GetAsync(listed.Id);
                    if (current == null || current.Archived)
                    {
                        ShowMessage(current == null
                            ? $"Set '{listed.Name}' no longer exists on the server."
                            : $"Set '{listed.Name}' was archived. Restore it before adding drawings.",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return null;
                    }
                }

                edit = new DrawingSetEditSession(sets, current, Environment.UserName);
                if (current == null)
                {
                    edit.Name = nest.Name ?? "";
                    edit.Customer = nest.Customer ?? "";
                }
                result = await merge.ApplyAsync(library, edit);
            }
            catch (Exception ex) when (ex is IOException || ex is HttpRequestException || ex is TaskCanceledException)
            {
                // ServerTooOldException is an IOException too.
                ShowMessage($"Could not read the drawing library: {ex.Message}", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }

            var setName = listed?.Name;
            if (result.Added.Count == 0)
            {
                ShowMessage(result.Describe(setName), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }

            if (!result.IsClean
                && ShowMessage(result.Describe(setName) + Environment.NewLine + Environment.NewLine
                    + "Review and save the set?", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                return null;

            return EditSet(edit);
        }
    }
}
