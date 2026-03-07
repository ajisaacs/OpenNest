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

        public virtual bool SurvivesPlateChange => false;

        public virtual void OnPlateChanged() { }

        public abstract void DisconnectEvents();

        public abstract void CancelAction();

        public abstract bool IsBusy();
    }
}
