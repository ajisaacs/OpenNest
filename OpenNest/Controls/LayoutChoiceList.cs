using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using OpenNest.Forms;

namespace OpenNest.Controls
{
    /// <summary>
    /// Whole-job layouts stacked as cards, the best first: each a summary line over its sheets,
    /// with every sheet's free strip dimensioned. One card is selected: the best, until the user
    /// clicks another. Read-only like the sheet strips it holds.
    /// </summary>
    internal sealed class LayoutChoiceList : Panel
    {
        private const int Gap = 8;
        private static readonly Color Accent = Color.FromArgb(0, 120, 215);
        private static readonly Color Border = Color.FromArgb(200, 200, 200);

        private readonly List<Card> cards = new();
        private IReadOnlyList<NestLayoutChoice> choices = Array.Empty<NestLayoutChoice>();
        private NestLayoutChoice pinned;
        private NestLayoutChoice selected;
        private bool firstIsBest;
        private bool allowSelection;
        private string bestLabel = "Best so far";
        private ColorScheme colorScheme = ColorScheme.Default;

        public LayoutChoiceList()
        {
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
            Padding = new Padding(Gap);
        }

        public ColorScheme ColorScheme
        {
            get => colorScheme;
            set
            {
                colorScheme = value ?? ColorScheme.Default;
                foreach (var card in cards)
                    card.Strip.ColorScheme = colorScheme;
            }
        }

        /// <summary>The text shown when there are no layouts.</summary>
        public string EmptyText { get; set; } = "No layouts yet";

        /// <summary>How the first card is titled while it is the engine's best.</summary>
        public string BestLabel
        {
            get => bestLabel;
            set
            {
                bestLabel = value;
                Refill();
            }
        }

        /// <summary>Whether clicking a card selects it; without it the list only shows.</summary>
        public bool AllowSelection
        {
            get => allowSelection;
            set
            {
                allowSelection = value;
                Refill();
            }
        }

        /// <summary>The selected layout: the one the user clicked while it is still offered,
        /// otherwise the first. Null with no layouts.</summary>
        public NestLayoutChoice Selected => selected;

        public IReadOnlyList<NestLayoutChoice> Choices => choices;

        public event EventHandler SelectionChanged;

        /// <summary>A card was double-clicked: use its layout.</summary>
        public event EventHandler ChoiceActivated;

        /// <summary>Shows <paramref name="top"/>, best first. <paramref name="firstIsBest"/> titles
        /// the first card as the engine's best.</summary>
        public void SetChoices(IReadOnlyList<NestLayoutChoice> top, bool firstIsBest = true)
        {
            choices = top ?? Array.Empty<NestLayoutChoice>();
            this.firstIsBest = firstIsBest;
            if (pinned != null && !choices.Contains(pinned))
                pinned = null;
            Refill();
        }

        /// <summary>Selects <paramref name="choice"/> as a click on its card does.</summary>
        public void Pick(NestLayoutChoice choice)
        {
            if (!allowSelection || !choices.Contains(choice))
                return;
            pinned = choice;
            Refill();
        }

        private void Refill()
        {
            SuspendLayout();
            while (cards.Count > choices.Count)
            {
                var last = cards[^1];
                cards.RemoveAt(cards.Count - 1);
                Controls.Remove(last);
                last.Dispose();
            }
            while (cards.Count < choices.Count)
            {
                var card = new Card(this) { Font = Font };
                card.Strip.ColorScheme = colorScheme;
                cards.Add(card);
                Controls.Add(card);
            }

            var now = pinned ?? choices.FirstOrDefault();
            for (var i = 0; i < cards.Count; i++)
            {
                var title = i == 0 && firstIsBest ? bestLabel : $"Alternative {(firstIsBest ? i : i + 1)}";
                cards[i].Fill(choices[i], title, allowSelection, ReferenceEquals(choices[i], now));
            }
            Arrange();
            ResumeLayout();
            Invalidate();
            if (!ReferenceEquals(now, selected))
            {
                selected = now;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Arrange();
        }

        /// <summary>Cards share the height equally.</summary>
        private void Arrange()
        {
            if (cards.Count == 0)
                return;
            var area = DisplayRectangle;
            var height = System.Math.Max(40, (area.Height - Gap * (cards.Count - 1)) / cards.Count);
            for (var i = 0; i < cards.Count; i++)
                cards[i].Bounds = new Rectangle(area.Left, area.Top + i * (height + Gap), area.Width, height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (cards.Count == 0)
                TextRenderer.DrawText(e.Graphics, EmptyText, Font, ClientRectangle, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void Clicked(Card card, bool activate)
        {
            if (!allowSelection)
                return;
            Pick(card.Choice);
            if (activate)
                ChoiceActivated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>One layout: a frame (accented when selected) around its title and sheets.</summary>
        private sealed class Card : Panel
        {
            private readonly Label header;
            private Font bold;

            public Card(LayoutChoiceList owner)
            {
                Padding = new Padding(2);
                BackColor = Border;
                Strip = new SheetStripControl { Dock = DockStyle.Fill, ShowRemnant = true };
                header = new Label
                {
                    Dock = DockStyle.Top,
                    AutoSize = false,
                    AutoEllipsis = true,
                    Height = 28,
                    Padding = new Padding(8, 0, 8, 0),
                    TextAlign = ContentAlignment.MiddleLeft,
                    BackColor = Color.White,
                };
                Controls.Add(Strip);
                Controls.Add(header);
                foreach (var control in new Control[] { this, header, Strip })
                {
                    control.Click += (s, e) => owner.Clicked(this, activate: false);
                    control.DoubleClick += (s, e) => owner.Clicked(this, activate: true);
                }
            }

            public SheetStripControl Strip { get; }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    bold?.Dispose();
                base.Dispose(disposing);
            }

            public NestLayoutChoice Choice { get; private set; }

            public void Fill(NestLayoutChoice choice, string title, bool selectable, bool isSelected)
            {
                if (!ReferenceEquals(choice, Choice))
                {
                    Choice = choice;
                    Strip.SetSheets(choice.Sheets);
                }
                var marked = selectable && isSelected;
                var mark = !selectable ? "" : isSelected ? "●  " : "○  ";
                header.Text = $"{mark}{title}   ·   {choice.Summary}";
                header.Font = marked ? bold ??= new Font(Font, FontStyle.Bold) : Font;
                header.ForeColor = marked ? Accent : Color.FromArgb(51, 51, 51);
                BackColor = marked ? Accent : Border;
                Cursor = header.Cursor = Strip.Cursor = selectable ? Cursors.Hand : Cursors.Default;
            }
        }
    }
}
