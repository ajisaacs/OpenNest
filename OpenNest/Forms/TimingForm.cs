using System;
using System.Windows.Forms;

namespace OpenNest.Forms
{
    public partial class TimingForm : Form
    {
        public TimingForm()
        {
            InitializeComponent();
        }

        public Units Units { get; set; }

        public void SetCutTime(TimeSpan timeSpan)
        {
            cutTimeLabel.Text = GetTimeMsg(timeSpan);
        }

        public void SetCutDistance(double dist)
        {
            cutDistanceLabel.Text = string.Format("{0} {1}", System.Math.Round(dist, 4), UnitsHelper.GetShortString(Units));
        }

        public void SetRapidDistance(double dist)
        {
            rapidDistanceLabel.Text = string.Format("{0} {1}", System.Math.Round(dist, 4), UnitsHelper.GetShortString(Units));
        }

        public void SetIntersectionCount(int count)
        {
            interectionsLabel.Text = count.ToString();
        }

        public void SetPierceCount(int count)
        {
            piercesLabel.Text = count.ToString();
        }

        public void SetCutParameters(CutParameters cutparams)
        {
            feedrateLabel.Text = string.Format("{0} {1}/{2}", cutparams.Feedrate, UnitsHelper.GetShortString(Units), UnitsHelper.GetShortTimeUnit(Units));
            rapidLabel.Text = string.Format("{0} {1}/{2}", cutparams.RapidTravelRate, UnitsHelper.GetShortString(Units), UnitsHelper.GetShortTimeUnit(Units));
            pierceTimeLabel.Text = GetTimeMsg(cutparams.PierceTime);
        }

        public static string GetTimeMsg(TimeSpan time)
        {
            int hrs = time.Days * 24 + time.Hours;
            int min = time.Minutes;
            int sec = time.Seconds;

            string msg = string.Empty;

            if (hrs > 0)
            {
                msg += hrs.ToString() + "h ";
                msg += min.ToString().PadLeft(2, '0') + "m ";
                msg += sec.ToString().PadLeft(2, '0') + "s";
            }
            else if (min > 0)
            {
                msg += min.ToString().PadLeft(2, '0') + "m ";
                msg += sec.ToString().PadLeft(2, '0') + "s";
            }
            else
                msg += time.TotalSeconds.ToString("n0").PadLeft(2, '0') + "s";

            return msg;
        }
    }
}
