using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace PerfApp.Services;

/// <summary>Bir sınıf düzeyi için performans şablonu dosyası.</summary>
public sealed record PerformanceTemplate(string FileName, string Label);

/// <summary>
/// Orijinal performans ölçeği Excel dosyalarını (boşaltılmış kopyalarını) şablon olarak
/// kullanır: öğrenci listesini ve ölçüt puanlarını doldurur; formüller, renkler ve
/// sayfa düzeni aynen korunur (birebir çıktı).
/// </summary>
public static class TemplateExportService
{
    public static string TemplatesRoot { get; set; } = "";

    private static readonly Dictionary<int, PerformanceTemplate[]> Map = new()
    {
        [9] = new[] { new PerformanceTemplate("9.xlsx", "") },
        [10] = new[]
        {
            new PerformanceTemplate("10-1.xlsx", "1. Performans"),
            new PerformanceTemplate("10-2.xlsx", "2. Performans")
        },
        [11] = new[] { new PerformanceTemplate("11.xlsx", "") }
    };

    private static readonly Regex SumRegex = new(
        @"SUM\(\s*\$?[A-Z]{1,3}\$?(\d+)\s*:\s*\$?[A-Z]{1,3}\$?(\d+)\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ClassNameRegex = new(
        @"(?<!\d)(\d{1,2})\s*/\s*([A-ZÇĞİÖŞÜ])(?![A-Za-zÇĞİÖŞÜ])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Düzeyin şablon(lar)ını döndürür; dosya yoksa boş liste.</summary>
    public static IReadOnlyList<PerformanceTemplate> AvailableFor(int grade) =>
        Map.TryGetValue(grade, out var list)
            ? list.Where(t => File.Exists(Path.Combine(TemplatesRoot, t.FileName))).ToList()
            : Array.Empty<PerformanceTemplate>();

    public static string OutputFileName(string className, PerformanceTemplate t)
    {
        var safe = className.Replace('/', '-').Replace('\\', '-').Trim();
        return string.IsNullOrEmpty(t.Label) ? $"{safe}.xlsx" : $"{safe} - {t.Label}.xlsx";
    }

    /// <summary>Şablonu sınıf verisiyle doldurur.</summary>
    public static bool TryBuildFilled(PerformanceTemplate template, ClassExport ex, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        var path = Path.Combine(TemplatesRoot, template.FileName);
        if (!File.Exists(path)) return false;

        using var wb = new XLWorkbook(path);

        var info = FindStudentSheet(wb);
        if (info is null) return false;

        WriteRoster(info.Sheet, info.HeaderRow, info.NumCol, info.NameCol, info.SiraCol, ex.Students);

        var oldName = DetectClassName(wb);
        if (!string.IsNullOrEmpty(oldName) &&
            !string.Equals(oldName, ex.Class.Name, StringComparison.OrdinalIgnoreCase))
        {
            ReplaceText(wb, oldName!, ex.Class.Name);
        }

        foreach (var comp in ex.Components)
        {
            var ws = FindComponentSheet(wb, comp.Component.Name);
            if (ws is null) continue;
            if (!TryFindTotal(ws, out var sumStart, out var sumEnd, out var firstCol, out var colCount)) continue;

            var criteria = comp.Criteria.ToList();
            var rowCount = sumEnd - sumStart + 1;

            for (var i = 0; i < ex.Students.Count && i < colCount; i++)
            {
                var col = firstCol + i;
                var student = ex.Students[i];
                var scored = comp.Score.TryGetValue(student.Id, out var compScore) && compScore is not null;

                for (var k = 0; k < criteria.Count && k < rowCount; k++)
                {
                    var cell = ws.Cell(sumStart + k, col);
                    if (scored && criteria[k].Earned.TryGetValue(student.Id, out var earned))
                        cell.Value = Math.Round(earned, 2);
                    else
                        cell.Clear(XLClearOptions.Contents);
                }
            }
        }

        wb.CalculateMode = XLCalculateMode.Auto;
        try { wb.RecalculateAllFormulas(); } catch { /* formüller açılışta hesaplanır */ }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        bytes = ms.ToArray();
        return true;
    }

    // ---- Öğrenci bilgileri sayfası ------------------------------------------------

    private sealed record StudentSheet(IXLWorksheet Sheet, int HeaderRow, int NumCol, int NameCol, int SiraCol);

    private static StudentSheet? FindStudentSheet(XLWorkbook wb)
    {
        foreach (var ws in wb.Worksheets)
        {
            var last = Math.Min(ws.LastRowUsed()?.RowNumber() ?? 0, 12);
            for (var r = 1; r <= last; r++)
            {
                var numCol = 0; var nameCol = 0; var siraCol = 0;
                foreach (var cell in ws.Row(r).CellsUsed())
                {
                    var t = Norm(cell.GetString());
                    if (t.Length == 0) continue;
                    if (t.Contains("OGRENCINO") || (t.Contains("NO") && t.Contains("OGRENCI") && !t.Contains("SIRA")))
                        numCol = cell.Address.ColumnNumber;
                    if (t.Contains("ADSOYAD") || t.Contains("ADI")) nameCol = cell.Address.ColumnNumber;
                    if (t.Contains("SIRA")) siraCol = cell.Address.ColumnNumber;
                }
                if (numCol > 0 && nameCol > 0)
                    return new StudentSheet(ws, r, numCol, nameCol, siraCol);
            }
        }
        return null;
    }

    private static void WriteRoster(IXLWorksheet ws, int headerRow, int numCol, int nameCol, int siraCol,
        IReadOnlyList<Models.Student> students)
    {
        var start = headerRow + 1;
        var lastRow = Math.Max(ws.LastRowUsed()?.RowNumber() ?? start, start);

        for (var i = 0; i < students.Count; i++)
        {
            var r = start + i;
            var st = students[i];
            if (siraCol > 0) ws.Cell(r, siraCol).Value = i + 1;
            if (numCol > 0)
            {
                if (st.Number is not null) ws.Cell(r, numCol).Value = st.Number.Value;
                else ws.Cell(r, numCol).Clear(XLClearOptions.Contents);
            }
            ws.Cell(r, nameCol).Value = st.FullName;
        }

        for (var r = start + students.Count; r <= lastRow; r++)
        {
            foreach (var c in new[] { siraCol, numCol, nameCol })
                if (c > 0) ws.Cell(r, c).Clear(XLClearOptions.Contents);
        }
    }

    // ---- Bileşen sayfaları --------------------------------------------------------

    private static IXLWorksheet? FindComponentSheet(XLWorkbook wb, string componentName)
    {
        var target = Norm(componentName);
        if (target.Length < 4) return null;

        IXLWorksheet? best = null; var bestLen = 0;
        foreach (var ws in wb.Worksheets)
        {
            var n = Norm(ws.Name);
            if (n.Length < 4) continue;

            var match = n == target || target.StartsWith(n, StringComparison.Ordinal)
                        || n.StartsWith(target, StringComparison.Ordinal);
            if (!match) continue;

            if (n.Length > bestLen) { best = ws; bestLen = n.Length; }
        }
        return best;
    }

    private static bool TryFindTotal(IXLWorksheet ws, out int sumStart, out int sumEnd, out int firstCol, out int colCount)
    {
        sumStart = sumEnd = firstCol = colCount = 0;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

        for (var r = 1; r <= lastRow; r++)
        {
            var isTotal = false;
            for (var c = 1; c <= Math.Min(6, lastCol); c++)
            {
                var cell = ws.Cell(r, c);
                if (cell.HasFormula) continue;
                if (Norm(cell.GetString()) == "TOPLAM") { isTotal = true; break; }
            }
            if (!isTotal) continue;

            for (var c = 2; c <= lastCol; c++)
            {
                var cell = ws.Cell(r, c);
                if (!cell.HasFormula) continue;
                var m = SumRegex.Match(cell.FormulaA1);
                if (!m.Success) continue;

                firstCol = c;
                sumStart = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                sumEnd = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);

                var count = 0;
                for (var c2 = c; c2 <= lastCol; c2++)
                {
                    var cell2 = ws.Cell(r, c2);
                    if (cell2.HasFormula && SumRegex.IsMatch(cell2.FormulaA1)) count++;
                    else break;
                }
                colCount = count;
                return true;
            }
        }

        return false;
    }

    // ---- Sınıf adı ---------------------------------------------------------------

    private static string? DetectClassName(XLWorkbook wb)
    {
        foreach (var ws in wb.Worksheets)
        {
            var last = Math.Min(ws.LastRowUsed()?.RowNumber() ?? 0, 3);
            for (var r = 1; r <= last; r++)
            {
                foreach (var cell in ws.Row(r).CellsUsed())
                {
                    if (cell.DataType != XLDataType.Text) continue;
                    var m = ClassNameRegex.Match(cell.GetString());
                    if (m.Success)
                        return m.Groups[1].Value + "/" + m.Groups[2].Value.ToUpper(Tr);
                }
            }
        }
        return null;
    }

    private static void ReplaceText(XLWorkbook wb, string oldValue, string newValue)
    {
        foreach (var ws in wb.Worksheets)
        {
            foreach (var cell in ws.CellsUsed())
            {
                if (cell.HasFormula) continue;
                if (cell.DataType != XLDataType.Text) continue;
                var s = cell.GetString();
                if (s.Contains(oldValue, StringComparison.Ordinal))
                    cell.Value = s.Replace(oldValue, newValue, StringComparison.Ordinal);
            }
        }
    }

    private static string Norm(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var up = value.ToUpper(Tr);
        var sb = new StringBuilder(up.Length);
        foreach (var ch in up)
        {
            var c = ch switch
            {
                'Ç' => 'C',
                'Ğ' => 'G',
                'İ' => 'I',
                'I' => 'I',
                'Ö' => 'O',
                'Ş' => 'S',
                'Ü' => 'U',
                _ => ch
            };
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        }
        return sb.ToString();
    }
}
