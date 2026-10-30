using Microsoft.EntityFrameworkCore;
using PerfApp.Data;
using PerfApp.Models;

namespace PerfApp.Services;

public static class RubricService
{
    /// <summary>
    /// Bileşenin gruplarını yeniden oluşturur. Eski grup puanları silinir.
    /// </summary>
    public static async Task RegroupAsync(AppDbContext db, AssessmentComponent component, CancellationToken ct = default)
    {
        if (component.Groups.Count > 0)
        {
            db.Groups.RemoveRange(component.Groups);
            await db.SaveChangesAsync(ct);
        }

        // Silme sonrası ölçütlerin grup bağlantısını temizle.
        foreach (var criterion in component.Criteria)
            criterion.GroupId = null;

        var criteria = component.Criteria.OrderBy(c => c.Order).ThenBy(c => c.Id).ToList();
        var plan = RubricGrouper.Plan(component.Code, criteria.Count);
        var groups = new List<RubricGroup>();

        foreach (var item in plan.Select((value, i) => (value, i)))
        {
            var group = new RubricGroup
            {
                ComponentId = component.Id,
                Order = item.i + 1,
                Name = item.value.Name
            };
            db.Groups.Add(group);
            groups.Add(group);
        }

        await db.SaveChangesAsync(ct);

        for (var i = 0; i < plan.Count; i++)
        {
            foreach (var criterionIndex in plan[i].CriterionIndexes)
                criteria[criterionIndex].GroupId = groups[i].Id;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Yeni eklenen ölçütü, sıralamaya göre uygun gruba bağlar.
    /// </summary>
    public static int? SuggestGroupId(AssessmentComponent component, int order)
    {
        var groups = component.Groups.OrderBy(g => g.Order).ToList();
        if (groups.Count == 0) return null;

        var plan = RubricGrouper.Plan(component.Code, Math.Max(component.Criteria.Count, order));
        var index = Math.Clamp(order - 1, 0, plan.Count - 1);
        return groups[Math.Min(index, groups.Count - 1)].Id;
    }
}
