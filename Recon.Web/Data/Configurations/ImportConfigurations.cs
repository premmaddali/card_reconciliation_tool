using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recon.Web.Domain.Import;

namespace Recon.Web.Data.Configurations;

public class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
{
    public void Configure(EntityTypeBuilder<ImportBatch> b)
    {
        b.HasKey(x => x.ImportBatchId);
        b.Property(x => x.SourceType).HasConversion<byte>();
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.FileName).HasMaxLength(260);
        b.Property(x => x.ContentHash).HasMaxLength(64);
        b.Property(x => x.PeriodKey).HasMaxLength(7);
        // Same file cannot be re-imported under the same source type.
        b.HasIndex(x => new { x.SourceType, x.ContentHash }).IsUnique();
        b.HasIndex(x => new { x.SourceType, x.PeriodKey });
        b.HasIndex(x => x.SchemeProfileId);
    }
}

public class ReconRunConfiguration : IEntityTypeConfiguration<ReconRun>
{
    public void Configure(EntityTypeBuilder<ReconRun> b)
    {
        b.HasKey(x => x.ReconRunId);
        b.Property(x => x.Function).HasConversion<byte>();
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.VerificationStatus).HasConversion<byte>();
        b.Property(x => x.PeriodKey).HasMaxLength(7);
        b.Property(x => x.NextAction).HasMaxLength(120);
        b.Property(x => x.ResponsibleActor).HasMaxLength(250);
        b.Property(x => x.AverageConfidence).HasColumnType("decimal(5,2)");
        b.Property(x => x.AmountTolerance).HasColumnType("decimal(19,4)");

        b.HasOne(x => x.ProcessorBatch).WithMany().HasForeignKey(x => x.ProcessorBatchId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.GlBatch).WithMany().HasForeignKey(x => x.GlBatchId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.NetworkBatch).WithMany().HasForeignKey(x => x.NetworkBatchId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.Function, x.PeriodKey });
        b.HasIndex(x => new { x.VerificationStatus, x.CurrentWorkflowStepId });
        b.HasIndex(x => x.SchemeProfileId);
    }
}

public class BatchArchiveConfiguration : IEntityTypeConfiguration<BatchArchive>
{
    public void Configure(EntityTypeBuilder<BatchArchive> b)
    {
        b.HasKey(x => x.BatchArchiveId);
        b.Property(x => x.Function).HasConversion<byte>();
        b.Property(x => x.PeriodKey).HasMaxLength(7);
        b.Property(x => x.SnapshotJson).HasColumnType("nvarchar(max)");
        b.HasIndex(x => x.OriginalReconRunId).IsUnique();
        b.HasIndex(x => new { x.Function, x.PeriodKey, x.SchemeProfileId });
    }
}

public class BatchArchiveTransactionConfiguration : IEntityTypeConfiguration<BatchArchiveTransaction>
{
    public void Configure(EntityTypeBuilder<BatchArchiveTransaction> b)
    {
        b.HasKey(x => x.BatchArchiveTransactionId);
        b.Property(x => x.MatchStatus).HasConversion<byte>();
        b.Property(x => x.ReconciliationAmount).HasColumnType("decimal(19,4)");
        b.Property(x => x.GlAmount).HasColumnType("decimal(19,4)");
        b.Property(x => x.SnapshotJson).HasColumnType("nvarchar(max)");
        b.HasOne(x => x.BatchArchive).WithMany(x => x.Transactions).HasForeignKey(x => x.BatchArchiveId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class BatchFreezeConfiguration : IEntityTypeConfiguration<BatchFreeze>
{
    public void Configure(EntityTypeBuilder<BatchFreeze> b)
    {
        b.HasKey(x => x.BatchFreezeId);
        b.Property(x => x.Function).HasConversion<byte>();
        b.Property(x => x.PeriodKey).HasMaxLength(7);
        b.HasIndex(x => new { x.Function, x.PeriodKey, x.SchemeProfileId }).IsUnique();
    }
}
