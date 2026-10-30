using System.Globalization;
using PerfApp.Models;

namespace PerfApp.Services;

/// <summary>
/// Öğrenci listelerinin sıralama biçimini yönetir.
/// Varsayılan: okul/sınıf numarası. Alternatif: ad soyad (Türkçe alfabetik).
/// </summary>
public static class StudentOrdering
{
    public const string ByNumber = "no";
    public const string ByName = "ad";

    private static readonly StringComparer TrComparer =
        StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true);

    /// <summary>Bilinmeyen/boş değeri varsayılana (numara) çevirir.</summary>
    public static string Normalize(string? sort) =>
        string.Equals(sort, ByName, StringComparison.OrdinalIgnoreCase) ? ByName : ByNumber;

    public static bool IsByName(string? sort) => Normalize(sort) == ByName;

    public static IEnumerable<Student> Apply(IEnumerable<Student> students, string? sort)
    {
        return IsByName(sort)
            ? students.OrderBy(s => s.FullName, TrComparer).ThenBy(s => s.Number ?? int.MaxValue)
            : students.OrderBy(s => s.Number ?? int.MaxValue).ThenBy(s => s.FullName, TrComparer);
    }

    public static List<Student> Sort(IEnumerable<Student> students, string? sort) =>
        Apply(students, sort).ToList();
}
