using System.Globalization;

namespace PerfApp.Services;

public static class Ui
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public static string N(double? value) => value is null ? "—" : value.Value.ToString("0.##", Tr);

    public static string N(double value) => value.ToString("0.##", Tr);

    public static string Pct(double value) => value.ToString("0.#", Tr) + "%";

    public static string Initials(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        if (parts.Length == 1) return parts[0][..1].ToUpper(Tr);
        return (parts[0][..1] + parts[^1][..1]).ToUpper(Tr);
    }

    public static string ShortName(string fullName, int max = 22)
    {
        if (fullName.Length <= max) return fullName;
        return fullName[..(max - 1)].TrimEnd() + "…";
    }
}

public static class UiInv
{
    public static string Inv(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
