namespace DynamicDave.Umbraco.MapToRedirect.Csv;

/// <summary>One data record of an uploaded redirect CSV. <see cref="RowNumber"/> is the 1-based record number in the file (a header counts as 1).</summary>
public record CsvRow(int RowNumber, string OldUrl, string NewUrl);
