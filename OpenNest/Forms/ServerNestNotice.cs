using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    /// <summary>
    /// Tells the user a server nest ended while OpenNest was not in front: a notification-area
    /// balloon, which Windows 10 and 11 show as a notification. A real toast would need an
    /// AppUserModelID registered with a Start menu shortcut, which the Velopack install does not
    /// promise. Clicking the balloon (or the icon) brings OpenNest forward with its topmost
    /// window, which is the review once the result has been checked. The icon goes away once
    /// OpenNest is in front again, however the user got there.
    /// </summary>
    internal sealed class ServerNestNotice : IDisposable
    {
        private readonly Form window;
        private readonly NotifyIcon icon;
        private readonly Timer returned;
        private bool disposed;

        private ServerNestNotice(Form window, string title, string text)
        {
            this.window = window;
            icon = new NotifyIcon
            {
                Icon = window.Icon ?? SystemIcons.Application,
                Text = "OpenNest",
                Visible = true,
            };
            icon.BalloonTipClicked += (s, e) => BringForward();
            icon.MouseClick += (s, e) => BringForward();
            icon.ShowBalloonTip(15000, title, text, ToolTipIcon.Info);
            returned = new Timer { Interval = 1000 };
            returned.Tick += (s, e) =>
            {
                if (!IsAway(window))
                    Dispose();
            };
            returned.Start();
        }

        /// <summary>The notice, or null when OpenNest is in front (the user sees the result anyway).</summary>
        public static ServerNestNotice ShowIfAway(IWin32Window owner, string title, string text)
        {
            var window = (owner as Control)?.FindForm() ?? Application.OpenForms.Cast<Form>().FirstOrDefault();
            return window == null || window.IsDisposed || !IsAway(window) ? null : new ServerNestNotice(window, title, text);
        }

        /// <summary>No OpenNest window is active, or the main window is minimized.</summary>
        internal static bool IsAway(Form window) =>
            Form.ActiveForm == null || window.WindowState == FormWindowState.Minimized;

        internal bool IsShown => !disposed && icon.Visible;

        internal void BringForward()
        {
            if (!window.IsDisposed)
            {
                if (window.WindowState == FormWindowState.Minimized)
                    window.WindowState = FormWindowState.Normal;
                // An owned modal (the progress, validation report or review) is what needs the user.
                var top = Application.OpenForms.Cast<Form>().LastOrDefault(f => f.Visible && f.Modal && !f.IsDisposed) ?? window;
                top.Activate();
            }
            Dispose();
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            returned.Dispose();
            icon.Visible = false;
            icon.Dispose();
        }
    }
}
