using System.Security.Cryptography;
using System.Data;
using System.Reflection;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.VisualBasic.FileIO;
using Recon.Web.Data;
using Recon.Web.Domain.Import;
using Recon.Web.Domain.Staging;
using Recon.Web.Services.Parsing;

namespace Recon.Web.Services.Import;

public record ImportOutcome(bool Success, SourceType? DetectedType, int RowCount, string? Error, bool Duplicate = false, int? ImportBatchId = null, string? SourcePeriod = null);

public interface IImportService
{
    /// <summary>Detects the source type from the header, then parses and persists the file.
    /// <paramref name="schemeProfileId"/> tags the batch with the owning scheme (null = legacy).</summary>
    Task<ImportOutcome> ImportAsync(Stream fileStream, string fileName, string periodKey, int? schemeProfileId = null, CancellationToken ct = default);
}

/// <summary>
/// Reads a BankOne export, auto-detects which of the five sources it is by header signature,
/// normalizes each row and bulk-inserts into the matching staging table under one ImportBatch.
/// </summary>
public class ImportService(IDbContextFactory<AppDbContext> dbFactory) : IImportService
{
    public async Task<ImportOutcome> ImportAsync(Stream fileStream, string fileName, string periodKey, int? schemeProfileId = null, CancellationToken ct = default)
    {
        // Buffer to a seekable stream so we can hash and then read.
        using var ms = new MemoryStream();
        await fileStream.CopyToAsync(ms, ct);
        ms.Position = 0;
        var hash = Convert.ToHexString(SHA256.HashData(ms.GetBuffer().AsSpan(0, checked((int)ms.Length))));
        ms.Position = 0;

        if (Path.GetExtension(fileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
            return await ImportCsvAsync(ms, fileName, periodKey, hash, schemeProfileId, ct);

        XLWorkbook wb;
        try { wb = new XLWorkbook(ms); }
        catch (Exception ex) { return new ImportOutcome(false, null, 0, $"Not a readable .xlsx: {ex.Message}"); }

        using (wb)
        {
            var ws = wb.Worksheets.First();
            var (type, headerRow) = DetectSource(ws);
            if (type is null)
                return new ImportOutcome(false, null, 0, "Unrecognised file — header did not match any known BankOne export.");

            await using var db = await dbFactory.CreateDbContextAsync(ct);
            // Same file already staged → reuse it rather than re-parsing (e.g. one GL dump across batches).
            var existing = await db.ImportBatches
                .FirstOrDefaultAsync(b => b.SourceType == type && b.ContentHash == hash, ct);
            if (existing is { Status: ImportStatus.Parsed })
            {
                var sourcePeriod = await RefreshSourcePeriodAsync(db, existing, ct);
                if (type is SourceType.GlDump or SourceType.InterchangeFee)
                    await RefreshSourceEvidenceAsync(db, existing.ImportBatchId, type.Value, HeaderMap(ws, headerRow), DataRows(ws, headerRow), ct);
                return new ImportOutcome(true, type, existing.RowCount, null, Duplicate: true, ImportBatchId: existing.ImportBatchId, SourcePeriod: sourcePeriod);
            }

            if (existing is not null && (existing.Status != ImportStatus.Failed || !await PrepareFailedRetryAsync(db, existing, ct)))
                return new ImportOutcome(false, type, 0, "This file already has an active or referenced import. Check its status in Import files before retrying.", ImportBatchId: existing.ImportBatchId);
            var batch = existing ?? new ImportBatch
            {
                SourceType = type.Value,
                FileName = fileName,
                ContentHash = hash,
                PeriodKey = periodKey,
                Status = ImportStatus.Uploaded,
                SchemeProfileId = schemeProfileId
            };
            batch.PeriodKey = periodKey; batch.SchemeProfileId = schemeProfileId;
            if (existing is null) db.ImportBatches.Add(batch);
            await db.SaveChangesAsync(ct);

            try
            {
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                var count = Parse(type.Value, HeaderMap(ws, headerRow), DataRows(ws, headerRow), batch.ImportBatchId, db);
                await BulkInsertStagingAsync(db, type.Value, ct);
                db.ChangeTracker.Clear();
                db.ChangeTracker.AutoDetectChangesEnabled = true;

                var persistedBatch = await db.ImportBatches.FindAsync([batch.ImportBatchId], ct)
                    ?? throw new InvalidOperationException("Import batch was not found after staging.");
                persistedBatch.RowCount = count;
                persistedBatch.Status = ImportStatus.Parsed;
                var sourcePeriod = await RefreshSourcePeriodAsync(db, persistedBatch, ct);
                await db.SaveChangesAsync(ct);
                return new ImportOutcome(true, type, count, null, ImportBatchId: batch.ImportBatchId, SourcePeriod: sourcePeriod);
            }
            catch (Exception ex)
            {
                db.ChangeTracker.Clear();
                db.ChangeTracker.AutoDetectChangesEnabled = true;
                var failedBatch = await db.ImportBatches.FindAsync([batch.ImportBatchId], ct);
                if (failedBatch is null) throw;
                failedBatch.Status = ImportStatus.Failed;
                failedBatch.ParseError = ex.Message;
                await db.SaveChangesAsync(ct);
                return new ImportOutcome(false, type, 0, ex.Message);
            }
        }
    }

    private static Task BulkInsertStagingAsync(AppDbContext db, SourceType type, CancellationToken ct) => type switch
    {
        SourceType.NetworkT464 => BulkInsertAsync(db, db.ChangeTracker.Entries<StgNetworkT464>().Where(e => e.State == EntityState.Added).Select(e => e.Entity), "StgNetworkT464", nameof(StgNetworkT464.StgNetworkT464Id), ct),
        SourceType.InterchangeFee => BulkInsertAsync(db, db.ChangeTracker.Entries<StgInterchangeFee>().Where(e => e.State == EntityState.Added).Select(e => e.Entity), "StgInterchangeFees", nameof(StgInterchangeFee.StgInterchangeFeeId), ct),
        SourceType.PosSettlement or SourceType.AtmSettlement => BulkInsertAsync(db, db.ChangeTracker.Entries<StgDebitSettlement>().Where(e => e.State == EntityState.Added).Select(e => e.Entity), "StgDebitSettlements", nameof(StgDebitSettlement.StgDebitSettlementId), ct),
        SourceType.GlDump => BulkInsertGlAsync(db, ct),
        _ => Task.CompletedTask
    };

    private static async Task BulkInsertGlAsync(AppDbContext db, CancellationToken ct)
    {
        await BulkInsertAsync(db, db.ChangeTracker.Entries<StgGlEntry>().Where(e => e.State == EntityState.Added).Select(e => e.Entity), "StgGlEntries", nameof(StgGlEntry.StgGlEntryId), ct);
        await BulkInsertAsync(db, db.ChangeTracker.Entries<StgGlBulkPosting>().Where(e => e.State == EntityState.Added).Select(e => e.Entity), "StgGlBulkPostings", nameof(StgGlBulkPosting.StgGlBulkPostingId), ct);
    }

    private static async Task BulkInsertAsync<T>(AppDbContext db, IEnumerable<T> entities, string tableName, string keyName, CancellationToken ct)
    {
        var rows = entities.ToList();
        if (rows.Count == 0) return;

        var properties = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanRead && p.Name != keyName && IsScalar(p.PropertyType)).ToArray();
        var table = new DataTable();
        foreach (var property in properties)
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            table.Columns.Add(property.Name, type == typeof(DateOnly) ? typeof(DateTime) : type == typeof(TimeOnly) ? typeof(TimeSpan) : type);
        }
        foreach (var entity in rows)
        {
            var values = properties.Select(p => ToDbValue(p.GetValue(entity))).ToArray();
            table.Rows.Add(values);
        }

        var connection = (SqlConnection)db.Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await connection.OpenAsync(ct);
        try
        {
            using var copy = new SqlBulkCopy(connection, SqlBulkCopyOptions.TableLock, null)
            {
                DestinationTableName = $"dbo.{tableName}", BatchSize = 20000, BulkCopyTimeout = 300,
                EnableStreaming = true
            };
            foreach (var property in properties) copy.ColumnMappings.Add(property.Name, property.Name);
            await copy.WriteToServerAsync(table, ct);
        }
        finally
        {
            if (!wasOpen) await connection.CloseAsync();
        }
    }

    private static bool IsScalar(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(DateOnly) || t == typeof(TimeOnly) || t == typeof(Guid);
    }

    private static object ToDbValue(object? value) => value switch
    {
        null => DBNull.Value,
        DateOnly date => date.ToDateTime(TimeOnly.MinValue),
        TimeOnly time => time.ToTimeSpan(),
        _ => value
    };

    // ---- source detection ----------------------------------------------------
    // Export versions use either spaces or underscores for the same column names.
    private static string NormalizeHeader(string value) => string.Join(" ",
        value.Replace('_', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static (SourceType?, int headerRow) DetectSource(IXLWorksheet ws)
    {
        for (var r = 1; r <= 4; r++)
        {
            var wordingCol = ColIndex(ws, r, "TRANSACTION WORDING");
            var wording = wordingCol > 0
                ? ws.Column(wordingCol).CellsUsed().Where(c => c.Address.RowNumber > r).Take(50).Select(c => c.GetString())
                : null;
            var type = DetectSource(ws.Row(r).CellsUsed().Select(c => c.GetString()), wording);
            if (type is not null) return (type, r);
        }
        return (null, 0);
    }

    private static SourceType? DetectSource(IEnumerable<string> headers, IEnumerable<string>? wording = null)
    {
        var set = headers.Select(NormalizeHeader).Where(v => v.Length > 0).ToHashSet();
        if (set.Contains("HEADER CODE") && set.Contains("MESSAGE TYPE INDICATOR")) return SourceType.NetworkT464;
        // This specific signature must precede the shared settlement-report columns.
        if (set.Contains("MICROFILM REF NUMBER") && set.Contains("INTERCHANGE FEES")) return SourceType.InterchangeFee;
        if (set.Contains("SOL ID") && set.Contains("GL SUB HEAD CODE")) return SourceType.GlDump;
        if (!set.Contains("MERCHANT ACRONYM") || !set.Contains("SETTLEMENT AMOUNT")) return null;
        return wording?.Any(v => v.Contains("WITHDRAWAL", StringComparison.OrdinalIgnoreCase)) == true
            ? SourceType.AtmSettlement : SourceType.PosSettlement;
    }
    private async Task<ImportOutcome> ImportCsvAsync(Stream stream, string fileName, string periodKey, string hash, int? schemeProfileId, CancellationToken ct)
    {
        stream.Position = 0;
        var rows = new List<CsvImportRow>();
        using (var parser = new TextFieldParser(stream))
        {
            parser.TextFieldType = FieldType.Delimited;
            parser.SetDelimiters(",");
            parser.HasFieldsEnclosedInQuotes = true;
            var line = 0;
            while (!parser.EndOfData)
            {
                var fields = parser.ReadFields();
                line++;
                if (fields is { Length: > 0 }) rows.Add(new CsvImportRow(line, fields));
            }
        }

        CsvImportRow? header = null;
        SourceType? type = null;
        foreach (var candidate in rows.Take(4))
        {
            var headings = candidate.Fields;
            var wordingIndex = Array.FindIndex(headings, h => NormalizeHeader(h) == "TRANSACTION WORDING");
            type = DetectSource(headings, wordingIndex < 0 ? null : rows.SkipWhile(r => r != candidate).Skip(1).Take(50).Select(r => r.GetCell(wordingIndex + 1)?.ToString() ?? ""));
            if (type is not null) { header = candidate; break; }
        }
        if (header is null || type is null)
            return new ImportOutcome(false, null, 0, "Unrecognised CSV — header did not match any known BankOne export.");

        var headers = header.Fields.Select((name, index) => (name: NormalizeHeader(name), column: index + 1))
            .Where(x => x.name.Length > 0).ToDictionary(x => x.name, x => x.column, StringComparer.OrdinalIgnoreCase);
        var dataRows = rows.Where(r => r.SourceRowNumber > header.SourceRowNumber).Cast<IImportRow>().ToList();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.ImportBatches.FirstOrDefaultAsync(b => b.SourceType == type && b.ContentHash == hash, ct);
        if (existing is { Status: ImportStatus.Parsed })
        {
            var sourcePeriod = await RefreshSourcePeriodAsync(db, existing, ct);
            if (type is SourceType.GlDump or SourceType.InterchangeFee)
                await RefreshSourceEvidenceAsync(db, existing.ImportBatchId, type.Value, headers, dataRows, ct);
            return new ImportOutcome(true, type, existing.RowCount, null, Duplicate: true, ImportBatchId: existing.ImportBatchId, SourcePeriod: sourcePeriod);
        }

        if (existing is not null && (existing.Status != ImportStatus.Failed || !await PrepareFailedRetryAsync(db, existing, ct)))
            return new ImportOutcome(false, type, 0, "This file already has an active or referenced import. Check its status in Import files before retrying.", ImportBatchId: existing.ImportBatchId);
        var batch = existing ?? new ImportBatch { SourceType = type.Value, FileName = fileName, ContentHash = hash, PeriodKey = periodKey, Status = ImportStatus.Uploaded, SchemeProfileId = schemeProfileId };
        batch.PeriodKey = periodKey; batch.SchemeProfileId = schemeProfileId;
        if (existing is null) db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        try
        {
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var count = Parse(type.Value, headers, dataRows, batch.ImportBatchId, db);
            await BulkInsertStagingAsync(db, type.Value, ct);
            db.ChangeTracker.Clear();
            db.ChangeTracker.AutoDetectChangesEnabled = true;
            var persistedBatch = await db.ImportBatches.FindAsync([batch.ImportBatchId], ct) ?? throw new InvalidOperationException("Import batch was not found after staging.");
            persistedBatch.RowCount = count;
            persistedBatch.Status = ImportStatus.Parsed;
            var sourcePeriod = await RefreshSourcePeriodAsync(db, persistedBatch, ct);
            await db.SaveChangesAsync(ct);
            return new ImportOutcome(true, type, count, null, ImportBatchId: batch.ImportBatchId, SourcePeriod: sourcePeriod);
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            db.ChangeTracker.AutoDetectChangesEnabled = true;
            var failedBatch = await db.ImportBatches.FindAsync([batch.ImportBatchId], ct);
            if (failedBatch is null) throw;
            failedBatch.Status = ImportStatus.Failed;
            failedBatch.ParseError = ex.Message;
            await db.SaveChangesAsync(ct);
            return new ImportOutcome(false, type, 0, ex.Message);
        }
    }

    private static async Task<string?> RefreshSourcePeriodAsync(AppDbContext db, ImportBatch batch, CancellationToken ct)
    {
        IQueryable<DateOnly?>? dates = batch.SourceType switch
        {
            SourceType.InterchangeFee => db.StgInterchangeFees.Where(t => t.ImportBatchId == batch.ImportBatchId)
                .Select(t => t.ProcessingDate ?? (t.TransactionDate.HasValue ? DateOnly.FromDateTime(t.TransactionDate.Value) : (DateOnly?)null)),
            SourceType.GlDump => db.StgGlEntries.Where(g => g.ImportBatchId == batch.ImportBatchId).Select(g => g.TranDate)
                .Concat(db.StgGlBulkPostings.Where(g => g.ImportBatchId == batch.ImportBatchId).Select(g => g.TranDate)),
            SourceType.PosSettlement or SourceType.AtmSettlement => db.StgDebitSettlements.Where(t => t.ImportBatchId == batch.ImportBatchId)
                .Select(t => t.ProcessingDate ?? t.TransactionDate),
            _ => null
        };
        if (dates is null) return null;
        // Processing/posting dates determine the import month. Transaction/value dates
        // can precede it. Do not infer a single-month label for mixed-month evidence.
        var months = await dates.Where(d => d.HasValue).Select(d => new { d!.Value.Year, d.Value.Month }).Distinct().Take(2).ToListAsync(ct);
        if (months.Count == 1)
        {
            var sourcePeriod = $"{months[0].Year:0000}-{months[0].Month:00}";
            if (batch.PeriodKey != sourcePeriod)
            {
                batch.PeriodKey = sourcePeriod;
                await db.SaveChangesAsync(ct);
            }
            return sourcePeriod;
        }
        return null;
    }

    private static async Task<bool> PrepareFailedRetryAsync(AppDbContext db, ImportBatch batch, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var current = await db.ImportBatches.FromSqlInterpolated($"SELECT * FROM [ImportBatches] WITH (UPDLOCK, HOLDLOCK) WHERE [ImportBatchId] = {batch.ImportBatchId}")
            .AsNoTracking().SingleAsync(ct);
        if (current.Status != ImportStatus.Failed || await db.ReconRuns.AnyAsync(r => r.ProcessorBatchId == batch.ImportBatchId ||
            r.GlBatchId == batch.ImportBatchId || r.NetworkBatchId == batch.ImportBatchId, ct)) return false;
        // Only an unreferenced failed import is replaceable. Remove its partial staging
        // inside the same transaction and reserve the existing batch ID for this retry.
        await db.NormalizedTransactions.Where(r => r.ImportBatchId == batch.ImportBatchId).ExecuteDeleteAsync(ct);
        await db.NormalizedGlEntries.Where(r => r.ImportBatchId == batch.ImportBatchId).ExecuteDeleteAsync(ct);
        switch (batch.SourceType)
        {
            case SourceType.InterchangeFee:
                await db.StgInterchangeFees.Where(r => r.ImportBatchId == batch.ImportBatchId).ExecuteDeleteAsync(ct); break;
            case SourceType.GlDump:
                await db.StgGlBulkPostings.Where(r => r.ImportBatchId == batch.ImportBatchId).ExecuteDeleteAsync(ct);
                await db.StgGlEntries.Where(r => r.ImportBatchId == batch.ImportBatchId).ExecuteDeleteAsync(ct); break;
            case SourceType.PosSettlement: case SourceType.AtmSettlement:
                await db.StgDebitSettlements.Where(r => r.ImportBatchId == batch.ImportBatchId).ExecuteDeleteAsync(ct); break;
            case SourceType.NetworkT464:
                await db.StgNetworkT464.Where(r => r.ImportBatchId == batch.ImportBatchId).ExecuteDeleteAsync(ct); break;
        }
        batch.Status = ImportStatus.Uploaded; batch.ParseError = null; batch.RowCount = 0;
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        return true;
    }

    private static int ColIndex(IXLWorksheet ws, int headerRow, string name)
    {
        foreach (var c in ws.Row(headerRow).CellsUsed())
            if (NormalizeHeader(c.GetString()) == NormalizeHeader(name))
                return c.Address.ColumnNumber;
        return -1;
    }

    private static async Task RefreshSourceEvidenceAsync(AppDbContext db, int batchId, SourceType type, Dictionary<string, int> headers, IEnumerable<IImportRow> rows, CancellationToken ct)
    {
        var sourceRows = rows.ToList();
        if (type == SourceType.GlDump)
        {
            if (headers.ContainsKey(NormalizeHeader("VALUE_DATE")))
            {
                var dates = sourceRows.ToDictionary(r => r.SourceRowNumber, r => ValueParsers.Date(Cell(r, headers, "VALUE_DATE")));
                foreach (var row in await db.StgGlEntries.Where(x => x.ImportBatchId == batchId && x.ValueDate == null).ToListAsync(ct))
                    if (dates.TryGetValue(row.SourceRowNumber, out var value)) row.ValueDate = value;
                foreach (var row in await db.StgGlBulkPostings.Where(x => x.ImportBatchId == batchId && x.ValueDate == null).ToListAsync(ct))
                    if (dates.TryGetValue(row.SourceRowNumber, out var value)) row.ValueDate = value;
            }
            if (headers.ContainsKey(NormalizeHeader("TRAN_RMKS")))
            {
                // Legacy tokens cannot be reversed. Restore only from the same original file.
                var remarks = sourceRows.ToDictionary(r => r.SourceRowNumber,
                    r => global::Recon.Web.Services.Recon.CardIdentity.ImportRemarks(Cell(r, headers, "TRAN_RMKS")));
                foreach (var row in await db.StgGlEntries.Where(x => x.ImportBatchId == batchId && x.TranRmks != null && x.TranRmks.StartsWith("card:v1:")).ToListAsync(ct))
                    if (remarks.TryGetValue(row.SourceRowNumber, out var value)) row.TranRmks = value;
                foreach (var row in await db.StgGlBulkPostings.Where(x => x.ImportBatchId == batchId && x.TranRmks != null && x.TranRmks.StartsWith("card:v1:")).ToListAsync(ct))
                    if (remarks.TryGetValue(row.SourceRowNumber, out var value)) row.TranRmks = value;
            }
        }
        else if (type == SourceType.InterchangeFee && headers.ContainsKey(NormalizeHeader("CARD_ACCOUNT_ENTITY_ID")))
        {
            var cards = sourceRows.ToDictionary(r => r.SourceRowNumber,
                r => global::Recon.Web.Services.Recon.CardIdentity.FromSource(Cell(r, headers, "CARD_ACCOUNT_ENTITY_ID")));
            foreach (var row in await db.StgInterchangeFees.Where(x => x.ImportBatchId == batchId && x.CardAccountEntityId == null).ToListAsync(ct))
                if (cards.TryGetValue(row.SourceRowNumber, out var value)) row.CardAccountEntityId = value;
        }
        await db.SaveChangesAsync(ct);
    }

    private interface IImportRow
    {
        int SourceRowNumber { get; }
        object? GetCell(int column);
    }

    private sealed class ExcelImportRow(IXLRangeRow row) : IImportRow
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

    private sealed class CsvImportRow : IImportRow
    {
        public CsvImportRow(int sourceRowNumber, string[] fields)
        {
            SourceRowNumber = sourceRowNumber;
            Fields = fields;
        }
        public int SourceRowNumber { get; }
        public string[] Fields { get; }
        public object? GetCell(int column) => column > 0 && column <= Fields.Length && !string.IsNullOrWhiteSpace(Fields[column - 1])
            ? Fields[column - 1].Trim() : null;
    }

    /// <summary>Builds a header-name → column-number map for the detected header row.</summary>
    private static Dictionary<string, int> HeaderMap(IXLWorksheet ws, int headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in ws.Row(headerRow).CellsUsed())
            map[NormalizeHeader(c.GetString())] = c.Address.ColumnNumber;
        return map;
    }

    private static IEnumerable<IImportRow> DataRows(IXLWorksheet ws, int headerRow)
    {
        var used = ws.RangeUsed();
        if (used is null) yield break;
        foreach (var row in used.RowsUsed())
        {
            if (row.RowNumber() <= headerRow) continue;
            yield return new ExcelImportRow(row);
        }
    }

    private static object? Cell(IImportRow row, Dictionary<string, int> h, string name)
    {
        if (!h.TryGetValue(NormalizeHeader(name), out var col)) return null;
        return row.GetCell(col);
    }

    /// <summary>Ignore trailing/empty worksheet rows so they cannot become zero-value reconciliation exceptions.</summary>
    private static bool IsBlankRow(IImportRow row, Dictionary<string, int> headers) => !headers.Values.Distinct()
        .Any(column => row.GetCell(column) is { } value && !string.IsNullOrWhiteSpace(value.ToString()));

    // ---- per-source parsers --------------------------------------------------
    private static int Parse(SourceType type, Dictionary<string, int> headers, IEnumerable<IImportRow> rows, int batchId, AppDbContext db) => type switch
    {
        SourceType.NetworkT464 => ParseT464(headers, rows, batchId, db),
        SourceType.InterchangeFee => ParseInterchange(headers, rows, batchId, db),
        SourceType.PosSettlement => ParseDebit(headers, rows, batchId, SettlementChannel.Pos, db),
        SourceType.AtmSettlement => ParseDebit(headers, rows, batchId, SettlementChannel.Atm, db),
        SourceType.GlDump => ParseGl(headers, rows, batchId, db),
        _ => 0
    };

    private static int ParseT464(Dictionary<string, int> h, IEnumerable<IImportRow> rows, int batchId, AppDbContext db)
    {
        var n = 0;
        foreach (var row in rows)
        {
            if (IsBlankRow(row, h)) continue;
            var txnCcy = ValueParsers.Int(Cell(row, h, "Currency Code Transaction"));
            var setCcy = ValueParsers.Int(Cell(row, h, "Currency Code Settlement"));
            db.StgNetworkT464.Add(new StgNetworkT464
            {
                ImportBatchId = batchId,
                SourceRowNumber = row.SourceRowNumber,
                HeaderCode = ValueParsers.Clean(Cell(row, h, "Header Code")),
                MessageTypeIndicator = ValueParsers.Clean(Cell(row, h, "Message Type Indicator")),
                SwitchSerialNumber = ValueParsers.Clean(Cell(row, h, "Switch Serial Number")),
                ProcessorId = ValueParsers.Clean(Cell(row, h, "Processor ID")),
                TransactionDate = ValueParsers.DateMmddyy(Cell(row, h, "Transaction Date")),
                TransactionTime = ValueParsers.TimeHhmmss(Cell(row, h, "Transaction Time")),
                Pan = ValueParsers.Clean(Cell(row, h, "PAN")),
                ProcessingCode = ValueParsers.Clean(Cell(row, h, "Processing Code")),
                TraceNumber = ValueParsers.Clean(Cell(row, h, "Trace Number")),
                MerchantType = ValueParsers.Clean(Cell(row, h, "Merchant Type")),
                ReferenceNumber = ValueParsers.Clean(Cell(row, h, "Reference Number")),
                AcquirerInstitutionId = ValueParsers.Clean(Cell(row, h, "Acquirer InstitutionID")),
                TerminalId = ValueParsers.Clean(Cell(row, h, "Terminal ID")),
                ResponseCode = ValueParsers.Clean(Cell(row, h, "Response Code")),
                TxnCcy = txnCcy,
                TxnCcyCode = ValueParsers.CcyCode(txnCcy),
                CompletedAmtLocalRaw = ValueParsers.Clean(Cell(row, h, "Completed Amt TransLocal")),
                CompletedAmtLocal = ValueParsers.SignedAmount(Cell(row, h, "Completed Amt TransLocal"), Cell(row, h, "Completed Amt TransLocal Indicator")),
                SettleCcy = setCcy,
                SettleCcyCode = ValueParsers.CcyCode(setCcy),
                ConversionRate = ValueParsers.Amount(Cell(row, h, "Conversion Rate Settlement")),
                CompletedAmtSettleRaw = ValueParsers.Clean(Cell(row, h, "Completed Amt Settlement")),
                CompletedAmtSettle = ValueParsers.SignedAmount(Cell(row, h, "Completed Amt Settlement"), Cell(row, h, "Completed Amt Settlement Indicator")),
                InterchangeFeeRaw = ValueParsers.Clean(Cell(row, h, "Interchange Fee")),
                InterchangeFee = ValueParsers.SignedAmount(Cell(row, h, "Interchange Fee"), Cell(row, h, "Interchange Fee Indicator")),
                RequestedAmtLocal = ValueParsers.Amount(Cell(row, h, "Requested Amt TransLocal")),
            });
            n++;
        }
        return n;
    }

    private static int ParseDebit(Dictionary<string, int> h, IEnumerable<IImportRow> rows, int batchId, SettlementChannel channel, AppDbContext db)
    {
        var n = 0;
        foreach (var row in rows)
        {
            if (IsBlankRow(row, h)) continue;
            var txnCcy = ValueParsers.Int(Cell(row, h, "TRANSACTION_CURRENCY"));
            var feeCcy = ValueParsers.Int(Cell(row, h, "INTERCHANGE_FEES_CURRENCY"));
            db.StgDebitSettlements.Add(new StgDebitSettlement
            {
                ImportBatchId = batchId,
                SourceRowNumber = row.SourceRowNumber,
                Channel = channel,
                ExtractedDate = ValueParsers.Date(Cell(row, h, "EXTRACTED_DATE")),
                MerchantAcronym = ValueParsers.Clean(Cell(row, h, "MERCHANT_ACRONYM")),
                ProcessingCode = ValueParsers.Clean(Cell(row, h, "PROCESSING_CODE")),
                ReferenceNumber = ValueParsers.Clean(Cell(row, h, "REFERENCE_NUMBER")),
                CardNumberHashed = ValueParsers.Clean(Cell(row, h, "CARD_NUMBER_HASHED")),
                CardNumber = ValueParsers.Clean(Cell(row, h, "CARD_NUMBER")),
                Wording = ValueParsers.Clean(Cell(row, h, "WORDING")),
                ProductType = ValueParsers.Clean(Cell(row, h, "PRODUCT_TYPE")),
                TransactionSign = ValueParsers.Clean(Cell(row, h, "TRANSACTION_SIGN")),
                TransactionWording = ValueParsers.Clean(Cell(row, h, "TRANSACTION_WORDING")),
                TransactionDate = ValueParsers.Date(Cell(row, h, "TRANSACTION_DATE")),
                ProcessingDate = ValueParsers.Date(Cell(row, h, "PROCESSING_DATE")),
                TxnAmountRaw = ValueParsers.Clean(Cell(row, h, "TRANSACTION_AMOUNT")),
                TxnAmount = ValueParsers.SignedAmount(Cell(row, h, "TRANSACTION_AMOUNT"), Cell(row, h, "TRANSACTION_SIGN")),
                TxnCcy = txnCcy,
                TxnCcyCode = ValueParsers.CcyCode(txnCcy),
                InterchangeFeesRaw = ValueParsers.Clean(Cell(row, h, "INTERCHANGE_FEES")),
                InterchangeFees = ValueParsers.SignedAmount(Cell(row, h, "INTERCHANGE_FEES"), Cell(row, h, "INTERCHANGE_FEES_SIGN")),
                IntFeesCcy = feeCcy,
                IntFeesCcyCode = ValueParsers.CcyCode(feeCcy),
                BillingAmount = ValueParsers.Amount(Cell(row, h, "BILLING_AMOUNT")),
                BillingCcy = ValueParsers.Int(Cell(row, h, "BILLING_CURRENCY")),
                ConversionFees = ValueParsers.Amount(Cell(row, h, "CONVERSION_FEES")),
                SettlementAmount = ValueParsers.Amount(Cell(row, h, "SETTLEMENT_AMOUNT")),
                SettlementCcy = ValueParsers.Int(Cell(row, h, "SETTLEMENT_CURRENCY")),
            });
            n++;
        }
        return n;
    }

    private static int ParseInterchange(Dictionary<string, int> h, IEnumerable<IImportRow> rows, int batchId, AppDbContext db)
    {
        var n = 0;
        foreach (var row in rows)
        {
            if (IsBlankRow(row, h)) continue;
            db.StgInterchangeFees.Add(new StgInterchangeFee
            {
                ImportBatchId = batchId,
                SourceRowNumber = row.SourceRowNumber,
                MicrofilmRefNumber = ValueParsers.Clean(Cell(row, h, "MICROFILM REF NUMBER")),
                MicrofilmRefSeq = ValueParsers.Int(Cell(row, h, "MICROFILM REF SEQ")),
                ReferenceNumber = ValueParsers.Clean(Cell(row, h, "REFERENCE NUMBER")),
                CardNumberHashed = ValueParsers.Clean(Cell(row, h, "CARD NUMBER")),
                CardAccountEntityId = global::Recon.Web.Services.Recon.CardIdentity.FromSource(Cell(row, h, "CARD ACCOUNT ENTITY ID")),
                ProductCode = ValueParsers.Clean(Cell(row, h, "PRODUCT CODE")),
                ProductType = ValueParsers.Clean(Cell(row, h, "PRODUCT TYPE")),
                ClientCode = ValueParsers.Clean(Cell(row, h, "CLIENT CODE")),
                CardAccountNumber = ValueParsers.Clean(Cell(row, h, "CARD ACCOUNT NUMBER")),
                TransactionDate = ValueParsers.DateTimeValue(Cell(row, h, "TRANSACTION DATE")),
                ProcessingDate = ValueParsers.Date(Cell(row, h, "PROCESSING DATE")),
                TransactionCode = ValueParsers.Clean(Cell(row, h, "TRANSACTION CODE")),
                TransactionWording = ValueParsers.Clean(Cell(row, h, "TRANSACTION WORDING")),
                TxnAmount = ValueParsers.Amount(Cell(row, h, "TRANSACTION AMOUNT")),
                TxnCcy = ValueParsers.Int(Cell(row, h, "TRANSACTION CURRENCY")),
                BillingAmount = ValueParsers.Amount(Cell(row, h, "BILLING AMOUNT")),
                BillingCcy = ValueParsers.Int(Cell(row, h, "BILLING CURRENCY")),
                SettlementAmount = ValueParsers.Amount(Cell(row, h, "SETTLEMENT AMOUNT")),
                SettlementCcy = ValueParsers.Int(Cell(row, h, "SETTLEMENT CURRENCY")),
                InterchangeFeesRaw = ValueParsers.Clean(Cell(row, h, "INTERCHANGE FEES")),
                InterchangeFees = ValueParsers.SignedAmount(Cell(row, h, "INTERCHANGE FEES"), Cell(row, h, "INTERCHANGE FEES SIGN")),
                IntFeesCcy = ValueParsers.Int(Cell(row, h, "INTERCHANGE FEES CURRENCY")),
                OrigineCode = ValueParsers.Clean(Cell(row, h, "ORIGINE CODE")),
                TerminalId = ValueParsers.Clean(Cell(row, h, "TERMINAL ID")),
                MerchantAcronym = ValueParsers.Clean(Cell(row, h, "MERCHANT ACRONYM")),
                IssMarkupAmount = ValueParsers.Amount(Cell(row, h, "ISS MARKUP AMOUNT")),
                ConversionFees = ValueParsers.Amount(Cell(row, h, "CONVERSION FEES")),
            });
            n++;
        }
        return n;
    }

    private static int ParseGl(Dictionary<string, int> h, IEnumerable<IImportRow> rows, int batchId, AppDbContext db)
    {
        var n = 0;
        foreach (var row in rows)
        {
            if (IsBlankRow(row, h)) continue;
            var entry = new StgGlEntry
            {
                ImportBatchId = batchId,
                SourceRowNumber = row.SourceRowNumber,
                SolId = ValueParsers.Clean(Cell(row, h, "SOL_ID")),
                AcctNum = ValueParsers.Clean(Cell(row, h, "ACCT_NUM")),
                GlSubHeadCode = ValueParsers.Clean(Cell(row, h, "GL_SUB_HEAD_CODE")),
                GlAccount = ValueParsers.Clean(Cell(row, h, "GL_ACCOUNT")),
                AcctName = ValueParsers.Clean(Cell(row, h, "ACCT_NAME")),
                TranDate = ValueParsers.Date(Cell(row, h, "TRAN_DATE")),
                ValueDate = ValueParsers.Date(Cell(row, h, "VALUE_DATE")),
                TranId = ValueParsers.Clean(Cell(row, h, "TRAN_ID")),
                RefNum = ValueParsers.Clean(Cell(row, h, "REF_NUM")),
                TranParticular = ValueParsers.Clean(Cell(row, h, "TRAN_PARTICULAR")),
                TranParticular2 = ValueParsers.Clean(Cell(row, h, "TRAN_PARTICULAR_2")),
                TranParticularCode = ValueParsers.Clean(Cell(row, h, "TRAN_PARTICULAR_CODE")),
                Debit = ValueParsers.Amount(Cell(row, h, "DEBIT")),
                Credit = ValueParsers.Amount(Cell(row, h, "CREDIT")),
                TranRmks = global::Recon.Web.Services.Recon.CardIdentity.ImportRemarks(Cell(row, h, "TRAN_RMKS")),
                TranAmt = ValueParsers.Amount(Cell(row, h, "TRAN_AMT")),
                TranCcyCode = ValueParsers.Clean(Cell(row, h, "TRAN_CRNCY_CODE")),
                EntryUserId = ValueParsers.Clean(Cell(row, h, "ENTRY_USER_ID")),
                VfdUserId = ValueParsers.Clean(Cell(row, h, "VFD_USER_ID")),
                EntryDate = ValueParsers.DateTimeValue(Cell(row, h, "ENTRY_DATE")),
                VfdDate = ValueParsers.DateTimeValue(Cell(row, h, "VFD_DATE")),
                Rate = ValueParsers.Amount(Cell(row, h, "RATE")),
                PartTranSrlNum = ValueParsers.Int(Cell(row, h, "PART_TRAN_SRL_NUM")),
                MurAmt = ValueParsers.Amount(Cell(row, h, "MUR_AMT")),
            };

            if (IsGlBulkPosting(entry.TranParticular, entry.TranParticular2, entry.TranRmks))
                db.StgGlBulkPostings.Add(ToBulkPosting(entry));
            else
                db.StgGlEntries.Add(entry);
            n++;
        }
        return n;
    }

    private static bool IsGlBulkPosting(string? tranParticular, string? tranParticular2, string? tranRmks)
    {
        var text = string.Join(' ', tranParticular, tranParticular2, tranRmks).ToUpperInvariant();
        return text.Contains("DEBIT CARD SETTLEMENT", StringComparison.Ordinal)
            || text.Contains("MC SETTLEMENT", StringComparison.Ordinal)
            || text.Contains("CARD SETTLEMENT", StringComparison.Ordinal)
            || text.Contains("SETTLEMENT DD", StringComparison.Ordinal);
    }

    private static StgGlBulkPosting ToBulkPosting(StgGlEntry entry) => new()
    {
        ImportBatchId = entry.ImportBatchId,
        SourceRowNumber = entry.SourceRowNumber,
        SolId = entry.SolId,
        AcctNum = entry.AcctNum,
        GlSubHeadCode = entry.GlSubHeadCode,
        GlAccount = entry.GlAccount,
        AcctName = entry.AcctName,
        TranDate = entry.TranDate,
        ValueDate = entry.ValueDate,
        TranId = entry.TranId,
        RefNum = entry.RefNum,
        TranParticular = entry.TranParticular,
        TranParticular2 = entry.TranParticular2,
        TranParticularCode = entry.TranParticularCode,
        Debit = entry.Debit,
        Credit = entry.Credit,
        TranRmks = entry.TranRmks,
        TranAmt = entry.TranAmt,
        TranCcyCode = entry.TranCcyCode,
        EntryUserId = entry.EntryUserId,
        VfdUserId = entry.VfdUserId,
        EntryDate = entry.EntryDate,
        VfdDate = entry.VfdDate,
        Rate = entry.Rate,
        PartTranSrlNum = entry.PartTranSrlNum,
        MurAmt = entry.MurAmt,
    };
}
