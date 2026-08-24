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

    private sealed class TestCurrentUser : ICurrentUserService
    {
        public Guid UserId => CurrentUserService.SystemUserId;
    }
}
