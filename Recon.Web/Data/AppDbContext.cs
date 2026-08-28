using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Recon.Web.Domain.Common;
using Recon.Web.Domain.Identity;
using Recon.Web.Domain.Import;
using Recon.Web.Domain.Recon;
using Recon.Web.Domain.Reference;
using Recon.Web.Domain.Staging;
using Recon.Web.Platform.Model;

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
    public DbSet<ReconParameters> ReconParameters => Set<ReconParameters>();
    public DbSet<BatchArchive> BatchArchives => Set<BatchArchive>();
    public DbSet<BatchArchiveTransaction> BatchArchiveTransactions => Set<BatchArchiveTransaction>();
    public DbSet<BatchFreeze> BatchFreezes => Set<BatchFreeze>();

    // Staging
    public DbSet<StgNetworkT464> StgNetworkT464 => Set<StgNetworkT464>();
    public DbSet<StgDebitSettlement> StgDebitSettlements => Set<StgDebitSettlement>();
    public DbSet<StgInterchangeFee> StgInterchangeFees => Set<StgInterchangeFee>();
    public DbSet<StgGlEntry> StgGlEntries => Set<StgGlEntry>();
    public DbSet<StgGlBulkPosting> StgGlBulkPostings => Set<StgGlBulkPosting>();

    // Recon results
    public DbSet<ReconMatch> ReconMatches => Set<ReconMatch>();
    public DbSet<ReconException> ReconExceptions => Set<ReconException>();
    public DbSet<ReconExceptionComment> ReconExceptionComments => Set<ReconExceptionComment>();
    public DbSet<ReconPassSummary> ReconPassSummaries => Set<ReconPassSummary>();
    public DbSet<AtmNetworkValidation> AtmNetworkValidations => Set<AtmNetworkValidation>();
    public DbSet<ReconWorkflowDefinition> ReconWorkflowDefinitions => Set<ReconWorkflowDefinition>();
    public DbSet<ReconWorkflowStep> ReconWorkflowSteps => Set<ReconWorkflowStep>();
    public DbSet<ReconWorkflowStepRole> ReconWorkflowStepRoles => Set<ReconWorkflowStepRole>();
    public DbSet<ReconWorkflowActor> ReconWorkflowActors => Set<ReconWorkflowActor>();
    public DbSet<ReconWorkflowHistory> ReconWorkflowHistories => Set<ReconWorkflowHistory>();
    public DbSet<GlSettlementAccount> GlSettlementAccounts => Set<GlSettlementAccount>();
    public DbSet<IsoCurrency> IsoCurrencies => Set<IsoCurrency>();

    // Platform (generic multi-scheme)
    public DbSet<SchemeProfile> SchemeProfiles => Set<SchemeProfile>();
    public DbSet<NormalizedTransaction> NormalizedTransactions => Set<NormalizedTransaction>();
    public DbSet<NormalizedGlEntry> NormalizedGlEntries => Set<NormalizedGlEntry>();

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
