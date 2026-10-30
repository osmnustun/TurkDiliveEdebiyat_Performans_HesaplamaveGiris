using Microsoft.EntityFrameworkCore;
using PerfApp.Models;

namespace PerfApp.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<SchoolClass> SchoolClasses => Set<SchoolClass>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<AssessmentComponent> Components => Set<AssessmentComponent>();
    public DbSet<RubricGroup> Groups => Set<RubricGroup>();
    public DbSet<RubricCriterion> Criteria => Set<RubricCriterion>();
    public DbSet<GroupScore> GroupScores => Set<GroupScore>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<SchoolClass>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Student>().HasIndex(x => new { x.SchoolClassId, x.FullName });
        b.Entity<Student>().HasOne(x => x.SchoolClass).WithMany(x => x.Students)
            .HasForeignKey(x => x.SchoolClassId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<AssessmentComponent>().HasIndex(x => new { x.Grade, x.Term, x.Code }).IsUnique();
        b.Entity<RubricGroup>().HasOne(x => x.Component).WithMany(x => x.Groups)
            .HasForeignKey(x => x.ComponentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RubricCriterion>().HasOne(x => x.Component).WithMany(x => x.Criteria)
            .HasForeignKey(x => x.ComponentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RubricCriterion>().HasOne(x => x.Group).WithMany(x => x.Criteria)
            .HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<GroupScore>().HasIndex(x => new { x.StudentId, x.RubricGroupId, x.Term }).IsUnique();
        b.Entity<GroupScore>().HasOne(x => x.Student).WithMany(x => x.GroupScores)
            .HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<GroupScore>().HasOne(x => x.RubricGroup).WithMany(x => x.Scores)
            .HasForeignKey(x => x.RubricGroupId).OnDelete(DeleteBehavior.Cascade);
    }
}
