using OpenNest.Engine.Jobs;

namespace OpenNest.Engine.Tests.Jobs;

public class RotationPolicyTests
{
    [Fact]
    public void AutomaticReturnsRightAnglesInOrder()
    {
        Assert.Equal(new[] { 0, System.Math.PI / 2, System.Math.PI, 3 * System.Math.PI / 2 },
            RotationPolicy.Automatic.EnumerateAngles());
    }

    [Fact]
    public void FixedIncludesNormalizedHalfTurnEquivalent()
    {
        var policy = RotationPolicy.Fixed(-System.Math.PI / 2, true);
        Assert.Equal(new[] { 3 * System.Math.PI / 2, System.Math.PI / 2 }, policy.EnumerateAngles(1));
        AssertLegal(policy);
    }

    [Fact]
    public void OversizedSweepEvenlySamplesGridIncludingEndpoints()
    {
        var policy = RotationPolicy.BoundedSweep(0, 1, 0.01);
        Assert.Equal(new[] { 0, 0.25, 0.5, 0.75, 1 }, policy.EnumerateAngles(5));
        AssertLegal(policy, 5);
    }

    [Fact]
    public void SubsamplingRoundsToLegalGridPoints()
    {
        var policy = RotationPolicy.BoundedSweep(0, 1, 0.1);
        var angles = policy.EnumerateAngles(4);
        Assert.Equal(4, angles.Count);
        Assert.Equal(0.3, angles[1], 10);
        Assert.Equal(0.7, angles[2], 10);
        Assert.Equal(1, angles[3]);
        AssertLegal(policy, 4);
    }

    [Fact]
    public void OffGridEndUsesLastLegalGridPoint()
    {
        var policy = RotationPolicy.BoundedSweep(0, 1, 0.3);
        Assert.Equal(0.9, policy.EnumerateAngles(2)[1], 10);
        AssertLegal(policy, 2);
    }

    [Fact]
    public void SweepEquivalentsFollowEachBaseAngleAndAreDeduplicated()
    {
        var policy = RotationPolicy.BoundedSweep(0, System.Math.PI, System.Math.PI / 2, true);
        Assert.Equal(new[] { 0, System.Math.PI, System.Math.PI / 2, 3 * System.Math.PI / 2 },
            policy.EnumerateAngles());
        AssertLegal(policy);
    }

    [Fact]
    public void SweepCrossingFullTurnNormalizesWithoutSorting()
    {
        var policy = RotationPolicy.BoundedSweep(3 * System.Math.PI / 2,
            5 * System.Math.PI / 2, System.Math.PI / 2, true);
        Assert.Equal(new[] { 3 * System.Math.PI / 2, System.Math.PI / 2, 0, System.Math.PI },
            policy.EnumerateAngles());
        AssertLegal(policy);
    }

    [Fact]
    public void FullTurnAndNearDuplicateAnglesCollapse()
    {
        Assert.Equal(4, RotationPolicy.BoundedSweep(0, 2 * System.Math.PI,
            System.Math.PI / 2).EnumerateAngles().Count);
        Assert.Single(RotationPolicy.BoundedSweep(-1e-8, 1e-8, 1e-8).EnumerateAngles());
    }

    [Fact]
    public void OneSampleReturnsTheSweepStart()
    {
        Assert.Single(RotationPolicy.BoundedSweep(0.2, 0.3, 1).EnumerateAngles(1));
        Assert.Equal(new[] { 0.0 }, RotationPolicy.BoundedSweep(0, 1, 0.5).EnumerateAngles(1));
        Assert.Equal(new[] { 0.0, System.Math.PI },
            RotationPolicy.BoundedSweep(0, 1, 0.5, true).EnumerateAngles(1));
        Assert.Equal(4, RotationPolicy.Automatic.EnumerateAngles(1).Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidSampleCapThrows(int maxSamples)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RotationPolicy.Fixed(0).EnumerateAngles(maxSamples));
    }

    [Fact]
    public void DefaultSweepCapIs720BaseSamples()
    {
        var policy = RotationPolicy.BoundedSweep(0, 1, 0.0001, true);
        var angles = policy.EnumerateAngles();
        Assert.Equal(1440, angles.Count);
        Assert.Equal(1, angles[^2]);
        AssertLegal(policy);
    }

    private static void AssertLegal(RotationPolicy policy, int maxSamples = 720)
    {
        var angles = policy.EnumerateAngles(maxSamples);
        Assert.Equal(angles, policy.EnumerateAngles(maxSamples));
        Assert.All(angles, angle =>
        {
            Assert.True(angle >= 0 && angle < 2 * System.Math.PI);
            Assert.True(policy.Allows(angle));
        });
    }
}
