using PerfApp.Models;

namespace PerfApp.Services;

/// <summary>
/// Uzun ölçüt listelerini 3-5 ana gruba böler. Gruplar, ölçüt sırası korunarak
/// dengeli (mümkün olduğunca eşit sayıda ölçüt içeren) biçimde oluşturulur.
/// </summary>
public static class RubricGrouper
{
    private static readonly Dictionary<string, string[]> NamePools = new()
    {
        ["K1"] = new[] { "Hazırlık ve Planlama", "İçerik ve Anlam", "Dil ve Anlatım", "Sunum ve Beden Dili", "Etkileşim ve Zaman" },
        ["K2"] = new[] { "Konuya Hâkimiyet", "İçerik ve Anlam", "Dil ve Anlatım", "Sunum ve Beden Dili", "Etkileşim ve Zaman" },
        ["Y1"] = new[] { "Planlama ve Yapı", "İçerik ve Tema", "Dil ve Anlatım", "Anlatım ve Biçim", "Yazım ve Sunum" },
        ["Y2"] = new[] { "Planlama ve Yapı", "İçerik ve Tema", "Dil ve Anlatım", "Anlatım ve Biçim", "Yazım ve Sunum" },
        ["B1"] = new[] { "Okuma Öncesi", "Anlama ve Özetleme", "Çözümleme", "Eleştirel Okuma", "İlişkilendirme" },
        ["B2"] = new[] { "Okuma Öncesi", "Anlama ve Özetleme", "Çözümleme", "Eleştirel Okuma", "İlişkilendirme" },
        ["D"] = new[] { "Hazırlık ve Sorumluluk", "Katılım ve İletişim", "İş Birliği ve Görev", "Genel Tutum", "Gelişim" }
    };

    public static int CountFor(int criteriaCount)
    {
        if (criteriaCount <= 0) return 0;
        var wanted = (int)Math.Ceiling(criteriaCount / 3.0);
        return Math.Clamp(wanted, Math.Min(3, criteriaCount), Math.Min(5, criteriaCount));
    }

    public static List<string> NamePool(string code) =>
        NamePools.TryGetValue(code, out var pool) ? pool.ToList() : new List<string> { "Grup 1", "Grup 2", "Grup 3", "Grup 4", "Grup 5" };

    public sealed record GroupPlan(string Name, List<int> CriterionIndexes);

    /// <summary>
    /// Ölçüt sayısına göre grup adlarını ve hangi ölçütlerin (sıra indeksi) hangi grupta
    /// olacağını döndürür.
    /// </summary>
    public static List<GroupPlan> Plan(string code, int criteriaCount)
    {
        if (criteriaCount <= 0) return new List<GroupPlan>();

        var groupCount = CountFor(criteriaCount);
        var pool = NamePool(code);
        var baseSize = criteriaCount / groupCount;
        var remainder = criteriaCount % groupCount;
        var plans = new List<GroupPlan>();
        var index = 0;

        for (var g = 0; g < groupCount; g++)
        {
            var size = baseSize + (g < remainder ? 1 : 0);
            var indexes = Enumerable.Range(index, size).ToList();
            index += size;
            plans.Add(new GroupPlan(pool[g % pool.Count], indexes));
        }

        return plans;
    }
}
