using System.Drawing;
using System.Windows.Forms;

namespace OpenNest.Controls
{
    /// <summary>
    /// A slim green confirmation strip, one line of text 30 pixels high (scaled with the form):
    /// a pale tint with dark text and a border in the same hue, and a close button. A message
    /// longer than the strip ends in an ellipsis. Hidden until <see cref="Show(string)"/>; it
    /// stays until the next message replaces it or the operator closes it.
    /// </summary>
    public sealed class NoticeBanner : Panel
    {
        private static readonly Color Tint = Color.FromArgb(221, 240, 214);
        private static readonly Color Ink = Color.FromArgb(28, 88, 28);

        private readonly Label messageLabel;
        private readonly Button closeButton;
        private string message = "";

        public NoticeBanner()
        {
            BackColor = Tint;
            Height = 30;
            Padding = new Padding(8, 1, 1, 1);
            DoubleBuffered = true;
            ResizeRedraw = true;
            Visible = false;

            messageLabel = new Label
            {
                AutoEllipsis = true,
                AutoSize = false,
                Dock = DockStyle.Fill,
                ForeColor = Ink,
                Name = "messageLabel",
                TextAlign = ContentAlignment.MiddleLeft,
            };
            closeButton = new Button
            {
                Dock = DockStyle.Right,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Ink,
                AccessibleName = "Close",
                Name = "closeButton",
                TabStop = false,
                Text = "✕",
                Width = 26,
            };
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.Click += (_, _) => Hide();

            // Fill is laid out last, so the label takes what the button leaves.
            Controls.Add(messageLabel);
            Controls.Add(closeButton);
        }

        /// <summary>The message shown, or empty while hidden.</summary>
        public string Message => Visible ? message : "";

        /// <summary>Shows <paramref name="message"/>, replacing whatever was there.</summary>
        public void Show(string message)
        {
            this.message = message ?? "";
            messageLabel.Text = this.message;
            Visible = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            ControlPaint.DrawBorder(e.Graphics, ClientRectangle, Ink, ButtonBorderStyle.Solid);
        }
    }
}
