using System;
using System.Windows.Forms;

namespace OpenNest
{
    public static class MdiExtensions
    {
        /// <summary>
        /// Sets the display state of the 3D bevel on MDI client area.
        /// Source: http://stackoverflow.com/questions/7752696/how-to-remove-3d-border-sunken-from-mdiclient-component-in-mdi-parent-form
        /// </summary>
        /// <param name="form"></param>
        /// <param name="show"></param>
        /// <returns></returns>
        public static bool SetBevel(this Form form, bool show)
        {
            foreach (Control c in form.Controls)
            {
                if (c is MdiClient == false)
                    continue;

                var client = c as MdiClient;

                int windowLong = Win32.GetWindowLong(c.Handle, Win32.GWL_EXSTYLE);

                if (show)
                    windowLong |= Win32.WS_EX_CLIENTEDGE;
                else
                    windowLong &= ~Win32.WS_EX_CLIENTEDGE;

                Win32.SetWindowLong(c.Handle, Win32.GWL_EXSTYLE, windowLong);

                // Update the non-client area.
                Win32.SetWindowPos(client.Handle, IntPtr.Zero, 0, 0, 0, 0,
                    Win32.SWP_NOACTIVATE | Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOZORDER |
                    Win32.SWP_NOOWNERZORDER | Win32.SWP_FRAMECHANGED);

                return true;
            }
            return false;
        }
    }
}
