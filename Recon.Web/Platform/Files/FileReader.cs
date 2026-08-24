using Microsoft.VisualBasic.FileIO;

namespace Recon.Web.Platform.Files;

/// <summary>
/// One data row from an imported file, abstracting over Excel and CSV so adapters
/// can read cells by column number without knowing the physical format.
/// </summary>
public interface IImportRow
{
    int SourceRowNumber { get; }
    object? GetCell(int column);
}

/// <summary>Header-name → 1-based column-number map for the detected header row.</summary>
public sealed class HeaderMap
{
    private readonly Dictionary<string, int> _map;

    public HeaderMap(IEnumerable<KeyValuePair<string, int>> entries)
    {
        _map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, column) in entries)
            if (!string.IsNullOrWhiteSpace(name) && !_map.ContainsKey(name.Trim()))
                _map[name.Trim()] = column;
    }

    public int ColumnCount => _map.Count;

    public bool TryGetColumn(string name, out int column) => _map.TryGetValue(name, out column);

    /// <summary>Reads a raw cell value by header name; null when the column is absent or blank.</summary>
    public object? Cell(IImportRow row, string name) =>
        _map.TryGetValue(name, out var col) ? row.GetCell(col) : null;

    /// <summary>True when every mapped column of the row is empty — trailing/blank worksheet rows
    /// are skipped so they cannot become zero-value reconciliation exceptions.</summary>
    public bool IsBlankRow(IImportRow row) =>
        !_map.Values.Distinct().Any(column => row.GetCell(column) is { } v && !string.IsNullOrWhiteSpace(v.ToString()));
}

/// <summary>A parsed file: header map plus its data rows. Excel and CSV both produce this.</summary>
public sealed class ParsedFile(HeaderMap headers, IReadOnlyList<IImportRow> rows)
{
    public HeaderMap Headers { get; } = headers;
    public IReadOnlyList<IImportRow> Rows { get; } = rows;
}

/// <summary>Reads Excel worksheets or CSV streams into a <see cref="ParsedFile"/> given the header row number.</summary>
public static class FileReader
{
    public static ParsedFile FromExcel(ClosedXML.Excel.IXLWorksheet ws, int headerRow)
    {
        var headers = new HeaderMap(ws.Row(headerRow).CellsUsed()
            .Select(c => new KeyValuePair<string, int>(c.GetString().Trim(), c.Address.ColumnNumber)));
        var used = ws.RangeUsed();
        var rows = new List<IImportRow>();
        if (used is not null)
            foreach (var row in used.RowsUsed())
                if (row.RowNumber() > headerRow)
                    rows.Add(new ExcelRow(row));
        return new ParsedFile(headers, rows);
    }

    public static async Task<ParsedFile> FromCsvAsync(Stream stream, int maxHeaderScanRows, CancellationToken ct = default)
    {
        var lines = new List<CsvRow>();
        using (var parser = new TextFieldParser(stream))
        {
            parser.TextFieldType = FieldType.Delimited;
            parser.SetDelimiters(",");
            parser.HasFieldsEnclosedInQuotes = true;
            while (!parser.EndOfData)
            {
                ct.ThrowIfCancellationRequested();
                var fields = parser.ReadFields();
                if (fields is { Length: > 0 })
                    lines.Add(new CsvRow(lines.Count + 1, fields));
            }
        }
        return new ParsedFile(new HeaderMap([]), lines); // headers resolved by the caller/detector
    }

    /// <summary>CSV variant where the caller already knows which line is the header.</summary>
    public static ParsedFile FromCsvLines(IReadOnlyList<CsvRow> lines, CsvRow headerRow)
    {
        var headers = new HeaderMap(headerRow.Fields.Select((name, i) => new KeyValuePair<string, int>(name.Trim(), i + 1)));
        var rows = lines.Where(l => l.SourceRowNumber > headerRow.SourceRowNumber).Cast<IImportRow>().ToList();
        return new ParsedFile(headers, rows);
    }

    private sealed class ExcelRow(ClosedXML.Excel.IXLRangeRow row) : IImportRow
    {
        public int SourceRowNumber => row.RowNumber();
        public object? GetCell(int column)
        {
            var value = row.Worksheet.Cell(SourceRowNumber, column).Value;
            if (value.IsBlank) return null;
            if (value.IsDateTime) return value.GetDateTime();
            if (value.IsNumber) return value.GetNumber();
            if (value.IsBoolean) return value.GetBoolean();
            return value.IsText ? value.GetText() : value.ToString();
        }
    }

    public sealed class CsvRow(int sourceRowNumber, string[] fields) : IImportRow
    {
        public int SourceRowNumber { get; } = sourceRowNumber;
        public string[] Fields { get; } = fields;
        public object? GetCell(int column) => column > 0 && column <= Fields.Length && !string.IsNullOrWhiteSpace(Fields[column - 1])
            ? Fields[column - 1].Trim() : null;
    }
}
