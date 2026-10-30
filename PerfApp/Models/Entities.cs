namespace PerfApp.Models;

public class SchoolClass
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Grade { get; set; }
    public string AcademicYear { get; set; } = "2026-2027";
    public List<Student> Students { get; set; } = new();
}

public class Student
{
    public int Id { get; set; }
    public int SchoolClassId { get; set; }
    public SchoolClass? SchoolClass { get; set; }
    public int? Number { get; set; }
    public string FullName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public List<GroupScore> GroupScores { get; set; } = new();
}

public class AssessmentComponent
{
    public int Id { get; set; }
    public int Grade { get; set; }
    public int Term { get; set; } = 1;
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "P1";
    public int Order { get; set; }
    public List<RubricGroup> Groups { get; set; } = new();
    public List<RubricCriterion> Criteria { get; set; } = new();
}

public class RubricGroup
{
    public int Id { get; set; }
    public int ComponentId { get; set; }
    public AssessmentComponent? Component { get; set; }
    public int Order { get; set; }
    public string Name { get; set; } = "";
    public List<RubricCriterion> Criteria { get; set; } = new();
    public List<GroupScore> Scores { get; set; } = new();
}

public class RubricCriterion
{
    public int Id { get; set; }
    public int ComponentId { get; set; }
    public AssessmentComponent? Component { get; set; }
    public int? GroupId { get; set; }
    public RubricGroup? Group { get; set; }
    public int Order { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public double MaxPoints { get; set; }
}

public class GroupScore
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public Student? Student { get; set; }
    public int RubricGroupId { get; set; }
    public RubricGroup? RubricGroup { get; set; }
    public int Term { get; set; } = 1;
    public int Score { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
