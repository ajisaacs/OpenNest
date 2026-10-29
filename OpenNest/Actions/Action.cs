using System.Drawing;
using OpenNest.Controls;
using OpenNest.Geometry;

namespace OpenNest.Actions
{
    public abstract class Action
    {
        protected PlateView plateView;

        protected Action(PlateView plateView)
        {
            this.plateView = plateView;
        }

        protected RectangleF GetRectangle(Vector worldPt1, Vector worldPt2)
        {
            var pt1 = plateView.PointWorldToGraph(worldPt1);
            var pt2 = plateView.PointWorldToGraph(worldPt2);

            var x = pt1.X < pt2.X ? pt1.X : pt2.X;
            var y = pt1.Y < pt2.Y ? pt1.Y : pt2.Y;
            var w = System.Math.Abs(pt2.X - pt1.X);
            var h = System.Math.Abs(pt2.Y - pt1.Y);

            return new RectangleF(x, y, w, h);
        }

        public virtual bool SurvivesPlateChange => false;

        /// <summary>
        /// Whether Escape from Select may bring this action back after it ends.
        /// </summary>
        public virtual bool ResumeOnEscape => true;

        /// <summary>Status bar guidance shown after the action's name, or null for the name alone.</summary>
        public virtual string Hint => null;

        /// <summary>The help topic F1 opens while this action is active, or null for the general help.</summary>
        public virtual string HelpTopic => null;

        /// <summary>Whether the action uses F1 itself, so F1 reaches the plate view instead of opening help.</summary>
        public virtual bool HandlesF1 => false;

        public virtual void OnPlateChanged() { }

        public virtual void ConnectEvents() { }

        public abstract void DisconnectEvents();

        public abstract void CancelAction();

        public abstract bool IsBusy();

        /// <summary>Whether Undo and Redo must wait: by default, while the action is busy.</summary>
        public virtual bool BlocksUndo => IsBusy();

        /// <summary>
        /// Steps back within the action, for Ctrl+Z before the whole-nest Undo; false when the
        /// action has nothing of its own to step back.
        /// </summary>
        public virtual bool TryUndoStep() => false;
    }
}
