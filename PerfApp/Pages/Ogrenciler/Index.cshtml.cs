using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PerfApp.Data;
using PerfApp.Models;
using PerfApp.Services;

namespace PerfApp.Pages.Ogrenciler;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public List<SchoolClass> Classes { get; private set; } = new();
    public SchoolClass? SelectedClass { get; private set; }
    public List<Student> Students { get; private set; } = new();
    public Student? EditStudent { get; private set; }
    public string? Query { get; private set; }
    public int SelectedClassId { get; private set; }
    public string Sort { get; private set; } = StudentOrdering.ByNumber;

    public async Task OnGetAsync(int? classId, string? q, int? editId, string? sort)
    {
        Classes = await _db.SchoolClasses.Include(c => c.Students).OrderBy(c => c.Name).ToListAsync();
        SelectedClassId = classId ?? Classes.FirstOrDefault()?.Id ?? 0;
        SelectedClass = Classes.FirstOrDefault(c => c.Id == SelectedClassId);
        Query = q;
        Sort = StudentOrdering.Normalize(sort);

        if (SelectedClass is not null)
        {
            IEnumerable<Student> list = SelectedClass.Students;
            if (!string.IsNullOrWhiteSpace(q))
            {
                var needle = q.Trim();
                list = list.Where(s => s.FullName.Contains(needle, StringComparison.InvariantCultureIgnoreCase)
                                       || (s.Number?.ToString().Contains(needle) ?? false));
            }

            Students = StudentOrdering.Sort(list, Sort);
        }

        if (editId is not null)
            EditStudent = await _db.Students.FindAsync(editId);
    }

    public async Task<IActionResult> OnPostAddAsync(int classId, int? number, string? fullName, string? sort)
    {
        fullName = fullName?.Trim();
        if (string.IsNullOrWhiteSpace(fullName))
        {
            TempData["Toast"] = "Öğrenci adı boş olamaz.";
            return RedirectToPage(new { classId, sort });
        }

        var cls = await _db.SchoolClasses.Include(c => c.Students).FirstOrDefaultAsync(c => c.Id == classId);
        if (cls is null) return RedirectToPage();

        if (cls.Students.Any(s => string.Equals(s.FullName, fullName, StringComparison.InvariantCultureIgnoreCase)))
        {
            TempData["Toast"] = fullName + " bu sınıfta zaten kayıtlı.";
            return RedirectToPage(new { classId, sort });
        }

        _db.Students.Add(new Student { SchoolClassId = classId, Number = number, FullName = fullName });
        await _db.SaveChangesAsync();
        TempData["Toast"] = fullName + " eklendi.";
        return RedirectToPage(new { classId, sort });
    }

    public async Task<IActionResult> OnPostUpdateAsync(int id, int classId, int? number, string? fullName, string? sort)
    {
        var student = await _db.Students.FindAsync(id);
        if (student is null) return RedirectToPage(new { classId, sort });

        fullName = fullName?.Trim();
        if (string.IsNullOrWhiteSpace(fullName))
        {
            TempData["Toast"] = "Öğrenci adı boş olamaz.";
            return RedirectToPage(new { classId, editId = id, sort });
        }

        var duplicate = await _db.Students.AnyAsync(s => s.Id != id && s.SchoolClassId == classId
            && s.FullName.ToLower() == fullName.ToLower());
        if (duplicate)
        {
            TempData["Toast"] = fullName + " bu sınıfta zaten kayıtlı.";
            return RedirectToPage(new { classId, editId = id, sort });
        }

        student.FullName = fullName;
        student.Number = number;
        student.SchoolClassId = classId;
        await _db.SaveChangesAsync();
        TempData["Toast"] = "Öğrenci bilgileri güncellendi.";
        return RedirectToPage(new { classId, sort });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, int classId, string? sort)
    {
        var student = await _db.Students.FindAsync(id);
        if (student is not null)
        {
            var name = student.FullName;
            _db.Students.Remove(student);
            await _db.SaveChangesAsync();
            TempData["Toast"] = name + " silindi.";
        }
        return RedirectToPage(new { classId, sort });
    }
}
