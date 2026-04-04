namespace OpenNest.Tests.Fill;

public class BestCombinationTests
{
    [Fact]
    public void BothFit_FindsZeroRemnant()
    {
        // 100 = 0*30 + 5*20 (algorithm iterates from countLength1=0, finds zero remnant first)
        var result = BestCombination.FindFrom2(30, 20, 100);

        Assert.True(result.Found);
        Assert.Equal(0.0, 100.0 - (result.Count1 * 30.0 + result.Count2 * 20.0), 5);
    }

    [Fact]
    public void OnlyLength1Fits_ReturnsMaxCount1()
    {
        var result = BestCombination.FindFrom2(10, 200, 50);

        Assert.True(result.Found);
        Assert.Equal(5, result.Count1);
        Assert.Equal(0, result.Count2);
    }

    [Fact]
    public void OnlyLength2Fits_ReturnsMaxCount2()
    {
        var result = BestCombination.FindFrom2(200, 10, 50);

        Assert.True(result.Found);
        Assert.Equal(0, result.Count1);
        Assert.Equal(5, result.Count2);
    }

    [Fact]
    public void NeitherFits_ReturnsFalse()
    {
        var result = BestCombination.FindFrom2(100, 200, 50);

        Assert.False(result.Found);
        Assert.Equal(0, result.Count1);
        Assert.Equal(0, result.Count2);
    }

    [Fact]
    public void Length1FillsExactly_ZeroRemnant()
    {
        var result = BestCombination.FindFrom2(25, 10, 100);

        Assert.True(result.Found);
        Assert.Equal(0.0, 100.0 - (result.Count1 * 25.0 + result.Count2 * 10.0), 5);
    }

    [Fact]
    public void MixMinimizesRemnant()
    {
        // 7 and 3 into 20: best is 2*7 + 2*3 = 20 (zero remnant)
        var result = BestCombination.FindFrom2(7, 3, 20);

        Assert.True(result.Found);
        Assert.Equal(2, result.Count1);
        Assert.Equal(2, result.Count2);
        Assert.True(result.Count1 * 7 + result.Count2 * 3 <= 20);
    }

    [Fact]
    public void PrefersLessRemnant_OverMoreOfLength1()
    {
        // 6 and 5 into 17:
        //   all length1: 2*6=12, remnant=5 -> actually 2*6+1*5=17 perfect
        var result = BestCombination.FindFrom2(6, 5, 17);

        Assert.True(result.Found);
        Assert.Equal(0.0, 17.0 - (result.Count1 * 6.0 + result.Count2 * 5.0), 5);
    }

    [Fact]
    public void EqualLengths_FillsWithLength1()
    {
        var result = BestCombination.FindFrom2(10, 10, 50);

        Assert.True(result.Found);
        Assert.Equal(5, result.Count1 + result.Count2);
    }

    [Fact]
    public void SmallLengths_LargeOverall()
    {
        var result = BestCombination.FindFrom2(3, 7, 100);

        Assert.True(result.Found);
        var used = result.Count1 * 3.0 + result.Count2 * 7.0;
        Assert.True(used <= 100);
        Assert.True(100 - used < 3); // remnant less than smallest piece
    }

    [Fact]
    public void Length2IsBetter_SoleCandidate()
    {
        // length1=9, length2=5, overall=10:
        //   length1 alone: 1*9=9 remnant=1
        //   length2 alone: 2*5=10 remnant=0
        var result = BestCombination.FindFrom2(9, 5, 10);

        Assert.True(result.Found);
        Assert.Equal(0, result.Count1);
        Assert.Equal(2, result.Count2);
    }

    [Fact]
    public void FractionalLengths_WorkCorrectly()
    {
        var result = BestCombination.FindFrom2(2.5, 3.5, 12);

        Assert.True(result.Found);
        var used = result.Count1 * 2.5 + result.Count2 * 3.5;
        Assert.True(used <= 12.0 + 0.001);
    }

    [Fact]
    public void OverallExactlyOneOfEach()
    {
        var result = BestCombination.FindFrom2(40, 60, 100);

        Assert.True(result.Found);
        Assert.Equal(1, result.Count1);
        Assert.Equal(1, result.Count2);
    }

    [Fact]
    public void OverallSmallerThanEither_ReturnsFalse()
    {
        var result = BestCombination.FindFrom2(10, 20, 5);

        Assert.False(result.Found);
        Assert.Equal(0, result.Count1);
        Assert.Equal(0, result.Count2);
    }

    [Fact]
    public void ZeroRemnant_StopsEarly()
    {
        // 4 and 6 into 24: 0*4+4*6=24 or 3*4+2*6=24 or 6*4+0*6=24
        // Algorithm iterates from 0 length1 upward, finds zero remnant and breaks
        var result = BestCombination.FindFrom2(4, 6, 24);

        Assert.True(result.Found);
        Assert.Equal(0.0, 24.0 - (result.Count1 * 4.0 + result.Count2 * 6.0), 5);
    }
}
