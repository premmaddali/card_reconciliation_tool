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

        b.HasOne(x => x.ProcessorBatch).WithMany().HasForeignKey(x => x.ProcessorBatchId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.GlBatch).WithMany().HasForeignKey(x => x.GlBatchId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne(x => x.NetworkBatch).WithMany().HasForeignKey(x => x.NetworkBatchId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.Function, x.PeriodKey });
        b.HasIndex(x => new { x.VerificationStatus, x.CurrentWorkflowStepId });
        b.HasIndex(x => x.SchemeProfileId);
    }
}
