using System.Text;
using DynamicDave.Umbraco.MapToRedirect.Csv;

namespace DynamicDave.Umbraco.MapToRedirect.Tests;

public class CsvFormatTests
{
    private static CsvReadResult Read(string text, long maxBytes = 10_000_000, int maxRows = 50_000)
        => CsvFormat.Read(new MemoryStream(Encoding.UTF8.GetBytes(text)), maxBytes, maxRows);

    [Fact]
    public void Reads_header_columns_in_any_order_and_case()
    {
        var result = Read("new url,Old URL\n/b,/a");

        Assert.Null(result.Error);
        Assert.Equal([new CsvRow(2, "/a", "/b")], result.Rows);
    }

    [Fact]
    public void Reads_dutch_headers()
    {
        var result = Read("Oude URL;Nieuwe URL\n/a;/b");

        Assert.Null(result.Error);
        Assert.Equal([new CsvRow(2, "/a", "/b")], result.Rows);
    }

    [Fact]
    public void Without_header_uses_first_two_columns()
    {
        var result = Read("/a,/b\n/c,/d");

        Assert.Null(result.Error);
        Assert.Equal([new CsvRow(1, "/a", "/b"), new CsvRow(2, "/c", "/d")], result.Rows);
    }

    [Fact]
    public void Reads_dutch_excel_semicolon_export()
    {
        var result = Read("\uFEFFOld URL;New URL;Note\r\n\"/a;b\";/x;\"say \"\"hi\"\"\"\r\n\r\n");

        Assert.Null(result.Error);
        Assert.Equal([new CsvRow(2, "/a;b", "/x")], result.Rows);
    }

    [Fact]
    public void Detects_tab_delimiter()
    {
        var result = Read("Old URL\tNew URL\n/a\t/b");

        Assert.Null(result.Error);
        Assert.Equal([new CsvRow(2, "/a", "/b")], result.Rows);
    }

    [Fact]
    public void Missing_new_url_column_is_an_error()
    {
        var result = Read("Old URL;Something\n/a;/b");

        Assert.Equal(CsvReadError.MissingColumns, result.Error);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Missing_cells_yield_empty_strings()
    {
        var result = Read("Old URL,New URL\n/a");

        Assert.Equal([new CsvRow(2, "/a", "")], result.Rows);
    }

    [Fact]
    public void Too_many_rows_is_an_error()
    {
        var result = Read("/a,/b\n/c,/d\n/e,/f", maxRows: 2);

        Assert.Equal(CsvReadError.TooManyRows, result.Error);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Too_large_is_an_error()
    {
        var result = Read("/aaaa,/bbbbbbbb", maxBytes: 10);

        Assert.Equal(CsvReadError.TooLarge, result.Error);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Binary_input_with_nul_bytes_is_unreadable()
    {
        var result = CsvFormat.Read(new MemoryStream([0x41, 0x00, 0x42, 0x00, 0x01]));

        Assert.Equal(CsvReadError.Unreadable, result.Error);
    }

    [Fact]
    public void Reads_windows_1252_export()
    {
        var bytes = new List<byte>(Encoding.ASCII.GetBytes("Old URL;New URL\r\n/caf"));
        bytes.Add(0xE9); // e acute
        bytes.AddRange(Encoding.ASCII.GetBytes(";/prijs-"));
        bytes.Add(0x80); // euro sign
        bytes.AddRange(Encoding.ASCII.GetBytes("\r\n"));

        var result = CsvFormat.Read(new MemoryStream(bytes.ToArray()));

        Assert.Null(result.Error);
        Assert.Equal([new CsvRow(2, "/café", "/prijs-€")], result.Rows);
    }

    [Fact]
    public void Reads_utf16_le_with_bom()
    {
        var text = "Old URL\tNew URL\r\n/café\t/b\r\n";
        var bytes = new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes(text)).ToArray();

        var result = CsvFormat.Read(new MemoryStream(bytes));

        Assert.Null(result.Error);
        Assert.Equal([new CsvRow(2, "/café", "/b")], result.Rows);
    }

    [Fact]
    public void Reads_utf16_be_with_bom()
    {
        var bytes = new byte[] { 0xFE, 0xFF }.Concat(Encoding.BigEndianUnicode.GetBytes("/a;/b")).ToArray();

        var result = CsvFormat.Read(new MemoryStream(bytes));

        Assert.Equal([new CsvRow(1, "/a", "/b")], result.Rows);
    }

    [Fact]
    public void Blank_line_flood_is_cheap_and_yields_no_rows()
    {
        var result = Read(new string('\n', 10_000_000));

        Assert.Null(result.Error);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Single_column_flood_is_too_many_rows()
    {
        var result = Read(string.Concat(Enumerable.Repeat("a\n", 5_000_000)));

        Assert.Equal(CsvReadError.TooManyRows, result.Error);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Header_with_very_many_delimiters_still_works()
    {
        var result = Read("Old URL,New URL" + new string(',', 1_000_000) + "\n/a,/b");

        Assert.Null(result.Error);
        Assert.Equal([new CsvRow(2, "/a", "/b")], result.Rows);
    }

    [Fact]
    public void Empty_rows_are_skipped_and_not_counted_toward_the_limit()
    {
        var result = Read("Old URL;New URL\n/a;/b\n;;\n  ; \n/c;/d\n;;", maxRows: 2);

        Assert.Null(result.Error);
        Assert.Equal([new CsvRow(2, "/a", "/b"), new CsvRow(5, "/c", "/d")], result.Rows);
    }

    [Fact]
    public void Write_round_trips()
    {
        var bytes = CsvFormat.Write(
            ["Old URL", "New URL"],
            [["/a,b", "/say \"hi\""], ["/multi\r\nline", "/plain"]]);

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3).ToArray());

        var result = CsvFormat.Read(new MemoryStream(bytes));

        Assert.Null(result.Error);
        Assert.Equal(
            [new CsvRow(2, "/a,b", "/say \"hi\""), new CsvRow(3, "/multi\r\nline", "/plain")],
            result.Rows);
    }
}
