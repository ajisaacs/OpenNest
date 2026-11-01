using System.Reflection;

namespace OpenNest.WinForms.Tests;

/// <summary>Reads and sets private members from tests. Import with <c>using static</c>.</summary>
internal static class TestReflection
{
    /// <summary>The named private instance field declared by the owner's own type.</summary>
    public static T Field<T>(object owner, string name) =>
        Field<T>(owner.GetType(), owner, name);

    /// <summary>The named private instance field declared by <paramref name="type"/>.</summary>
    public static T Field<T>(Type type, object owner, string name) =>
        (T)type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    /// <summary>Sets the compiler-generated backing field of an auto-property.</summary>
    public static void SetBackingField(object target, string property, object value) =>
        target.GetType()
            .GetField(
                $"<{property}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(target, value);
}
