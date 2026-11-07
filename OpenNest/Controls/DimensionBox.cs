using System;
using System.Windows.Forms;

namespace OpenNest.Controls
{
    /// <summary>
    /// The text box typed into over an on-canvas dimension. Enter raises <see cref="Commit"/>
    /// instead of reaching the form. Escape and Tab are claimed only while
    /// <see cref="Cancel"/> or <see cref="Advance"/> has a handler, so a host that leaves them
    /// unhandled keeps its own Escape and focus behavior.
    /// </summary>
    internal sealed class DimensionBox : TextBox
    {
        public event EventHandler Commit;

        public event EventHandler Cancel;

        /// <summary>Tab: commit and move on; the argument is true for Shift+Tab.</summary>
        public event EventHandler<bool> Advance;

        protected override bool IsInputKey(Keys keyData) =>
            (keyData & Keys.KeyCode) switch
            {
                Keys.Enter => true,
                Keys.Escape => Cancel != null,
                Keys.Tab => Advance != null && (keyData & (Keys.Control | Keys.Alt)) == 0,
                _ => base.IsInputKey(keyData),
            };

        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Enter:
                    e.SuppressKeyPress = true;
                    Commit?.Invoke(this, EventArgs.Empty);
                    return;
                case Keys.Escape when Cancel != null:
                    e.SuppressKeyPress = true;
                    Cancel(this, EventArgs.Empty);
                    return;
                case Keys.Tab when Advance != null:
                    e.SuppressKeyPress = true;
                    Advance(this, e.Shift);
                    return;
            }
            base.OnKeyDown(e);
        }
    }
}
