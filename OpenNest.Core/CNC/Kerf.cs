namespace OpenNest.CNC
{
    public class Kerf : ICode
    {
        public Kerf(KerfType kerf = KerfType.Left)
        {
            Value = kerf;
        }

        public KerfType Value { get; set; }

        public CodeType Type
        {
            get { return CodeType.SetKerf; }
        }

        public ICode Clone()
        {
            return new Kerf(Value);
        }

        public override string ToString()
        {
            switch (Value)
            {
                case KerfType.Left:
                    return "G41";

                case KerfType.Right:
                    return "G42";

                case KerfType.None:
                    return "G40";
            }

            return string.Empty;
        }
    }
}
