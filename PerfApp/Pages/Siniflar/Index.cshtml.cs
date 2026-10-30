using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PerfApp.Data;
using PerfApp.Models;
using PerfApp.Services;

namespace PerfApp.Pages.Siniflar;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public List<SchoolClass> Classes { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Classes = await _db.SchoolClasses
            .Include(c => c.Students)
            .OrderBy(c => c.Name)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostAddAsync(string? name)
    {
        var normalized = StudentImportService.NormalizeClassName(name);
        if (normalized is null)
        {
            TempData["Toast"] = "Sınıf adı 9/C biçiminde olmalı.";
            return RedirectToPage();
        }

        if (await _db.SchoolClasses.AnyAsync(c => c.Name == normalized))
        {
            TempData["Toast"] = normalized + " sınıfı zaten var.";
            return RedirectToPage();
        }

        var grade = int.Parse(normalized.Split('/')[0]);
        _db.SchoolClasses.Add(new SchoolClass { Name = normalized, Grade = grade });
        await _db.SaveChangesAsync();
        TempData["Toast"] = normalized + " sınıfı eklendi.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRenameAsync(int id, string? name)
    {
        var cls = await _db.SchoolClasses.FindAsync(id);
        if (cls is null) return RedirectToPage();

        var normalized = StudentImportService.NormalizeClassName(name);
        if (normalized is null)
        {
            TempData["Toast"] = "Sınıf adı 9/C biçiminde olmalı.";
            return RedirectToPage();
        }

        if (await _db.SchoolClasses.AnyAsync(c => c.Id != id && c.Name == normalized))
        {
            TempData["Toast"] = normalized + " sınıfı zaten var.";
            return RedirectToPage();
        }

        cls.Name = normalized;
        cls.Grade = int.Parse(normalized.Split('/')[0]);
        await _db.SaveChangesAsync();
        TempData["Toast"] = "Sınıf güncellendi.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var cls = await _db.SchoolClasses.Include(c => c.Students).FirstOrDefaultAsync(c => c.Id == id);
        if (cls is null) return RedirectToPage();

        _db.SchoolClasses.Remove(cls);
        await _db.SaveChangesAsync();
        TempData["Toast"] = cls.Name + " sınıfı ve öğrencileri silindi.";
        return RedirectToPage();
    }
}
