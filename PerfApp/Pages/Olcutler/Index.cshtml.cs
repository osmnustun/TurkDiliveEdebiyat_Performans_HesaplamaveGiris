using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PerfApp.Data;
using PerfApp.Models;

namespace PerfApp.Pages.Olcutler;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;
    public IndexModel(AppDbContext db) => _db = db;

    public List<AssessmentComponent> Components { get; private set; } = new();
    public int Grade { get; private set; }

    public async Task OnGetAsync(int? grade)
    {
        Grade = grade ?? 9;
        Components = await _db.Components
            .Include(c => c.Groups)
            .Include(c => c.Criteria)
            .Where(c => c.Grade == Grade && c.Term == 1)
            .OrderBy(c => c.Order)
            .ToListAsync();
    }
}
