using System;
using System.ComponentModel;
using Action = OpenNest.Actions.Action;

namespace OpenNest.Controls
{
    internal class ActionManager
    {
        private readonly PlateView view;
        private Action currentAction;
        private Action previousAction;

        public ActionManager(PlateView view)
        {
            this.view = view;
        }

        public Action CurrentAction => currentAction;

        public void SetAction(Type type)
        {
            var action = Activator.CreateInstance(type, view) as Action;
            if (action == null)
                return;

            if (currentAction != null)
            {
                if (type == typeof(Actions.ActionSelect) && !(currentAction is Actions.ActionSelect))
                    previousAction = currentAction;
                else
                    previousAction = null;

                currentAction.CancelAction();
                currentAction.DisconnectEvents();
                currentAction = null;
            }

            currentAction = action;
            view.Status = GetDisplayName(type);
        }

        public void SetAction(Type type, params object[] args)
        {
            if (currentAction != null)
            {
                previousAction = null;
                currentAction.CancelAction();
                currentAction.DisconnectEvents();
                currentAction = null;
            }

            Array.Resize(ref args, args.Length + 1);
            for (var i = args.Length - 2; i >= 0; i--)
                args[i + 1] = args[i];
            args[0] = view;

            var action = Activator.CreateInstance(type, args) as Action;
            if (action == null)
                return;

            currentAction = action;
            view.Status = GetDisplayName(type);
        }

        public void ProcessEscapeKey()
        {
            if (currentAction.IsBusy())
                currentAction.CancelAction();
            else if (currentAction is Actions.ActionSelect && previousAction != null)
                RestorePreviousAction();
            else
                view.SetAction(typeof(Actions.ActionSelect));
        }

        public void RestorePreviousAction()
        {
            var action = previousAction;
            previousAction = null;

            currentAction.CancelAction();
            currentAction.DisconnectEvents();

            action.ConnectEvents();
            currentAction = action;

            view.Status = GetDisplayName(action.GetType());
        }

        public void OnPlateChanged()
        {
            if (currentAction == null || !currentAction.SurvivesPlateChange)
                view.SetAction(typeof(Actions.ActionSelect));
            else
                currentAction.OnPlateChanged();
        }

        public void Cleanup()
        {
            if (currentAction != null)
            {
                currentAction.CancelAction();
                currentAction.DisconnectEvents();
                currentAction = null;
            }
        }

        private string GetDisplayName(Type type)
        {
            var attributes = type.GetCustomAttributes(true);
            foreach (var attr in attributes)
            {
                if (attr is DisplayNameAttribute displayNameAttr)
                    return displayNameAttr.DisplayName;
            }
            return type.Name;
        }
    }
}
