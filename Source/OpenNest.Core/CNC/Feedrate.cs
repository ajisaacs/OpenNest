namespace OpenNest.CNC
{
    public class Feedrate : ICode
    {
        public const int UseDefault = -1;

        public const int UseMax = -2;

        public Feedrate()
        {
        }

        public Feedrate(double value)
        {
            Value = value;
        }

        public double Value { get; set; }

        public CodeType Type
        {
            get { return CodeType.SetFeedrate; }
        }

        public ICode Clone()
        {
            return new Feedrate(Value);
        }

        public override string ToString()
        {
            return string.Format("F{0}", Value);
        }
    }
}
