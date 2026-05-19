using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Text;
using RosterScheduler.Models;

namespace RosterScheduler.Parsing;

public class RawSurveyRow
{
    public DateTime Timestamp { get; set; }
    public string Name { get; set; } = "";
    public string Sessions { get; set; } = "";
    public string Roles { get; set; } = "";
    public string UnavailableDates { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Email { get; set; } = "";
}

public class CsvParser
{
    public List<RawSurveyRow> Parse(string filePath)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null,
        };

        // Register Big5 (code page 950) for Traditional Chinese CSV files
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(950);

        using var reader = new StreamReader(filePath, encoding);
        using var csv = new CsvReader(reader, config);

        csv.Context.RegisterClassMap<SurveyRowMap>();
        return csv.GetRecords<RawSurveyRow>().ToList();
    }
}

public class SurveyRowMap : ClassMap<RawSurveyRow>
{
    public SurveyRowMap()
    {
        // Columns by index to be robust against Chinese header encoding issues
        Map(m => m.Timestamp).Index(0).TypeConverterOption.Format("M/d/yyyy H:mm:ss");
        Map(m => m.Name).Index(1);
        Map(m => m.Sessions).Index(2);
        Map(m => m.Roles).Index(3);
        Map(m => m.UnavailableDates).Index(4);
        Map(m => m.Notes).Index(5);
        Map(m => m.Email).Index(6);
    }
}
