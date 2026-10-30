using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PerfApp.Data;
using PerfApp.Models;

namespace PerfApp.Services;

public sealed record ParsedStudent(int? Number, string FullName);
public sealed class ParsedRoster
{
    public string? ClassName { get; set; }
    public int? Grade { get; set; }
    public string? SourceSheet { get; set; }
    public List<ParsedStudent> Students { get; set; } = new();
}

public sealed record ImportSummary(string ClassName, int Added, int Updated, int Removed, int Total);
public sealed record FolderImportSummary(List<ImportSummary> Files, List<string> Skipped);

/// <summary>Basit şablon (Öğrenci No · Ad Soyad · Sınıf) satırı.</summary>
public sealed record TemplateStudent(int? Number, string FullName, string? ClassName);
public sealed record TemplateClassSummary(string ClassName, int Added, int Updated, int Total, bool IsNewClass);
public sealed record TemplateImportSummary(List<TemplateClassSummary> Classes, List<string> Errors, int Added);

public partial class StudentImportService
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly Regex ClassRegex = new(@"(?<!\d)(9|10|11)\s*[/\-]\s*([A-ZÇĞİÖŞÜ])(?=\s|$|S[İI]N[İI]F)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FileClassRegex = new(@"(?<!\d)(9|10|11)\s*[/\-]\s*([A-ZÇĞİÖŞÜ])(?![A-Za-zÇĞİÖŞÜçğıöşü])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex GradeRegex = new(@"(?<!\d)(9|10|11)\s*\.?\s*S[İI]N[İI]F", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public ParsedRoster ParseFile(string path)
    {
        using var wb = new XLWorkbook(path);
        var result = new ParsedRoster { ClassName = DetectClassName(wb, path) };
        if (result.ClassName is not null)
            result.Grade = int.Parse(result.ClassName.Split('/')[0], CultureInfo.InvariantCulture);

        foreach (var ws in wb.Worksheets)
        {
            if (TryReadRoster(ws, out var headerRow, out var nameCol, out var numberCol, out var students))
            {
                result.SourceSheet = ws.Name;
                result.Students = students;
                result.Grade ??= DetectGradeFromText(ws.Name);
                return result;
            }
        }

        // Başlık bulunamazsa ilk sayfada bilinen klasik düzeni dene.
        var first = wb.Worksheets.FirstOrDefault();
        if (first is not null)
        {
            var list = new List<ParsedStudent>();
            for (var r = 4; r <= first.LastRowUsed()?.RowNumber(); r++)
            {
                var name = CellText(first.Cell(r, 4));
                if (!IsValidName(name)) continue;
                list.Add(new ParsedStudent(ParseNumber(first.Cell(r, 3)), name));
            }
            if (list.Count > 0)
            {
                result.Students = list;
                result.SourceSheet = first.Name + " (varsayılan düzen)";
            }
        }

        return result;
    }

    private static bool TryReadRoster(IXLWorksheet ws, out int headerRow, out int nameCol, out int numberCol, out List<ParsedStudent> students)
    {
        headerRow = 0; nameCol = 0; numberCol = 0; students = new();

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        if (lastRow == 0) return false;

        for (var r = 1; r <= Math.Min(15, lastRow); r++)
        {
            var nCol = 0; var numCol = 0;
            foreach (var cell in ws.Row(r).CellsUsed())
            {
                var t = NormalizeHeader(CellText(cell));
                if (t.Length == 0) continue;
                if (t.Contains("ADI") || t.Contains("ADSOYAD"))
                    nCol = cell.Address.ColumnNumber;
                if (t.Contains("NO") && !t.Contains("SIRA"))
                    numCol = cell.Address.ColumnNumber;
            }

            if (nCol > 0)
            {
                var list = new List<ParsedStudent>();
                for (var dr = r + 1; dr <= lastRow; dr++)
                {
                    var name = CellText(ws.Cell(dr, nCol));
                    if (!IsValidName(name)) continue;
                    list.Add(new ParsedStudent(numCol > 0 ? ParseNumber(ws.Cell(dr, numCol)) : null, name));
                }

                if (list.Count > 0)
                {
                    headerRow = r; nameCol = nCol; numberCol = numCol; students = list;
                    return true;
                }
            }
        }

        return false;
    }

    private static string? DetectClassName(XLWorkbook wb, string path)
    {
        var votes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var ws in wb.Worksheets)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var r = 1; r <= 3; r++)
            {
                foreach (var cell in ws.Row(r).CellsUsed())
                {
                    if (cell.DataType != XLDataType.Text) continue;
                    foreach (Match m in ClassRegex.Matches(cell.GetString()))
                        found.Add(m.Groups[1].Value + "/" + m.Groups[2].Value.ToUpper(Tr));
                }
            }
            foreach (var tag in found) votes[tag] = votes.GetValueOrDefault(tag) + 1;
        }

        if (votes.Count > 0)
            return votes.OrderByDescending(v => v.Value).ThenBy(v => v.Key).First().Key;

        var fileName = Path.GetFileNameWithoutExtension(path);
        var fm = FileClassRegex.Match(fileName);
        if (fm.Success) return fm.Groups[1].Value + "/" + fm.Groups[2].Value.ToUpper(Tr);

        var gm = GradeRegex.Match(fileName);
        if (gm.Success) return gm.Groups[1].Value + "/?";

        return null;
    }

    private static int? DetectGradeFromText(string text)
    {
        var m = GradeRegex.Match(text);
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    private static string NormalizeHeader(string value)
    {
        var s = value.ToUpper(Tr);
        return new string(s.Where(ch => !char.IsWhiteSpace(ch) && ch != '.' && ch != ':' && ch != '-').ToArray());
    }

    private static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        name = name.Trim();
        if (name.Length < 2) return false;
        if (name is "0" or "-" or "—") return false;
        if (name.All(ch => char.IsDigit(ch) || ch is '.' or ',' or ' ')) return false;
        return true;
    }

    private static string CellText(IXLCell cell)
    {
        if (cell.IsEmpty()) return "";
        return cell.DataType == XLDataType.Text ? cell.GetString().Trim() : cell.GetFormattedString().Trim();
    }

    private static int? ParseNumber(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        if (cell.DataType == XLDataType.Number)
        {
            var d = cell.GetDouble();
            if (d > 0 && d < int.MaxValue) return (int)Math.Round(d);
        }
        var s = CellText(cell);
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;
    }

    public static string? NormalizeClassName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var m = Regex.Match(value.Trim(), @"^(\d{1,2})\s*[/\-]?\s*([A-Za-zÇĞİÖŞÜçğıöşü])$");
        if (!m.Success) return null;
        var grade = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        if (grade is < 9 or > 12) return null;
        return grade + "/" + m.Groups[2].Value.ToUpper(Tr);
    }

    public async Task<ImportSummary> ImportAsync(AppDbContext db, ParsedRoster roster, string className, int grade,
        bool replace, CancellationToken ct = default)
    {
        var cls = await db.SchoolClasses.Include(c => c.Students)
            .FirstOrDefaultAsync(c => c.Name == className, ct);
        if (cls is null)
        {
            cls = new SchoolClass { Name = className, Grade = grade, AcademicYear = "2026-2027" };
            db.SchoolClasses.Add(cls);
            await db.SaveChangesAsync(ct);
        }

        var removed = 0;
        if (replace)
        {
            removed = cls.Students.Count;
            db.Students.RemoveRange(cls.Students);
            cls.Students.Clear();
            await db.SaveChangesAsync(ct);
        }

        var existing = cls.Students.ToDictionary(s => s.FullName, s => s, StringComparer.OrdinalIgnoreCase);
        var added = 0; var updated = 0;

        foreach (var row in roster.Students)
        {
            if (existing.TryGetValue(row.FullName, out var student))
            {
                if (row.Number is not null && student.Number != row.Number)
                {
                    student.Number = row.Number;
                    updated++;
                }
                continue;
            }

            var newStudent = new Student { SchoolClassId = cls.Id, FullName = row.FullName, Number = row.Number };
            db.Students.Add(newStudent);
            existing[row.FullName] = newStudent;
            added++;
        }

        await db.SaveChangesAsync(ct);
        var total = await db.Students.CountAsync(s => s.SchoolClassId == cls.Id, ct);
        return new ImportSummary(className, added, updated, removed, total);
    }

    public async Task<FolderImportSummary> ImportFolderAsync(AppDbContext db, string folder, CancellationToken ct = default)
    {
        var summaries = new List<ImportSummary>();
        var skipped = new List<string>();

        if (!Directory.Exists(folder))
            return new FolderImportSummary(summaries, skipped);

        foreach (var path in Directory.GetFiles(folder, "*.xlsx").OrderBy(p => p))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith("~$", StringComparison.Ordinal)) continue;

            try
            {
                var roster = ParseFile(path);
                if (roster.Students.Count == 0)
                {
                    skipped.Add(name + " — öğrenci listesi bulunamadı");
                    continue;
                }

                var className = roster.ClassName;
                if (className is null || className.EndsWith("/?"))
                {
                    skipped.Add(name + " — sınıf adı belirlenemedi");
                    continue;
                }

                var grade = roster.Grade ?? int.Parse(className.Split('/')[0], CultureInfo.InvariantCulture);
                var existing = await db.SchoolClasses.Include(c => c.Students).FirstOrDefaultAsync(c => c.Name == className, ct);

                // Mevcut sınıfta öğrenci varsa yalnızca eksikleri ekle.
                var summary = await ImportAsync(db, roster, className, grade, replace: existing is null || existing.Students.Count == 0, ct);
                summaries.Add(summary);
            }
            catch (Exception ex)
            {
                skipped.Add(name + " — " + ex.Message);
            }
        }

        return new FolderImportSummary(summaries, skipped);
    }

    // ---------- Basit şablon (Öğrenci No · Ad Soyad · Sınıf) ----------

    /// <summary>Şablon dosyasındaki satırları okur. Başlık satırından "No", "Ad Soyad", "Sınıf" sütunları bulunur.</summary>
    public List<TemplateStudent> ParseTemplate(string path)
    {
        var list = new List<TemplateStudent>();
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.FirstOrDefault();
        if (ws is null) return list;

        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        if (lastRow == 0) return list;

        var headerRow = 0; var nameCol = 0; var numCol = 0; var classCol = 0;
        for (var r = 1; r <= Math.Min(10, lastRow); r++)
        {
            foreach (var cell in ws.Row(r).CellsUsed())
            {
                var t = NormalizeHeader(CellText(cell));
                if (t.Length == 0) continue;
                if (t.Contains("ADSOYAD") || t.Contains("ADI") || t == "AD") nameCol = cell.Address.ColumnNumber;
                if (t.Contains("SINIF") || t.Contains("SUBE")) classCol = cell.Address.ColumnNumber;
                if (t.Contains("NO") && !t.Contains("SIRA")) numCol = cell.Address.ColumnNumber;
            }
            if (nameCol > 0) { headerRow = r; break; }
        }

        // Başlık bulunamazsa varsayılan düzen: A=No, B=Ad Soyad, C=Sınıf.
        if (nameCol == 0) { headerRow = 1; numCol = 1; nameCol = 2; classCol = 3; }

        for (var r = headerRow + 1; r <= lastRow; r++)
        {
            var name = CellText(ws.Cell(r, nameCol));
            if (!IsValidName(name)) continue;
            var number = numCol > 0 ? ParseNumber(ws.Cell(r, numCol)) : null;
            var cls = classCol > 0 ? CellText(ws.Cell(r, classCol)) : null;
            list.Add(new TemplateStudent(number, name, string.IsNullOrWhiteSpace(cls) ? null : cls));
        }

        return list;
    }

    /// <summary>Şablon satırlarını sınıflara göre içe aktarır; olmayan sınıfları otomatik oluşturur.</summary>
    public async Task<TemplateImportSummary> ImportTemplateAsync(AppDbContext db, List<TemplateStudent> rows, CancellationToken ct = default)
    {
        var classes = new List<TemplateClassSummary>();
        var errors = new List<string>();
        var totalAdded = 0;

        var groups = rows.GroupBy(r => (r.ClassName ?? "").Trim(), StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var normalized = NormalizeClassName(group.Key);
            if (normalized is null)
            {
                var label = string.IsNullOrWhiteSpace(group.Key) ? "(sınıf boş)" : group.Key;
                errors.Add($"Sınıf adı geçersiz, atlandı: \"{label}\" ({group.Count()} öğrenci)");
                continue;
            }

            var grade = int.Parse(normalized.Split('/')[0], CultureInfo.InvariantCulture);
            var cls = await db.SchoolClasses.Include(c => c.Students)
                .FirstOrDefaultAsync(c => c.Name == normalized, ct);
            var isNew = cls is null;
            if (cls is null)
            {
                cls = new SchoolClass { Name = normalized, Grade = grade, AcademicYear = "2026-2027" };
                db.SchoolClasses.Add(cls);
                await db.SaveChangesAsync(ct);
            }

            var existing = cls.Students.ToDictionary(s => s.FullName, s => s, StringComparer.OrdinalIgnoreCase);
            var added = 0; var updated = 0;

            foreach (var row in group)
            {
                if (existing.TryGetValue(row.FullName, out var student))
                {
                    if (row.Number is not null && student.Number != row.Number)
                    {
                        student.Number = row.Number;
                        updated++;
                    }
                    continue;
                }

                var newStudent = new Student { SchoolClassId = cls.Id, FullName = row.FullName, Number = row.Number };
                db.Students.Add(newStudent);
                existing[row.FullName] = newStudent;
                added++;
            }

            await db.SaveChangesAsync(ct);
            var total = await db.Students.CountAsync(s => s.SchoolClassId == cls.Id, ct);
            classes.Add(new TemplateClassSummary(normalized, added, updated, total, isNew));
            totalAdded += added;
        }

        return new TemplateImportSummary(
            classes.OrderBy(c => c.ClassName, StringComparer.OrdinalIgnoreCase).ToList(),
            errors, totalAdded);
    }

    /// <summary>İndirilebilir örnek şablonu (.xlsx) üretir.</summary>
    public byte[] BuildTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Öğrenciler");

        ws.Cell(1, 1).Value = "Öğrenci No";
        ws.Cell(1, 2).Value = "Ad Soyad";
        ws.Cell(1, 3).Value = "Sınıf";

        var head = ws.Range("A1:C1");
        head.Style.Font.Bold = true;
        head.Style.Font.FontColor = XLColor.White;
        head.Style.Fill.BackgroundColor = XLColor.FromHtml("#0F766E");
        head.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        head.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(1).Height = 22;

        ws.Column(1).Width = 14;
        ws.Column(2).Width = 36;
        ws.Column(3).Width = 12;
        ws.SheetView.FreezeRows(1);

        var info = wb.AddWorksheet("Açıklama");
        void Line(int row, string text, bool bold = false)
        {
            var c = info.Cell(row, 1);
            c.Value = text;
            c.Style.Font.Bold = bold;
            c.Style.Alignment.WrapText = true;
        }
        Line(1, "Öğrenci Şablonu — Nasıl doldurulur?", true);
        Line(2, "1) \"Öğrenciler\" sayfasını açın.");
        Line(3, "2) A sütunu: Öğrenci No  ·  B sütunu: Ad Soyad  ·  C sütunu: Sınıf");
        Line(4, "3) Sınıfı 9/C, 10/A gibi yazın. Sınıf sistemde yoksa otomatik oluşturulur.");
        Line(5, "4) Başlık satırını silmeyin. Boş satırları yok sayabilirsiniz.");
        Line(6, "5) Dosyayı kaydedip uygulamadan yükleyin.");
        info.Column(1).Width = 90;

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
