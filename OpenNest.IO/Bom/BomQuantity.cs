using System.Globalization;

namespace OpenNest.IO.Bom;

/// <summary>Reads a part quantity the operator typed in the import dialog.</summary>
public static class BomQuantity
{
    /// <summary>
    /// Accepts a whole number of at least 1, with optional surrounding
    /// spaces. Rejects blanks, signs, decimals, exponents, group
    /// separators and values above <see cref="int.MaxValue"/>.
    /// </summary>
    public static bool TryParse(string text, out int quantity)
    {
        if (
            int.TryParse(
                text,
                NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
                CultureInfo.CurrentCulture,
                out quantity
            )
            && quantity >= 1
        )
            return true;

        quantity = 0;
        return false;
    }
}
