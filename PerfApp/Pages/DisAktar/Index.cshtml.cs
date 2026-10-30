using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PerfApp.Data;
using PerfApp.Models;
using PerfApp.Services;

namespace PerfApp.Pages.DisAktar;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public List<SchoolClass> Classes { get; private set; } = new();
    public SchoolClass? SelectedClass { get; private set; }
    public int SelectedClassId { get; private set; }
    public string Sort { get; private set; } = StudentOrdering.ByNumber;
    public int TotalStudents { get; private set; }
    public int CompletedStudents { get; private set; }

    public async Task OnGetAsync(int? classId, string? sort) => await LoadAsync(classId, sort);

    private async Task LoadAsync(int? classId, string? sort)
    {
        Classes = await _db.SchoolClasses.Include(c => c.Students).OrderBy(c => c.Name).ToListAsync();
        SelectedClassId = classId ?? Classes.FirstOrDefault()?.Id ?? 0;
        SelectedClass = Classes.FirstOrDefault(c => c.Id == SelectedClassId);
        Sort = StudentOrdering.Normalize(sort);
        if (SelectedClass is null) return;

        TotalStudents = SelectedClass.Students.Count;

        var groupIds = await _db.Groups
            .Where(g => g.Component!.Grade == SelectedClass.Grade && g.Component.Term == 1)
            .Select(g => g.Id)
            .ToListAsync();

        var studentIds = SelectedClass.Students.Select(s => s.Id).ToList();
        var rows = await _db.GroupScores
            .Where(s => s.Term == 1 && studentIds.Contains(s.StudentId) && groupIds.Contains(s.RubricGroupId))
            .Select(s => new { s.StudentId, s.RubricGroupId })
            .ToListAsync();

        CompletedStudents = rows
            .GroupBy(r => r.StudentId)
            .Count(g => groupIds.Count > 0 && groupIds.All(id => g.Any(x => x.RubricGroupId == id)));
    }

    public async Task<IActionResult> OnGetExcelAsync(int classId, string? sort)
    {
        var export = await ExportService.BuildAsync(_db, classId, sort);
        if (export is null) return NotFound();

        var bytes = ExportService.BuildExcel(export);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"performans-{Slug(export.Class.Name)}.xlsx");
    }

    public async Task<IActionResult> OnGetPdfAsync(int classId, string? sort)
    {
        var export = await ExportService.BuildAsync(_db, classId, sort);
        if (export is null) return NotFound();

        var bytes = ExportService.BuildPdf(export);
        return File(bytes, "application/pdf", $"performans-{Slug(export.Class.Name)}.pdf");
    }

    private static string Slug(string name) =>
        name.Replace('/', '-').Replace(' ', '-').ToLowerInvariant();
}
