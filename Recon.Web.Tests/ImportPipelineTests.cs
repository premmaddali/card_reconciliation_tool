using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Recon.Web.Data;
using Recon.Web.Domain.Import;
using Recon.Web.Domain.Staging;
using Recon.Web.Services;
using Recon.Web.Services.Import;
using Xunit;

namespace Recon.Web.Tests;

/// <summary>
/// End-to-end import checks against the real BankOne sample exports in SharedFiles/.
/// Each test uses its own throwaway SQL Server database so runs stay isolated and repeatable.
/// </summary>
public class ImportPipelineTests : IAsyncLifetime
{
    private static readonly string SharedFiles =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "SharedFiles"));

    private readonly string _dbName = "Recon_Test_" + Guid.NewGuid().ToString("N")[..8];
    private ServiceProvider _sp = null!;

    public async Task InitializeAsync()
    {
        var conn = $"Server=localhost;Database={_dbName};Trusted_Connection=True;TrustServerCertificate=True";
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserService, TestCurrentUser>();
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContextFactory<AppDbContext>((sp, o) =>
        {
            o.UseSqlServer(conn);
            o.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
            o.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
        });
        services.AddScoped<IImportService, ImportService>();
        _sp = services.BuildServiceProvider();

        var db = await _sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        var db = await _sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        await db.Database.EnsureDeletedAsync();
        await _sp.DisposeAsync();
    }

    private async Task<ImportOutcome> Import(string fileName)
    {
        var svc = _sp.GetRequiredService<IImportService>();
        await using var fs = new FileStream(Path.Combine(SharedFiles, fileName), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return await svc.ImportAsync(fs, fileName, "2026-03");
    }

    [Fact]
    public async Task T464_detects_type_parses_all_rows_and_normalizes_amounts()
    {
        var outcome = await Import("Copy of T464.xlsx");

        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal(SourceType.NetworkT464, outcome.DetectedType);
        Assert.Equal(11533, outcome.RowCount);

        var db = await _sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        // First data row: Completed Amt TransLocal "0000003000.00" with indicator C, fee "00000026.00" C.
        var first = await db.StgNetworkT464.OrderBy(x => x.SourceRowNumber).FirstAsync();
        Assert.Equal("605813539621", first.ReferenceNumber);
        Assert.Equal(3000.00m, first.CompletedAmtLocal);   // padding stripped, sign applied
        Assert.Equal(26.00m, first.InterchangeFee);
        Assert.Equal(480, first.TxnCcy);
        Assert.Equal("MUR", first.TxnCcyCode);
    }

    [Fact]
    public async Task Atm_settlement_detects_channel_and_treats_NULL_fees_as_null()
    {
        var outcome = await Import("SharedFiles (5)/1-ATM MARCH 2026 DEBIT_SETTELMENT_REPORT.xlsx");

        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal(SourceType.AtmSettlement, outcome.DetectedType);
        Assert.Equal(22987, outcome.RowCount);

        var db = await _sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.True(await db.StgDebitSettlements.AllAsync(x => x.Channel == SettlementChannel.Atm));
        // ATM sample interchange fees are the literal "NULL" → must normalize to null.
        var first = await db.StgDebitSettlements.OrderBy(x => x.SourceRowNumber).FirstAsync();
        Assert.Null(first.InterchangeFees);
        Assert.Equal(-500m, first.TxnAmount);   // sign D → negative
    }

    [Fact]
    public async Task Duplicate_upload_reuses_the_existing_parsed_batch()
    {
        var first = await Import("Copy of T464.xlsx");
        Assert.True(first.Success);
        var second = await Import("Copy of T464.xlsx");
        Assert.True(second.Success, second.Error);
        Assert.True(second.Duplicate);
        Assert.Equal(first.ImportBatchId, second.ImportBatchId);

        var db = await _sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal(1, await db.ImportBatches.CountAsync());
    }

    [Theory]
    [InlineData("3-INTERCHANGE_FEE_REPORT_CREDIT_AND_DEBIT (1).xlsx", "605827366477", 335, 480, 335, 480)]
    [InlineData("April 2023.xlsx", "311714987036", 10000, 480, 220, 840)]
    public async Task Interchange_export_versions_detect_and_map_the_same_columns(string fileName, string reference, decimal amount, int currency, decimal settlement, int settlementCurrency)
    {
        var outcome = await Import("SharedFiles (5)/" + fileName);
        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal(SourceType.InterchangeFee, outcome.DetectedType);
        Assert.True(outcome.RowCount > 80000);
        await using var db = await _sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        Assert.Equal(outcome.RowCount, await db.StgInterchangeFees.CountAsync());
        Assert.Empty(await db.StgDebitSettlements.ToListAsync());
        var first = await db.StgInterchangeFees.OrderBy(x => x.SourceRowNumber).FirstAsync();
        Assert.Equal(reference, first.ReferenceNumber);
        Assert.NotNull(first.MicrofilmRefNumber);
        Assert.NotNull(first.CardAccountNumber);
        Assert.NotNull(first.TransactionDate);
        Assert.NotNull(first.ProductType);
        Assert.Equal(amount, first.TxnAmount);
        Assert.Equal(currency, first.TxnCcy);
        Assert.Equal(settlement, first.SettlementAmount);
        Assert.Equal(settlementCurrency, first.SettlementCcy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Csv_interchange_accepts_both_header_styles_even_with_atm_wording(bool underscores)
    {
        var header = "MICROFILM REF NUMBER,INTERCHANGE FEES,INTERCHANGE FEES SIGN,MERCHANT ACRONYM,SETTLEMENT AMOUNT,TRANSACTION WORDING,REFERENCE NUMBER,TRANSACTION AMOUNT,TRANSACTION CURRENCY";
        if (underscores) header = header.Replace(' ', '_');
        var csv = header + "\n12345,1.25,D,Merchant,99,ATM WITHDRAWAL,ABC123,99,480\n";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var svc = _sp.GetRequiredService<IImportService>();
        var outcome = await svc.ImportAsync(stream, "interchange.csv", "2023-04");
        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal(SourceType.InterchangeFee, outcome.DetectedType);
        Assert.Equal(1, outcome.RowCount);
        await using var db = await _sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var row = await db.StgInterchangeFees.SingleAsync();
        Assert.Equal("ABC123", row.ReferenceNumber);
        Assert.Equal(99m, row.TxnAmount);
        Assert.Equal(-1.25m, row.InterchangeFees);
    }

    [Theory]
    [InlineData("MERCHANT_ACRONYM,SETTLEMENT_AMOUNT,TRANSACTION_WORDING,REFERENCE_NUMBER", "Shop,12,PURCHASE,REF1", SourceType.PosSettlement)]
    [InlineData("MERCHANT_ACRONYM,SETTLEMENT_AMOUNT,TRANSACTION_WORDING,REFERENCE_NUMBER", "ATM,12,ATM WITHDRAWAL,REF1", SourceType.AtmSettlement)]
    [InlineData("SOL_ID,GL_SUB_HEAD_CODE,FORACID,TRAN_AMT", "1,2,123,12", SourceType.GlDump)]
    public async Task Csv_other_source_signatures_remain_distinct(string header, string data, SourceType expected)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(header + "\n" + data + "\n"));
        var outcome = await _sp.GetRequiredService<IImportService>().ImportAsync(stream, "sample.csv", "2023-04");
        Assert.True(outcome.Success, outcome.Error);
        Assert.Equal(expected, outcome.DetectedType);
        Assert.Equal(1, outcome.RowCount);
    }
    private sealed class TestCurrentUser : ICurrentUserService
    {
        public Guid UserId => CurrentUserService.SystemUserId;
    }
}
