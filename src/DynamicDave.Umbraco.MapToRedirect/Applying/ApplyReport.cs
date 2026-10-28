using System.Globalization;
using System.Text;
using System.Text.Json;
using DynamicDave.Umbraco.MapToRedirect.Csv;
using DynamicDave.Umbraco.MapToRedirect.Persistence;

namespace DynamicDave.Umbraco.MapToRedirect.Applying;

/// <summary>The results of apply and undo jobs: the report CSV and the summary JSON for the UI.</summary>
public static class ApplyReport
{
    private static readonly string[] Header = ["Old URL", "New URL", "Outcome", "Message"];
    private static readonly JsonSerializerOptions SummaryJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>The report CSV as text. It starts with U+FEFF, so the UTF-8 download starts with a BOM (for Excel).</summary>
    public static string Csv(IEnumerable<ImportRowRecord> rows) => Encoding.UTF8.GetString(CsvFormat.Write(
        Header,
        rows.Select(r => (IReadOnlyList<string>)[r.OldUrl ?? string.Empty, r.NewUrl ?? string.Empty, r.Outcome.ToString(), r.Message ?? string.Empty])));

    public static string Summary(ImportCounts counts) => JsonSerializer.Serialize(counts, SummaryJson);

    public static string Summary(UndoResult result) =>
        JsonSerializer.Serialize(new UndoSummary(result.Removed, result.Restored, result.LeftAlone), SummaryJson);

    public static string FileName(DateTime utcNow) =>
        $"redirect-report-{utcNow.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.csv";

    private sealed record UndoSummary(int Removed, int Restored, int LeftAlone);
}
