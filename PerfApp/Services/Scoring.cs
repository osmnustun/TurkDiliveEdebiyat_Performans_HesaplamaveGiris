using PerfApp.Models;

namespace PerfApp.Services;

public sealed record CriterionResult(int Id, string Title, double MaxPoints, double Earned);
public sealed record GroupResult(int Id, string Name, int Order, double MaxPoints, int? Score, double Earned, double SharePercent, List<CriterionResult> Criteria);
public sealed record ComponentResult(AssessmentComponent Component, List<GroupResult> Groups, bool Complete, int? Score);

/// <summary>
/// Excel'deki hesaplama mantığının birebir karşılığı.
/// - Grup puanı (0-100), gruptaki her ölçütün maksimum puanı oranında dağıtılır.
/// - Bileşen puanı = round(toplam / bileşen maksimumu * 100).
/// - 1. performans: 9. sınıf dört bileşenin %25'inin ayrı ayrı yuvarlanmış toplamı,
///   10. sınıf dört bileşenin ortalaması, 11. sınıf %25'lerin toplamı.
/// - 2. performans: B1*0,33 + B2*0,33 + D*0,34.
/// </summary>
public static class Scoring
{
    public static ComponentResult Component(AssessmentComponent component, IReadOnlyDictionary<int, int> scores)
    {
        var groups = new List<GroupResult>();
        var componentMax = component.Criteria.Sum(c => c.MaxPoints);

        foreach (var group in component.Groups.OrderBy(g => g.Order))
        {
            var groupMax = group.Criteria.Sum(c => c.MaxPoints);
            int? score = scores.TryGetValue(group.Id, out var rawScore)
                ? Math.Clamp(rawScore, 0, 100)
                : null;

            var criteria = group.Criteria
                .OrderBy(c => c.Order)
                .Select(c => new CriterionResult(c.Id, c.Title, c.MaxPoints,
                    // Ölçüt puanı tam sayıya yuvarlanır (2.4 → 2, 2.5 → 3): yarım ve üstü yukarı.
                    score is null ? 0 : Math.Round(score.Value / 100.0 * c.MaxPoints, MidpointRounding.AwayFromZero)))
                .ToList();

            var earned = criteria.Sum(c => c.Earned);
            var share = componentMax > 0 ? groupMax / componentMax * 100 : 0;
            groups.Add(new GroupResult(group.Id, group.Name, group.Order, groupMax, score, earned, share, criteria));
        }

        var complete = groups.Count > 0 && groups.All(g => g.Score.HasValue);
        var totalEarned = groups.Sum(g => g.Earned);
        int? componentScore = complete && componentMax > 0
            ? (int)Math.Round(totalEarned / componentMax * 100, MidpointRounding.AwayFromZero)
            : null;

        return new ComponentResult(component, groups, complete, componentScore);
    }

    public static double? P1(int grade, IReadOnlyList<ComponentResult> results)
    {
        if (results.Count < 4 || results.Any(r => r.Score is null)) return null;
        var values = results.Take(4).Select(r => (double)r.Score!.Value).ToArray();

        return grade switch
        {
            9 => values.Sum(v => Math.Round(v * 0.25, MidpointRounding.AwayFromZero)),
            10 => Math.Round(values.Average(), 2),
            _ => Math.Round(values.Sum(v => v * 0.25), 2)
        };
    }

    public static double? P2(ComponentResult? b1, ComponentResult? b2, ComponentResult? d)
    {
        if (b1?.Score is null || b2?.Score is null || d?.Score is null) return null;
        return Math.Round(b1.Score.Value * 0.33 + b2.Score.Value * 0.33 + d.Score.Value * 0.34, 2);
    }
}
