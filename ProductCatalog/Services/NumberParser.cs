using System.Globalization;

namespace ProductCatalog.Services;

/// <summary>Приема и „49,90“, и „49.90“ – потребителят не трябва да мисли за десетичния знак.</summary>
public static class NumberParser
{
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = text.Trim()
            .Replace(" ", string.Empty)
            .Replace(" ", string.Empty)
            .Replace(',', '.');

        return decimal.TryParse(
            normalized,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out value);
    }

    public static string Format(decimal value, int decimals) =>
        value.ToString("F" + decimals, App.Bulgarian);
}
