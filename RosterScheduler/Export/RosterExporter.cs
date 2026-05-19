using ClosedXML.Excel;
using RosterScheduler.Models;
using RosterScheduler.Scheduling;

namespace RosterScheduler.Export;

public class RosterExporter
{
    public void Export(ScheduleResult result, string outputPath)
    {
        using var wb = new XLWorkbook();
        WriteRosterSheet(wb, result);
        WriteUnfilledSheet(wb, result);
        WriteWarningsSheet(wb, result);
        wb.SaveAs(outputPath);
    }

    private void WriteRosterSheet(XLWorkbook wb, ScheduleResult result)
    {
        var ws = wb.Worksheets.Add("服事表");

        // Build column headers: Session + Role combinations
        var roleConfigs = ConstraintConfig.RoleConfigs;
        // Header row 1: date
        // Header row 2: session | role1 | role2 | ...
        // We create one block per session
        var sessions = ConstraintConfig.AllSessions;

        // Build header columns: (session, role, roleIndex) 
        var columns = new List<(Session Session, RoleConfig RoleConfig, int Index)>();
        foreach (var session in sessions)
        {
            foreach (var role in roleConfigs)
            {
                for (int i = 0; i < role.MaxCount; i++)
                {
                    columns.Add((session, role, i));
                }
            }
        }

        // Row 1: column headers
        ws.Cell(1, 1).Value = "主日日期";
        ws.Cell(1, 1).Style.Font.Bold = true;
        for (int c = 0; c < columns.Count; c++)
        {
            var (sess, roleConfig, idx) = columns[c];
            var label = roleConfig.MaxCount > 1 ? $"{sess} {roleConfig.RoleName} {idx + 1}" : $"{sess} {roleConfig.RoleName}";
            ws.Cell(1, c + 2).Value = label;
            ws.Cell(1, c + 2).Style.Font.Bold = true;
        }

        // Data rows
        int row = 2;
        foreach (var date in ConstraintConfig.Q2Sundays)
        {
            ws.Cell(row, 1).Value = date.ToString("yyyy/M/d");

            for (int c = 0; c < columns.Count; c++)
            {
                var (sess, roleConfig2, idx) = columns[c];
                var assignment = result.Assignments.FirstOrDefault(a =>
                    a.Slot.Date == date &&
                    a.Slot.Session == sess &&
                    a.Slot.Role == roleConfig2.RoleName &&
                    a.Slot.RoleIndex == idx);

                ws.Cell(row, c + 2).Value = assignment?.Member.Name ?? "";
            }
            row++;
        }

        ws.Columns().AdjustToContents();
    }

    private void WriteUnfilledSheet(XLWorkbook wb, ScheduleResult result)
    {
        var ws = wb.Worksheets.Add("未填滿崗位");
        ws.Cell(1, 1).Value = "日期";
        ws.Cell(1, 2).Value = "場次";
        ws.Cell(1, 3).Value = "崗位";
        ws.Row(1).Style.Font.Bold = true;

        int row = 2;
        foreach (var slot in result.UnfilledSlots.OrderBy(s => s.Date).ThenBy(s => s.Session).ThenBy(s => s.Role))
        {
            ws.Cell(row, 1).Value = slot.Date.ToString("yyyy/M/d");
            ws.Cell(row, 2).Value = slot.Session.ToString();
            ws.Cell(row, 3).Value = slot.Role;
            row++;
        }

        if (result.UnfilledSlots.Count == 0)
            ws.Cell(2, 1).Value = "（所有崗位已填滿）";

        ws.Columns().AdjustToContents();
    }

    private void WriteWarningsSheet(XLWorkbook wb, ScheduleResult result)
    {
        var ws = wb.Worksheets.Add("警告備註");
        ws.Cell(1, 1).Value = "警告訊息（需人工確認）";
        ws.Cell(1, 1).Style.Font.Bold = true;

        int row = 2;
        foreach (var warning in result.Warnings)
        {
            ws.Cell(row, 1).Value = warning;
            row++;
        }

        if (result.Warnings.Count == 0)
            ws.Cell(2, 1).Value = "（無警告）";

        ws.Column(1).Width = 80;
    }
}
