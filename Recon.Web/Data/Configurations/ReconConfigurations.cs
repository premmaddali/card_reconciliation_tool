using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recon.Web.Domain.Recon;

namespace Recon.Web.Data.Configurations;

public class ReconMatchConfiguration : IEntityTypeConfiguration<ReconMatch>
{
    public void Configure(EntityTypeBuilder<ReconMatch> b)
    {
        b.HasKey(x => x.ReconMatchId);
        b.Property(x => x.MatchStatus).HasConversion<byte>();
        b.Property(x => x.ProcessorSource).HasConversion<byte>();
        b.Property(x => x.SettlementLeg).HasConversion<byte>().HasDefaultValue(ReconSettlementLeg.DebitCardholder);
        b.Property(x => x.ReferenceNumber).HasMaxLength(32);
        b.Property(x => x.ProcessorAccountNumber).HasMaxLength(128);
        b.Property(x => x.ProcessorProductType).HasMaxLength(4);
        b.Property(x => x.ProcessorMerchant).HasMaxLength(256);
        b.Property(x => x.ProcessorDescription).HasMaxLength(512);
        b.Property(x => x.GlDescription).HasMaxLength(512);
        b.Property(x => x.GlAccountNumber).HasMaxLength(20);
        b.Property(x => x.Confidence).HasColumnType("decimal(5,2)");
        b.Property(x => x.ScoreBreakdown).HasMaxLength(200);
        b.Property(x => x.MatchComment).HasMaxLength(500);
        foreach (var p in new[] { nameof(ReconMatch.ProcessorAmount), nameof(ReconMatch.BillingAmount), nameof(ReconMatch.SettlementAmount), nameof(ReconMatch.ReconciliationAmount), nameof(ReconMatch.GlAmount) })
            b.Property(p).HasColumnType("decimal(19,4)");
        b.HasOne(x => x.ReconRun).WithMany().HasForeignKey(x => x.ReconRunId);
        b.HasIndex(x => new { x.ReconRunId, x.MatchStatus });
    }
}

public class ReconExceptionConfiguration : IEntityTypeConfiguration<ReconException>
{
    public void Configure(EntityTypeBuilder<ReconException> b)
    {
        b.HasKey(x => x.ReconExceptionId);
        b.Property(x => x.ExceptionType).HasConversion<byte>();
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.SettlementLeg).HasConversion<byte>().HasDefaultValue(ReconSettlementLeg.DebitCardholder);
        b.Property(x => x.ReferenceNumber).HasMaxLength(32);
        b.Property(x => x.ProcessorAccountNumber).HasMaxLength(128);
        b.Property(x => x.GlAccountNumber).HasMaxLength(20);
        b.Property(x => x.Narrative).HasMaxLength(512);
        b.Property(x => x.Confidence).HasColumnType("decimal(5,2)");
        foreach (var p in new[] { nameof(ReconException.ExpectedAmount), nameof(ReconException.ActualAmount),
                     nameof(ReconException.Difference) })
            b.Property(p).HasColumnType("decimal(19,4)");
        b.HasOne(x => x.ReconRun).WithMany().HasForeignKey(x => x.ReconRunId);
        b.HasIndex(x => new { x.ReconRunId, x.Status });
    }
}

public class ReconExceptionCommentConfiguration : IEntityTypeConfiguration<ReconExceptionComment>
{
    public void Configure(EntityTypeBuilder<ReconExceptionComment> b)
    {
        b.HasKey(x => x.ReconExceptionCommentId);
        b.HasOne(x => x.ReconException).WithMany(x => x.Comments).HasForeignKey(x => x.ReconExceptionId);
    }
}

public class ReconPassSummaryConfiguration : IEntityTypeConfiguration<ReconPassSummary>
{
    public void Configure(EntityTypeBuilder<ReconPassSummary> b)
    {
        b.HasKey(x => x.ReconPassSummaryId);
        foreach (var p in new[] { nameof(ReconPassSummary.MatchedAmount), nameof(ReconPassSummary.ExceptionAmount) })
            b.Property(p).HasColumnType("decimal(19,4)");
        b.Property(x => x.AverageConfidence).HasColumnType("decimal(5,2)");
        b.HasOne(x => x.ReconRun).WithMany().HasForeignKey(x => x.ReconRunId);
        b.Property(x => x.SettlementLeg).HasConversion<byte>().HasDefaultValue(ReconSettlementLeg.DebitCardholder);
        b.HasIndex(x => new { x.ReconRunId, x.SettlementLeg, x.Ccy, x.Day });
    }
}

public class AtmNetworkValidationConfiguration : IEntityTypeConfiguration<AtmNetworkValidation>
{
    public void Configure(EntityTypeBuilder<AtmNetworkValidation> b)
    {
        b.HasKey(x => x.AtmNetworkValidationId);
        b.Property(x => x.Status).HasConversion<byte>();
        b.Property(x => x.ReferenceNumber).HasMaxLength(32);
        b.Property(x => x.Narrative).HasMaxLength(512);
        foreach (var property in new[] { nameof(AtmNetworkValidation.NetworkAmount), nameof(AtmNetworkValidation.NetworkFee), nameof(AtmNetworkValidation.AtmAmount), nameof(AtmNetworkValidation.AtmFee) })
            b.Property(property).HasColumnType("decimal(19,4)");
        b.HasOne<Recon.Web.Domain.Import.ReconRun>().WithMany().HasForeignKey(x => x.ReconRunId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.ReconRunId, x.Status });
        b.HasIndex(x => new { x.ReconRunId, x.ReferenceNumber });
    }
}

public class GlSettlementAccountConfiguration : IEntityTypeConfiguration<GlSettlementAccount>
{
    public void Configure(EntityTypeBuilder<GlSettlementAccount> b)
    {
        b.HasKey(x => x.GlSettlementAccountId);
        b.Property(x => x.AcctNum).HasMaxLength(20);
        b.Property(x => x.Role).HasConversion<byte>();
        b.Property(x => x.Description).HasMaxLength(80);
        b.HasIndex(x => x.AcctNum).IsUnique();
    }
}
