using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PerfApp.Data;
using PerfApp.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PerfApp.Services;

// ---- Dışa aktarma veri modeli --------------------------------------------------

public sealed class ExportCriterionRow
{
    public int CriterionId { get; init; }
    public int Order { get; init; }
    public string Title { get; init; } = "";
    public double MaxPoints { get; init; }
    public Dictionary<int, double> Earned { get; } = new();
}

public sealed class ExportGroupRow
{
    public string Name { get; init; } = "";
    public double MaxPoints { get; init; }
    public List<ExportCriterionRow> Criteria { get; } = new();
}

public sealed class ExportComponent
{
    public AssessmentComponent Component { get; init; } = null!;
    public double MaxPoints { get; init; }
    public List<ExportGroupRow> Groups { get; } = new();
    public Dictionary<int, int?> Score { get; } = new();

    /// <summary>Tüm ölçütler, orijinal Excel sırasına göre (gruplar düzleştirilir).</summary>
    public IEnumerable<ExportCriterionRow> Criteria =>
        Groups.SelectMany(g => g.Criteria).OrderBy(c => c.Order);
}

public sealed class ClassExport
{
    public SchoolClass Class { get; init; } = null!;
    public List<Student> Students { get; init; } = new();
    public List<ExportComponent> Components { get; init; } = new();
    public Dictionary<int, double?> P1 { get; } = new();
    public Dictionary<int, double?> P2 { get; } = new();
}

// ---- Servis ---------------------------------------------------------------------

/// <summary>
/// Girilen grup puanlarını orijinal ölçütlere dağıtarak, Excel dosyalarının
/// düzenini (ölçüt satırları × öğrenci sütunları) koruyan Excel ve PDF üretir.
/// </summary>
public static class ExportService
{
    public static string SchoolName { get; set; } = "";
    public const string AcademicYear = "2026-2027";
    public const string Term = "1. DÖNEM";

    private static string ClassHeading(string className) =>
        string.IsNullOrWhiteSpace(SchoolName) ? $"{className} SINIFI" : $"{SchoolName} · {className} SINIFI";
    private const string NumberFormat = "0.##";

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    static ExportService()
    {
        TryRegisterFonts();
    }

    private static void TryRegisterFonts()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        Register(new[]
        {
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
            "/usr/share/fonts/TTF/DejaVuSans.ttf",
            "/usr/share/fonts/liberation/LiberationSans-Regular.ttf",
            "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf"
        });
        Register(new[]
        {
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
            "/usr/share/fonts/TTF/DejaVuSans-Bold.ttf",
            "/usr/share/fonts/liberation/LiberationSans-Bold.ttf",
            "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf"
        });
    }

    private static void Register(string[] paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (!File.Exists(path)) continue;
                using var stream = File.OpenRead(path);
                QuestPDF.Drawing.FontManager.RegisterFontFromStream(stream);
                return;
            }
            catch
            {
                // Sessizce bir sonraki adayı dene.
            }
        }
    }

    private static string FontFamily =>
        File.Exists("/usr/share/fonts/TTF/DejaVuSans.ttf") ||
        File.Exists("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf")
            ? "DejaVu Sans"
            : "Liberation Sans";

    // ---- Veri toplama -----------------------------------------------------------

    public static async Task<ClassExport?> BuildAsync(AppDbContext db, int classId, string? sort)
    {
        var cls = await db.SchoolClasses.Include(c => c.Students).FirstOrDefaultAsync(c => c.Id == classId);
        if (cls is null) return null;

        var components = await db.Components
            .Include(c => c.Groups).ThenInclude(g => g.Criteria)
            .Include(c => c.Criteria)
            .Where(c => c.Grade == cls.Grade && c.Term == 1)
            .OrderBy(c => c.Order)
            .ToListAsync();

        var students = StudentOrdering.Sort(cls.Students, sort);
        var studentIds = students.Select(s => s.Id).ToList();

        var scores = await db.GroupScores
            .Where(s => s.Term == 1 && studentIds.Contains(s.StudentId))
            .Select(s => new { s.StudentId, s.RubricGroupId, s.Score })
            .ToListAsync();

        var byStudent = scores
            .GroupBy(s => s.StudentId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.RubricGroupId, x => x.Score));

        var export = new ClassExport { Class = cls, Students = students };

        var componentsExport = new List<ExportComponent>();
        foreach (var comp in components)
        {
            var ec = new ExportComponent
            {
                Component = comp,
                MaxPoints = comp.Criteria.Sum(c => c.MaxPoints)
            };
            foreach (var grp in comp.Groups.OrderBy(g => g.Order))
            {
                var gr = new ExportGroupRow
                {
                    Name = grp.Name,
                    MaxPoints = grp.Criteria.Sum(c => c.MaxPoints)
                };
                foreach (var crit in grp.Criteria.OrderBy(c => c.Order))
                {
                    gr.Criteria.Add(new ExportCriterionRow
                    {
                        CriterionId = crit.Id,
                        Order = crit.Order,
                        Title = crit.Title,
                        MaxPoints = crit.MaxPoints
                    });
                }
                ec.Groups.Add(gr);
            }
            componentsExport.Add(ec);
        }

        var critRows = componentsExport
            .SelectMany(c => c.Groups)
            .SelectMany(g => g.Criteria)
            .ToDictionary(c => c.CriterionId);

        foreach (var student in students)
        {
            var dict = byStudent.TryGetValue(student.Id, out var d) ? d : new Dictionary<int, int>();
            var results = new List<ComponentResult>();

            for (var i = 0; i < components.Count; i++)
            {
                var result = Scoring.Component(components[i], dict);
                results.Add(result);
                componentsExport[i].Score[student.Id] = result.Score;

                foreach (var group in result.Groups)
                {
                    foreach (var criterion in group.Criteria)
                    {
                        if (critRows.TryGetValue(criterion.Id, out var row))
                            row.Earned[student.Id] = criterion.Earned;
                    }
                }
            }

            var p1Results = results.Where(r => r.Component.Kind == "P1").OrderBy(r => r.Component.Order).ToList();
            export.P1[student.Id] = Scoring.P1(cls.Grade, p1Results);
            export.P2[student.Id] = Scoring.P2(
                results.FirstOrDefault(r => r.Component.Code == "B1"),
                results.FirstOrDefault(r => r.Component.Code == "B2"),
                results.FirstOrDefault(r => r.Component.Code == "D"));
        }

        export.Components.AddRange(componentsExport);
        return export;
    }

    // ---- Excel ------------------------------------------------------------------

    public static byte[] BuildExcel(ClassExport ex)
    {
        using var wb = new XLWorkbook();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        BuildSummarySheet(wb, ex, used);
        foreach (var comp in ex.Components)
            BuildComponentSheet(wb, ex, comp, used);
        BuildRosterSheet(wb, ex, used);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void BuildSummarySheet(XLWorkbook wb, ClassExport ex, HashSet<string> used)
    {
        var ws = wb.AddWorksheet(UniqueSheetName("Özet", used));
        var lastCol = 3 + ex.Components.Count + 2;

        ws.Cell(1, 1).Value = ClassHeading(ex.Class.Name);
        ws.Range(1, 1, 1, lastCol).Merge();
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13);
        ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Cell(2, 1).Value = $"{AcademicYear} EĞİTİM ÖĞRETİM YILI · {Term} PERFORMANS PUANLARI";
        ws.Range(2, 1, 2, lastCol).Merge();
        ws.Cell(2, 1).Style.Font.SetFontSize(10);
        ws.Cell(2, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        var headers = new List<string> { "SIRA NO", "ÖĞRENCI NO", "ÖĞRENCİNİN ADI VE SOYADI" };
        headers.AddRange(ex.Components.Select(c => c.Component.Code));
        headers.Add("1. PERFORMANS");
        headers.Add("2. PERFORMANS");

        for (var c = 0; c < headers.Count; c++)
        {
            var cell = ws.Cell(3, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.SetBold();
            cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#0F766E"));
            cell.Style.Font.SetFontColor(XLColor.White);
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.WrapText = true;
        }

        var row = 4;
        var index = 1;
        foreach (var student in ex.Students)
        {
            ws.Cell(row, 1).Value = index;
            ws.Cell(row, 3).Value = student.FullName;
            if (student.Number is not null) ws.Cell(row, 2).Value = student.Number.Value;
            ws.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

            for (var i = 0; i < ex.Components.Count; i++)
            {
                var score = ex.Components[i].Score.GetValueOrDefault(student.Id);
                if (score is not null) ws.Cell(row, 4 + i).Value = score.Value;
            }
            var p1 = ex.P1.GetValueOrDefault(student.Id);
            var p2 = ex.P2.GetValueOrDefault(student.Id);
            if (p1 is not null) ws.Cell(row, 4 + ex.Components.Count).Value = Math.Round(p1.Value, 2);
            if (p2 is not null) ws.Cell(row, 4 + ex.Components.Count + 1).Value = Math.Round(p2.Value, 2);

            row++;
            index++;
        }

        ws.Cell(row, 1).Value = "ORTALAMA";
        ws.Cell(row, 3).Value = "Sınıf ortalaması";
        for (var i = 0; i < ex.Components.Count; i++)
        {
            var values = ex.Students
                .Select(s => ex.Components[i].Score.GetValueOrDefault(s.Id))
                .Where(v => v.HasValue).Select(v => (double)v!.Value).ToList();
            if (values.Count > 0) ws.Cell(row, 4 + i).Value = Math.Round(values.Average(), 2);
        }
        var avgP1 = ex.P1.Values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        var avgP2 = ex.P2.Values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (avgP1.Count > 0) ws.Cell(row, 4 + ex.Components.Count).Value = Math.Round(avgP1.Average(), 2);
        if (avgP2.Count > 0) ws.Cell(row, 4 + ex.Components.Count + 1).Value = Math.Round(avgP2.Average(), 2);
        for (var c = 1; c <= lastCol; c++)
            ws.Cell(row, c).Style.Font.SetBold();

        ws.Column(1).Width = 8;
        ws.Column(2).Width = 12;
        ws.Column(3).Width = 28;
        for (var i = 0; i < ex.Components.Count + 2; i++)
            ws.Column(4 + i).Width = 13;
        ws.SheetView.FreezeRows(3);
        ws.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        for (var r = 4; r <= row; r++)
            for (var c = 4; c <= lastCol; c++)
                ws.Cell(r, c).Style.NumberFormat.Format = NumberFormat;
        ws.Range(3, 1, 3, lastCol).Style.Border.SetBottomBorder(XLBorderStyleValues.Medium);
    }

    private static void BuildComponentSheet(XLWorkbook wb, ClassExport ex, ExportComponent comp, HashSet<string> used)
    {
        var title = UniqueSheetName(comp.Component.Code + " " + comp.Component.Name, used);
        var ws = wb.AddWorksheet(title);
        var studentCount = ex.Students.Count;
        var lastCol = 2 + studentCount;

        ws.Cell(1, 1).Value = $"{ClassHeading(ex.Class.Name)} · {comp.Component.Name}";
        ws.Range(1, 1, 1, lastCol).Merge();
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(12);
        ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Cell(2, 1).Value = $"{AcademicYear} · {Term} · Toplam {Ui.N(comp.MaxPoints)} puan";
        ws.Range(2, 1, 2, lastCol).Merge();
        ws.Cell(2, 1).Style.Font.SetFontSize(9);
        ws.Cell(2, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        ws.Cell(3, 1).Value = "ÖLÇÜT";
        ws.Cell(3, 2).Value = "MAKS";
        ws.Range(3, 1, 4, 1).Merge();
        ws.Range(3, 2, 4, 2).Merge();

        for (var i = 0; i < studentCount; i++)
        {
            var student = ex.Students[i];
            if (student.Number is not null) ws.Cell(3, 3 + i).Value = student.Number.Value;
            ws.Cell(4, 3 + i).Value = student.FullName;
        }

        for (var c = 1; c <= lastCol; c++)
        {
            var head = ws.Range(3, c, 4, c);
            head.Style.Font.SetBold();
            head.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E6F4F1"));
            head.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            head.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            head.Style.Alignment.WrapText = true;
            head.Style.Border.SetBottomBorder(XLBorderStyleValues.Thin);
            head.Style.Border.SetTopBorder(XLBorderStyleValues.Thin);
            head.Style.Border.SetLeftBorder(XLBorderStyleValues.Thin);
            head.Style.Border.SetRightBorder(XLBorderStyleValues.Thin);
        }

        var row = 5;
        foreach (var crit in comp.Criteria)
        {
            ws.Cell(row, 1).Value = crit.Title;
            ws.Cell(row, 1).Style.Alignment.WrapText = true;
            ws.Cell(row, 2).Value = Math.Round(crit.MaxPoints, 2);
            for (var i = 0; i < studentCount; i++)
            {
                var student = ex.Students[i];
                if (crit.Earned.TryGetValue(student.Id, out var earned))
                    ws.Cell(row, 3 + i).Value = Math.Round(earned, 2);
            }
            row++;
        }

        ws.Cell(row, 1).Value = "TOPLAM";
        ws.Cell(row, 2).Value = Math.Round(comp.MaxPoints, 2);
        for (var i = 0; i < studentCount; i++)
        {
            var student = ex.Students[i];
            var sum = comp.Criteria
                .Where(c => c.Earned.ContainsKey(student.Id))
                .Sum(c => c.Earned[student.Id]);
            ws.Cell(row, 3 + i).Value = Math.Round(sum, 2);
        }
        var totalRow = row;
        row++;

        ws.Cell(row, 1).Value = "BİLEŞEN PUANI (100)";
        for (var i = 0; i < studentCount; i++)
        {
            var student = ex.Students[i];
            var score = comp.Score.GetValueOrDefault(student.Id);
            if (score is not null) ws.Cell(row, 3 + i).Value = score.Value;
        }
        var scoreRow = row;

        for (var c = 1; c <= lastCol; c++)
        {
            foreach (var r in new[] { totalRow, scoreRow })
            {
                ws.Cell(r, c).Style.Font.SetBold();
                ws.Cell(r, c).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#E6F4F1"));
            }
        }
        ws.Cell(scoreRow, 1).Style.Font.SetFontColor(XLColor.FromHtml("#0F766E"));

        ws.Column(1).Width = 42;
        ws.Column(2).Width = 7;
        for (var i = 0; i < studentCount; i++)
            ws.Column(3 + i).Width = 11;

        ws.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        for (var r = 5; r <= scoreRow; r++)
        {
            for (var c = 3; c <= lastCol; c++)
                ws.Cell(r, c).Style.NumberFormat.Format = NumberFormat;
            ws.Cell(r, 2).Style.NumberFormat.Format = NumberFormat;
        }
        ws.SheetView.FreezeRows(4);
        ws.SheetView.FreezeColumns(2);
    }

    private static void BuildRosterSheet(XLWorkbook wb, ClassExport ex, HashSet<string> used)
    {
        var ws = wb.AddWorksheet(UniqueSheetName("Öğrenci Bilgileri", used));
        ws.Cell(1, 1).Value = "SIRA NO";
        ws.Cell(1, 2).Value = "ÖĞRENCI NO";
        ws.Cell(1, 3).Value = "ÖĞRENCİNİN ADI VE SOYADI";
        for (var c = 1; c <= 3; c++)
        {
            ws.Cell(1, c).Style.Font.SetBold();
            ws.Cell(1, c).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#0F766E"));
            ws.Cell(1, c).Style.Font.SetFontColor(XLColor.White);
        }

        var r = 2;
        var i = 1;
        foreach (var student in ex.Students)
        {
            ws.Cell(r, 1).Value = i;
            if (student.Number is not null) ws.Cell(r, 2).Value = student.Number.Value;
            ws.Cell(r, 3).Value = student.FullName;
            r++;
            i++;
        }
        ws.Column(1).Width = 9;
        ws.Column(2).Width = 12;
        ws.Column(3).Width = 32;
        ws.SheetView.FreezeRows(1);
    }

    private static string UniqueSheetName(string name, HashSet<string> used)
    {
        var invalid = new[] { '[', ']', ':', '*', '?', '/', '\\' };
        var clean = new string(name.Where(ch => !invalid.Contains(ch)).ToArray()).Trim();
        if (clean.Length == 0) clean = "Sayfa";
        if (clean.Length > 31) clean = clean[..31];

        var candidate = clean;
        var n = 2;
        while (!used.Add(candidate))
        {
            var suffix = " (" + n + ")";
            candidate = clean.Length + suffix.Length > 31 ? clean[..(31 - suffix.Length)] + suffix : clean + suffix;
            n++;
        }
        return candidate;
    }

    // ---- PDF --------------------------------------------------------------------

    private const string HeaderFill = "#E6F4F1";
    private const string TotalFill = "#E6F4F1";
    private const string LineColor = "#E2E8F0";
    private const string BrandColor = "#0F766E";

    public static byte[] BuildPdf(ClassExport ex)
    {
        var family = FontFamily;

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                SetupPage(page, family, ex);
                page.Content().Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Background(BrandColor).PaddingVertical(3).PaddingHorizontal(6)
                        .Text("1. ve 2. Performans Puanları").FontColor(Colors.White).Bold().FontSize(9);

                    var codes = ex.Components.Select(c => c.Component.Code).ToList();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.ConstantColumn(24);
                            cols.ConstantColumn(28);
                            cols.RelativeColumn(2.4f);
                            foreach (var _ in codes) cols.RelativeColumn();
                            cols.RelativeColumn();
                            cols.RelativeColumn();
                        });

                        table.Header(h =>
                        {
                            h.Cell().Element(HeaderStyle).Text("Sıra").FontSize(6.5f).Bold();
                            h.Cell().Element(HeaderStyle).Text("No").FontSize(6.5f).Bold();
                            h.Cell().Element(HeaderStyle).Text("Adı Soyadı").FontSize(6.5f).Bold();
                            foreach (var code in codes)
                                h.Cell().Element(HeaderStyle).Text(code).FontSize(6.5f).Bold();
                            h.Cell().Element(HeaderStyle).Text("1. Perf.").FontSize(6.5f).Bold();
                            h.Cell().Element(HeaderStyle).Text("2. Perf.").FontSize(6.5f).Bold();
                        });

                        var idx = 1;
                        foreach (var student in ex.Students)
                        {
                            table.Cell().Element(BodyStyle).Text(idx.ToString(Tr)).FontSize(6.5f);
                            table.Cell().Element(BodyStyle).Text(student.Number?.ToString(Tr) ?? "").FontSize(6.5f);
                            table.Cell().Element(BodyStyle).Text(student.FullName).FontSize(6.5f);
                            foreach (var comp in ex.Components)
                            {
                                var s = comp.Score.GetValueOrDefault(student.Id);
                                table.Cell().Element(BodyStyle).Text(s?.ToString(Tr) ?? "—").FontSize(6.5f);
                            }
                            table.Cell().Element(BlueStyle).Text(Fmt(ex.P1.GetValueOrDefault(student.Id))).FontSize(6.5f);
                            table.Cell().Element(PurpleStyle).Text(Fmt(ex.P2.GetValueOrDefault(student.Id))).FontSize(6.5f);
                            idx++;
                        }
                    });

                    col.Item().PaddingTop(6).Text(
                        "Bileşen puanları 100'lük sisteme çevrilmiştir. 1. ve 2. Performans puanları Excel formülleriyle hesaplanır. " +
                        "Grup puanları, orijinal ölçütlere maksimum puanları oranında dağıtılmıştır.").FontSize(7.5f);
                });
            });

            foreach (var comp in ex.Components)
            {
                doc.Page(page =>
                {
                    SetupPage(page, family, ex);
                    page.Content().Column(col =>
                    {
                        col.Spacing(6);
                        col.Item().Background(BrandColor).PaddingVertical(3).PaddingHorizontal(6)
                            .Text($"{comp.Component.Code} · {comp.Component.Name}  —  Toplam {Ui.N(comp.MaxPoints)} puan")
                            .FontColor(Colors.White).Bold().FontSize(9);

                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(ex.Students.Count > 26 ? 130 : 170);
                                cols.ConstantColumn(24);
                                foreach (var _ in ex.Students) cols.RelativeColumn();
                            });

                            table.Header(h =>
                            {
                                h.Cell().Element(HeaderStyle).Text("ÖLÇÜT").FontSize(6.5f).Bold();
                                h.Cell().Element(HeaderStyle).Text("Maks").FontSize(6.5f).Bold();
                                foreach (var student in ex.Students)
                                    h.Cell().Element(HeaderStyle).Text(ShortName(student.FullName)).FontSize(6.5f).Bold();
                            });

                            foreach (var crit in comp.Criteria)
                            {
                                table.Cell().Element(BodyStyle).Text(crit.Title).FontSize(6.5f);
                                table.Cell().Element(BodyStyle).Text(Ui.N(crit.MaxPoints)).FontSize(6.5f);
                                foreach (var student in ex.Students)
                                {
                                    var v = crit.Earned.TryGetValue(student.Id, out var e) ? e : (double?)null;
                                    table.Cell().Element(BodyStyle).Text(Fmt(v)).FontSize(6.5f);
                                }
                            }

                            table.Cell().ColumnSpan(2).Element(TotalStyle).Text("TOPLAM").Bold().FontSize(6.5f);
                            foreach (var student in ex.Students)
                            {
                                var sum = comp.Criteria
                                    .Where(c => c.Earned.ContainsKey(student.Id))
                                    .Sum(c => c.Earned[student.Id]);
                                table.Cell().Element(TotalStyle).Text(Ui.N(sum)).Bold().FontSize(6.5f);
                            }

                            table.Cell().ColumnSpan(2).Element(TotalStyle).Text("BİLEŞEN PUANI (100)").Bold().FontSize(6.5f);
                            foreach (var student in ex.Students)
                            {
                                var s = comp.Score.GetValueOrDefault(student.Id);
                                table.Cell().Element(TotalStyle).Text(s?.ToString(Tr) ?? "—").Bold().FontSize(6.5f);
                            }
                        });
                    });
                });
            }
        }).GeneratePdf();
    }

    private static void SetupPage(PageDescriptor page, string family, ClassExport ex)
    {
        var wide = ex.Students.Count > 26;
        page.Size(wide ? PageSizes.A3.Landscape() : PageSizes.A4.Landscape());
        page.Margin(14);
        page.DefaultTextStyle(x => x.FontFamily(family).FontSize(7.5f));
        page.PageColor(Colors.White);

        page.Header().Column(header =>
        {
            if (!string.IsNullOrWhiteSpace(SchoolName))
                header.Item().Text(SchoolName).FontSize(9).Bold();
            header.Item().Text($"{ex.Class.Name} Sınıfı · {AcademicYear} · {Term}")
                .FontSize(8).FontColor(Colors.Grey.Darken1);
            header.Item().PaddingTop(2).LineHorizontal(0.8f).LineColor(BrandColor);
        });

        page.Footer().AlignCenter().Text(text =>
        {
            text.DefaultTextStyle(t => t.FontSize(7).FontColor(Colors.Grey.Medium));
            text.Span("Sayfa ");
            text.CurrentPageNumber();
            text.Span(" / ");
            text.TotalPages();
        });
    }

    private static IContainer HeaderStyle(IContainer container) =>
        container.Background(HeaderFill).BorderBottom(0.6f).BorderColor(BrandColor)
            .PaddingVertical(2).PaddingHorizontal(2)
            .DefaultTextStyle(t => t.FontColor("#134E4A"));

    private static IContainer BodyStyle(IContainer container) =>
        container.BorderBottom(0.4f).BorderColor(LineColor).PaddingVertical(1.2f).PaddingHorizontal(2);

    private static IContainer TotalStyle(IContainer container) =>
        container.Background(TotalFill).BorderTop(0.6f).BorderColor(BrandColor)
            .PaddingVertical(1.5f).PaddingHorizontal(2);

    private static IContainer BlueStyle(IContainer container) =>
        container.Background("#EFF6FF").PaddingVertical(1.2f).PaddingHorizontal(2);

    private static IContainer PurpleStyle(IContainer container) =>
        container.Background("#F5F3FF").PaddingVertical(1.2f).PaddingHorizontal(2);

    private static string ShortName(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 2) return fullName;
        return parts[0] + " " + parts[^1];
    }

    private static string Fmt(double? value) => value is null ? "—" : Ui.N(Math.Round(value.Value, 2));
}
