using System.Globalization;
using System.Text;
using OpenNest.Clamps;
using OpenNest.CNC;
using OpenNest.CutOffs;
using OpenNest.Geometry;

namespace OpenNest.Tests.CutOffs;

/// <summary>
/// Pins everything <see cref="AutomaticCutOffPlanner.Create"/> returns for a spread of plans, so a
/// restructuring of the planner can be checked for identical output. The expected digests were
/// recorded from the planner itself; a deliberate behavior change re-records them by running
/// <see cref="Record"/> with OPENNEST_CUTOFF_CHARACTERIZE set to an output file.
/// </summary>
public class AutomaticCutOffPlannerCharacterizationTests
{
    private static readonly Dictionary<string, Func<AutomaticCutOffPlan>> Scenarios = new()
    {
        ["fixed-q1"] = () => Plan(Fixed(1)),
        ["fixed-q2"] = () => Plan(Fixed(2)),
        ["fixed-q3"] = () => Plan(Fixed(3)),
        ["fixed-q4"] = () => Plan(Fixed(4)),
        ["fixed-overtravel-q3"] = () => Plan(Fixed(3), settings: new CutOffSettings { Overtravel = 3 }),
        ["fixed-clamp-rail-q1"] = () => Plan(WithRail(Fixed(1), 1.5, 35.0, 80.5)),
        ["fixed-clamp-rail-q2"] = () => Plan(WithRail(Fixed(2), 1.5, 35.0, 80.5)),
        ["fixed-adjacent-clamps-q4"] = () => Plan(WithRail(Fixed(4), 1.5, 80.5, 82.35)),
        ["fixed-hanging-clamp-q3"] = () => Plan(WithRail(Fixed(3, 119), 1.5, 120.5), 119.6),
        ["fixed-clamp-moved-tail-below-minimum"] = () => Plan(WithRail(Fixed(1), 1.5, 80.5), minimumTail: 38.6),
        ["fixed-tail-too-short-q1"] = () => Plan(Fixed(1, 104), minimumTail: 16),
        ["fixed-tail-too-short-q3"] = () => Plan(Fixed(3, 104), minimumTail: 16),
        ["fixed-blocking-manual-cut"] = () => Plan(BlockingManualCut()),
        ["fixed-existing-separator-rerun"] = () => Plan(Rerun(Fixed(1, 110)), minimumTail: 12),
        ["fixed-existing-near-separator-on-clamp-q2"] = () => Plan(NearSeparatorOnClamp(2)),
        ["fixed-head-lock"] = () => Plan(Fixed(1), settings: HeadLock()),
        ["shift-q1"] = () => Shift(AutomaticCutOffShiftTests.UserCase(1)),
        ["shift-q2"] = () => Shift(AutomaticCutOffShiftTests.UserCase(2)),
        ["shift-q3"] = () => Shift(AutomaticCutOffShiftTests.UserCase(3)),
        ["shift-q4"] = () => Shift(AutomaticCutOffShiftTests.UserCase(4)),
        ["shift-clamp-q1"] = () => Shift(ShiftClamp(1, 2, 35)),
        ["shift-clamp-q3"] = () => Shift(ShiftClamp(3, 2, 35)),
        ["shift-no-clamp-free-q2"] = () => Shift(ShiftClamp(2, 40, 40)),
        ["shift-existing-boundary-q1"] = () => Shift(ExistingBoundary(1)),
        ["shift-existing-boundary-q4"] = () => Shift(ExistingBoundary(4)),
        ["shift-tail-too-short-q2"] = () => Plan(LongSheet(2), minimumTail: 12, minimumStrip: 17.5),
        ["shift-head-lock-q1"] = () => Shift(AutomaticCutOffShiftTests.UserCase(1), HeadLock()),
        ["horizontal-new-q1"] = () => Plan(Band(1), minimumTail: 12, keepHorizontal: true),
        ["horizontal-new-q2"] = () => Plan(Band(2), minimumTail: 12, keepHorizontal: true),
        ["horizontal-new-q3"] = () => Plan(Band(3), minimumTail: 12, keepHorizontal: true),
        ["horizontal-new-q4"] = () => Plan(Band(4), minimumTail: 12, keepHorizontal: true),
        ["horizontal-existing-q3"] = () => Plan(ExistingBand(3), minimumTail: 12),
        ["horizontal-existing-rerun-q4"] = () => Plan(Rerun(Band(4), keepHorizontal: true),
            minimumTail: 12, keepHorizontal: true),
        ["horizontal-shifted-q1"] = () => Plan(Band(1), minimumTail: 12, minimumStrip: 17.5, keepHorizontal: true),
        ["horizontal-shifted-q3"] = () => Plan(Band(3), minimumTail: 12, minimumStrip: 17.5, keepHorizontal: true),
        ["horizontal-corner-tail"] = () => Plan(Band(1, 65), minimumTail: 12, keepHorizontal: true),
        ["horizontal-narrow-band-loses"] = () => Plan(NarrowBand(), minimumTail: 12, keepHorizontal: true),
        ["horizontal-clamp-on-line"] = () => Plan(BandWithClamp(), minimumTail: 12, keepHorizontal: true),
        ["horizontal-manual-beside-line"] = () => Plan(BandWithManualCut(), minimumTail: 12, keepHorizontal: true),
        ["horizontal-filtered-separator"] = () => Plan(Band(1), minimumTail: 12, keepHorizontal: true,
            settings: new CutOffSettings { MinSegmentLength = 100 }),
        ["horizontal-head-lock-q3"] = () => Plan(Band(3), minimumTail: 12, keepHorizontal: true, settings: HeadLock()),
        ["no-parts"] = () => Plan(new Plate(81, 120)),
        ["cut-off-only"] = () => Plan(CutOffOnly()),
    };

    private static readonly Dictionary<string, string> Expected = new()
    {
        ["fixed-q1"] = "occupied=80 used=80.50001 tail=39.49999 separatorX=80.50001 horizontalY=null band=0 strip=121.9188 grid=121.9188 blocking=False previews=3\ncut Vertical x=35 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 35,0;LinearMove 35,19.9797;RapidMove 35,40.0203;LinearMove 35,81]\ncut Vertical x=70 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 70,0;LinearMove 70,19.9797;RapidMove 70,40.0203;LinearMove 70,81]\ncut Vertical x=80.50001 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 80.50001,0;LinearMove 80.50001,81]\ndiag SegmentedCut blocking=False x=35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\n",
        ["fixed-q2"] = "occupied=80 used=80.50001 tail=39.49999 separatorX=-80.50001 horizontalY=null band=0 strip=121.9188 grid=121.9188 blocking=False previews=3\ncut Vertical x=-35 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -35,0;LinearMove -35,19.9797;RapidMove -35,40.0203;LinearMove -35,81]\ncut Vertical x=-70 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -70,0;LinearMove -70,19.9797;RapidMove -70,40.0203;LinearMove -70,81]\ncut Vertical x=-80.50001 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -80.50001,0;LinearMove -80.50001,81]\ndiag SegmentedCut blocking=False x=-35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=-70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\n",
        ["fixed-q3"] = "occupied=80 used=80.50001 tail=39.49999 separatorX=-80.50001 horizontalY=null band=0 strip=121.9188 grid=121.9188 blocking=False previews=3\ncut Vertical x=-35 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -35,-81;LinearMove -35,-40.0203;RapidMove -35,-19.9797;LinearMove -35,0]\ncut Vertical x=-70 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -70,-81;LinearMove -70,-40.0203;RapidMove -70,-19.9797;LinearMove -70,0]\ncut Vertical x=-80.50001 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -80.50001,-81;LinearMove -80.50001,0]\ndiag SegmentedCut blocking=False x=-35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=-70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\n",
        ["fixed-q4"] = "occupied=80 used=80.50001 tail=39.49999 separatorX=80.50001 horizontalY=null band=0 strip=121.9188 grid=121.9188 blocking=False previews=3\ncut Vertical x=35 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 35,-81;LinearMove 35,-40.0203;RapidMove 35,-19.9797;LinearMove 35,0]\ncut Vertical x=70 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 70,-81;LinearMove 70,-40.0203;RapidMove 70,-19.9797;LinearMove 70,0]\ncut Vertical x=80.50001 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 80.50001,-81;LinearMove 80.50001,0]\ndiag SegmentedCut blocking=False x=35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\n",
        ["fixed-overtravel-q3"] = "occupied=80 used=80.50001 tail=39.49999 separatorX=-80.50001 horizontalY=null band=0 strip=127.9188 grid=127.9188 blocking=False previews=3\ncut Vertical x=-35 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -35,-81;LinearMove -35,-40.0203;RapidMove -35,-19.9797;LinearMove -35,3]\ncut Vertical x=-70 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -70,-81;LinearMove -70,-40.0203;RapidMove -70,-19.9797;LinearMove -70,3]\ncut Vertical x=-80.50001 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -80.50001,-81;LinearMove -80.50001,3]\ndiag SegmentedCut blocking=False x=-35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=-70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\n",
        ["fixed-clamp-rail-q1"] = "occupied=80 used=81.50001 tail=38.49999 separatorX=81.50001 horizontalY=null band=0 strip=60.9594 grid=60.9594 blocking=False previews=2\ncut Vertical x=70 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 70,0;LinearMove 70,19.9797;RapidMove 70,40.0203;LinearMove 70,81]\ncut Vertical x=81.50001 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 81.50001,0;LinearMove 81.50001,81]\ndiag ClampBlockedCut blocking=False x=35 The proposed cut crosses clamp 'clamp-1' (including its clearance); it was skipped.\ndiag SegmentedCut blocking=False x=70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ClampMovedTailSeparator blocking=False x=81.50001 The tail separator moved from 80.50001 to 81.50001 to clear clamp 'clamp-2' (including its clearance).\n",
        ["fixed-clamp-rail-q2"] = "occupied=80 used=81.50001 tail=38.49999 separatorX=-81.50001 horizontalY=null band=0 strip=60.9594 grid=60.9594 blocking=False previews=2\ncut Vertical x=-70 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -70,0;LinearMove -70,19.9797;RapidMove -70,40.0203;LinearMove -70,81]\ncut Vertical x=-81.50001 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -81.50001,0;LinearMove -81.50001,81]\ndiag ClampBlockedCut blocking=False x=-35 The proposed cut crosses clamp 'clamp-1' (including its clearance); it was skipped.\ndiag SegmentedCut blocking=False x=-70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ClampMovedTailSeparator blocking=False x=-81.50001 The tail separator moved from -80.50001 to -81.50001 to clear clamp 'clamp-2' (including its clearance).\n",
        ["fixed-adjacent-clamps-q4"] = "occupied=80 used=83.35001 tail=36.64999 separatorX=83.35001 horizontalY=null band=0 strip=121.9188 grid=121.9188 blocking=False previews=3\ncut Vertical x=35 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 35,-81;LinearMove 35,-40.0203;RapidMove 35,-19.9797;LinearMove 35,0]\ncut Vertical x=70 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 70,-81;LinearMove 70,-40.0203;RapidMove 70,-19.9797;LinearMove 70,0]\ncut Vertical x=83.35001 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 83.35001,-81;LinearMove 83.35001,0]\ndiag SegmentedCut blocking=False x=35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ClampMovedTailSeparator blocking=False x=83.35001 The tail separator moved from 80.50001 to 83.35001 to clear clamp 'clamp-2' (including its clearance).\n",
        ["fixed-hanging-clamp-q3"] = "occupied=119 used=120 tail=0 separatorX=null horizontalY=null band=0 strip=0 grid=0 blocking=False previews=0\ndiag ClampBlockedCut blocking=False x=-119.6 The proposed cut crosses clamp 'clamp-1' (including its clearance); it was skipped.\n",
        ["fixed-clamp-moved-tail-below-minimum"] = "occupied=80 used=120 tail=0 separatorX=null horizontalY=null band=0 strip=202.9188 grid=202.9188 blocking=False previews=3\ncut Vertical x=35 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 35,0;LinearMove 35,19.9797;RapidMove 35,40.0203;LinearMove 35,81]\ncut Vertical x=70 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 70,0;LinearMove 70,19.9797;RapidMove 70,40.0203;LinearMove 70,81]\ncut Vertical x=105 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 105,0;LinearMove 105,81]\ndiag SegmentedCut blocking=False x=35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag TailBelowMinimum blocking=False x=81.50001 The proposed tail is shorter than the minimum tail length; the final separator was skipped and strip lines continue to the sheet end. Existing cut-offs are unchanged. No retained tail is claimed.\n",
        ["fixed-tail-too-short-q1"] = "occupied=104 used=120 tail=0 separatorX=null horizontalY=null band=0 strip=202.9188 grid=202.9188 blocking=False previews=3\ncut Vertical x=35 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 35,0;LinearMove 35,19.9797;RapidMove 35,40.0203;LinearMove 35,81]\ncut Vertical x=70 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 70,0;LinearMove 70,19.9797;RapidMove 70,40.0203;LinearMove 70,81]\ncut Vertical x=105 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 105,0;LinearMove 105,81]\ndiag SegmentedCut blocking=False x=35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag TailBelowMinimum blocking=False x=104.50001 The proposed tail is shorter than the minimum tail length; the final separator was skipped and strip lines continue to the sheet end. Existing cut-offs are unchanged. No retained tail is claimed.\n",
        ["fixed-tail-too-short-q3"] = "occupied=104 used=120 tail=0 separatorX=null horizontalY=null band=0 strip=202.9188 grid=202.9188 blocking=False previews=3\ncut Vertical x=-35 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -35,-81;LinearMove -35,-40.0203;RapidMove -35,-19.9797;LinearMove -35,0]\ncut Vertical x=-70 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -70,-81;LinearMove -70,-40.0203;RapidMove -70,-19.9797;LinearMove -70,0]\ncut Vertical x=-105 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -105,-81;LinearMove -105,0]\ndiag SegmentedCut blocking=False x=-35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=-70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag TailBelowMinimum blocking=False x=-104.50001 The proposed tail is shorter than the minimum tail length; the final separator was skipped and strip lines continue to the sheet end. Existing cut-offs are unchanged. No retained tail is claimed.\n",
        ["fixed-blocking-manual-cut"] = "occupied=80 used=120 tail=0 separatorX=null horizontalY=null band=0 strip=0 grid=0 blocking=True previews=0\ndiag LimitedCutOffConflict blocking=True x=35 A manual cut with different limits lies on or within 0.50001 of this line. Manual review is required; no cuts may be applied.\ndiag SegmentedCut blocking=False x=70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\n",
        ["fixed-existing-separator-rerun"] = "occupied=110 used=120 tail=0 separatorX=null horizontalY=null band=0 strip=0 grid=0 blocking=False previews=0\ndiag ExistingCutOff blocking=False x=35 An equivalent full-span cut-off already exists; no duplicate was added.\ndiag SegmentedCut blocking=False x=35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ExistingCutOff blocking=False x=70 An equivalent full-span cut-off already exists; no duplicate was added.\ndiag SegmentedCut blocking=False x=70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ExistingCutOff blocking=False x=105 An equivalent full-span cut-off already exists; no duplicate was added.\ndiag SegmentedCut blocking=False x=105 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag TailBelowMinimum blocking=False x=110.50001 The proposed tail is shorter than the minimum tail length; the final separator was skipped and strip lines continue to the sheet end. Existing cut-offs are unchanged. No retained tail is claimed.\n",
        ["fixed-existing-near-separator-on-clamp-q2"] = "occupied=80 used=120 tail=0 separatorX=null horizontalY=null band=0 strip=121.9188 grid=121.9188 blocking=False previews=2\ncut Vertical x=-35 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -35,0;LinearMove -35,19.9797;RapidMove -35,40.0203;LinearMove -35,81]\ncut Vertical x=-70 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -70,0;LinearMove -70,19.9797;RapidMove -70,40.0203;LinearMove -70,81]\ndiag SegmentedCut blocking=False x=-35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=-70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ClampBlockedCut blocking=False x=-80.50001 The proposed cut crosses clamp 'clamp-1' (including its clearance); it was skipped.\ndiag NoSafeTailSeparator blocking=False x=-80.50001 No safe full-width tail separator survives the current settings. Review the tail manually.\n",
        ["fixed-head-lock"] = "occupied=80 used=80.50001 tail=39.49999 separatorX=80.50001 horizontalY=null band=0 strip=121.9188 grid=121.9188 blocking=False previews=3\ncut Vertical x=35 y=0 start=null end=null overcut=1.5 farFirst=False gaps=[] program=[RapidMove 35,0;LinearMove 35,19.9797;RapidMove 35,40.0203;LinearMove 35,81;Comment :HEAD HEIGHT LOCK;LinearMove 35,82.5;Comment :HEAD HEIGHT UNLOCK]\ncut Vertical x=70 y=0 start=null end=null overcut=1.5 farFirst=False gaps=[] program=[RapidMove 70,0;LinearMove 70,19.9797;RapidMove 70,40.0203;LinearMove 70,81;Comment :HEAD HEIGHT LOCK;LinearMove 70,82.5;Comment :HEAD HEIGHT UNLOCK]\ncut Vertical x=80.50001 y=0 start=null end=null overcut=1.5 farFirst=False gaps=[] program=[RapidMove 80.50001,0;LinearMove 80.50001,81;Comment :HEAD HEIGHT LOCK;LinearMove 80.50001,82.5;Comment :HEAD HEIGHT UNLOCK]\ndiag SegmentedCut blocking=False x=35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\n",
        ["shift-q1"] = "occupied=80 used=80.30001 tail=39.69999 separatorX=80.30001 horizontalY=null band=0 strip=79.9188 grid=120 blocking=False previews=3\ncut Vertical x=34.75 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 34.75,0;LinearMove 34.75,9.979700000000001;RapidMove 34.75,30.020300000000002;LinearMove 34.75,60]\ncut Vertical x=69.75 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 69.75,0;LinearMove 69.75,9.979700000000001;RapidMove 69.75,30.020300000000002;LinearMove 69.75,60]\ncut Vertical x=80.30001 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 80.30001,0;LinearMove 80.30001,60]\ndiag SegmentedCut blocking=False x=34.75 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ShiftedCut blocking=False x=34.75 Moved from 35 to 34.75 model units; this line cuts 20.0406 model units less.\ndiag SegmentedCut blocking=False x=69.75 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ShiftedCut blocking=False x=69.75 Moved from 70 to 69.75 model units; this line cuts 20.0406 model units less.\n",
        ["shift-q2"] = "occupied=80 used=80.30001 tail=39.69999 separatorX=-80.30001 horizontalY=null band=0 strip=79.9188 grid=120 blocking=False previews=3\ncut Vertical x=-34.75 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -34.75,0;LinearMove -34.75,9.979700000000001;RapidMove -34.75,30.020300000000002;LinearMove -34.75,60]\ncut Vertical x=-69.75 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -69.75,0;LinearMove -69.75,9.979700000000001;RapidMove -69.75,30.020300000000002;LinearMove -69.75,60]\ncut Vertical x=-80.30001 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -80.30001,0;LinearMove -80.30001,60]\ndiag SegmentedCut blocking=False x=-34.75 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ShiftedCut blocking=False x=-34.75 Moved from 35 to 34.75 model units; this line cuts 20.0406 model units less.\ndiag SegmentedCut blocking=False x=-69.75 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ShiftedCut blocking=False x=-69.75 Moved from 70 to 69.75 model units; this line cuts 20.0406 model units less.\n",
        ["shift-q3"] = "occupied=80 used=80.30001 tail=39.69999 separatorX=-80.30001 horizontalY=null band=0 strip=79.9188 grid=120 blocking=False previews=3\ncut Vertical x=-34.75 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -34.75,-60;LinearMove -34.75,-30.020300000000002;RapidMove -34.75,-9.979700000000001;LinearMove -34.75,0]\ncut Vertical x=-69.75 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -69.75,-60;LinearMove -69.75,-30.020300000000002;RapidMove -69.75,-9.979700000000001;LinearMove -69.75,0]\ncut Vertical x=-80.30001 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -80.30001,-60;LinearMove -80.30001,0]\ndiag SegmentedCut blocking=False x=-34.75 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ShiftedCut blocking=False x=-34.75 Moved from 35 to 34.75 model units; this line cuts 20.0406 model units less.\ndiag SegmentedCut blocking=False x=-69.75 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ShiftedCut blocking=False x=-69.75 Moved from 70 to 69.75 model units; this line cuts 20.0406 model units less.\n",
        ["shift-q4"] = "occupied=80 used=80.30001 tail=39.69999 separatorX=80.30001 horizontalY=null band=0 strip=79.9188 grid=120 blocking=False previews=3\ncut Vertical x=34.75 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 34.75,-60;LinearMove 34.75,-30.020300000000002;RapidMove 34.75,-9.979700000000001;LinearMove 34.75,0]\ncut Vertical x=69.75 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 69.75,-60;LinearMove 69.75,-30.020300000000002;RapidMove 69.75,-9.979700000000001;LinearMove 69.75,0]\ncut Vertical x=80.30001 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 80.30001,-60;LinearMove 80.30001,0]\ndiag SegmentedCut blocking=False x=34.75 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ShiftedCut blocking=False x=34.75 Moved from 35 to 34.75 model units; this line cuts 20.0406 model units less.\ndiag SegmentedCut blocking=False x=69.75 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ShiftedCut blocking=False x=69.75 Moved from 70 to 69.75 model units; this line cuts 20.0406 model units less.\n",
        ["shift-clamp-q1"] = "occupied=80 used=80.30001 tail=39.69999 separatorX=80.30001 horizontalY=null band=0 strip=79.9188 grid=120 blocking=False previews=3\ncut Vertical x=33.7 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 33.7,0;LinearMove 33.7,9.979700000000001;RapidMove 33.7,30.020300000000002;LinearMove 33.7,60]\ncut Vertical x=68.7 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 68.7,0;LinearMove 68.7,9.979700000000001;RapidMove 68.7,30.020300000000002;LinearMove 68.7,60]\ncut Vertical x=80.30001 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 80.30001,0;LinearMove 80.30001,60]\ndiag SegmentedCut blocking=False x=33.7 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ShiftedCut blocking=False x=33.7 Moved from 35 to 33.7 model units; this line cuts 20.0406 model units less.\ndiag SegmentedCut blocking=False x=68.7 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ShiftedCut blocking=False x=68.7 Moved from 70 to 68.7 model units; this line cuts 20.0406 model units less.\n",
        ["shift-clamp-q3"] = "occupied=80 used=80.30001 tail=39.69999 separatorX=-80.30001 horizontalY=null band=0 strip=79.9188 grid=120 blocking=False previews=3\ncut Vertical x=-33.7 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -33.7,-60;LinearMove -33.7,-30.020300000000002;RapidMove -33.7,-9.979700000000001;LinearMove -33.7,0]\ncut Vertical x=-68.7 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -68.7,-60;LinearMove -68.7,-30.020300000000002;RapidMove -68.7,-9.979700000000001;LinearMove -68.7,0]\ncut Vertical x=-80.30001 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -80.30001,-60;LinearMove -80.30001,0]\ndiag SegmentedCut blocking=False x=-33.7 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ShiftedCut blocking=False x=-33.7 Moved from 35 to 33.7 model units; this line cuts 20.0406 model units less.\ndiag SegmentedCut blocking=False x=-68.7 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ShiftedCut blocking=False x=-68.7 Moved from 70 to 68.7 model units; this line cuts 20.0406 model units less.\n",
        ["shift-no-clamp-free-q2"] = "occupied=80 used=80.30001 tail=39.69999 separatorX=-80.30001 horizontalY=null band=0 strip=60 grid=120 blocking=False previews=2\ncut Vertical x=-70 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -70,0;LinearMove -70,60]\ncut Vertical x=-80.30001 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -80.30001,0;LinearMove -80.30001,60]\ndiag ClampBlockedCut blocking=False x=-35 The proposed cut crosses clamp 'clamp-1' (including its clearance); it was skipped.\n",
        ["shift-existing-boundary-q1"] = "occupied=80 used=80.30001 tail=39.69999 separatorX=80.30001 horizontalY=null band=0 strip=39.9594 grid=60 blocking=False previews=2\ncut Vertical x=34.75 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 34.75,0;LinearMove 34.75,9.979700000000001;RapidMove 34.75,30.020300000000002;LinearMove 34.75,60]\ncut Vertical x=80.30001 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 80.30001,0;LinearMove 80.30001,60]\ndiag SegmentedCut blocking=False x=34.75 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ShiftedCut blocking=False x=34.75 Moved from 35 to 34.75 model units; this line cuts 20.0406 model units less.\n",
        ["shift-existing-boundary-q4"] = "occupied=80 used=80.30001 tail=39.69999 separatorX=80.30001 horizontalY=null band=0 strip=39.9594 grid=60 blocking=False previews=2\ncut Vertical x=34.75 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 34.75,-60;LinearMove 34.75,-30.020300000000002;RapidMove 34.75,-9.979700000000001;LinearMove 34.75,0]\ncut Vertical x=80.30001 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 80.30001,-60;LinearMove 80.30001,0]\ndiag SegmentedCut blocking=False x=34.75 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ShiftedCut blocking=False x=34.75 Moved from 35 to 34.75 model units; this line cuts 20.0406 model units less.\n",
        ["shift-tail-too-short-q2"] = "occupied=139.75 used=144 tail=0 separatorX=null horizontalY=null band=0 strip=207.8376 grid=215.8782 blocking=False previews=4\ncut Vertical x=-35 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -35,0;LinearMove -35,1.9797;RapidMove -35,10.0203;LinearMove -35,60]\ncut Vertical x=-70 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -70,0;LinearMove -70,1.9797;RapidMove -70,10.0203;LinearMove -70,60]\ncut Vertical x=-105 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -105,0;LinearMove -105,1.9797;RapidMove -105,10.0203;LinearMove -105,60]\ncut Vertical x=-139.75 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -139.75,0;LinearMove -139.75,1.9797;RapidMove -139.75,10.0203;LinearMove -139.75,60]\ndiag SegmentedCut blocking=False x=-35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=-70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=-105 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=-139.75 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ShiftedCut blocking=False x=-139.75 Moved from 140 to 139.75 model units; this line cuts 8.0406 model units less.\ndiag TailBelowMinimum blocking=False x=-140.00001 The proposed tail is shorter than the minimum tail length; the final separator was skipped and strip lines continue to the sheet end. Existing cut-offs are unchanged. No retained tail is claimed.\n",
        ["shift-head-lock-q1"] = "occupied=80 used=80.30001 tail=39.69999 separatorX=80.30001 horizontalY=null band=0 strip=79.9188 grid=120 blocking=False previews=3\ncut Vertical x=34.75 y=0 start=null end=null overcut=1.5 farFirst=False gaps=[] program=[RapidMove 34.75,0;LinearMove 34.75,9.979700000000001;RapidMove 34.75,30.020300000000002;LinearMove 34.75,60;Comment :HEAD HEIGHT LOCK;LinearMove 34.75,61.5;Comment :HEAD HEIGHT UNLOCK]\ncut Vertical x=69.75 y=0 start=null end=null overcut=1.5 farFirst=False gaps=[] program=[RapidMove 69.75,0;LinearMove 69.75,9.979700000000001;RapidMove 69.75,30.020300000000002;LinearMove 69.75,60;Comment :HEAD HEIGHT LOCK;LinearMove 69.75,61.5;Comment :HEAD HEIGHT UNLOCK]\ncut Vertical x=80.30001 y=0 start=null end=null overcut=1.5 farFirst=False gaps=[] program=[RapidMove 80.30001,0;LinearMove 80.30001,60;Comment :HEAD HEIGHT LOCK;LinearMove 80.30001,61.5;Comment :HEAD HEIGHT UNLOCK]\ndiag SegmentedCut blocking=False x=34.75 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ShiftedCut blocking=False x=34.75 Moved from 35 to 34.75 model units; this line cuts 20.0406 model units less.\ndiag SegmentedCut blocking=False x=69.75 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ShiftedCut blocking=False x=69.75 Moved from 70 to 69.75 model units; this line cuts 20.0406 model units less.\n",
        ["horizontal-new-q1"] = "occupied=95.5 used=96 tail=0 separatorX=null horizontalY=21.30001 band=26.69999 strip=2.518819999999996 grid=2.518819999999996 blocking=False previews=3\ncut Vertical x=35 y=0 start=null end=21.30001 overcut=0 farFirst=False gaps=[] program=[RapidMove 35,0;LinearMove 35,0.9797;RapidMove 35,21.020300000000002;LinearMove 35,21.30001]\ncut Vertical x=70 y=0 start=null end=21.30001 overcut=0 farFirst=False gaps=[] program=[RapidMove 70,0;LinearMove 70,0.9797;RapidMove 70,21.020300000000002;LinearMove 70,21.30001]\ncut Horizontal x=0 y=21.30001 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 0,21.30001;LinearMove 96,21.30001]\ndiag SegmentedCut blocking=False x=35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag TailBelowMinimum blocking=False x=95.80001 The proposed tail is shorter than the minimum tail length; the final separator was skipped and strip lines continue to the sheet end. Existing cut-offs are unchanged. No retained tail is claimed.\ndiag HorizontalRemnant blocking=False x=null Vertical lines stop at the new horizontal separator at Y = 21.30001, keeping a 26.69999-wide band along the sheet length.\n",
        ["horizontal-new-q2"] = "occupied=95.5 used=96 tail=0 separatorX=null horizontalY=21.30001 band=26.69999 strip=2.518819999999996 grid=2.518819999999996 blocking=False previews=3\ncut Vertical x=-35 y=0 start=null end=21.30001 overcut=0 farFirst=False gaps=[] program=[RapidMove -35,0;LinearMove -35,0.9797;RapidMove -35,21.020300000000002;LinearMove -35,21.30001]\ncut Vertical x=-70 y=0 start=null end=21.30001 overcut=0 farFirst=False gaps=[] program=[RapidMove -70,0;LinearMove -70,0.9797;RapidMove -70,21.020300000000002;LinearMove -70,21.30001]\ncut Horizontal x=0 y=21.30001 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -96,21.30001;LinearMove 0,21.30001]\ndiag SegmentedCut blocking=False x=-35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=-70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag TailBelowMinimum blocking=False x=-95.80001 The proposed tail is shorter than the minimum tail length; the final separator was skipped and strip lines continue to the sheet end. Existing cut-offs are unchanged. No retained tail is claimed.\ndiag HorizontalRemnant blocking=False x=null Vertical lines stop at the new horizontal separator at Y = 21.30001, keeping a 26.69999-wide band along the sheet length.\n",
        ["horizontal-new-q3"] = "occupied=95.5 used=96 tail=0 separatorX=null horizontalY=-21.30001 band=26.69999 strip=2.518819999999996 grid=2.518819999999996 blocking=False previews=3\ncut Vertical x=-35 y=0 start=-21.30001 end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -35,-21.30001;LinearMove -35,-21.020300000000002;RapidMove -35,-0.9797;LinearMove -35,0]\ncut Vertical x=-70 y=0 start=-21.30001 end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -70,-21.30001;LinearMove -70,-21.020300000000002;RapidMove -70,-0.9797;LinearMove -70,0]\ncut Horizontal x=0 y=-21.30001 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -96,-21.30001;LinearMove 0,-21.30001]\ndiag SegmentedCut blocking=False x=-35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=-70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag TailBelowMinimum blocking=False x=-95.80001 The proposed tail is shorter than the minimum tail length; the final separator was skipped and strip lines continue to the sheet end. Existing cut-offs are unchanged. No retained tail is claimed.\ndiag HorizontalRemnant blocking=False x=null Vertical lines stop at the new horizontal separator at Y = -21.30001, keeping a 26.69999-wide band along the sheet length.\n",
        ["horizontal-new-q4"] = "occupied=95.5 used=96 tail=0 separatorX=null horizontalY=-21.30001 band=26.69999 strip=2.518819999999996 grid=2.518819999999996 blocking=False previews=3\ncut Vertical x=35 y=0 start=-21.30001 end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 35,-21.30001;LinearMove 35,-21.020300000000002;RapidMove 35,-0.9797;LinearMove 35,0]\ncut Vertical x=70 y=0 start=-21.30001 end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 70,-21.30001;LinearMove 70,-21.020300000000002;RapidMove 70,-0.9797;LinearMove 70,0]\ncut Horizontal x=0 y=-21.30001 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 0,-21.30001;LinearMove 96,-21.30001]\ndiag SegmentedCut blocking=False x=35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag TailBelowMinimum blocking=False x=95.80001 The proposed tail is shorter than the minimum tail length; the final separator was skipped and strip lines continue to the sheet end. Existing cut-offs are unchanged. No retained tail is claimed.\ndiag HorizontalRemnant blocking=False x=null Vertical lines stop at the new horizontal separator at Y = -21.30001, keeping a 26.69999-wide band along the sheet length.\n",
        ["horizontal-existing-q3"] = "occupied=95.5 used=96 tail=0 separatorX=null horizontalY=-23.5 band=24.5 strip=6.918799999999996 grid=6.918799999999996 blocking=False previews=2\ncut Vertical x=-35 y=0 start=-23.5 end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -35,-23.5;LinearMove -35,-21.020300000000002;RapidMove -35,-0.9797;LinearMove -35,0]\ncut Vertical x=-70 y=0 start=-23.5 end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -70,-23.5;LinearMove -70,-21.020300000000002;RapidMove -70,-0.9797;LinearMove -70,0]\ndiag SegmentedCut blocking=False x=-35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=-70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag TailBelowMinimum blocking=False x=-95.80001 The proposed tail is shorter than the minimum tail length; the final separator was skipped and strip lines continue to the sheet end. Existing cut-offs are unchanged. No retained tail is claimed.\ndiag HorizontalRemnant blocking=False x=null Vertical lines stop at the existing horizontal separator at Y = -23.5, keeping a 24.5-wide band along the sheet length.\n",
        ["horizontal-existing-rerun-q4"] = "occupied=95.5 used=96 tail=0 separatorX=null horizontalY=-21.30001 band=26.69999 strip=0 grid=0 blocking=False previews=0\ndiag ExistingCutOff blocking=False x=35 An equivalent full-span cut-off already exists; no duplicate was added.\ndiag SegmentedCut blocking=False x=35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag ExistingCutOff blocking=False x=70 An equivalent full-span cut-off already exists; no duplicate was added.\ndiag SegmentedCut blocking=False x=70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag TailBelowMinimum blocking=False x=95.80001 The proposed tail is shorter than the minimum tail length; the final separator was skipped and strip lines continue to the sheet end. Existing cut-offs are unchanged. No retained tail is claimed.\ndiag HorizontalRemnant blocking=False x=null Vertical lines stop at the existing horizontal separator at Y = -21.30001, keeping a 26.69999-wide band along the sheet length.\n",
        ["horizontal-shifted-q1"] = "occupied=95.5 used=96 tail=0 separatorX=null horizontalY=21.30001 band=26.69999 strip=2.518819999999996 grid=2.518819999999996 blocking=False previews=3\ncut Vertical x=35 y=0 start=null end=21.30001 overcut=0 farFirst=False gaps=[] program=[RapidMove 35,0;LinearMove 35,0.9797;RapidMove 35,21.020300000000002;LinearMove 35,21.30001]\ncut Vertical x=70 y=0 start=null end=21.30001 overcut=0 farFirst=False gaps=[] program=[RapidMove 70,0;LinearMove 70,0.9797;RapidMove 70,21.020300000000002;LinearMove 70,21.30001]\ncut Horizontal x=0 y=21.30001 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 0,21.30001;LinearMove 96,21.30001]\ndiag SegmentedCut blocking=False x=35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag TailBelowMinimum blocking=False x=95.80001 The proposed tail is shorter than the minimum tail length; the final separator was skipped and strip lines continue to the sheet end. Existing cut-offs are unchanged. No retained tail is claimed.\ndiag HorizontalRemnant blocking=False x=null Vertical lines stop at the new horizontal separator at Y = 21.30001, keeping a 26.69999-wide band along the sheet length.\n",
        ["horizontal-shifted-q3"] = "occupied=95.5 used=96 tail=0 separatorX=null horizontalY=-21.30001 band=26.69999 strip=2.518819999999996 grid=2.518819999999996 blocking=False previews=3\ncut Vertical x=-35 y=0 start=-21.30001 end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -35,-21.30001;LinearMove -35,-21.020300000000002;RapidMove -35,-0.9797;LinearMove -35,0]\ncut Vertical x=-70 y=0 start=-21.30001 end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -70,-21.30001;LinearMove -70,-21.020300000000002;RapidMove -70,-0.9797;LinearMove -70,0]\ncut Horizontal x=0 y=-21.30001 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove -96,-21.30001;LinearMove 0,-21.30001]\ndiag SegmentedCut blocking=False x=-35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=-70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag TailBelowMinimum blocking=False x=-95.80001 The proposed tail is shorter than the minimum tail length; the final separator was skipped and strip lines continue to the sheet end. Existing cut-offs are unchanged. No retained tail is claimed.\ndiag HorizontalRemnant blocking=False x=null Vertical lines stop at the new horizontal separator at Y = -21.30001, keeping a 26.69999-wide band along the sheet length.\n",
        ["horizontal-corner-tail"] = "occupied=65 used=65.30001 tail=30.69999 separatorX=65.30001 horizontalY=21.30001 band=26.69999 strip=1.259409999999998 grid=1.259409999999998 blocking=False previews=3\ncut Vertical x=35 y=0 start=null end=21.30001 overcut=0 farFirst=False gaps=[] program=[RapidMove 35,0;LinearMove 35,0.9797;RapidMove 35,21.020300000000002;LinearMove 35,21.30001]\ncut Vertical x=65.30001 y=0 start=null end=21.30001 overcut=0 farFirst=False gaps=[] program=[RapidMove 65.30001,0;LinearMove 65.30001,21.30001]\ncut Horizontal x=0 y=21.30001 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 0,21.30001;LinearMove 96,21.30001]\ndiag SegmentedCut blocking=False x=35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag HorizontalRemnant blocking=False x=null Vertical lines stop at the new horizontal separator at Y = 21.30001, keeping a 26.69999-wide band along the sheet length.\n",
        ["horizontal-narrow-band-loses"] = "occupied=30.5 used=30.80001 tail=65.19999 separatorX=30.80001 horizontalY=null band=0 strip=0 grid=0 blocking=False previews=1\ncut Vertical x=30.80001 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 30.80001,0;LinearMove 30.80001,48]\n",
        ["horizontal-clamp-on-line"] = "occupied=95.5 used=96 tail=0 separatorX=null horizontalY=null band=0 strip=55.9188 grid=55.9188 blocking=False previews=2\ncut Vertical x=35 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 35,0;LinearMove 35,0.9797;RapidMove 35,21.020300000000002;LinearMove 35,48]\ncut Vertical x=70 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 70,0;LinearMove 70,0.9797;RapidMove 70,21.020300000000002;LinearMove 70,48]\ndiag NoSafeHorizontalSeparator blocking=False x=null A horizontal separator at Y = 21.30001 would cross clamp 'far'; vertical lines cross the full sheet width instead.\ndiag SegmentedCut blocking=False x=35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag TailBelowMinimum blocking=False x=95.80001 The proposed tail is shorter than the minimum tail length; the final separator was skipped and strip lines continue to the sheet end. Existing cut-offs are unchanged. No retained tail is claimed.\n",
        ["horizontal-manual-beside-line"] = "occupied=95.5 used=96 tail=0 separatorX=null horizontalY=null band=0 strip=55.9188 grid=55.9188 blocking=False previews=2\ncut Vertical x=35 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 35,0;LinearMove 35,0.9797;RapidMove 35,21.020300000000002;LinearMove 35,48]\ncut Vertical x=70 y=0 start=null end=null overcut=0 farFirst=False gaps=[] program=[RapidMove 70,0;LinearMove 70,0.9797;RapidMove 70,21.020300000000002;LinearMove 70,48]\ndiag NoSafeHorizontalSeparator blocking=False x=null A manual cut on or within 0.30001 of Y = 21.30001 is not a usable separator; vertical lines cross the full sheet width instead.\ndiag SegmentedCut blocking=False x=35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag TailBelowMinimum blocking=False x=95.80001 The proposed tail is shorter than the minimum tail length; the final separator was skipped and strip lines continue to the sheet end. Existing cut-offs are unchanged. No retained tail is claimed.\n",
        ["horizontal-filtered-separator"] = "occupied=95.5 used=96 tail=0 separatorX=null horizontalY=null band=0 strip=0 grid=0 blocking=False previews=0\ndiag NoSafeHorizontalSeparator blocking=False x=null No full-length horizontal separator survives the current settings; vertical lines cross the full sheet width instead.\ndiag EmptyCut blocking=False x=35 The line has no usable cut segments after part clearance and minimum-length filtering.\ndiag EmptyCut blocking=False x=70 The line has no usable cut segments after part clearance and minimum-length filtering.\ndiag TailBelowMinimum blocking=False x=95.80001 The proposed tail is shorter than the minimum tail length; the final separator was skipped and strip lines continue to the sheet end. Existing cut-offs are unchanged. No retained tail is claimed.\n",
        ["horizontal-head-lock-q3"] = "occupied=95.5 used=96 tail=0 separatorX=null horizontalY=-21.30001 band=26.69999 strip=2.518819999999996 grid=2.518819999999996 blocking=False previews=3\ncut Vertical x=-35 y=0 start=-21.30001 end=null overcut=1.5 farFirst=False gaps=[] program=[RapidMove -35,-21.30001;LinearMove -35,-21.020300000000002;RapidMove -35,-0.9797;LinearMove -35,0]\ncut Vertical x=-70 y=0 start=-21.30001 end=null overcut=1.5 farFirst=False gaps=[] program=[RapidMove -70,-21.30001;LinearMove -70,-21.020300000000002;RapidMove -70,-0.9797;LinearMove -70,0]\ncut Horizontal x=0 y=-21.30001 start=null end=null overcut=1.5 farFirst=False gaps=[] program=[RapidMove 0,-21.30001;LinearMove -96,-21.30001;Comment :HEAD HEIGHT LOCK;LinearMove -97.5,-21.30001;Comment :HEAD HEIGHT UNLOCK]\ndiag SegmentedCut blocking=False x=-35 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag SegmentedCut blocking=False x=-70 Part clearance or segment filtering interrupts this line; review scrap separation before relying on it.\ndiag TailBelowMinimum blocking=False x=-95.80001 The proposed tail is shorter than the minimum tail length; the final separator was skipped and strip lines continue to the sheet end. Existing cut-offs are unchanged. No retained tail is claimed.\ndiag HorizontalRemnant blocking=False x=null Vertical lines stop at the new horizontal separator at Y = -21.30001, keeping a 26.69999-wide band along the sheet length.\n",
        ["no-parts"] = "occupied=0 used=0 tail=0 separatorX=null horizontalY=null band=0 strip=0 grid=0 blocking=False previews=0\n",
        ["cut-off-only"] = "occupied=0 used=0 tail=0 separatorX=null horizontalY=null band=0 strip=0 grid=0 blocking=False previews=0\n",
    };

    public static IEnumerable<object[]> ScenarioNames() => Scenarios.Keys.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void Create_MatchesRecordedPlan(string scenario)
    {
        var actual = Digest(Scenarios[scenario]());

        Assert.True(Expected.TryGetValue(scenario, out var expected), $"No recorded plan for {scenario}.");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Record()
    {
        var path = Environment.GetEnvironmentVariable("OPENNEST_CUTOFF_CHARACTERIZE");
        if (string.IsNullOrEmpty(path))
            return;
        var builder = new StringBuilder();
        foreach (var (name, scenario) in Scenarios)
            builder.Append("        [\"").Append(name).Append("\"] = \"").Append(Digest(scenario())
                .Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")).Append("\",\n");
        File.WriteAllText(path, builder.ToString());
    }

    private static string Digest(AutomaticCutOffPlan plan)
    {
        var builder = new StringBuilder();
        builder.Append("occupied=").Append(R(plan.OccupiedSpan))
            .Append(" used=").Append(R(plan.UsedSpan))
            .Append(" tail=").Append(R(plan.TailLength))
            .Append(" separatorX=").Append(R(plan.TailSeparatorX))
            .Append(" horizontalY=").Append(R(plan.HorizontalSeparatorY))
            .Append(" band=").Append(R(plan.HorizontalRemnantWidth))
            .Append(" strip=").Append(R(plan.StripCutLength))
            .Append(" grid=").Append(R(plan.FixedGridCutLength))
            .Append(" blocking=").Append(plan.HasBlockingDiagnostics)
            .Append(" previews=").Append(plan.PreviewParts.Count).Append('\n');
        for (var index = 0; index < plan.Definitions.Count; index++)
        {
            var cut = plan.Definitions[index];
            Assert.Same(cut.Drawing, plan.PreviewParts[index].BaseDrawing);
            builder.Append("cut ").Append(cut.Axis)
                .Append(" x=").Append(R(cut.Position.X)).Append(" y=").Append(R(cut.Position.Y))
                .Append(" start=").Append(R(cut.StartLimit)).Append(" end=").Append(R(cut.EndLimit))
                .Append(" overcut=").Append(R(cut.HeadLockOvercut))
                .Append(" farFirst=").Append(cut.FarEdgeFirst)
                .Append(" gaps=[").Append(string.Join(";", cut.Gaps.Select(g => R(g.Start) + ".." + R(g.End))))
                .Append("] program=[").Append(string.Join(";", cut.Drawing.Program.Codes.Select(Code)))
                .Append("]\n");
        }
        foreach (var diagnostic in plan.Diagnostics)
            builder.Append("diag ").Append(diagnostic.Code).Append(" blocking=").Append(diagnostic.IsBlocking)
                .Append(" x=").Append(R(diagnostic.X)).Append(' ').Append(diagnostic.Message).Append('\n');
        return builder.ToString();
    }

    private static string Code(ICode code) => code is Motion motion
        ? code.GetType().Name + " " + R(motion.EndPoint.X) + "," + R(motion.EndPoint.Y)
        : code.GetType().Name + " " + code;

    private static string R(double? value) =>
        value.HasValue ? value.Value.ToString("R", CultureInfo.InvariantCulture) : "null";

    private static AutomaticCutOffPlan Plan(Plate plate, double spacing = 35, double minimumTail = 0,
        double? minimumStrip = null, bool keepHorizontal = false, CutOffSettings? settings = null) =>
        AutomaticCutOffPlanner.Create(plate, new AutomaticCutOffOptions
        {
            Spacing = spacing,
            MinimumTailLength = minimumTail,
            MinimumStripLength = minimumStrip,
            KeepHorizontalRemnant = keepHorizontal,
        }, settings ?? new CutOffSettings());

    private static AutomaticCutOffPlan Shift(Plate plate, CutOffSettings? settings = null) =>
        AutomaticCutOffPlanner.Create(plate, AutomaticCutOffShiftTests.Options(),
            settings ?? new CutOffSettings { PartClearance = 0.02 });

    private static CutOffSettings HeadLock() => new() { LockHeadHeight = true, HeadLockOvercut = 1.5 };

    // AutomaticCutOffPlannerTests' plate: 81 by 120 with one part from X 10 through occupied.
    private static Plate Fixed(int quadrant, double occupied = 80)
    {
        var plate = new Plate(81, 120) { Quadrant = quadrant, PartSpacing = 0.5, Quantity = 2 };
        var x = quadrant is 2 or 3 ? -occupied : 10;
        var y = quadrant is 3 or 4 ? -40 : 20;
        plate.Parts.Add(AutomaticCutOffShiftTests.Rectangle(occupied - 10, 20, x, y));
        return plate;
    }

    // AutomaticCutOffHorizontalRemnantTests' plate: 48 by 96 with one part from X 0.5 through occupiedX.
    private static Plate Band(int quadrant, double occupiedX = 95.5)
    {
        var plate = new Plate(48, 96) { Quadrant = quadrant, PartSpacing = 0.3 };
        var x = quadrant is 2 or 3 ? -occupiedX : 0.5;
        var y = quadrant is 3 or 4 ? -21 : 1;
        plate.Parts.Add(AutomaticCutOffShiftTests.Rectangle(occupiedX - 0.5, 20, x, y));
        return plate;
    }

    private static Plate WithRail(Plate plate, double width, params double[] centers)
    {
        plate.EdgeSpacing = new Spacing(0.25, 0.25);
        plate.SetClampRail(new ClampRail(width, 0.625, centers));
        return plate;
    }

    private static Plate ShiftClamp(int quadrant, double width, double center)
    {
        var plate = AutomaticCutOffShiftTests.UserCase(quadrant);
        plate.EdgeSpacing = new Spacing(0, 0.25, 0, 0);
        plate.SetClampRail(new ClampRail(width, 0.625, new[] { center }));
        return plate;
    }

    private static Plate BlockingManualCut()
    {
        var plate = Fixed(1);
        plate.ClampZones.Add(new ClampZone("under-manual", new Box(34.25, 0, 1.5, 0.625)));
        plate.CutOffs.Add(new CutOff(new Vector(35, 0), CutOffAxis.Vertical) { StartLimit = 10, EndLimit = 60 });
        return plate;
    }

    private static Plate NearSeparatorOnClamp(int quadrant)
    {
        var plate = Fixed(quadrant);
        var sign = quadrant is 2 or 3 ? -1 : 1;
        plate.SetClampRail(new ClampRail(1.0000025, 0.625, new[] { 80.00000625 }));
        var existing = new CutOff(new Vector(sign * 80.500005, 0), CutOffAxis.Vertical);
        plate.CutOffs.Add(existing);
        existing.Regenerate(plate, new CutOffSettings());
        return plate;
    }

    private static Plate Rerun(Plate plate, bool keepHorizontal = false)
    {
        var settings = new CutOffSettings();
        plate.CutOffs.AddRange(Plan(plate, minimumTail: keepHorizontal ? 12 : 0,
            keepHorizontal: keepHorizontal).Definitions);
        plate.RegenerateCutOffs(settings);
        return plate;
    }

    private static Plate ExistingBoundary(int quadrant)
    {
        var plate = AutomaticCutOffShiftTests.UserCase(quadrant);
        var sign = quadrant is 2 or 3 ? -1 : 1;
        plate.CutOffs.Add(new CutOff(new Vector(sign * 50, 0), CutOffAxis.Vertical));
        plate.RegenerateCutOffs(new CutOffSettings());
        return plate;
    }

    private static Plate LongSheet(int quadrant)
    {
        var plate = new Plate(60, 144) { Quadrant = quadrant, PartSpacing = 0.25 };
        plate.Parts.Add(AutomaticCutOffShiftTests.Rectangle(138, 8, quadrant is 2 or 3 ? -139.75 : 1.75,
            quadrant is 3 or 4 ? -10 : 2));
        return plate;
    }

    private static Plate ExistingBand(int quadrant)
    {
        var plate = Band(quadrant);
        plate.CutOffs.Add(new CutOff(new Vector(0, (quadrant is 3 or 4 ? -1 : 1) * 23.5), CutOffAxis.Horizontal));
        return plate;
    }

    private static Plate NarrowBand()
    {
        var plate = new Plate(48, 96) { PartSpacing = 0.3 };
        plate.Parts.Add(AutomaticCutOffShiftTests.Rectangle(30, 40, 0.5, 1));
        return plate;
    }

    private static Plate BandWithClamp()
    {
        var plate = Band(1);
        plate.ClampZones.Add(new ClampZone("far", new Box(40, 20, 5, 5)));
        return plate;
    }

    private static Plate BandWithManualCut()
    {
        var plate = Band(1);
        plate.CutOffs.Add(new CutOff(new Vector(0, 21.5), CutOffAxis.Horizontal) { EndLimit = 30 });
        return plate;
    }

    private static Plate CutOffOnly()
    {
        var plate = new Plate(81, 120);
        plate.CutOffs.Add(new CutOff(new Vector(110, 0), CutOffAxis.Vertical));
        plate.RegenerateCutOffs(new CutOffSettings());
        return plate;
    }
}
