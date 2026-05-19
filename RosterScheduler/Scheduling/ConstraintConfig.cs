using RosterScheduler.Models;

namespace RosterScheduler.Scheduling;

public class ConstraintConfig
{
    /// <summary>Q2 2026 Sundays (13 total)</summary>
    public static readonly List<DateOnly> Q2Sundays = new()
    {
        new DateOnly(2026, 4, 5),
        new DateOnly(2026, 4, 12),
        new DateOnly(2026, 4, 19),
        new DateOnly(2026, 4, 26),
        new DateOnly(2026, 5, 3),
        new DateOnly(2026, 5, 10),
        new DateOnly(2026, 5, 17),
        new DateOnly(2026, 5, 24),
        new DateOnly(2026, 5, 31),
        new DateOnly(2026, 6, 7),
        new DateOnly(2026, 6, 14),
        new DateOnly(2026, 6, 21),
        new DateOnly(2026, 6, 28),
    };

    public static readonly List<Session> AllSessions = new() { Session.第一堂, Session.第二堂 };

    /// <summary>
    /// Ordered role configs — scheduler fills in this order per Sunday/session.
    /// Optional=true means unfilled slots are warnings, not hard failures.
    /// </summary>
    public static readonly List<RoleConfig> RoleConfigs = new()
    {
        new RoleConfig { RoleName = "領會",     MinCount = 1, MaxCount = 1 },
        new RoleConfig { RoleName = "鋼琴",     MinCount = 1, MaxCount = 1 },
        new RoleConfig { RoleName = "鼓",       MinCount = 1, MaxCount = 1 },
        new RoleConfig { RoleName = "BASS",     MinCount = 1, MaxCount = 1 },
        new RoleConfig { RoleName = "吉他",     MinCount = 1, MaxCount = 1 },
        new RoleConfig { RoleName = "VOCAL",    MinCount = 1, MaxCount = 3 },
        new RoleConfig { RoleName = "音控",     MinCount = 1, MaxCount = 1 },
        new RoleConfig { RoleName = "導播",     MinCount = 1, MaxCount = 1 },
        new RoleConfig { RoleName = "字幕",     MinCount = 1, MaxCount = 1 },
        new RoleConfig { RoleName = "音控見習", MinCount = 0, MaxCount = 1, Optional = true },
        new RoleConfig { RoleName = "小提琴",   MinCount = 0, MaxCount = 1, Optional = true },
    };
}
