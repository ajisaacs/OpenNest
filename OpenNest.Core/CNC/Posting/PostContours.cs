using System;
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.CNC.Posting;

/// <summary>
/// Reduces a part program to the contours a post writes: hole calls and incremental programs
/// flattened to one absolute frame, each contour split into pierce, lead-in and cuts, the
/// perimeter picked out and the kerf side chosen. Posts format these; they do not redo the
/// geometry. Caller programs are never modified.
/// </summary>
public static class PostContours
{
    /// <summary>
    /// The part's contours in sheet coordinates. A display perimeter's tail is never cut and an
    /// unplanned one is refused (<see cref="DisplayPerimeterTail.ForPosting"/>). Cut-off contours
    /// are open lines on the beam centreline: no perimeter and no kerf.
    /// </summary>
    public static List<PostContour> Extract(Part part)
    {
        if (part == null)
            throw new ArgumentNullException(nameof(part));

        var program = DisplayPerimeterTail.ForPosting(part.Program, DisplayPerimeterTail.PartName(part));
        var contours = Extract(program, part.Location);
        if (part.BaseDrawing?.IsCutOff == true)
        {
            foreach (var contour in contours)
            {
                contour.IsPerimeter = false;
                contour.Kerf = KerfType.None;
            }
        }
        return contours;
    }

    /// <summary>
    /// The program's contours with <paramref name="origin"/> added to every point: the part
    /// location for sheet coordinates, <see cref="Vector.Zero"/> for the part's own frame.
    /// </summary>
    public static List<PostContour> Extract(Program program, Vector origin)
    {
        var contours = new List<PostContour>();
        Flatten(program, origin, contours);
        Classify(contours);
        return contours;
    }

    /// <summary>Picks the perimeter (marks excluded) and each contour's kerf side.</summary>
    private static void Classify(List<PostContour> contours)
    {
        var perimeter = ContourWinding.PerimeterIndex(contours);
        for (var i = 0; i < contours.Count; i++)
        {
            var contour = contours[i];
            contour.IsPerimeter = i == perimeter;
            contour.Kerf = contour.IsMark
                ? KerfType.None
                : ContourWinding.CompensationSide(contour, contour.IsPerimeter);
        }
    }

    /// <summary>
    /// Walks one program whose coordinates are relative to <paramref name="origin"/>; hole calls
    /// recurse with their offset added. A contour starts at each rapid. Moves before the first
    /// rapid start at the frame origin, where an absolute program begins. A cut after a run of
    /// suppressed moves starts a new contour pierced where the run ends, so the gap is crossed
    /// with the beam off rather than cut as a chord.
    /// </summary>
    private static void Flatten(Program program, Vector origin, List<PostContour> contours)
    {
        if (program == null)
            return;

        var codes = program.Codes;
        if (program.Mode == Mode.Incremental)
        {
            var clone = (Program)program.Clone();
            clone.Mode = Mode.Absolute;
            codes = clone.Codes;
        }

        PostContour current = null;
        Vector? resumeAt = null;

        void Flush()
        {
            if (current != null && (current.LeadIn != null || current.Cuts.Count > 0))
                contours.Add(current);
            current = null;
        }

        foreach (var code in codes)
        {
            if (code is Motion { Suppressed: true } suppressed)
            {
                resumeAt = suppressed.EndPoint;
                continue;
            }

            if (HeadHeightLock.IsLock(code) || HeadHeightLock.IsUnlock(code))
            {
                current ??= new PostContour { Pierce = origin };
                current.HeadLocks.Add((current.Cuts.Count, HeadHeightLock.IsLock(code)));
                continue;
            }

            switch (code)
            {
                case RapidMove rapid:
                    Flush();
                    resumeAt = null;
                    current = new PostContour { Pierce = rapid.EndPoint + origin };
                    break;

                case SubProgramCall call:
                    // A hole call is its own contour.
                    Flush();
                    resumeAt = null;
                    Flatten(call.Program, origin + call.Offset, contours);
                    break;

                case Motion motion:
                    if (resumeAt.HasValue)
                    {
                        Flush();
                        current = new PostContour { Pierce = resumeAt.Value + origin };
                        resumeAt = null;
                    }
                    current ??= new PostContour { Pierce = origin };

                    var absolute = (Motion)motion.Clone();
                    absolute.Offset(origin);

                    if (LayerOf(motion) == LayerType.Leadin)
                    {
                        if (current.LeadIn == null)
                            current.LeadIn = absolute;
                        else
                            current.LeadInExtra.Add(absolute);
                    }
                    else
                    {
                        // Contour moves are tagged Display by default and still cut.
                        current.Cuts.Add(absolute);
                    }
                    break;
            }
        }

        Flush();
    }

    internal static LayerType LayerOf(Motion motion) =>
        motion switch
        {
            LinearMove linear => linear.Layer,
            ArcMove arc => arc.Layer,
            _ => LayerType.Cut,
        };
}
