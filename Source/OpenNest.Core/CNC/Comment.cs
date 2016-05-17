namespace OpenNest.CNC
{
    public class Comment : ICode
    {
        public Comment()
        {
        }

        public Comment(string value)
        {
            Value = value;
        }

        public string Value { get; set; }

        public CodeType Type
        {
            get { return CodeType.Comment; }
        }

        public ICode Clone()
        {
            return new Comment(Value);
        }

        public override string ToString()
        {
            return ':' + Value;
        }
    }
}
