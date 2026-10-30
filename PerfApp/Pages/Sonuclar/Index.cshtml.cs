using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PerfApp.Data;
using PerfApp.Models;
using PerfApp.Services;

namespace PerfApp.Pages.Sonuclar;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public sealed record Row(Student Student, Dictionary<string, int?> Scores, double? P1, double? P2);

    public List<SchoolClass> Classes { get; private set; } = new();
    public SchoolClass? SelectedClass { get; private set; }
    public int SelectedClassId { get; private set; }
    public string Sort { get; private set; } = StudentOrdering.ByNumber;
    public List<Row> Rows { get; } = new();
    public List<string> ComponentCodes { get; } = new();
    public double? AverageP1 { get; private set; }
    public double? AverageP2 { get; private set; }
    public int Missing { get; private set; }

    public async Task OnGetAsync(int? classId, string? sort) => await LoadAsync(classId, sort);

    private async Task LoadAsync(int? classId, string? sort)
    {
        Classes = await _db.SchoolClasses.Include(c => c.Students).OrderBy(c => c.Name).ToListAsync();
        SelectedClassId = classId ?? Classes.FirstOrDefault()?.Id ?? 0;
        SelectedClass = Classes.FirstOrDefault(c => c.Id == SelectedClassId);
        Sort = StudentOrdering.Normalize(sort);
        if (SelectedClass is null) return;

        var components = await _db.Components
            .Include(c => c.Groups).ThenInclude(g => g.Criteria)
            .Include(c => c.Criteria)
            .Where(c => c.Grade == SelectedClass.Grade && c.Term == 1)
            .OrderBy(c => c.Order)
            .ToListAsync();

        ComponentCodes.AddRange(components.Select(c => c.Code));

        var studentIds = SelectedClass.Students.Select(s => s.Id).ToList();
        var scores = await _db.GroupScores
            .Where(s => s.Term == 1 && studentIds.Contains(s.StudentId))
            .Select(s => new { s.StudentId, s.RubricGroupId, s.Score })
            .ToListAsync();

        var byStudent = scores.GroupBy(s => s.StudentId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.RubricGroupId, x => x.Score));

        var students = StudentOrdering.Sort(SelectedClass.Students, Sort);

        foreach (var student in students)
        {
            var dict = byStudent.TryGetValue(student.Id, out var value) ? value : new Dictionary<int, int>();
            var results = components.Select(c => Scoring.Component(c, dict)).ToList();
            var codes = results.ToDictionary(r => r.Component.Code, r => r.Score);

            var p1Results = results.Where(r => r.Component.Kind == "P1").OrderBy(r => r.Component.Order).ToList();
            var p1 = Scoring.P1(SelectedClass.Grade, p1Results);
            var p2 = Scoring.P2(
                results.FirstOrDefault(r => r.Component.Code == "B1"),
                results.FirstOrDefault(r => r.Component.Code == "B2"),
                results.FirstOrDefault(r => r.Component.Code == "D"));

            Rows.Add(new Row(student, codes, p1, p2));
        }

        var p1Values = Rows.Where(r => r.P1.HasValue).Select(r => r.P1!.Value).ToList();
        var p2Values = Rows.Where(r => r.P2.HasValue).Select(r => r.P2!.Value).ToList();
        AverageP1 = p1Values.Count > 0 ? Math.Round(p1Values.Average(), 2) : null;
        AverageP2 = p2Values.Count > 0 ? Math.Round(p2Values.Average(), 2) : null;
        Missing = Rows.Count(r => r.P1 is null || r.P2 is null);
    }

    public async Task<IActionResult> OnGetCsvAsync(int? classId, string? sort)
    {
        await LoadAsync(classId, sort);
        if (SelectedClass is null) return NotFound();

        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        var header = new List<string> { "Sıra", "Okul No", "Adı Soyadı" };
        header.AddRange(ComponentCodes);
        header.AddRange(new[] { "1. Performans", "2. Performans" });
        sb.AppendLine(string.Join(';', header.Select(Escape)));

        var index = 1;
        foreach (var row in Rows)
        {
            var cells = new List<string>
            {
                index.ToString(CultureInfo.InvariantCulture),
                row.Student.Number?.ToString(CultureInfo.InvariantCulture) ?? "",
                row.Student.FullName
            };
            cells.AddRange(ComponentCodes.Select(code =>
                row.Scores.TryGetValue(code, out var value) && value.HasValue
                    ? value.Value.ToString(CultureInfo.InvariantCulture) : ""));
            cells.Add(row.P1?.ToString("0.##", CultureInfo.InvariantCulture) ?? "");
            cells.Add(row.P2?.ToString("0.##", CultureInfo.InvariantCulture) ?? "");
            sb.AppendLine(string.Join(';', cells.Select(Escape)));
            index++;
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv; charset=utf-8", $"performans-{SelectedClass.Name.Replace('/', '-')}.csv");
    }

    private static string Escape(string value) =>
        value.Contains(';') || value.Contains('"') || value.Contains('\n')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}
