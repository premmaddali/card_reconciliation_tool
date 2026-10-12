using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Recon.Web.Data;
using Recon.Web.Domain.Import;
using Recon.Web.Domain.Recon;
using Recon.Web.Domain.Staging;
using Recon.Web.Platform.Adapters;
using Recon.Web.Services.Recon;
using Xunit;

namespace Recon.Web.Tests;

public class MatchingSafeguardTests : IAsyncLifetime
{
    private ServiceProvider _services = null!;
    private IDbContextFactory<AppDbContext> Factory => _services.GetRequiredService<IDbContextFactory<AppDbContext>>();
    private static readonly DateOnly Day = new(2023, 3, 15);
    private int _processorBatch, _glBatch;
    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<AppDbContext>(o => o.UseSqlServer($"Server=localhost;Database=Recon_Safeguard_{Guid.NewGuid():N};Trusted_Connection=True;TrustServerCertificate=True"));
        _services = services.BuildServiceProvider();
        await using var db = await Factory.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        await DbSeeder.SeedGlAccountsAsync(db);
        var p = new ImportBatch { SourceType = SourceType.InterchangeFee, PeriodKey = "2023-03", ContentHash = "p", Status = ImportStatus.Parsed };
        var g = new ImportBatch { SourceType = SourceType.GlDump, PeriodKey = "2023-03", ContentHash = "g", Status = ImportStatus.Parsed };
        db.AddRange(p, g); await db.SaveChangesAsync();
        _processorBatch = p.ImportBatchId; _glBatch = g.ImportBatchId;
    }
    public async Task DisposeAsync()
    {
        await using var db = await Factory.CreateDbContextAsync();
        await db.Database.EnsureDeletedAsync(); await _services.DisposeAsync();
    }
    private async Task AddProcessor(decimal amount = 100, int currency = 480, string? reference = null, string code = "01", string? card = null)
    {
        await using var db = await Factory.CreateDbContextAsync();
        db.Add(new StgInterchangeFee { ImportBatchId = _processorBatch, ProductType = "DC", OrigineCode = "1",
            ReferenceNumber = reference, CardAccountEntityId = CardIdentity.Normalize(card), TransactionCode = code, TransactionDate = Day.ToDateTime(TimeOnly.MinValue),
            ProcessingDate = Day, BillingAmount = amount, BillingCcy = currency, TxnAmount = amount, TxnCcy = currency });
        await db.SaveChangesAsync();
    }
    private async Task AddGl(decimal amount = 100, string? currency = "MUR", string? reference = null, int dateLag = 0, string? card = null)
    {
        await using var db = await Factory.CreateDbContextAsync();
        db.Add(new StgGlEntry { ImportBatchId = _glBatch, AcctNum = "9849065772", TranAmt = amount, TranCcyCode = currency,
            TranDate = Day.AddDays(dateLag), ValueDate = Day.AddDays(dateLag), TranParticular = "POS/TEST", TranParticular2 = reference, TranRmks = CardIdentity.ImportRemarks(card) });
        await db.SaveChangesAsync();
    }
    private Task<ReconRunResult> Run(string period = "2023-03", decimal tolerance = 0) =>
        new ReconEngine(Factory, Options.Create(new MatchWeights())).RunAsync(ReconFunction.InterchangeToGl,
            _processorBatch, _glBatch, period, applyAmountTolerance: tolerance > 0, amountTolerance: tolerance);

    [Theory]
    [InlineData(100.03, 0)] [InlineData(101.29, 2)]
    public async Task Nearby_amount_without_reference_is_included_in_candidate_search(decimal amount, decimal tolerance)
    {
        await AddProcessor(amount); await AddGl();
        Assert.Equal(1, (await Run(tolerance: tolerance)).MatchedCount);
    }
    [Theory]
    [InlineData(2, 1)] [InlineData(1, 2)] [InlineData(2, 2)]
    public async Task Ties_are_ambiguous_and_never_assigned_by_source_order(int processors, int glRows)
    {
        for (var i = 0; i < processors; i++) await AddProcessor();
        for (var i = 0; i < glRows; i++) await AddGl();
        var result = await Run();
        Assert.Equal(0, result.MatchedCount);
        await using var db = await Factory.CreateDbContextAsync();
        Assert.Equal(processors, await db.ReconMatches.CountAsync(x => x.ReconRunId == result.ReconRunId && x.MatchStatus == MatchStatus.Ambiguous && x.GlStagingId == null));
    }
    [Fact]
    public async Task Different_events_with_same_reference_cannot_reuse_one_GL_row()
    {
        await AddProcessor(reference: "SHARED", code: "01"); await AddProcessor(reference: "SHARED", code: "09");
        await AddGl(reference: "SHARED");
        var result = await Run(); Assert.Equal(0, result.MatchedCount);
        await using var db = await Factory.CreateDbContextAsync();
        Assert.Equal(2, await db.ReconMatches.CountAsync(x => x.ReconRunId == result.ReconRunId && x.MatchStatus == MatchStatus.Ambiguous));
    }
    [Theory]
    [InlineData("USD")] [InlineData("unknown")]
    public async Task Exact_reference_cannot_bypass_currency_checks(string glCurrency)
    {
        await AddProcessor(reference: "REF"); await AddGl(currency: glCurrency, reference: "REF");
        Assert.Equal(0, (await Run()).MatchedCount);
    }
    [Fact]
    public async Task Exact_reference_with_disagreeing_dates_does_not_report_perfect_score()
    {
        await AddProcessor(reference: "REF"); await AddGl(reference: "REF", dateLag: 12);
        var result = await Run(); Assert.Equal(1, result.MatchedCount);
        Assert.True(result.AverageConfidence < 100);
    }
    [Fact]
    public async Task Strong_reference_evidence_can_distinguish_equal_amount_candidates()
    {
        await AddProcessor(reference: "REF"); await AddGl(reference: "OTHER"); await AddGl(reference: "REF");
        Assert.Equal(1, (await Run()).MatchedCount);
    }
    [Fact]
    public async Task Matching_amount_and_date_cannot_override_conflicting_identity_references()
    {
        await AddProcessor(reference: "PROCESSOR"); await AddGl(reference: "DIFFERENT");
        Assert.Equal(0, (await Run()).MatchedCount);
    }
    [Fact]
    public async Task Card_identity_distinguishes_equal_amount_transactions_in_native_and_normalized_runs()
    {
        await AddProcessor(card: "4111111111111111"); await AddProcessor(card: "5555555555554444");
        await AddGl(card: "5555555555554444"); await AddGl(card: "4111111111111111");
        Assert.Equal(2, (await Run()).MatchedCount);
        await using var db = await Factory.CreateDbContextAsync();
        await NormalizedProjection.ProjectBatchAsync(db, _processorBatch);
        await NormalizedProjection.ProjectBatchAsync(db, _glBatch);
        var normalized = await Run(); Assert.Equal(2, normalized.MatchedCount);
        var matches = await db.ReconMatches.Where(x => x.ReconRunId == normalized.ReconRunId).ToListAsync();
        Assert.All(matches, m => Assert.Contains("card identity agrees", m.ScoreBreakdown));
        Assert.DoesNotContain("4111111111111111", System.Text.Json.JsonSerializer.Serialize(matches));
    }
    [Theory]
    [InlineData(null)] [InlineData("SAMEREF")]
    public async Task Card_conflict_blocks_amount_and_reference_shortcuts(string? reference)
    {
        await AddProcessor(reference: reference, card: "4111111111111111");
        await AddGl(reference: reference, card: "5555555555554444");
        Assert.Equal(0, (await Run()).MatchedCount);
    }
    [Fact]
    public async Task Repeated_transactions_on_same_card_remain_ambiguous()
    {
        await AddProcessor(card: "4111111111111111"); await AddProcessor(card: "4111111111111111");
        await AddGl(card: "4111111111111111"); await AddGl(card: "4111111111111111");
        Assert.Equal(0, (await Run()).MatchedCount);
    }
    [Theory]
    [InlineData(1, "MUR", 100, 1)] [InlineData(2, "MUR", 100, 1)]
    [InlineData(3, "MUR", 100, 0)] [InlineData(0, "USD", 100, 0)] [InlineData(0, "MUR", 110, 0)]
    public async Task Card_identity_requires_date_amount_and_currency_agreement(int lag, string currency, decimal amount, int expected)
    {
        await AddProcessor(card: "4111111111111111");
        await AddGl(card: "4111111111111111", dateLag: lag, currency: currency, amount: amount);
        Assert.Equal(expected, (await Run()).MatchedCount);
    }
    [Fact]
    public async Task New_TMS_and_GL_headers_import_plain_card_evidence()
    {
        async Task<Recon.Web.Services.Import.ImportOutcome> Import(string name, string csv)
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
            return await new Recon.Web.Services.Import.ImportService(Factory).ImportAsync(stream, name, "2023-03");
        }
        var p = await Import("new-tms.csv", "MICROFILM_REF_NUMBER,INTERCHANGE_FEES,CARD_ACCOUNT_ENTITY_ID,CARD_NUMBER,PRODUCT_TYPE,ORIGINE_CODE,TRANSACTION_DATE,PROCESSING_DATE,TRANSACTION_AMOUNT,TRANSACTION_CURRENCY,BILLING_AMOUNT,BILLING_CURRENCY\n,0,4111111111111111,4111111111111111,DC,1,2023-03-15,2023-03-15,100,480,100,480\n");
        var g = await Import("new-gl.csv", "SOL_ID,GL_SUB_HEAD_CODE,ACCT_NUM,TRAN_DATE,VALUE_DATE,TRAN_AMT,TRAN_CRNCY_CODE,TRAN_PARTICULAR,TRAN_RMKS\n1,1,9849065772,2023-03-15,2023-03-15,100,MUR,POS/TEST,4111111111111111\n");
        Assert.True(p.Success, p.Error); Assert.True(g.Success, g.Error);
        await using var db = await Factory.CreateDbContextAsync();
        var transaction = await db.StgInterchangeFees.SingleAsync(x => x.ImportBatchId == p.ImportBatchId);
        var gl = await db.StgGlEntries.SingleAsync(x => x.ImportBatchId == g.ImportBatchId);
        Assert.Equal(CardIdentity.Normalize("4111111111111111"), transaction.CardAccountEntityId);
        Assert.Equal(transaction.CardAccountEntityId, gl.TranRmks);
        Assert.Equal("4111111111111111", transaction.CardAccountEntityId);
        Assert.Null(transaction.CardIdentityToken);
        var result = await new ReconEngine(Factory, Options.Create(new MatchWeights())).RunAsync(ReconFunction.InterchangeToGl,
            p.ImportBatchId!.Value, g.ImportBatchId!.Value, "2023-03");
        Assert.Equal(1, result.MatchedCount);
    }
    [Fact]
    public async Task Same_file_reimport_restores_plain_evidence_without_rewriting_reconciliation()
    {
        var service = new Recon.Web.Services.Import.ImportService(Factory);
        const string tms = "MICROFILM_REF_NUMBER,INTERCHANGE_FEES,CARD_ACCOUNT_ENTITY_ID,PRODUCT_TYPE,ORIGINE_CODE,TRANSACTION_DATE,PROCESSING_DATE,BILLING_AMOUNT,BILLING_CURRENCY\n,0,4111111111111111,DC,1,2023-03-15,2023-03-15,100,480\n";
        const string gl = "SOL_ID,GL_SUB_HEAD_CODE,ACCT_NUM,TRAN_DATE,VALUE_DATE,TRAN_AMT,TRAN_CRNCY_CODE,TRAN_PARTICULAR,TRAN_RMKS\n1,1,9849065772,2023-03-15,2023-03-15,100,MUR,POS/TEST,4111111111111111\n";
        async Task<Recon.Web.Services.Import.ImportOutcome> Import(string file, string text)
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
            return await service.ImportAsync(stream, file, "2023-03");
        }
        var p = await Import("restore-tms.csv", tms); var g = await Import("restore-gl.csv", gl);
        Assert.True(p.Success, p.Error); Assert.True(g.Success, g.Error);
        var run = await new ReconEngine(Factory, Options.Create(new MatchWeights())).RunAsync(ReconFunction.InterchangeToGl,
            p.ImportBatchId!.Value, g.ImportBatchId!.Value, "2023-03");
        string originalResults;
        await using (var db = await Factory.CreateDbContextAsync())
        {
            originalResults = System.Text.Json.JsonSerializer.Serialize(await db.ReconMatches.Where(x => x.ReconRunId == run.ReconRunId).ToListAsync());
            var row = await db.StgInterchangeFees.SingleAsync(x => x.ImportBatchId == p.ImportBatchId);
            row.CardAccountEntityId = null; row.CardIdentityToken = "card:v1:" + new string('A', 64);
            (await db.StgGlEntries.SingleAsync(x => x.ImportBatchId == g.ImportBatchId)).TranRmks = row.CardIdentityToken;
            await db.SaveChangesAsync();
        }
        var restoredP = await Import("restore-tms.csv", tms); var restoredG = await Import("restore-gl.csv", gl);
        Assert.True(restoredP.Duplicate); Assert.True(restoredG.Duplicate);
        Assert.Equal(p.ImportBatchId, restoredP.ImportBatchId); Assert.Equal(g.ImportBatchId, restoredG.ImportBatchId);
        await using var read = await Factory.CreateDbContextAsync();
        Assert.Equal("4111111111111111", (await read.StgInterchangeFees.SingleAsync(x => x.ImportBatchId == p.ImportBatchId)).CardAccountEntityId);
        Assert.Equal("4111111111111111", (await read.StgGlEntries.SingleAsync(x => x.ImportBatchId == g.ImportBatchId)).TranRmks);
        Assert.Equal(originalResults, System.Text.Json.JsonSerializer.Serialize(await read.ReconMatches.Where(x => x.ReconRunId == run.ReconRunId).ToListAsync()));
    }
    [Fact]
    public async Task Wrong_period_is_rejected_before_creating_a_run()
    {
        await AddProcessor(); await AddGl();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run("2026-03"));
        await using var db = await Factory.CreateDbContextAsync(); Assert.Empty(await db.ReconRuns.ToListAsync());
    }
    [Fact]
    public async Task Import_month_comes_from_processing_and_posting_dates_and_duplicate_import_corrects_old_label()
    {
        var service = new Recon.Web.Services.Import.ImportService(Factory);
        const string tms = "MICROFILM_REF_NUMBER,INTERCHANGE_FEES,PRODUCT_TYPE,TRANSACTION_DATE,PROCESSING_DATE,BILLING_AMOUNT,BILLING_CURRENCY\n,0,DC,2025-03-31,2025-04-02,100,480\n";
        const string gl = "SOL_ID,GL_SUB_HEAD_CODE,ACCT_NUM,TRAN_DATE,VALUE_DATE,TRAN_AMT,TRAN_CRNCY_CODE\n1,1,9849065772,2025-04-01,2025-03-31,100,MUR\n";
        async Task<Recon.Web.Services.Import.ImportOutcome> Import(string name, string text, string month)
        {
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
            return await service.ImportAsync(stream, name, month);
        }
        var p = await Import("april-tms.csv", tms, "2026-03"); var g = await Import("april-gl.csv", gl, "2026-03");
        Assert.True(p.Success, p.Error); Assert.True(g.Success, g.Error);
        await using (var db = await Factory.CreateDbContextAsync())
        {
            Assert.Equal("2025-04", (await db.ImportBatches.FindAsync(p.ImportBatchId))!.PeriodKey);
            Assert.Equal("2025-04", (await db.ImportBatches.FindAsync(g.ImportBatchId))!.PeriodKey);
            (await db.ImportBatches.FindAsync(p.ImportBatchId))!.PeriodKey = "2026-03";
            (await db.ImportBatches.FindAsync(g.ImportBatchId))!.PeriodKey = "2026-03";
            await db.SaveChangesAsync();
        }
        Assert.True((await Import("april-tms.csv", tms, "2025-04")).Duplicate);
        Assert.True((await Import("april-gl.csv", gl, "2025-04")).Duplicate);
        await using var read = await Factory.CreateDbContextAsync();
        Assert.Equal("2025-04", (await read.ImportBatches.FindAsync(p.ImportBatchId))!.PeriodKey);
        Assert.Equal("2025-04", (await read.ImportBatches.FindAsync(g.ImportBatchId))!.PeriodKey);
        await new ReconEngine(Factory, Options.Create(new MatchWeights())).RunAsync(ReconFunction.InterchangeToGl,
            p.ImportBatchId!.Value, g.ImportBatchId!.Value, "2025-04");
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Failed_plain_card_import_can_be_retried_without_duplicate_hash_or_partial_rows(bool excel)
    {
        const string header = "MICROFILM_REF_NUMBER,INTERCHANGE_FEES,CARD_ACCOUNT_ENTITY_ID,PRODUCT_TYPE,TRANSACTION_DATE,BILLING_AMOUNT,BILLING_CURRENCY";
        const string data = ",0,4111111111111111,DC,2023-03-15,100,480";
        using var source = new MemoryStream();
        var name = excel ? "retry-card.xlsx" : "retry-card.csv";
        if (excel)
        {
            using var workbook = new ClosedXML.Excel.XLWorkbook(); var sheet = workbook.AddWorksheet("TMS");
            var headings = header.Split(','); var values = data.Split(',');
            for (var column = 0; column < headings.Length; column++)
            { sheet.Cell(1, column + 1).Value = headings[column]; sheet.Cell(2, column + 1).Value = values[column]; }
            workbook.SaveAs(source);
        }
        else source.Write(System.Text.Encoding.UTF8.GetBytes(header + "\n" + data + "\n"));
        var service = new Recon.Web.Services.Import.ImportService(Factory);
        source.Position = 0; var first = await service.ImportAsync(source, name, "2023-03"); Assert.True(first.Success, first.Error);
        await using (var db = await Factory.CreateDbContextAsync())
        {
            var batch = (await db.ImportBatches.FindAsync(first.ImportBatchId))!;
            batch.Status = ImportStatus.Failed; batch.ParseError = "Previous card key failure";
            var partial = await db.StgInterchangeFees.SingleAsync(x => x.ImportBatchId == first.ImportBatchId);
            partial.CardAccountEntityId = null; partial.CardIdentityToken = "card:v1:" + new string('A', 64);
            await db.SaveChangesAsync();
        }
        source.Position = 0; var retry = await service.ImportAsync(source, name, "2023-03");
        Assert.True(retry.Success, retry.Error); Assert.False(retry.Duplicate); Assert.Equal(first.ImportBatchId, retry.ImportBatchId);
        await using var read = await Factory.CreateDbContextAsync();
        var row = await read.StgInterchangeFees.SingleAsync(x => x.ImportBatchId == retry.ImportBatchId);
        Assert.Equal("4111111111111111", row.CardAccountEntityId); Assert.Null(row.CardIdentityToken);
        var parsed = (await read.ImportBatches.FindAsync(retry.ImportBatchId))!;
        Assert.Equal(ImportStatus.Parsed, parsed.Status); Assert.Null(parsed.ParseError); Assert.Equal(1, parsed.RowCount);
    }
    [Fact]
    public async Task Retrying_failed_import_cannot_remove_source_rows_referenced_by_reconciliation()
    {
        const string csv = "MICROFILM_REF_NUMBER,INTERCHANGE_FEES,CARD_ACCOUNT_ENTITY_ID,PRODUCT_TYPE,TRANSACTION_DATE,BILLING_AMOUNT,BILLING_CURRENCY\n,0,4111111111111111,DC,2023-03-15,100,480\n";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var service = new Recon.Web.Services.Import.ImportService(Factory);
        var first = await service.ImportAsync(stream, "referenced-card.csv", "2023-03"); Assert.True(first.Success, first.Error);
        await using (var db = await Factory.CreateDbContextAsync())
        {
            (await db.ImportBatches.FindAsync(first.ImportBatchId))!.Status = ImportStatus.Failed;
            db.ReconRuns.Add(new ReconRun { ProcessorBatchId = first.ImportBatchId!.Value, GlBatchId = _glBatch,
                Function = ReconFunction.InterchangeToGl, PeriodKey = "2023-03", Status = ReconRunStatus.Completed });
            await db.SaveChangesAsync();
        }
        stream.Position = 0; var retry = await service.ImportAsync(stream, "referenced-card.csv", "2023-03");
        Assert.False(retry.Success); Assert.Contains("referenced", retry.Error);
        await using var read = await Factory.CreateDbContextAsync();
        Assert.Equal(1, await read.StgInterchangeFees.CountAsync(x => x.ImportBatchId == first.ImportBatchId));
    }
    [Fact]
    public async Task Mislabelled_import_cannot_hide_source_posting_date_mismatch()
    {
        await AddProcessor(); await AddGl();
        await using var db = await Factory.CreateDbContextAsync();
        foreach (var batch in await db.ImportBatches.ToListAsync()) batch.PeriodKey = "2026-03";
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run("2026-03"));
    }
    [Fact]
    public async Task Normalized_and_native_batches_use_same_recorded_billing_currency()
    {
        await AddProcessor(currency: 840, reference: "USDREF"); await AddGl(currency: "USD", reference: "USDREF");
        var native = await Run(); Assert.Equal(1, native.MatchedCount);
        await using var db = await Factory.CreateDbContextAsync();
        await NormalizedProjection.ProjectBatchAsync(db, _processorBatch);
        await NormalizedProjection.ProjectBatchAsync(db, _glBatch);
        var normalized = await Run(); Assert.Equal(native.MatchedCount, normalized.MatchedCount);
        var match = await db.ReconMatches.SingleAsync(x => x.ReconRunId == normalized.ReconRunId);
        Assert.Equal(840, match.Ccy); Assert.Equal(840, match.GlCcy);
    }
}

public class LocalAiReviewValidationTests
{
    private static readonly MatchingReviewCase Evidence = new(1, 10, "unique_date_window", "01",
        ReconSettlementLeg.DebitCardholder, new DateOnly(2023, 3, 15), 100, 480, 1,
        [new ReviewGlEvidence(20, 30, new DateOnly(2023, 3, 16), 100, 480, 1)]);

    [Fact]
    public void Evidence_linked_review_recommendation_is_allowed() =>
        LocalAiReviewService.Validate([Evidence], [new(1, "review_date_candidate", [20], "Verify reference and posting direction.")]);

    [Theory]
    [InlineData(999, "review_date_candidate", 20)]
    [InlineData(1, "approve_match", 20)]
    [InlineData(1, "review_date_candidate", 999)]
    public void Unknown_cases_actions_and_GL_citations_are_rejected(int matchId, string action, int glId) =>
        Assert.Throws<InvalidOperationException>(() => LocalAiReviewService.Validate([Evidence], [new(matchId, action, [glId], "Suggestion")]));

    [Fact]
    public void Missing_or_duplicated_cases_are_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => LocalAiReviewService.Validate([Evidence], []));
        Assert.Throws<InvalidOperationException>(() => LocalAiReviewService.Validate([Evidence],
            [new(1, "review_date_candidate", [20], "Suggestion"), new(1, "review_date_candidate", [20], "Suggestion")]));
    }
}
