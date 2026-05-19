using System.Text.RegularExpressions;
using RosterScheduler.Models;

namespace RosterScheduler.Parsing;

public class DataNormalizer
{
    // Known roles (canonical names)
    private static readonly HashSet<string> KnownRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "音控", "導播", "音控見習", "領會", "吉他", "VOCAL", "字幕", "鋼琴", "BASS", "鼓", "小提琴"
    };

    // Couple/pair keywords in notes
    private static readonly string[] CoupleKeywords = { "夫妻一起", "跟.*排一起", "一起服事" };

    public (List<Member> Members, List<CoupleConstraint> CoupleConstraints, List<string> Warnings)
        Normalize(List<RawSurveyRow> rows)
    {
        var warnings = new List<string>();
        // Key: normalized name, Value: list of rows (we'll merge later)
        var byName = new Dictionary<string, List<RawSurveyRow>>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            // Split compound names like "吳鈞華/陳莉雯"
            var names = SplitCompoundNames(row.Name);
            foreach (var name in names)
            {
                var key = name.Trim();
                if (!byName.ContainsKey(key)) byName[key] = new List<RawSurveyRow>();
                byName[key].Add(row);
            }
        }

        var members = new List<Member>();
        var coupleConstraints = new List<CoupleConstraint>();
        var detectedCouples = new HashSet<string>();

        foreach (var (name, memberRows) in byName)
        {
            // Sort by timestamp descending; latest row wins for scalar fields
            var sorted = memberRows.OrderByDescending(r => r.Timestamp).ToList();
            var latest = sorted[0];

            var member = new Member
            {
                Name = name,
                Notes = latest.Notes,
                Email = latest.Email,
                MaxAssignments = int.MaxValue,
            };

            // Merge sessions (union across all rows)
            foreach (var row in sorted)
                foreach (var s in ParseSessions(row.Sessions))
                    member.AvailableSessions.Add(s);

            // Merge roles (union) and extract role-session restrictions
            foreach (var row in sorted)
            {
                var (roles, restrictions, roleWarnings) = ParseRoles(row.Roles, name);
                foreach (var r in roles) member.Roles.Add(r);
                foreach (var (role, sessions) in restrictions)
                    member.RoleSessionRestrictions[role] = sessions;
                warnings.AddRange(roleWarnings);
            }

            // Merge unavailable dates (union)
            foreach (var row in sorted)
            {
                var (dates, dateWarnings) = ParseUnavailableDates(row.UnavailableDates, name);
                foreach (var d in dates) member.UnavailableDates.Add(d);
                warnings.AddRange(dateWarnings);
            }

            // Extract MaxAssignments from notes
            member.MaxAssignments = ParseMaxAssignments(latest.Notes, warnings, name);

            // Detect couple/pair constraints from notes
            var couplePartner = DetectCoupleConstraint(name, latest.Notes);
            if (couplePartner != null)
            {
                var key = string.Compare(name, couplePartner, StringComparison.OrdinalIgnoreCase) < 0
                    ? $"{name}|{couplePartner}"
                    : $"{couplePartner}|{name}";
                if (!detectedCouples.Contains(key))
                {
                    detectedCouples.Add(key);
                    coupleConstraints.Add(new CoupleConstraint { MemberA = name, MemberB = couplePartner });
                }
            }

            members.Add(member);
        }

        // Also add couple constraints detected from compound name rows
        AddCoupleConstraintsFromCompoundNames(rows, coupleConstraints, detectedCouples);

        // Warn about unrecognized notes that may contain hidden constraints
        foreach (var member in members)
        {
            if (!string.IsNullOrWhiteSpace(member.Notes) &&
                member.MaxAssignments == int.MaxValue &&
                DetectCoupleConstraint(member.Name, member.Notes) == null)
            {
                warnings.Add($"[人工確認] {member.Name} 備註：{member.Notes}");
            }
        }

        return (members, coupleConstraints, warnings);
    }

    private List<string> SplitCompoundNames(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<string> { raw };
        // Split on / or ／ or 、 or & or and
        var parts = Regex.Split(raw, @"[/／、&]|\band\b").Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        return parts.Count > 0 ? parts : new List<string> { raw.Trim() };
    }

    private List<Session> ParseSessions(string raw)
    {
        var result = new List<Session>();
        if (string.IsNullOrWhiteSpace(raw)) return result;
        if (raw.Contains("第一堂")) result.Add(Session.第一堂);
        if (raw.Contains("第二堂")) result.Add(Session.第二堂);
        return result;
    }

    private (List<string> Roles, Dictionary<string, HashSet<Session>?> Restrictions, List<string> Warnings)
        ParseRoles(string raw, string memberName)
    {
        var roles = new List<string>();
        var restrictions = new Dictionary<string, HashSet<Session>?>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(raw)) return (roles, restrictions, warnings);

        // Special case: 吳永旭's "導播, 暫時無法配合第二堂的音控" pattern
        // Pattern: "暫時無法配合X堂的{role}" → add role with session restriction
        var restrictionMatch = Regex.Match(raw, @"暫時無法配合(第[一二])堂的(\S+)");
        if (restrictionMatch.Success)
        {
            var restrictedSession = restrictionMatch.Groups[1].Value == "第一" ? Session.第一堂 : Session.第二堂;
            var restrictedRole = restrictionMatch.Groups[2].Value;
            var normalizedRestricted = NormalizeRoleName(restrictedRole);
            if (normalizedRestricted != null)
            {
                // Only allow the OTHER session for this role
                var allowed = restrictedSession == Session.第一堂
                    ? new HashSet<Session> { Session.第二堂 }
                    : new HashSet<Session> { Session.第一堂 };
                restrictions[normalizedRestricted] = allowed;
                // Remove the restriction text before parsing other roles
                raw = raw.Replace(restrictionMatch.Value, "").Trim().TrimEnd(',').Trim();
            }
        }

        // Split on comma variants
        var parts = raw.Split(new[] { ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            var normalized = NormalizeRoleName(trimmed);
            if (normalized != null)
            {
                roles.Add(normalized);
            }
            else if (!string.IsNullOrWhiteSpace(trimmed))
            {
                warnings.Add($"[人工確認] {memberName} 的崗位「{trimmed}」無法辨識，已略過");
            }
        }

        return (roles, restrictions, warnings);
    }

    private string? NormalizeRoleName(string raw)
    {
        var trimmed = raw.Trim();
        // Direct match (case-insensitive)
        foreach (var known in KnownRoles)
            if (string.Equals(trimmed, known, StringComparison.OrdinalIgnoreCase))
                return known;

        // Contains match (handles trailing/leading noise)
        foreach (var known in KnownRoles)
            if (trimmed.Contains(known, StringComparison.OrdinalIgnoreCase))
                return known;

        return null;
    }

    private (HashSet<DateOnly> Dates, List<string> Warnings) ParseUnavailableDates(string raw, string memberName)
    {
        var dates = new HashSet<DateOnly>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(raw)) return (dates, warnings);

        // "皆可配合" or similar → no restrictions
        if (raw.Contains("皆可") || raw.Contains("都可")) return (dates, warnings);

        // Extract date patterns: M/D or M/DD (with optional annotation in parentheses)
        var matches = Regex.Matches(raw, @"(\d{1,2})/(\d{1,2})");
        foreach (Match m in matches)
        {
            int month = int.Parse(m.Groups[1].Value);
            int day = int.Parse(m.Groups[2].Value);
            try
            {
                // Q2 2026: months 4-6
                int year = 2026;
                dates.Add(new DateOnly(year, month, day));
            }
            catch
            {
                warnings.Add($"[人工確認] {memberName} 的不可用日期「{m.Value}」解析失敗");
            }
        }

        return (dates, warnings);
    }

    private int ParseMaxAssignments(string notes, List<string> warnings, string memberName)
    {
        if (string.IsNullOrWhiteSpace(notes)) return int.MaxValue;

        // Patterns: "不超過N次", "希望X次", "各一次", "一次就好"
        var match = Regex.Match(notes, @"不超過\s*(\d+)\s*次");
        if (match.Success) return int.Parse(match.Groups[1].Value);

        match = Regex.Match(notes, @"希望.*?(\d+)\s*次");
        if (match.Success) return int.Parse(match.Groups[1].Value);

        match = Regex.Match(notes, @"各一次|一次就好|排一次");
        if (match.Success) return 1;

        match = Regex.Match(notes, @"各(\d+)次");
        if (match.Success) return int.Parse(match.Groups[1].Value);

        return int.MaxValue;
    }

    private string? DetectCoupleConstraint(string memberName, string notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return null;

        // Pattern: "跟X排一起" or "與X一起" 
        var match = Regex.Match(notes, @"[跟與](\S+?)[排一]");
        if (match.Success)
        {
            var partner = match.Groups[1].Value.Trim();
            if (partner != memberName && partner.Length > 0)
                return partner;
        }

        // Pattern: "如果可以的話跟X排一起"
        match = Regex.Match(notes, @"跟(\S{2,4})排");
        if (match.Success)
        {
            var partner = match.Groups[1].Value.Trim();
            if (partner != memberName) return partner;
        }

        return null;
    }

    private void AddCoupleConstraintsFromCompoundNames(
        List<RawSurveyRow> rows,
        List<CoupleConstraint> constraints,
        HashSet<string> existing)
    {
        foreach (var row in rows)
        {
            var names = SplitCompoundNames(row.Name);
            if (names.Count >= 2)
            {
                // Also check if notes say "夫妻一起"
                bool isCouple = !string.IsNullOrWhiteSpace(row.Notes) && row.Notes.Contains("夫妻");
                isCouple = isCouple || names.Count == 2; // compound name = treat as pair

                if (isCouple)
                {
                    for (int i = 0; i < names.Count - 1; i++)
                    {
                        var a = names[i].Trim();
                        var b = names[i + 1].Trim();
                        var key = string.Compare(a, b, StringComparison.OrdinalIgnoreCase) < 0
                            ? $"{a}|{b}" : $"{b}|{a}";
                        if (!existing.Contains(key))
                        {
                            existing.Add(key);
                            constraints.Add(new CoupleConstraint { MemberA = a, MemberB = b });
                        }
                    }
                }
            }
        }
    }
}
