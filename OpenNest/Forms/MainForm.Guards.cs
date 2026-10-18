using System.Linq;
using System.Windows.Forms;
using OpenNest.Actions;
using OpenNest.Controls;

namespace OpenNest.Forms
{
    public partial class MainForm
    {
        /// <summary>
        /// True while whole-job nesting runs or a progress window is open; a closed
        /// progress window can still have a fill awaiting its final commit.
        /// </summary>
        private bool IsJobRunning =>
            nestingInProgress || Application.OpenForms.OfType<NestProgressForm>().Any();

        private PlateView[] ViewsOf(Nest nest) =>
            MdiChildren
                .OfType<EditNestForm>()
                .Where(form => ReferenceEquals(form.Nest, nest))
                .Select(form => form.PlateView)
                .ToArray();

        /// <summary>
        /// The guard for plate commands: a running job, or any of <paramref name="views"/>
        /// filling or holding a busy current action.
        /// </summary>
        private bool IsPlateWorkBusy(PlateView[] views) =>
            IsJobRunning
            || views.Any(v => v.IsFillInProgress || v.Actions.CurrentAction?.IsBusy() == true);

        /// <summary>
        /// A running job or a fill on any view of <paramref name="nest"/>; a busy current
        /// action does not count.
        /// </summary>
        private bool IsNestFillBusy(Nest nest) =>
            IsJobRunning || ViewsOf(nest).Any(v => v.IsFillInProgress);

        /// <summary>
        /// True while a nest run or fill may be reading <paramref name="nest"/>'s clamps.
        /// Clamps feed nesting and validation: never change them under a running operation.
        /// </summary>
        private bool ClampsInUse(Nest nest) => IsNestFillBusy(nest);

        /// <summary>
        /// True while whole-job nesting or a fill on any view of the nest can still
        /// change its plates. Unlike the other plate commands' guard, a busy current
        /// action does not count: ActionSelect reports busy whenever parts are
        /// selected, and Expand Spacing needs a selection.
        /// </summary>
        internal bool IsExpandSpacingBusy(EditNestForm editForm) => IsNestFillBusy(editForm.Nest);

        internal bool IsCuttingPlanBusy(PlateView[] views) => IsPlateWorkBusy(views);

        /// <summary>
        /// True while any operation could mutate nests or hold their plate views: whole-job
        /// nesting, an open progress window (a closed one can still await its commit), or a
        /// background database save serializing the live nest.
        /// </summary>
        private bool IsNestJobBusy() => IsJobRunning || databaseSaveInProgress;

        /// <summary>
        /// Refuses with <paramref name="message"/> while plate work on <paramref name="nest"/>
        /// is busy; otherwise returns every view of the nest to selection and returns true.
        /// </summary>
        private bool TryBeginPlateCommand(Nest nest, string message, string caption)
        {
            var views = ViewsOf(nest);
            if (IsPlateWorkBusy(views))
            {
                MessageBox.Show(this, message, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            foreach (var view in views)
                view.SetAction(typeof(ActionSelect));
            return true;
        }
    }
}
