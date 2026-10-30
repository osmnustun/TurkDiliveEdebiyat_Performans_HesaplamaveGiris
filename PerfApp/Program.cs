using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using PerfApp.Data;
using PerfApp.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 20 * 1024 * 1024);

var dataFolder = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(Path.Combine(dataFolder, "uploads"));
var dbPath = Path.Combine(dataFolder, "perf.db");

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddScoped<StudentImportService>();

var app = builder.Build();

// Okul adı (isteğe bağlı) appsettings "OkulAdi" anahtarından okunur; boşsa çıktılarda gösterilmez.
ExportService.SchoolName = builder.Configuration["OkulAdi"] ?? "";

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    var importer = services.GetRequiredService<StudentImportService>();
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Seed");
    await SeedService.InitializeAsync(db, app.Environment, importer, logger);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

app.Run();
