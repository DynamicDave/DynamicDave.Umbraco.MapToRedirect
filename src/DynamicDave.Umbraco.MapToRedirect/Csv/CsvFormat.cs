using System.Text;

namespace DynamicDave.Umbraco.MapToRedirect.Csv;

public enum CsvReadError { Unreadable, MissingColumns, TooLarge, TooManyRows }

public record CsvReadResult(IReadOnlyList<CsvRow> Rows, CsvReadError? Error);

/// <summary>Reads uploaded redirect CSVs and writes generated/report CSVs (RFC 4180).</summary>
public static class CsvFormat
{
    private static readonly char[] Delimiters = [',', ';', '\t'];
    private static readonly string[] OldHeaders = ["old url", "oude url"];
    private static readonly string[] NewHeaders = ["new url", "nieuwe url"];

    public static CsvReadResult Read(Stream input, long maxBytes = 10_000_000, int maxRows = 50_000)
    {
        string text;
        try
        {
            if (input.CanSeek && input.Length > maxBytes)
            {
                return Fail(CsvReadError.TooLarge);
            }

            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > maxBytes)
                {
                    return Fail(CsvReadError.TooLarge);
                }
            }

            text = new UTF8Encoding(false, true).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
        catch (Exception ex) when (ex is DecoderFallbackException or IOException or NotSupportedException or ObjectDisposedException)
        {
            return Fail(CsvReadError.Unreadable);
        }

        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        var records = Parse(text, DetectDelimiter(text));

        var oldIndex = 0;
        var newIndex = 1;
        var first = records.FirstOrDefault(r => !r.IsBlank);
        var skipNumber = 0;
        if (first is not null)
        {
            var oldHeader = IndexOfHeader(first.Fields, OldHeaders);
            var newHeader = IndexOfHeader(first.Fields, NewHeaders);
            if (oldHeader >= 0 || newHeader >= 0)
            {
                if (oldHeader < 0 || newHeader < 0)
                {
                    return Fail(CsvReadError.MissingColumns);
                }

                oldIndex = oldHeader;
                newIndex = newHeader;
                skipNumber = first.Number;
            }
        }

        var rows = new List<CsvRow>();
        foreach (var record in records)
        {
            if (record.IsBlank || record.Number <= skipNumber)
            {
                continue;
            }

            if (rows.Count >= maxRows)
            {
                return Fail(CsvReadError.TooManyRows);
            }

            rows.Add(new CsvRow(record.Number, Cell(record.Fields, oldIndex), Cell(record.Fields, newIndex)));
        }

        return new CsvReadResult(rows, null);
    }

    public static byte[] Write(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        AppendRecord(builder, header);
        foreach (var row in rows)
        {
            AppendRecord(builder, row);
        }

        var body = Encoding.UTF8.GetBytes(builder.ToString());
        return [.. Encoding.UTF8.GetPreamble(), .. body];
    }

    private static void AppendRecord(StringBuilder builder, IReadOnlyList<string> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            var value = fields[i] ?? string.Empty;
            if (value.IndexOfAny(['"', ',', '\r', '\n']) >= 0)
            {
                builder.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
            }
            else
            {
                builder.Append(value);
            }
        }

        builder.Append("\r\n");
    }

    private static CsvReadResult Fail(CsvReadError error) => new([], error);

    private static string Cell(List<string> fields, int index) => index < fields.Count ? fields[index] : string.Empty;

    private static int IndexOfHeader(List<string> fields, string[] names)
        => fields.FindIndex(f => names.Contains(f.Trim(), StringComparer.OrdinalIgnoreCase));

    /// <summary>Picks the delimiter with the most occurrences, outside quotes, in the first non-empty line.</summary>
    private static char DetectDelimiter(string text)
    {
        var counts = new int[Delimiters.Length];
        var inQuotes = false;
        var seenContent = false;
        foreach (var c in text)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                seenContent = true;
            }
            else if (!inQuotes && (c == '\n' || c == '\r'))
            {
                if (seenContent)
                {
                    break;
                }
            }
            else
            {
                seenContent = true;
                if (!inQuotes)
                {
                    var index = Array.IndexOf(Delimiters, c);
                    if (index >= 0)
                    {
                        counts[index]++;
                    }
                }
            }
        }

        var best = 0;
        for (var i = 1; i < counts.Length; i++)
        {
            if (counts[i] > counts[best])
            {
                best = i;
            }
        }

        return Delimiters[best];
    }

    private sealed record Record(int Number, List<string> Fields)
    {
        public bool IsBlank => Fields.Count == 1 && Fields[0].Length == 0;
    }

    private static List<Record> Parse(string text, char delimiter)
    {
        var records = new List<Record>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var number = 1;
        var pending = false; // current record has any content or delimiter

        void EndRecord()
        {
            fields.Add(field.ToString());
            records.Add(new Record(number++, fields));
            fields = [];
            field.Clear();
            pending = false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
                pending = true;
            }
            else if (c == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
                pending = true;
            }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                EndRecord();
            }
            else
            {
                field.Append(c);
                pending = true;
            }
        }

        if (pending)
        {
            EndRecord();
        }

        return records;
    }
}
