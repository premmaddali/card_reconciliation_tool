using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recon.Web.Domain.Staging;

namespace Recon.Web.Data.Configurations;

public class StgNetworkT464Configuration : IEntityTypeConfiguration<StgNetworkT464>
{
    public void Configure(EntityTypeBuilder<StgNetworkT464> b)
    {
        b.HasKey(x => x.StgNetworkT464Id);
        b.Property(x => x.ReferenceNumber).HasMaxLength(12);
        foreach (var p in new[] { nameof(StgNetworkT464.CompletedAmtLocal), nameof(StgNetworkT464.CompletedAmtSettle),
                     nameof(StgNetworkT464.InterchangeFee), nameof(StgNetworkT464.RequestedAmtLocal) })
            b.Property(p).HasColumnType("decimal(19,4)");
        b.Property(x => x.ConversionRate).HasColumnType("decimal(19,8)");
        b.HasIndex(x => x.ReferenceNumber);
        b.HasIndex(x => x.ImportBatchId);
    }
}

public class StgDebitSettlementConfiguration : IEntityTypeConfiguration<StgDebitSettlement>
{
    public void Configure(EntityTypeBuilder<StgDebitSettlement> b)
    {
        b.HasKey(x => x.StgDebitSettlementId);
        b.Property(x => x.Channel).HasConversion<byte>();
        b.Property(x => x.ReferenceNumber).HasMaxLength(12);
        foreach (var p in new[] { nameof(StgDebitSettlement.TxnAmount), nameof(StgDebitSettlement.InterchangeFees),
                     nameof(StgDebitSettlement.BillingAmount), nameof(StgDebitSettlement.ConversionFees),
                     nameof(StgDebitSettlement.SettlementAmount) })
            b.Property(p).HasColumnType("decimal(19,4)");
        b.HasIndex(x => x.ReferenceNumber);
        b.HasIndex(x => new { x.Channel, x.ReferenceNumber });
        b.HasIndex(x => x.ImportBatchId);
    }
}

public class StgInterchangeFeeConfiguration : IEntityTypeConfiguration<StgInterchangeFee>
{
    public void Configure(EntityTypeBuilder<StgInterchangeFee> b)
    {
        b.HasKey(x => x.StgInterchangeFeeId);
        b.Property(x => x.ReferenceNumber).HasMaxLength(12);
        foreach (var p in new[] { nameof(StgInterchangeFee.TxnAmount), nameof(StgInterchangeFee.BillingAmount),
                     nameof(StgInterchangeFee.SettlementAmount), nameof(StgInterchangeFee.InterchangeFees),
                     nameof(StgInterchangeFee.IssMarkupAmount), nameof(StgInterchangeFee.ConversionFees) })
            b.Property(p).HasColumnType("decimal(19,4)");
        b.HasIndex(x => x.ReferenceNumber);
        b.HasIndex(x => x.ImportBatchId);
    }
}

public class StgGlEntryConfiguration : IEntityTypeConfiguration<StgGlEntry>
{
    public void Configure(EntityTypeBuilder<StgGlEntry> b)
    {
        b.HasKey(x => x.StgGlEntryId);
        foreach (var p in new[] { nameof(StgGlEntry.Debit), nameof(StgGlEntry.Credit), nameof(StgGlEntry.TranAmt),
                     nameof(StgGlEntry.Rate), nameof(StgGlEntry.MurAmt) })
            b.Property(p).HasColumnType("decimal(19,4)");
        b.HasIndex(x => x.GlSubHeadCode);
        b.HasIndex(x => x.TranDate);
        b.HasIndex(x => x.ImportBatchId);
    }
}

public class StgGlBulkPostingConfiguration : IEntityTypeConfiguration<StgGlBulkPosting>
{
    public void Configure(EntityTypeBuilder<StgGlBulkPosting> b)
    {
        b.HasKey(x => x.StgGlBulkPostingId);
        foreach (var p in new[] { nameof(StgGlBulkPosting.Debit), nameof(StgGlBulkPosting.Credit), nameof(StgGlBulkPosting.TranAmt),
                     nameof(StgGlBulkPosting.Rate), nameof(StgGlBulkPosting.MurAmt) })
            b.Property(p).HasColumnType("decimal(19,4)");
        b.HasIndex(x => x.GlSubHeadCode);
        b.HasIndex(x => x.TranDate);
        b.HasIndex(x => x.ImportBatchId);
        b.HasIndex(x => x.AcctNum);
    }
}
