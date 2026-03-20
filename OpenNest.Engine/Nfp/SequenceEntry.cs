namespace OpenNest.Engine.Nfp
{
    /// <summary>
    /// An entry in a placement sequence — identifies which drawing to place and at what rotation.
    /// </summary>
    public readonly struct SequenceEntry
    {
        public int DrawingId { get; }
        public double Rotation { get; }
        public Drawing Drawing { get; }

        public SequenceEntry(int drawingId, double rotation, Drawing drawing)
        {
            DrawingId = drawingId;
            Rotation = rotation;
            Drawing = drawing;
        }

        public SequenceEntry WithRotation(double rotation)
        {
            return new SequenceEntry(DrawingId, rotation, Drawing);
        }
    }
}
