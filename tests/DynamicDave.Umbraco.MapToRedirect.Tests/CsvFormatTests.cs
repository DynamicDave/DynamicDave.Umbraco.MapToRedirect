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
    public void Invalid_utf8_is_unreadable()
    {
        var result = CsvFormat.Read(new MemoryStream([0xFF, 0xFE, 0xC3, 0x28]));

        Assert.Equal(CsvReadError.Unreadable, result.Error);
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
