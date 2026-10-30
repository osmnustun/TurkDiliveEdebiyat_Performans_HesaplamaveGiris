using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PerfApp.Data;
using PerfApp.Models;
using PerfApp.Services;

namespace PerfApp.Pages.Olcutler;

public class DuzenleModel : PageModel
{
    private readonly AppDbContext _db;
    public DuzenleModel(AppDbContext db) => _db = db;

    public AssessmentComponent? Component { get; private set; }

    public async Task<IActionResult> OnGetAsync(int componentId)
    {
        if (!await LoadAsync(componentId)) return RedirectToPage("/Olcutler/Index");
        return Page();
    }

    private async Task<bool> LoadAsync(int componentId)
    {
        Component = await _db.Components
            .Include(c => c.Groups).ThenInclude(g => g.Criteria)
            .Include(c => c.Criteria)
            .FirstOrDefaultAsync(c => c.Id == componentId);
        if (Component is null) return false;
        Component.Groups = Component.Groups.OrderBy(g => g.Order).ToList();
        foreach (var group in Component.Groups)
            group.Criteria = group.Criteria.OrderBy(c => c.Order).ToList();
        return true;
    }

    private IActionResult Back(int componentId, string toast)
    {
        TempData["Toast"] = toast;
        return RedirectToPage(new { componentId });
    }

    public async Task<IActionResult> OnPostUpdateCriterionAsync(int id, int componentId, string? title,
        string? description, double maxPoints, int? groupId)
    {
        if (!await LoadAsync(componentId) || Component is null)
            return RedirectToPage("/Olcutler/Index");

        var criterion = Component.Criteria.FirstOrDefault(c => c.Id == id);
        if (criterion is null) return Back(componentId, "Ölçüt bulunamadı.");

        if (string.IsNullOrWhiteSpace(title))
            return Back(componentId, "Ölçüt adı boş olamaz.");
        if (maxPoints is <= 0 or > 200)
            return Back(componentId, "Maksimum puan 0'dan büyük ve 200'den küçük olmalı.");

        criterion.Title = title.Trim();
        criterion.Description = description?.Trim() ?? "";
        criterion.MaxPoints = maxPoints;

        if (groupId is not null && Component.Groups.Any(g => g.Id == groupId))
            criterion.GroupId = groupId;

        await _db.SaveChangesAsync();
        return Back(componentId, "Ölçüt güncellendi.");
    }

    public async Task<IActionResult> OnPostDeleteCriterionAsync(int id, int componentId)
    {
        if (!await LoadAsync(componentId) || Component is null)
            return RedirectToPage("/Olcutler/Index");

        var criterion = Component.Criteria.FirstOrDefault(c => c.Id == id);
        if (criterion is null) return Back(componentId, "Ölçüt bulunamadı.");

        var groupId = criterion.GroupId;
        _db.Criteria.Remove(criterion);
        await _db.SaveChangesAsync();

        if (groupId is not null)
        {
            var group = await _db.Groups.Include(g => g.Criteria).FirstOrDefaultAsync(g => g.Id == groupId);
            if (group is not null && group.Criteria.Count == 0)
            {
                _db.Groups.Remove(group);
                await _db.SaveChangesAsync();
            }
        }

        return Back(componentId, "Ölçüt silindi.");
    }

    public async Task<IActionResult> OnPostAddCriterionAsync(int componentId, string? title,
        string? description, double maxPoints, int? groupId)
    {
        if (!await LoadAsync(componentId) || Component is null)
            return RedirectToPage("/Olcutler/Index");

        if (string.IsNullOrWhiteSpace(title))
            return Back(componentId, "Ölçüt adı boş olamaz.");
        if (maxPoints is <= 0 or > 200)
            return Back(componentId, "Maksimum puan 0'dan büyük ve 200'den küçük olmalı.");

        var order = Component.Criteria.Count == 0 ? 1 : Component.Criteria.Max(c => c.Order) + 1;
        var criterion = new RubricCriterion
        {
            ComponentId = componentId,
            Order = order,
            Title = title.Trim(),
            Description = description?.Trim() ?? "",
            MaxPoints = maxPoints
        };

        if (groupId is not null && Component.Groups.Any(g => g.Id == groupId))
            criterion.GroupId = groupId;
        else
            criterion.GroupId = Component.Groups.OrderBy(g => g.Order).FirstOrDefault()?.Id;

        _db.Criteria.Add(criterion);
        await _db.SaveChangesAsync();
        return Back(componentId, "Yeni ölçüt eklendi.");
    }

    public async Task<IActionResult> OnPostRenameGroupAsync(int groupId, int componentId, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Back(componentId, "Grup adı boş olamaz.");

        var group = await _db.Groups.FindAsync(groupId);
        if (group is null) return Back(componentId, "Grup bulunamadı.");

        group.Name = name.Trim();
        await _db.SaveChangesAsync();
        return Back(componentId, "Grup adı güncellendi.");
    }

    public async Task<IActionResult> OnPostRegroupAsync(int componentId)
    {
        var component = await _db.Components
            .Include(c => c.Groups)
            .Include(c => c.Criteria)
            .FirstOrDefaultAsync(c => c.Id == componentId);
        if (component is null) return RedirectToPage("/Olcutler/Index");

        if (component.Criteria.Count == 0)
            return Back(componentId, "Gruplanacak ölçüt yok.");

        await RubricService.RegroupAsync(_db, component);
        return Back(componentId, "Gruplar yeniden oluşturuldu; bu bileşenin grup puanları sıfırlandı.");
    }
}
