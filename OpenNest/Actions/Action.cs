using OpenNest.Controls;

namespace OpenNest.Actions
{
    public abstract class Action
    {
        protected PlateView plateView;

        protected Action(PlateView plateView)
        {
            this.plateView = plateView;
        }

        public abstract void DisconnectEvents();

        public abstract void CancelAction();

        public abstract bool IsBusy();
    }
}
