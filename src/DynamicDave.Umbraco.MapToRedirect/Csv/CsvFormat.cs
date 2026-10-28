using System.Text;

namespace DynamicDave.Umbraco.MapToRedirect.Csv;

public enum CsvReadError { Unreadable, MissingColumns, TooLarge, TooManyRows }

public record CsvReadResult(IReadOnlyList<CsvRow> Rows, CsvReadError? Error);

/// <summary>Reads uploaded redirect CSVs and writes generated/report CSVs (RFC 4180).</summary>
public static class CsvFormat
{
    private const int MaxFieldsPerRecord = 50;
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

            var decoded = Decode(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
            if (decoded is null)
            {
                return Fail(CsvReadError.Unreadable);
            }

            text = decoded;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ObjectDisposedException)
        {
            return Fail(CsvReadError.Unreadable);
        }

        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        var oldIndex = 0;
        var newIndex = 1;
        var firstRecord = true;
        var rows = new List<CsvRow>();
        foreach (var record in Parse(text, DetectDelimiter(text)))
        {
            if (firstRecord)
            {
                firstRecord = false;
                var oldHeader = IndexOfHeader(record.Fields, OldHeaders);
                var newHeader = IndexOfHeader(record.Fields, NewHeaders);
                if (oldHeader >= 0 || newHeader >= 0)
                {
                    if (oldHeader < 0 || newHeader < 0)
                    {
                        return Fail(CsvReadError.MissingColumns);
                    }

                    oldIndex = oldHeader;
                    newIndex = newHeader;
                    continue;
                }
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

    /// <summary>
    /// UTF-8 (with or without BOM) and UTF-16 (BOM required, as Excel "Unicode Text" writes it); invalid UTF-8 falls back to
    /// Windows-1252 (Excel "CSV (puntkomma)" on Dutch Windows). Input containing NUL bytes is binary and returns null.
    /// </summary>
    private static string? Decode(ReadOnlySpan<byte> bytes)
    {
        try
        {
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                return new UnicodeEncoding(false, false, true).GetString(bytes[2..]);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                return new UnicodeEncoding(true, false, true).GetString(bytes[2..]);
            }

            if (bytes.Contains((byte)0))
            {
                return null;
            }

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetString(bytes);
            }
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
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

    private sealed record Record(int Number, List<string> Fields);

    /// <summary>
    /// Lazily yields records. Records whose fields are all empty or whitespace are skipped (they still consume a record
    /// number) and only the first <see cref="MaxFieldsPerRecord"/> fields of a record are kept.
    /// </summary>
    private static IEnumerable<Record> Parse(string text, char delimiter)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var number = 1;
        var pending = false; // current record has any content or delimiter

        void AddField()
        {
            if (fields.Count < MaxFieldsPerRecord)
            {
                fields.Add(field.ToString());
            }

            field.Clear();
        }

        Record? EndRecord()
        {
            AddField();
            var current = number++;
            pending = false;
            if (fields.TrueForAll(string.IsNullOrWhiteSpace))
            {
                fields.Clear();
                return null;
            }

            var record = new Record(current, fields);
            fields = [];
            return record;
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
                AddField();
                pending = true;
            }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                var record = EndRecord();
                if (record is not null)
                {
                    yield return record;
                }
            }
            else
            {
                field.Append(c);
                pending = true;
            }
        }

        if (pending)
        {
            var record = EndRecord();
            if (record is not null)
            {
                yield return record;
            }
        }
    }
}
