using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PerfApp.Data;

namespace PerfApp.Pages;

public class IndexModel : PageModel
{
    private readonly AppDbContext _db;

    public IndexModel(AppDbContext db) => _db = db;

    public sealed record ClassVm(int Id, string Name, int Grade, int StudentCount, int Scored, int Expected)
    {
        public int Percent => Expected == 0 ? 0 : (int)Math.Round(Scored * 100.0 / Expected);
    }

    public List<ClassVm> Classes { get; } = new();
    public int StudentCount { get; private set; }
    public int Scored { get; private set; }
    public int Expected { get; private set; }
    public int Percent => Expected == 0 ? 0 : (int)Math.Round(Scored * 100.0 / Expected);

    public async Task OnGetAsync()
    {
        var classes = await _db.SchoolClasses
            .Include(c => c.Students)
            .OrderBy(c => c.Name)
            .ToListAsync();

        var components = await _db.Components
            .Include(c => c.Groups)
            .Where(c => c.Term == 1)
            .ToListAsync();

        var scorePairs = await _db.GroupScores
            .Where(s => s.Term == 1)
            .Select(s => new { s.StudentId, s.RubricGroupId })
            .ToListAsync();
        var scoredPairs = scorePairs.Select(s => (s.StudentId, s.RubricGroupId)).ToHashSet();

        foreach (var cls in classes)
        {
            var gradeComponents = components.Where(c => c.Grade == cls.Grade).ToList();
            var expected = cls.Students.Count * gradeComponents.Count;
            var done = 0;

            foreach (var student in cls.Students)
            {
                foreach (var component in gradeComponents)
                {
                    if (component.Groups.Count > 0 && component.Groups.All(g => scoredPairs.Contains((student.Id, g.Id))))
                        done++;
                }
            }

            Classes.Add(new ClassVm(cls.Id, cls.Name, cls.Grade, cls.Students.Count, done, expected));
            StudentCount += cls.Students.Count;
            Scored += done;
            Expected += expected;
        }
    }
}
