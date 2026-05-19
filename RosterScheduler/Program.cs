using RosterScheduler.Export;
using RosterScheduler.Parsing;
using RosterScheduler.Scheduling;

// Parse CLI args: --input <path> --output <path>
string? inputPath = null;
string? outputPath = null;

for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--input")  inputPath  = args[i + 1];
    if (args[i] == "--output") outputPath = args[i + 1];
}

if (inputPath == null || outputPath == null)
{
    Console.Error.WriteLine("Usage: RosterScheduler --input <csv_path> --output <xlsx_path>");
    return 1;
}

if (!File.Exists(inputPath))
{
    Console.Error.WriteLine($"Input file not found: {inputPath}");
    return 1;
}

Console.WriteLine($"讀取問卷：{inputPath}");

// Phase 1: Parse CSV
var parser = new CsvParser();
var rows = parser.Parse(inputPath);
Console.WriteLine($"  → 讀取 {rows.Count} 筆問卷回填");

// Phase 2: Normalize
var normalizer = new DataNormalizer();
var (members, coupleConstraints, parseWarnings) = normalizer.Normalize(rows);
Console.WriteLine($"  → 正規化後共 {members.Count} 位成員，{coupleConstraints.Count} 組配對約束");

if (parseWarnings.Count > 0)
{
    Console.WriteLine($"  → 解析警告 ({parseWarnings.Count} 筆):");
    foreach (var w in parseWarnings)
        Console.WriteLine($"    {w}");
}

// Phase 3: Schedule
Console.WriteLine("開始排班...");
var engine = new SchedulerEngine(members, coupleConstraints);
var scheduleResult = engine.Schedule();

// Merge parse warnings into result
scheduleResult.Warnings.InsertRange(0, parseWarnings);

Console.WriteLine($"  → 完成 {scheduleResult.Assignments.Count} 個排班");
Console.WriteLine($"  → 未填滿 {scheduleResult.UnfilledSlots.Count} 個崗位");
Console.WriteLine($"  → {scheduleResult.Warnings.Count} 則警告");

// Phase 4: Export
Console.WriteLine($"輸出服事表：{outputPath}");
var exporter = new RosterExporter();
exporter.Export(scheduleResult, outputPath);

Console.WriteLine("完成！");

if (scheduleResult.UnfilledSlots.Count > 0)
{
    Console.WriteLine("\n未填滿崗位：");
    foreach (var slot in scheduleResult.UnfilledSlots.OrderBy(s => s.Date).ThenBy(s => s.Session))
        Console.WriteLine($"  {slot.Date:yyyy/M/d} {slot.Session} {slot.Role}");
}

return 0;
