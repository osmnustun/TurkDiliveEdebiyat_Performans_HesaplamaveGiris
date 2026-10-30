using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PerfApp.Data;
using PerfApp.Models;
using PerfApp.Services;

namespace PerfApp.Pages.Degerlendirme;

public class GirisModel : PageModel
{
    public const string FilterAll = "all";
    public const string FilterMissing = "eksik";

    private readonly AppDbContext _db;
    public GirisModel(AppDbContext db) => _db = db;

    public sealed class ScoreInput
    {
        public int GroupId { get; set; }
        public int Score { get; set; }
    }

    [BindProperty]
    public List<ScoreInput> Inputs { get; set; } = new();

    public SchoolClass? Class { get; private set; }
    public AssessmentComponent? Component { get; private set; }
    public List<Student> Students { get; private set; } = new();
    public Student? Current { get; private set; }
    public int Index { get; private set; }
    public int? PrevId { get; private set; }
    public int? NextId { get; private set; }
    public ComponentResult? Result { get; private set; }
    public HashSet<int> CompletedStudentIds { get; private set; } = new();
    public double TotalMaxPoints { get; private set; }
    public int ClassId { get; private set; }
    public int ComponentId { get; private set; }
    public int StudentId { get; private set; }
    public string Sort { get; private set; } = StudentOrdering.ByNumber;
    public string Filter { get; private set; } = FilterAll;
    public int TotalStudentCount { get; private set; }
    public int MissingCount { get; private set; }
    public bool NoMissing { get; private set; }
    public int Term => 1;

    public async Task<IActionResult> OnGetAsync(int classId, int componentId, int? studentId, string? sort, string? filter)
    {
        if (!await LoadAsync(classId, componentId, studentId, sort, filter))
        {
            TempData["Toast"] = "Sınıf veya performans bileşeni bulunamadı.";
            return RedirectToPage("/Degerlendirme/Index", new { classId, sort });
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int classId, int componentId, int studentId, string? action, string? sort, string? filter)
    {
        var submitted = (Inputs ?? new List<ScoreInput>())
            .GroupBy(i => i.GroupId)
            .ToDictionary(g => g.Key, g => Math.Clamp(g.First().Score, 0, 100));

        if (!await LoadAsync(classId, componentId, studentId, sort, filter) || Component is null || Current is null)
        {
            TempData["Toast"] = "Sınıf veya performans bileşeni bulunamadı.";
            return RedirectToPage("/Degerlendirme/Index", new { classId, sort });
        }

        var groupIds = Component.Groups.Select(g => g.Id).ToList();
        var existing = await _db.GroupScores
            .Where(s => s.StudentId == Current.Id && s.Term == Term && groupIds.Contains(s.RubricGroupId))
            .ToListAsync();

        foreach (var group in Component.Groups)
        {
            var value = submitted.GetValueOrDefault(group.Id, 0);
            var row = existing.FirstOrDefault(s => s.RubricGroupId == group.Id);
            if (row is null)
            {
                _db.GroupScores.Add(new GroupScore
                {
                    StudentId = Current.Id,
                    RubricGroupId = group.Id,
                    Term = Term,
                    Score = value,
                    UpdatedAt = DateTime.Now
                });
            }
            else
            {
                row.Score = value;
                row.UpdatedAt = DateTime.Now;
            }
        }

        await _db.SaveChangesAsync();
        TempData["Toast"] = Current.FullName + " için puanlar kaydedildi.";

        if (action == "next")
        {
            var next = Index + 1 < Students.Count ? Students[Index + 1].Id : (int?)null;
            if (next is null)
            {
                TempData["Toast"] = Filter == FilterMissing
                    ? "Notu girilmemiş son öğrenci kaydedildi."
                    : "Sınıfın son öğrencisi kaydedildi.";
                return RedirectToPage(new { classId, componentId, studentId = Current.Id, sort, filter });
            }
            return RedirectToPage(new { classId, componentId, studentId = next, sort, filter });
        }

        return RedirectToPage(new { classId, componentId, studentId = Current.Id, sort, filter });
    }

    private static string NormalizeFilter(string? filter) =>
        string.Equals(filter, FilterMissing, StringComparison.OrdinalIgnoreCase) ? FilterMissing : FilterAll;

    private async Task<bool> LoadAsync(int classId, int componentId, int? studentId, string? sort, string? filter)
    {
        ClassId = classId;
        ComponentId = componentId;
        Sort = StudentOrdering.Normalize(sort);
        Filter = NormalizeFilter(filter);

        Class = await _db.SchoolClasses
            .Include(c => c.Students)
            .FirstOrDefaultAsync(c => c.Id == classId);
        if (Class is null) return false;

        Component = await _db.Components
            .Include(c => c.Groups)
            .ThenInclude(g => g.Criteria)
            .Include(c => c.Criteria)
            .FirstOrDefaultAsync(c => c.Id == componentId && c.Term == Term);
        if (Component is null || Component.Grade != Class.Grade) return false;

        var ordered = StudentOrdering.Sort(Class.Students, Sort);
        if (ordered.Count == 0) return false;

        TotalStudentCount = ordered.Count;
        TotalMaxPoints = Component.Criteria.Sum(c => c.MaxPoints);

        var groupIds = Component.Groups.Select(g => g.Id).ToList();
        var studentIds = ordered.Select(s => s.Id).ToList();
        var rows = await _db.GroupScores
            .Where(s => s.Term == Term && studentIds.Contains(s.StudentId) && groupIds.Contains(s.RubricGroupId))
            .Select(s => new { s.StudentId, s.RubricGroupId })
            .ToListAsync();

        CompletedStudentIds = rows
            .GroupBy(r => r.StudentId)
            .Where(g => groupIds.Count > 0 && groupIds.All(id => g.Any(x => x.RubricGroupId == id)))
            .Select(g => g.Key)
            .ToHashSet();

        MissingCount = ordered.Count(s => !CompletedStudentIds.Contains(s.Id));

        Students = Filter == FilterMissing
            ? ordered.Where(s => !CompletedStudentIds.Contains(s.Id)).ToList()
            : ordered;

        // Notu eksik öğrenci kalmadıysa filtreyi boşa düşürmemek için tüm listeye dön.
        NoMissing = Students.Count == 0;
        if (NoMissing) Students = ordered;

        Current = studentId is null
            ? Students[0]
            : Students.FirstOrDefault(s => s.Id == studentId) ?? Students[0];
        StudentId = Current.Id;
        Index = Students.IndexOf(Current);

        PrevId = Index > 0 ? Students[Index - 1].Id : null;
        NextId = Index + 1 < Students.Count ? Students[Index + 1].Id : null;

        var rawScores = await _db.GroupScores
            .Where(s => s.StudentId == Current.Id && s.Term == Term && groupIds.Contains(s.RubricGroupId))
            .ToDictionaryAsync(s => s.RubricGroupId, s => s.Score);

        Result = Scoring.Component(Component, rawScores);
        Inputs = Result.Groups
            .Select(g => new ScoreInput { GroupId = g.Id, Score = g.Score ?? 0 })
            .ToList();

        return true;
    }
}
