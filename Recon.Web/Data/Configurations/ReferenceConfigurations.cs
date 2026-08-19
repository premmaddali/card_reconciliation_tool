using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recon.Web.Domain.Reference;

namespace Recon.Web.Data.Configurations;

public class IsoCurrencyConfiguration : IEntityTypeConfiguration<IsoCurrency>
{
    public void Configure(EntityTypeBuilder<IsoCurrency> b)
    {
        b.HasKey(x => x.NumericCode);
        b.Property(x => x.NumericCode).ValueGeneratedNever();
        b.Property(x => x.AlphaCode).HasMaxLength(3);
        b.Property(x => x.Name).HasMaxLength(64);
    }
}
