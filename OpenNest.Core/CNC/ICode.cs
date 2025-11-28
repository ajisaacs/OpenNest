namespace OpenNest.CNC
{
    public interface ICode
    {
        CodeType Type { get; }

        ICode Clone();
    }
}
