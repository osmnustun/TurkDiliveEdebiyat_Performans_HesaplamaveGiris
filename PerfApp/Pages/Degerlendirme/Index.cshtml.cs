using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PerfApp.Data;
using PerfApp.Models;
using PerfApp.Services;

namespace PerfApp.Pages.Degerlendirme;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public sealed record ComponentVm(AssessmentComponent Component, int Scored, int Total, int? FirstUnscoredId, int ClassId, string Sort)
    {
        public int Percent => Total == 0 ? 0 : (int)Math.Round(Scored * 100.0 / Total);
    }

    public List<SchoolClass> Classes { get; private set; } = new();
    public SchoolClass? SelectedClass { get; private set; }
    public int SelectedClassId { get; private set; }
    public string Sort { get; private set; } = StudentOrdering.ByNumber;
    public List<ComponentVm> P1 { get; } = new();
    public List<ComponentVm> P2 { get; } = new();

    public async Task OnGetAsync(int? classId, string? sort)
    {
        Classes = await _db.SchoolClasses.Include(c => c.Students).OrderBy(c => c.Name).ToListAsync();
        SelectedClassId = classId ?? Classes.FirstOrDefault()?.Id ?? 0;
        SelectedClass = Classes.FirstOrDefault(c => c.Id == SelectedClassId);
        Sort = StudentOrdering.Normalize(sort);
        if (SelectedClass is null) return;

        var components = await _db.Components
            .Include(c => c.Groups)
            .Include(c => c.Criteria)
            .Where(c => c.Grade == SelectedClass.Grade && c.Term == 1)
            .OrderBy(c => c.Order)
            .ToListAsync();

        var studentIds = SelectedClass.Students.Select(s => s.Id).ToList();
        var groupIds = components.SelectMany(c => c.Groups).Select(g => g.Id).ToList();

        var rows = await _db.GroupScores
            .Where(s => s.Term == 1 && studentIds.Contains(s.StudentId) && groupIds.Contains(s.RubricGroupId))
            .Select(s => new { s.StudentId, s.RubricGroupId })
            .ToListAsync();

        var byStudent = rows.GroupBy(r => r.StudentId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.RubricGroupId).ToHashSet());

        var students = StudentOrdering.Sort(SelectedClass.Students, Sort);

        foreach (var component in components)
        {
            var ids = component.Groups.Select(g => g.Id).ToList();
            var scored = 0;
            int? firstUnscored = null;

            foreach (var student in students)
            {
                var hasAll = ids.Count > 0 && byStudent.TryGetValue(student.Id, out var set) && ids.All(set.Contains);
                if (hasAll) scored++;
                else firstUnscored ??= student.Id;
            }

            var vm = new ComponentVm(component, scored, students.Count, firstUnscored, SelectedClassId, Sort);
            if (component.Kind == "P1") P1.Add(vm); else P2.Add(vm);
        }
    }

    public async Task<IActionResult> OnPostClearAllScoresAsync(int? classId, string? sort)
    {
        var count = await _db.GroupScores.CountAsync();
        if (count > 0)
        {
            _db.GroupScores.RemoveRange(_db.GroupScores);
            await _db.SaveChangesAsync();
        }

        TempData["Toast"] = count == 0
            ? "Silinecek not kaydı bulunamadı."
            : $"Test için girilen {count} not kaydı silindi.";

        return RedirectToPage(new { classId, sort });
    }
}
