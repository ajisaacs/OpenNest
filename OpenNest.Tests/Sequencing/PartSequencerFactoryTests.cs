using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.Sequencing;

namespace OpenNest.Tests.Sequencing;

public class PartSequencerFactoryTests
{
    [Theory]
    [InlineData(SequenceMethod.RightSide, typeof(RightSideSequencer))]
    [InlineData(SequenceMethod.LeftSide, typeof(LeftSideSequencer))]
    [InlineData(SequenceMethod.BottomSide, typeof(BottomSideSequencer))]
    [InlineData(SequenceMethod.EdgeStart, typeof(EdgeStartSequencer))]
    [InlineData(SequenceMethod.LeastCode, typeof(LeastCodeSequencer))]
    [InlineData(SequenceMethod.Advanced, typeof(AdvancedSequencer))]
    public void Create_ReturnsCorrectType(SequenceMethod method, Type expectedType)
    {
        var parameters = new SequenceParameters { Method = method };
        var sequencer = PartSequencerFactory.Create(parameters);
        Assert.IsType(expectedType, sequencer);
    }

    [Fact]
    public void Create_RightSideAlt_Throws()
    {
        var parameters = new SequenceParameters { Method = SequenceMethod.RightSideAlt };
        Assert.Throws<NotSupportedException>(() => PartSequencerFactory.Create(parameters));
    }
}
