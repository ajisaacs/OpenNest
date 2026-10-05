using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>
/// Exact text record of an object's public state (properties and fields, recursively; scalars by
/// bits, runtime types included) for detecting in-place edits of settings after capture. It is a
/// freshness fingerprint, not a serializer. Value types contribute fields only, so computed struct
/// properties cannot recurse without bound.
/// </summary>
internal static class StateFingerprint
{
    private const int MaxDepth = 16;
    private const int NodeBudget = 100000;

    internal static string Of(object value)
    {
        var text = new StringBuilder();
        var path = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var nodes = 0;
        Append(value, 0);
        return text.ToString();

        void Append(object item, int depth)
        {
            if (++nodes > NodeBudget)
                throw new NotSupportedException("Settings are too large to fingerprint exactly.");
            if (item == null)
            {
                text.Append("null;");
                return;
            }
            var type = item.GetType();
            text.Append(type.FullName).Append('=');
            switch (item)
            {
                case double number:
                    text.Append(BitConverter.DoubleToInt64Bits(number)).Append(';');
                    return;
                case float number:
                    text.Append(BitConverter.SingleToInt32Bits(number)).Append(';');
                    return;
                case string characters:
                    text.Append(characters.Length).Append(':').Append(characters).Append(';');
                    return;
            }
            if (type.IsPrimitive || type.IsEnum || item is decimal)
            {
                text.Append(Convert.ToString(item, CultureInfo.InvariantCulture)).Append(';');
                return;
            }
            if (depth >= MaxDepth || (!type.IsValueType && !path.Add(item)))
            {
                text.Append("<depth-or-cycle>;");
                return;
            }
            try
            {
                text.Append('{');
                if (item is IEnumerable sequence)
                {
                    foreach (var element in sequence)
                        Append(element, depth + 1);
                }
                else
                {
                    foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                        .OrderBy(f => f.Name, StringComparer.Ordinal))
                    {
                        text.Append(field.Name).Append(':');
                        Append(field.GetValue(item), depth + 1);
                    }
                    if (!type.IsValueType)
                        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                            .OrderBy(p => p.Name, StringComparer.Ordinal))
                        {
                            text.Append(property.Name).Append(':');
                            object read;
                            try
                            {
                                read = property.GetValue(item);
                            }
                            catch (TargetInvocationException exception)
                            {
                                text.Append("throws ").Append(exception.InnerException?.GetType().FullName).Append(';');
                                continue;
                            }
                            Append(read, depth + 1);
                        }
                }
                text.Append('}');
            }
            finally
            {
                if (!type.IsValueType)
                    path.Remove(item);
            }
        }
    }
}
