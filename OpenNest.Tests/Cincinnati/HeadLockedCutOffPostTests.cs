using System.Text;
using OpenNest.Geometry;
using OpenNest.Posts.Cincinnati;
using OpenNest.Posts.CincinnatiCIFiber;

namespace OpenNest.Tests.Cincinnati;

/// <summary>
/// Both Cincinnati posts write the configured head-height lock and unlock blocks around a
/// cut-off's overcut past the far sheet edge, and refuse the nest when the blocks are not set.
/// </summary>
public class HeadLockedCutOffPostTests
{
    private const string LockCode = "M210 (LOCK HEAD)";
    private const string UnlockCode = "M211 (UNLOCK HEAD)";

    [Fact]
    public void ClPost_TowardTheFarEdge_LocksAroundTheOvercut()
    {
        var lines = ClLines(Nest(farEdgeFirst: false));

        var locked = lines.IndexOf(LockCode);
        Assert.True(locked > 0, string.Join("\n", lines));
        AssertLine(lines, lines[locked - 1], "G1 X40 Y#110"); // The nominal far edge: the sheet width.
        AssertLine(lines, lines[locked + 1], "G1 X40 Y61.5");
        Assert.Equal(UnlockCode, lines[locked + 2]);
    }

    [Fact]
    public void ClPost_FromTheFarEdge_KeepsTheHeadDownForTheRapidBack()
    {
        var lines = ClLines(Nest(farEdgeFirst: true));

        var locked = lines.IndexOf(LockCode);
        var unlocked = lines.IndexOf(UnlockCode);
        Assert.True(locked > 0 && unlocked > locked, string.Join("\n", lines));
        AssertLine(lines, lines[locked + 1], "G1 X40 Y61.5");
        var between = lines.GetRange(locked, unlocked - locked);
        // No head raise while the head is locked; the rapid back returns to the nominal edge.
        Assert.DoesNotContain(between, line => line.Contains("M47"));
        Assert.Contains(between, line => line.Contains("G0 X40 Y#110"));
        AssertLine(lines, lines[unlocked + 1], "G1 X40 Y0");
    }

    [Fact]
    public void ClPost_WithoutLockCodes_RefusesBeforeWriting()
    {
        using var stream = new MemoryStream();
        var post = new CincinnatiPostProcessor(new CincinnatiPostConfig());

        var refusal = Assert.Throws<InvalidOperationException>(() => post.Post(Nest(farEdgeFirst: false), stream));

        Assert.Contains("head-height lock", refusal.Message);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void CiFiberPost_FromTheFarEdge_LocksAroundTheOvercutAndUnlocksBeforeCuttingBack()
    {
        var lines = CiFiberLines(Nest(farEdgeFirst: true));

        var locked = lines.IndexOf(LockCode);
        var unlocked = lines.IndexOf(UnlockCode);
        Assert.True(locked > 0 && unlocked > locked, string.Join("\n", lines));
        AssertLine(lines, lines[locked + 1], "G1X40Y61.5");
        AssertLine(lines, lines[unlocked + 1], "G1X40Y0");
    }

    [Fact]
    public void CiFiberPost_WithoutLockCodes_Refuses()
    {
        var post = new CIFiberPostProcessor(new CIFiberPostConfig());

        Assert.Throws<InvalidOperationException>(() => post.Post(Nest(farEdgeFirst: false), new MemoryStream()));
    }

    private static Nest Nest(bool farEdgeFirst)
    {
        var nest = new Nest("HEADLOCK");
        var plate = new Plate(60, 120);
        nest.Plates.Add(plate);
        plate.CutOffs.Add(new CutOff(new Vector(40, 0), CutOffAxis.Vertical)
        {
            HeadLockOvercut = 1.5,
            FarEdgeFirst = farEdgeFirst,
        });
        plate.RegenerateCutOffs(new CutOffSettings());
        return nest;
    }

    private static List<string> ClLines(Nest nest)
    {
        var post = new CincinnatiPostProcessor(new CincinnatiPostConfig
        {
            HeadHeightLockCode = LockCode,
            HeadHeightUnlockCode = UnlockCode,
        });
        using var stream = new MemoryStream();
        post.Post(nest, stream);
        return Lines(stream);
    }

    private static List<string> CiFiberLines(Nest nest)
    {
        var post = new CIFiberPostProcessor(new CIFiberPostConfig
        {
            HeadHeightLockCode = LockCode,
            HeadHeightUnlockCode = UnlockCode,
        });
        using var stream = new MemoryStream();
        post.Post(nest, stream);
        return Lines(stream);
    }

    // A posted line holds the expected move (a kerf-compensation code may come first).
    private static void AssertLine(List<string> lines, string line, string move) =>
        Assert.True(line.Contains(move), $"Expected '{move}' in '{line}':\n" + string.Join("\n", lines));

    private static List<string> Lines(MemoryStream stream) =>
        Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).ToList();
}
