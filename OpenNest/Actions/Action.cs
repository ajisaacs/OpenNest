using OpenNest.Controls;
using OpenNest.Geometry;
using System.Drawing;

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

        public virtual void OnPlateChanged() { }

        public virtual void ConnectEvents() { }

        public abstract void DisconnectEvents();

        public abstract void CancelAction();

        public abstract bool IsBusy();
    }
}
