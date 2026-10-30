using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PerfApp.Data;
using PerfApp.Models;
using PerfApp.Services;

namespace PerfApp.Pages.IceAktar;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly StudentImportService _importer;
    private readonly IWebHostEnvironment _env;

    public IndexModel(AppDbContext db, StudentImportService importer, IWebHostEnvironment env)
    {
        _db = db;
        _importer = importer;
        _env = env;
    }

    public List<SchoolClass> Classes { get; private set; } = new();
    public ParsedRoster? Preview { get; private set; }
    public string? Token { get; private set; }
    public string? PreviewFile { get; private set; }
    public int FolderFileCount { get; private set; }

    [BindProperty] public string? ClassName { get; set; }
    [BindProperty] public int? Grade { get; set; }
    [BindProperty] public string Mode { get; set; } = "replace";

    private string UploadFolder => Path.Combine(_env.ContentRootPath, "App_Data", "uploads");

    public async Task OnGetAsync(int? classId)
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Classes = await _db.SchoolClasses.OrderBy(c => c.Name).ToListAsync();
        var parent = Directory.GetParent(_env.ContentRootPath)?.FullName;
        if (parent is not null && Directory.Exists(parent))
            FolderFileCount = Directory.GetFiles(parent, "*.xlsx").Count(f => !Path.GetFileName(f).StartsWith("~$"));
    }

    public async Task<IActionResult> OnPostPreviewAsync(IFormFile? file)
    {
        await LoadAsync();

        if (file is null || file.Length == 0)
        {
            TempData["Toast"] = "Lütfen bir .xlsx dosyası seçin.";
            return RedirectToPage();
        }

        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Toast"] = "Yalnızca .xlsx uzantılı dosyalar desteklenir.";
            return RedirectToPage();
        }

        if (file.Length > 20 * 1024 * 1024)
        {
            TempData["Toast"] = "Dosya 20 MB'tan büyük olamaz.";
            return RedirectToPage();
        }

        Directory.CreateDirectory(UploadFolder);
        var token = Guid.NewGuid().ToString("N");
        var path = Path.Combine(UploadFolder, token + ".xlsx");

        await using (var stream = System.IO.File.Create(path))
            await file.CopyToAsync(stream);

        try
        {
            Preview = _importer.ParseFile(path);
            Token = token;
            PreviewFile = file.FileName;
            ClassName = Preview.ClassName is not null && !Preview.ClassName.EndsWith("/?") ? Preview.ClassName : null;
            Grade = Preview.Grade;

            if (Preview.Students.Count == 0)
            {
                TempData["Toast"] = "Bu dosyada öğrenci listesi bulunamadı.";
                System.IO.File.Delete(path);
                return RedirectToPage();
            }

            return Page();
        }
        catch (Exception ex)
        {
            System.IO.File.Delete(path);
            TempData["Toast"] = "Dosya okunamadı: " + ex.Message;
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostConfirmAsync(string? token, string? className, int? grade, string? mode)
    {
        await LoadAsync();

        if (!Guid.TryParse(token, out _))
        {
            TempData["Toast"] = "Geçersiz dosya oturumu. Lütfen yeniden yükleyin.";
            return RedirectToPage();
        }

        var path = Path.Combine(UploadFolder, token + ".xlsx");
        if (!System.IO.File.Exists(path))
        {
            TempData["Toast"] = "Yüklenen dosya bulunamadı. Lütfen yeniden yükleyin.";
            return RedirectToPage();
        }

        var normalized = StudentImportService.NormalizeClassName(className);
        if (normalized is null)
        {
            TempData["Toast"] = "Sınıf adı 9/C biçiminde olmalı.";
            return await RepreviewAsync(path, token, "Sınıf adı 9/C biçiminde olmalı.");
        }

        var parsedGrade = int.Parse(normalized.Split('/')[0]);
        if (grade is not null && grade != parsedGrade)
        {
            TempData["Toast"] = "Sınıf adı ile sınıf düzeyi uyuşmuyor.";
            return await RepreviewAsync(path, token, "Sınıf adı ile sınıf düzeyi uyuşmuyor.");
        }

        try
        {
            var roster = _importer.ParseFile(path);
            var summary = await _importer.ImportAsync(_db, roster, normalized, parsedGrade,
                replace: string.Equals(mode, "replace", StringComparison.OrdinalIgnoreCase));

            System.IO.File.Delete(path);

            var cls = await _db.SchoolClasses.FirstAsync(c => c.Name == normalized);
            TempData["Toast"] = $"{normalized}: {summary.Added} öğrenci eklendi, {summary.Total} öğrenci kayıtlı.";
            return RedirectToPage("/Ogrenciler/Index", new { classId = cls.Id });
        }
        catch (Exception ex)
        {
            TempData["Toast"] = "İçe aktarma başarısız: " + ex.Message;
            return await RepreviewAsync(path, token, "İçe aktarma başarısız: " + ex.Message);
        }
    }

    private Task<IActionResult> RepreviewAsync(string path, string token, string message)
    {
        Preview = _importer.ParseFile(path);
        Token = token;
        PreviewFile = Path.GetFileName(path);
        ClassName = Preview.ClassName;
        Grade = Preview.Grade;
        ModelState.Clear();
        return Task.FromResult<IActionResult>(Page());
    }

    public async Task<IActionResult> OnPostFolderAsync()
    {
        await LoadAsync();
        var parent = Directory.GetParent(_env.ContentRootPath)?.FullName;
        if (parent is null)
        {
            TempData["Toast"] = "Klasör bulunamadı.";
            return RedirectToPage();
        }

        var summary = await _importer.ImportFolderAsync(_db, parent);
        var added = summary.Files.Sum(f => f.Added);
        TempData["Toast"] = $"{summary.Files.Count} dosya işlendi, {added} yeni öğrenci eklendi.";
        return RedirectToPage("/Ogrenciler/Index");
    }
}
