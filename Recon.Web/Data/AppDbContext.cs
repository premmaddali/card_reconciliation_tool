using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Recon.Web.Domain.Common;
using Recon.Web.Domain.Identity;
using Recon.Web.Domain.Import;
using Recon.Web.Domain.Recon;
using Recon.Web.Domain.Reference;
using Recon.Web.Domain.Staging;

namespace Recon.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    // Identity / access
    public DbSet<AppPage> AppPages => Set<AppPage>();
    public DbSet<AppAccess> AppAccesses => Set<AppAccess>();
    public DbSet<AppAccessMenu> AppAccessMenus => Set<AppAccessMenu>();
    public DbSet<UserAppAccess> UserAppAccesses => Set<UserAppAccess>();

    // Import & run control
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ReconRun> ReconRuns => Set<ReconRun>();

    // Staging
    public DbSet<StgNetworkT464> StgNetworkT464 => Set<StgNetworkT464>();
    public DbSet<StgDebitSettlement> StgDebitSettlements => Set<StgDebitSettlement>();
    public DbSet<StgInterchangeFee> StgInterchangeFees => Set<StgInterchangeFee>();
    public DbSet<StgGlEntry> StgGlEntries => Set<StgGlEntry>();

    // Recon results
    public DbSet<ReconMatch> ReconMatches => Set<ReconMatch>();
    public DbSet<ReconException> ReconExceptions => Set<ReconException>();
    public DbSet<ReconExceptionComment> ReconExceptionComments => Set<ReconExceptionComment>();
    public DbSet<ReconPassSummary> ReconPassSummaries => Set<ReconPassSummary>();
    public DbSet<GlSettlementAccount> GlSettlementAccounts => Set<GlSettlementAccount>();
    public DbSet<IsoCurrency> IsoCurrencies => Set<IsoCurrency>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Global soft-delete query filter for every ISoftDeletable entity.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var body = Expression.Not(Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted)));
                modelBuilder.Entity(entityType.ClrType)
                    .HasQueryFilter(Expression.Lambda(body, parameter));
            }
        }
    }
}
