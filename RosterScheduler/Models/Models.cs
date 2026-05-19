namespace RosterScheduler.Models;

public enum Session { 第一堂, 第二堂 }

public class Member
{
    public string Name { get; set; } = "";
    public HashSet<Session> AvailableSessions { get; set; } = new();
    public HashSet<string> Roles { get; set; } = new();
    public HashSet<DateOnly> UnavailableDates { get; set; } = new();
    // Role-level session restrictions: role -> allowed sessions (null = all sessions)
    public Dictionary<string, HashSet<Session>?> RoleSessionRestrictions { get; set; } = new();
    public int MaxAssignments { get; set; } = int.MaxValue;
    public string Notes { get; set; } = "";
    public string Email { get; set; } = "";
}

public class RoleConfig
{
    public string RoleName { get; set; } = "";
    public int MinCount { get; set; } = 1;
    public int MaxCount { get; set; } = 1;
    /// <summary>If true, unfilled slots for this role don't count as violations</summary>
    public bool Optional { get; set; } = false;
}

public class ServiceSlot
{
    public DateOnly Date { get; set; }
    public Session Session { get; set; }
    public string Role { get; set; } = "";
    /// <summary>Index within the role (for roles with MaxCount > 1)</summary>
    public int RoleIndex { get; set; } = 0;
}

public class Assignment
{
    public Member Member { get; set; } = null!;
    public ServiceSlot Slot { get; set; } = null!;
}

public class CoupleConstraint
{
    public string MemberA { get; set; } = "";
    public string MemberB { get; set; } = "";
}

public class ScheduleResult
{
    public List<Assignment> Assignments { get; set; } = new();
    public List<ServiceSlot> UnfilledSlots { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
