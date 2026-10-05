using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using OpenNest.CNC.CuttingStrategy;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>
/// Exact text record of a settings object's authored state for detecting in-place edits of
/// settings after capture. It is a freshness fingerprint, not a serializer.
/// No code of a foreign (non-OpenNest) type is ever executed: capture reads no foreign
/// property getter (it could mutate live state or throw) and enumerates no foreign collection
/// (its enumerator could throw after capture). Concretely it renders invariant scalars
/// (doubles by bit pattern, so results are culture-free); arrays and the exact BCL containers
/// List/Dictionary/HashSet/KeyValuePair, whose contents are rendered recursively and, where
/// order is insertion order rather than authored semantics, sorted; concrete public types of
/// an OpenNest assembly through their public readable properties and fields; and any other
/// concrete type through its declared instance fields only, which include auto-property
/// backing fields, so edits to them are seen. Behavioral enumerables and delegates are
/// refused, as are depth and node-budget overflow and member-read failures; a reference
/// already on the current path renders as a stable cycle marker, which loses nothing because
/// the first visit rendered everything reachable from it. A refusal yields
/// <see cref="Invalid"/>, which never compares equal, so refused state is unequal (Stale)
/// instead of silently equal.
/// </summary>
internal static class StateFingerprint
{
    internal const string Invalid = "<invalid>";
    private const int MaxDepth = 24;
    private const int NodeBudget = 100000;
    private static readonly Assembly Bcl = typeof(object).Assembly;
    private static readonly Assembly[] OpenNestAssemblies =
        [typeof(CuttingParameters).Assembly, typeof(OpenNest.Geometry.Vector).Assembly];

    /// <summary>
    /// Fingerprint of <paramref name="value"/>, or <see cref="Invalid"/> when the exact
    /// authored state cannot be captured without executing foreign code or guessing.
    /// </summary>
    internal static string Of(object value)
    {
        var nodes = 0;
        var exact = true;
        var path = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var result = Render(value, 0);
        return exact ? result : Invalid;

        string Render(object item, int depth)
        {
            if (!exact)
                return Invalid;
            if (++nodes > NodeBudget || depth > MaxDepth)
            {
                exact = false; // Truncation is lossy: never claim it as equal-able state.
                return Invalid;
            }
            if (item == null)
                return "null";
            var type = item.GetType();
            switch (item)
            {
                case double number:
                    // Bit pattern: NaN, infinities and signed zero stay exact. Explicit
                    // invariant formatting: ambient culture can substitute other digits.
                    return $"{type.FullName}#{BitConverter.DoubleToInt64Bits(number).ToString(CultureInfo.InvariantCulture)}";
                case float number:
                    return $"{type.FullName}#{BitConverter.SingleToInt32Bits(number).ToString(CultureInfo.InvariantCulture)}";
                case string characters:
                    return $"{type.FullName}#{characters.Length.ToString(CultureInfo.InvariantCulture)}:{characters}";
            }
            if (type.IsPrimitive || type.IsEnum || item is decimal or Guid || item is DateTime
                || item is DateTimeOffset || item is TimeSpan)
                return $"{type.FullName}#{Convert.ToString(item, CultureInfo.InvariantCulture)}";
            if (typeof(Delegate).IsAssignableFrom(type))
            {
                exact = false; // A delegate is behavior, not authored state.
                return Invalid;
            }

            var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : null;
            var exactContainer = type.Assembly == Bcl && (type.IsArray
                || definition == typeof(List<>) || definition == typeof(Dictionary<,>)
                || definition == typeof(HashSet<>) || definition == typeof(KeyValuePair<,>));
            if (!exactContainer && typeof(IEnumerable).IsAssignableFrom(type))
            {
                // A behavioral collection: enumerating it would run foreign code now, or the
                // enumerator could throw after capture. Refuse instead of reading it.
                exact = false;
                return Invalid;
            }

            var tracked = !type.IsValueType && path.Add(item);
            if (!type.IsValueType && !tracked)
                return $"cycle@{type.FullName}"; // Already rendered on this path.
            try
            {
                if (definition == typeof(KeyValuePair<,>))
                    return $"{type.FullName}({Render(Property("Key"), depth + 1)}" +
                        $"=>{Render(Property("Value"), depth + 1)})";
                var entries = exactContainer
                    ? ((IEnumerable)item).Cast<object>().Select(element => Render(element, depth + 1)).ToList()
                    : StateMembers(item, depth + 1);
                if (!exact)
                    return Invalid;
                if (definition == typeof(Dictionary<,>) || definition == typeof(HashSet<>))
                    // Container order is insertion order, not authored semantics.
                    entries.Sort(StringComparer.Ordinal);
                return exactContainer
                    ? $"{type.FullName}[{string.Join("|", entries)}]"
                    : $"{type.FullName}({string.Join(";", entries)})";
            }
            finally
            {
                if (tracked)
                    path.Remove(item);
            }

            List<string> StateMembers(object target, int childDepth)
            {
                var parts = new List<string>();
                if (OpenNestAssemblies.Contains(type.Assembly))
                    // Own code: public readable properties and fields are the authored state.
                    // A property and a field sharing a name must not collapse; order by both.
                    foreach (var member in type
                        .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                        .Where(m => m is FieldInfo || m is PropertyInfo property && property.CanRead
                            && property.GetIndexParameters().Length == 0)
                        .OrderBy(m => m.Name, StringComparer.Ordinal)
                        .ThenBy(m => m.MemberType))
                    {
                        var value = member switch
                        {
                            PropertyInfo property => Read(() => property.GetValue(target)),
                            _ => Read(() => ((FieldInfo)member).GetValue(target)),
                        };
                        if (!exact)
                            return parts;
                        parts.Add($"{member.Name}={Render(value, childDepth)}");
                    }
                else
                    // Foreign type: declared instance fields only, including auto-property
                    // backing fields. Reading a field executes no foreign code.
                    for (var walk = type; walk != null && walk != typeof(Delegate) && exact; walk = walk.BaseType)
                        foreach (var field in walk.GetFields(BindingFlags.Public | BindingFlags.NonPublic
                            | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                            .OrderBy(f => f.Name, StringComparer.Ordinal))
                        {
                            var value = Read(() => field.GetValue(target));
                            if (!exact)
                                return parts;
                            parts.Add($"{walk.Name}.{field.Name}={Render(value, childDepth)}");
                        }
                return parts;
            }

            object Property(string name) => type.GetProperty(name)!.GetValue(item);

            object Read(Func<object> read)
            {
                try
                {
                    return read();
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    exact = false; // A member that cannot be read makes the state non-exact.
                    return null;
                }
            }
        }
    }
}
