using System;

namespace OpenNest.Shapes
{
    /// <summary>
    /// Marks a <see cref="double"/> shape parameter that is a plain number, such as a K-factor,
    /// rather than a length: the Shape Library shows it without fractions or an inch mark.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class UnitlessAttribute : Attribute
    {
    }
}
