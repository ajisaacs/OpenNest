using System;
using OpenNest.CNC.CuttingStrategy;

namespace OpenNest.Engine.Sequencing
{
    public static class PartSequencerFactory
    {
        public static IPartSequencer Create(SequenceParameters parameters)
        {
            return parameters.Method switch
            {
                SequenceMethod.RightSide  => new RightSideSequencer(),
                SequenceMethod.LeftSide   => new LeftSideSequencer(),
                SequenceMethod.BottomSide => new BottomSideSequencer(),
                SequenceMethod.EdgeStart  => new EdgeStartSequencer(),
                SequenceMethod.LeastCode  => new LeastCodeSequencer(),
                SequenceMethod.Advanced   => new AdvancedSequencer(parameters),
                _ => throw new NotSupportedException(
                    $"Sequence method '{parameters.Method}' is not supported.")
            };
        }
    }
}
