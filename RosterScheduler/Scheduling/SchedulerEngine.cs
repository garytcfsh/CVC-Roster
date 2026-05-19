using RosterScheduler.Models;

namespace RosterScheduler.Scheduling;

public class SchedulerEngine
{
    private readonly List<Member> _members;
    private readonly List<CoupleConstraint> _coupleConstraints;
    private readonly Random _rng = new(42); // fixed seed for reproducibility

    public SchedulerEngine(List<Member> members, List<CoupleConstraint> coupleConstraints)
    {
        _members = members;
        _coupleConstraints = coupleConstraints;
    }

    public ScheduleResult Schedule()
    {
        var result = new ScheduleResult();
        // Track: member name -> total assignments this season
        var seasonCount = _members.ToDictionary(m => m.Name, _ => 0, StringComparer.OrdinalIgnoreCase);
        // Track: (date, session) -> set of assigned member names (one role per person per slot)
        var slotAssigned = new Dictionary<(DateOnly, Session), HashSet<string>>();

        foreach (var date in ConstraintConfig.Q2Sundays)
        {
            foreach (var session in ConstraintConfig.AllSessions)
            {
                var key = (date, session);
                slotAssigned[key] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Build a tentative assignment for this date/session
                var tentative = new List<Assignment>();
                var tentativeUnfilled = new List<ServiceSlot>();

                foreach (var roleConfig in ConstraintConfig.RoleConfigs)
                {
                    int filled = 0;
                    for (int idx = 0; idx < roleConfig.MaxCount; idx++)
                    {
                        var slot = new ServiceSlot
                        {
                            Date = date,
                            Session = session,
                            Role = roleConfig.RoleName,
                            RoleIndex = idx,
                        };

                        var member = PickMember(slot, slotAssigned[key], seasonCount, fairnessPass: true);
                        if (member == null && !roleConfig.Optional)
                        {
                            // Two-pass: retry without fairness constraint
                            member = PickMember(slot, slotAssigned[key], seasonCount, fairnessPass: false);
                        }

                        if (member != null)
                        {
                            tentative.Add(new Assignment { Member = member, Slot = slot });
                            slotAssigned[key].Add(member.Name);
                            filled++;
                        }
                        else
                        {
                            // Only add unfilled if we haven't met minimum yet
                            if (filled < roleConfig.MinCount)
                                tentativeUnfilled.Add(slot);
                            // Stop trying more slots once we have at least 1 for optional extras
                            if (idx >= roleConfig.MinCount - 1) break;
                        }
                    }
                }

                // Apply couple constraints: verify pairs are co-assigned on this date/session
                ApplyCoupleConstraints(tentative, tentativeUnfilled, result.Warnings, slotAssigned[key], seasonCount, date, session);

                // Commit tentative assignments
                foreach (var a in tentative)
                {
                    result.Assignments.Add(a);
                    seasonCount[a.Member.Name]++;
                }
                result.UnfilledSlots.AddRange(tentativeUnfilled);
            }
        }

        return result;
    }

    private Member? PickMember(
        ServiceSlot slot,
        HashSet<string> alreadyAssignedThisSlot,
        Dictionary<string, int> seasonCount,
        bool fairnessPass)
    {
        var candidates = _members.Where(m =>
            // Has this role
            m.Roles.Contains(slot.Role, StringComparer.OrdinalIgnoreCase) &&
            // Serves in this session
            m.AvailableSessions.Contains(slot.Session) &&
            // Not blocked by role-specific session restriction
            IsRoleSessionAllowed(m, slot.Role, slot.Session) &&
            // Not unavailable this date
            !m.UnavailableDates.Contains(slot.Date) &&
            // Not already assigned something else this slot
            !alreadyAssignedThisSlot.Contains(m.Name) &&
            // Has remaining budget
            seasonCount[m.Name] < m.MaxAssignments
        ).ToList();

        if (candidates.Count == 0) return null;

        if (fairnessPass)
        {
            int minCount = candidates.Min(m => seasonCount[m.Name]);
            candidates = candidates.Where(m => seasonCount[m.Name] == minCount).ToList();
        }

        // Shuffle to break ties randomly (but reproducibly)
        return candidates.OrderBy(_ => _rng.Next()).First();
    }

    private bool IsRoleSessionAllowed(Member member, string role, Session session)
    {
        if (member.RoleSessionRestrictions.TryGetValue(role, out var allowedSessions))
        {
            if (allowedSessions != null && !allowedSessions.Contains(session))
                return false;
        }
        return true;
    }

    private void ApplyCoupleConstraints(
        List<Assignment> tentative,
        List<ServiceSlot> unfilled,
        List<string> warnings,
        HashSet<string> slotAssigned,
        Dictionary<string, int> seasonCount,
        DateOnly date,
        Session session)
    {
        foreach (var couple in _coupleConstraints)
        {
            bool aAssigned = tentative.Any(a =>
                string.Equals(a.Member.Name, couple.MemberA, StringComparison.OrdinalIgnoreCase));
            bool bAssigned = tentative.Any(a =>
                string.Equals(a.Member.Name, couple.MemberB, StringComparison.OrdinalIgnoreCase));

            if (aAssigned && !bAssigned)
            {
                // A is assigned but B is not — violates couple constraint
                // Check if B was even available
                var memberB = _members.FirstOrDefault(m =>
                    string.Equals(m.Name, couple.MemberB, StringComparison.OrdinalIgnoreCase));
                if (memberB != null)
                {
                    warnings.Add($"[配對衝突] {date:yyyy/M/d} {session}: {couple.MemberA} 已排班但 {couple.MemberB} 無法同排（移除 {couple.MemberA} 的排班）");
                    // Remove A from tentative to honor hard constraint
                    var toRemove = tentative.FirstOrDefault(a =>
                        string.Equals(a.Member.Name, couple.MemberA, StringComparison.OrdinalIgnoreCase));
                    if (toRemove != null)
                    {
                        tentative.Remove(toRemove);
                        slotAssigned.Remove(toRemove.Member.Name);
                        unfilled.Add(toRemove.Slot);
                    }
                }
            }
            else if (!aAssigned && bAssigned)
            {
                var memberA = _members.FirstOrDefault(m =>
                    string.Equals(m.Name, couple.MemberA, StringComparison.OrdinalIgnoreCase));
                if (memberA != null)
                {
                    warnings.Add($"[配對衝突] {date:yyyy/M/d} {session}: {couple.MemberB} 已排班但 {couple.MemberA} 無法同排（移除 {couple.MemberB} 的排班）");
                    var toRemove = tentative.FirstOrDefault(a =>
                        string.Equals(a.Member.Name, couple.MemberB, StringComparison.OrdinalIgnoreCase));
                    if (toRemove != null)
                    {
                        tentative.Remove(toRemove);
                        slotAssigned.Remove(toRemove.Member.Name);
                        unfilled.Add(toRemove.Slot);
                    }
                }
            }
        }
    }
}
