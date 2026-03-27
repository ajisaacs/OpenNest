using System;

namespace OpenNest.IO.Bom
{
    [AttributeUsage(AttributeTargets.Property)]
    public class ColumnAttribute : Attribute
    {
        public ColumnAttribute(params string[] names)
        {
            Names = names;
        }

        public string[] Names { get; }
    }
}
