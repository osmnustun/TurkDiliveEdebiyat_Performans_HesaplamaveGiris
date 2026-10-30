using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PerfApp.Data;
using PerfApp.Models;

namespace PerfApp.Services;

public static class SeedService
{
    public static async Task InitializeAsync(AppDbContext db, IWebHostEnvironment env, StudentImportService importer, ILogger logger)
    {
        await SeedRubricsAsync(db, env, logger);

        if (!await db.SchoolClasses.AnyAsync())
        {
            var folder = Directory.GetParent(env.ContentRootPath)?.FullName;
            if (folder is not null && Directory.Exists(folder))
            {
                logger.LogInformation("İlk kurulum: {Folder} klasöründeki Excel dosyalarından sınıflar alınıyor.", folder);
                var summary = await importer.ImportFolderAsync(db, folder);
                foreach (var file in summary.Files)
                    logger.LogInformation("Excel içe aktarıldı: {Class} (+{Added} öğrenci, toplam {Total})", file.ClassName, file.Added, file.Total);
                foreach (var skipped in summary.Skipped)
                    logger.LogWarning("Excel atlandı: {Reason}", skipped);
            }
        }
    }

    private static async Task SeedRubricsAsync(AppDbContext db, IWebHostEnvironment env, ILogger logger)
    {
        var path = Path.Combine(env.ContentRootPath, "Data", "rubrics.seed.json");
        if (!File.Exists(path))
        {
            logger.LogWarning("Ölçüt tohum dosyası bulunamadı: {Path}", path);
            return;
        }

        var json = await File.ReadAllTextAsync(path);
        var grades = JsonSerializer.Deserialize<List<GradeSeed>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (grades is null) return;

        foreach (var grade in grades.OrderBy(g => g.Grade))
        {
            foreach (var seed in grade.Components.OrderBy(c => c.Order))
            {
                var component = await db.Components
                    .Include(c => c.Criteria)
                    .Include(c => c.Groups)
                    .FirstOrDefaultAsync(c => c.Grade == grade.Grade && c.Term == 1 && c.Code == seed.Code);

                if (component is null)
                {
                    component = new AssessmentComponent
                    {
                        Grade = grade.Grade,
                        Term = 1,
                        Code = seed.Code,
                        Name = seed.Name,
                        Kind = seed.Kind,
                        Order = seed.Order
                    };
                    db.Components.Add(component);
                    await db.SaveChangesAsync();
                }

                if (component.Criteria.Count == 0)
                {
                    var order = 1;
                    foreach (var c in seed.Criteria)
                    {
                        component.Criteria.Add(new RubricCriterion
                        {
                            ComponentId = component.Id,
                            Order = order++,
                            Title = c.Title,
                            Description = c.Description ?? "",
                            MaxPoints = c.MaxPoints
                        });
                    }
                    await db.SaveChangesAsync();
                }

                if (component.Groups.Count == 0 && component.Criteria.Count > 0)
                    await RubricService.RegroupAsync(db, component);
            }
        }
    }

    private sealed class GradeSeed
    {
        public int Grade { get; set; }
        public List<ComponentSeed> Components { get; set; } = new();
    }

    private sealed class ComponentSeed
    {
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "P1";
        public int Order { get; set; }
        public List<CriterionSeed> Criteria { get; set; } = new();
    }

    private sealed class CriterionSeed
    {
        public string Title { get; set; } = "";
        public string? Description { get; set; }
        public double MaxPoints { get; set; }
    }
}
