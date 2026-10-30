using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PerfApp.Data;
using PerfApp.Models;
using PerfApp.Services;
using System.IO.Compression;

namespace PerfApp.Pages.DisAktar;

public class IndexModel : PageModel
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public List<SchoolClass> Classes { get; private set; } = new();
    public SchoolClass? SelectedClass { get; private set; }
    public int SelectedClassId { get; private set; }
    public string Sort { get; private set; } = StudentOrdering.ByNumber;
    public int TotalStudents { get; private set; }
    public int CompletedStudents { get; private set; }
    public bool UsingTemplates { get; private set; }
    public int TemplateCount { get; private set; }

    public async Task OnGetAsync(int? classId, string? sort) => await LoadAsync(classId, sort);

    private async Task LoadAsync(int? classId, string? sort)
    {
        Classes = await _db.SchoolClasses.Include(c => c.Students).OrderBy(c => c.Name).ToListAsync();
        SelectedClassId = classId ?? Classes.FirstOrDefault()?.Id ?? 0;
        SelectedClass = Classes.FirstOrDefault(c => c.Id == SelectedClassId);
        Sort = StudentOrdering.Normalize(sort);
        if (SelectedClass is null) return;

        TotalStudents = SelectedClass.Students.Count;
        TemplateCount = TemplateExportService.AvailableFor(SelectedClass.Grade).Count;
        UsingTemplates = TemplateCount > 0;

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

        var templates = TemplateExportService.AvailableFor(export.Class.Grade);

        // Tek şablon: doğrudan birebir .xlsx
        if (templates.Count == 1 && TemplateExportService.TryBuildFilled(templates[0], export, out var single))
            return File(single, XlsxContentType, $"performans-{Slug(export.Class.Name)}.xlsx");

        // Birden çok şablon (örn. 10. sınıf): ZIP
        if (templates.Count > 1)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var t in templates)
                {
                    if (!TemplateExportService.TryBuildFilled(t, export, out var bytes)) continue;
                    await AddEntryAsync(zip, TemplateExportService.OutputFileName(export.Class.Name, t), bytes);
                }
            }
            return File(ms.ToArray(), "application/zip", $"performans-{Slug(export.Class.Name)}.zip");
        }

        // Şablon yoksa eski üretilen düzen
        var fallback = ExportService.BuildExcel(export);
        return File(fallback, XlsxContentType, $"performans-{Slug(export.Class.Name)}.xlsx");
    }

    public async Task<IActionResult> OnGetZipAsync(string? sort)
    {
        var classes = await _db.SchoolClasses.Include(c => c.Students).OrderBy(c => c.Name).ToListAsync();

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var cls in classes)
            {
                var export = await ExportService.BuildAsync(_db, cls.Id, sort);
                if (export is null) continue;

                var folder = Slug(cls.Name);
                var templates = TemplateExportService.AvailableFor(cls.Grade);

                if (templates.Count == 0)
                {
                    var bytes = ExportService.BuildExcel(export);
                    await AddEntryAsync(zip, $"{folder}/performans-{folder}.xlsx", bytes);
                    continue;
                }

                foreach (var t in templates)
                {
                    if (!TemplateExportService.TryBuildFilled(t, export, out var bytes)) continue;
                    await AddEntryAsync(zip, $"{folder}/{TemplateExportService.OutputFileName(cls.Name, t)}", bytes);
                }
            }
        }

        return File(ms.ToArray(), "application/zip", "performans-tum-siniflar.zip");
    }

    private static async Task AddEntryAsync(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name);
        await using var stream = entry.Open();
        await stream.WriteAsync(bytes);
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
