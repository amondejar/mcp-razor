using System.Globalization;

namespace SalesOps;

/// <summary>Euro amounts in the culture the app runs with (the host's locale).</summary>
public static class Money
{
    public static string Format(decimal amount, CultureInfo culture, int decimals = 0) =>
        amount.ToString("C" + decimals, Euro(culture));

    /// <summary>Short amounts for chart axes: 120k €, 1.2M €.</summary>
    public static string Compact(decimal amount, CultureInfo culture)
    {
        var format = Euro(culture);
        var (value, suffix) = Math.Abs(amount) >= 1_000_000 ? (amount / 1_000_000, "M") : Math.Abs(amount) >= 1_000 ? (amount / 1_000, "k") : (amount, "");
        var number = value.ToString(value >= 100 || suffix.Length == 0 ? "N0" : "N1", format);
        return format.CurrencyPositivePattern is 0 or 2 ? $"{format.CurrencySymbol}{number}{suffix}" : $"{number}{suffix} {format.CurrencySymbol}";
    }

    public static string Percent(decimal value, CultureInfo culture, bool signed = true) =>
        (signed && value > 0 ? "+" : "") + value.ToString("N1", culture) + " %";

    private static NumberFormatInfo Euro(CultureInfo culture)
    {
        var format = (NumberFormatInfo)culture.NumberFormat.Clone();
        format.CurrencySymbol = "€";
        return format;
    }
}
