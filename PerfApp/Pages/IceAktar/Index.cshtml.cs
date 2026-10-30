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

    public sealed record ClassPreview(string ClassName, int Count, bool IsNewClass, bool IsValid);

    public List<SchoolClass> Classes { get; private set; } = new();
    public List<TemplateStudent>? Preview { get; private set; }
    public List<ClassPreview> PreviewClasses { get; private set; } = new();
    public List<string> PreviewWarnings { get; private set; } = new();
    public string? Token { get; private set; }
    public string? PreviewFile { get; private set; }
    public int FolderFileCount { get; private set; }

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

    /// <summary>Doldurulacak örnek şablonu indirir.</summary>
    public IActionResult OnGetTemplate()
    {
        var bytes = _importer.BuildTemplate();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Ogrenci-Sablonu.xlsx");
    }

    public async Task<IActionResult> OnPostPreviewAsync(IFormFile? file)
    {
        await LoadAsync();

        if (file is null || file.Length == 0)
        {
            TempData["Toast"] = "Lütfen doldurduğunuz .xlsx dosyasını seçin.";
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
            var students = _importer.ParseTemplate(path);
            if (students.Count == 0)
            {
                System.IO.File.Delete(path);
                TempData["Toast"] = "Dosyada öğrenci bulunamadı. Şablondaki başlık satırını koruduğunuzdan emin olun.";
                return RedirectToPage();
            }

            await BuildPreviewAsync(students);
            Token = token;
            PreviewFile = file.FileName;
            return Page();
        }
        catch (Exception ex)
        {
            System.IO.File.Delete(path);
            TempData["Toast"] = "Dosya okunamadı: " + ex.Message;
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostConfirmAsync(string? token)
    {
        await LoadAsync();

        if (!Guid.TryParse(token, out _))
        {
            TempData["Toast"] = "Geçersiz dosya oturumu. Lütfen dosyayı yeniden yükleyin.";
            return RedirectToPage();
        }

        var path = Path.Combine(UploadFolder, token + ".xlsx");
        if (!System.IO.File.Exists(path))
        {
            TempData["Toast"] = "Yüklenen dosya bulunamadı. Lütfen yeniden yükleyin.";
            return RedirectToPage();
        }

        try
        {
            var students = _importer.ParseTemplate(path);
            var summary = await _importer.ImportTemplateAsync(_db, students);
            System.IO.File.Delete(path);

            if (summary.Classes.Count == 0)
            {
                TempData["Toast"] = summary.Errors.Count > 0
                    ? "Hiçbir öğrenci aktarılamadı: " + summary.Errors[0]
                    : "Aktarılacak öğrenci bulunamadı.";
                return RedirectToPage();
            }

            var newClasses = summary.Classes.Count(c => c.IsNewClass);
            var message = $"{summary.Added} yeni öğrenci eklendi, {summary.Classes.Count} sınıf işlendi";
            if (newClasses > 0) message += $" ({newClasses} yeni sınıf oluşturuldu)";
            if (summary.Errors.Count > 0) message += $" · {summary.Errors.Count} satır atlandı";
            TempData["Toast"] = message + ".";

            var first = await _db.SchoolClasses.FirstOrDefaultAsync(c => c.Name == summary.Classes[0].ClassName);
            return RedirectToPage("/Ogrenciler/Index", new { classId = first?.Id });
        }
        catch (Exception ex)
        {
            TempData["Toast"] = "İçe aktarma başarısız: " + ex.Message;
            return RedirectToPage();
        }
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

    private async Task BuildPreviewAsync(List<TemplateStudent> students)
    {
        Preview = students;
        var known = await _db.SchoolClasses.Select(c => c.Name).ToListAsync();
        var knownSet = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);

        foreach (var group in students.GroupBy(s => (s.ClassName ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var normalized = StudentImportService.NormalizeClassName(group.Key);
            if (normalized is null)
            {
                var label = string.IsNullOrWhiteSpace(group.Key) ? "(sınıf boş)" : group.Key;
                PreviewWarnings.Add($"\"{label}\" geçerli bir sınıf adı değil — bu {group.Count()} öğrenci atlanacak.");
                continue;
            }

            PreviewClasses.Add(new ClassPreview(normalized, group.Count(), !knownSet.Contains(normalized), true));
        }
    }
}
