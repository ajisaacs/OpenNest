namespace OpenNest
{
    public struct Quantity
    {
        public int Nested { get; internal set; }

        public int Required { get; set; }

        public int Remaining
        {
            get { return Required - Nested; }
        }
    }
}
