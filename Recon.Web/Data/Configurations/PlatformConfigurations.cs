using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recon.Web.Platform.Model;

namespace Recon.Web.Data.Configurations;

public class SchemeProfileConfiguration : IEntityTypeConfiguration<SchemeProfile>
{
    public void Configure(EntityTypeBuilder<SchemeProfile> b)
    {
        b.ToTable("SchemeProfiles");
        b.HasKey(x => x.SchemeProfileId);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Scheme).HasMaxLength(50).IsRequired();
        b.Property(x => x.Cms).HasMaxLength(50).IsRequired();
        b.Property(x => x.TransactionAdapterCode).HasMaxLength(100).IsRequired();
        b.Property(x => x.GlcAdapterCode).HasMaxLength(100).IsRequired();
        b.Property(x => x.Functions).HasMaxLength(200);
        b.Property(x => x.MatchThresholdOverride).HasPrecision(5, 2);
        b.HasIndex(x => new { x.Scheme, x.Cms }).IsUnique();
    }
}

public class NormalizedTransactionConfiguration : IEntityTypeConfiguration<NormalizedTransaction>
{
    public void Configure(EntityTypeBuilder<NormalizedTransaction> b)
    {
        b.ToTable("NormalizedTransactions");
        b.HasKey(x => x.NormalizedTransactionId);
        b.Property(x => x.SourceTable).HasMaxLength(64);
        b.Property(x => x.Reference).HasMaxLength(64);
        b.Property(x => x.ExternalReference).HasMaxLength(64);
        b.Property(x => x.AuthorizationCode).HasMaxLength(16);
        b.Property(x => x.TraceNumber).HasMaxLength(32);
        b.Property(x => x.BatchNumber).HasMaxLength(32);
        b.Property(x => x.Merchant).HasMaxLength(128);
        b.Property(x => x.Terminal).HasMaxLength(32);
        b.Property(x => x.Acquirer).HasMaxLength(32);
        b.Property(x => x.Issuer).HasMaxLength(32);
        b.Property(x => x.MaskedCardNumber).HasMaxLength(48);
        b.Property(x => x.CardType).HasMaxLength(8);
        b.Property(x => x.Scheme).HasMaxLength(32);
        b.Property(x => x.Processor).HasMaxLength(64);
        b.Property(x => x.Account).HasMaxLength(48);
        b.Property(x => x.Description).HasMaxLength(256);
        b.Property(x => x.TransactionType).HasMaxLength(64);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.BillingAmount).HasPrecision(19, 4);
        b.Property(x => x.SettlementAmount).HasPrecision(19, 4);
        // High-volume staging-style table: no audit columns, immutable captures.
        b.HasIndex(x => new { x.ImportBatchId, x.Reference });
        b.HasIndex(x => new { x.ImportBatchId, x.Currency, x.Amount });
    }
}

public class NormalizedGlEntryConfiguration : IEntityTypeConfiguration<NormalizedGlEntry>
{
    public void Configure(EntityTypeBuilder<NormalizedGlEntry> b)
    {
        b.ToTable("NormalizedGlEntries");
        b.HasKey(x => x.NormalizedGlEntryId);
        b.Property(x => x.SourceTable).HasMaxLength(64);
        b.Property(x => x.Account).HasMaxLength(48);
        b.Property(x => x.Reference).HasMaxLength(64);
        b.Property(x => x.Description).HasMaxLength(256);
        b.Property(x => x.DebitCreditIndicator).HasMaxLength(2);
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.HasIndex(x => new { x.ImportBatchId, x.Reference });
        b.HasIndex(x => new { x.ImportBatchId, x.Account });
    }
}
