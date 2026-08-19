using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recon.Web.Domain.Identity;

namespace Recon.Web.Data.Configurations;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.Property(x => x.FullName).HasMaxLength(150).IsRequired();
        b.Property(x => x.EmployeeCode).HasMaxLength(30);
        b.HasIndex(x => x.EmployeeCode).IsUnique().HasFilter("[EmployeeCode] IS NOT NULL");
        b.Property(x => x.Designation).HasMaxLength(100);
        b.Property(x => x.AuthProvider).HasConversion<byte>();
        b.Property(x => x.AdAccountName).HasMaxLength(150);
        b.HasIndex(x => x.AdAccountName).IsUnique().HasFilter("[AdAccountName] IS NOT NULL");
    }
}

public class AppPageConfiguration : IEntityTypeConfiguration<AppPage>
{
    public void Configure(EntityTypeBuilder<AppPage> b)
    {
        b.ToTable("AppPage");
        b.HasKey(x => x.AppPageId);
        b.Property(x => x.PageName).HasMaxLength(100).IsRequired();
        b.Property(x => x.PageCode).HasMaxLength(50).IsRequired();
        b.HasIndex(x => x.PageCode).IsUnique();
        b.Property(x => x.Route).HasMaxLength(200);
        b.Property(x => x.Icon).HasMaxLength(50);
        b.HasOne(x => x.ParentPage).WithMany(x => x.Children)
            .HasForeignKey(x => x.ParentPageId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AppAccessConfiguration : IEntityTypeConfiguration<AppAccess>
{
    public void Configure(EntityTypeBuilder<AppAccess> b)
    {
        b.ToTable("AppAccess");
        b.HasKey(x => x.AppAccessId);
        b.Property(x => x.AccessName).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.AccessName).IsUnique().HasFilter("[IsDeleted] = 0");
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.UserType).HasConversion<byte>();
        b.HasOne(x => x.LandingPage).WithMany()
            .HasForeignKey(x => x.LandingPageId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AppAccessMenuConfiguration : IEntityTypeConfiguration<AppAccessMenu>
{
    public void Configure(EntityTypeBuilder<AppAccessMenu> b)
    {
        b.ToTable("AppAccessMenu");
        b.HasKey(x => x.AppAccessMenuId);
        b.HasIndex(x => new { x.AppAccessId, x.PageId }).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasOne(x => x.AppAccess).WithMany(x => x.Menus)
            .HasForeignKey(x => x.AppAccessId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Page).WithMany()
            .HasForeignKey(x => x.PageId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class UserAppAccessConfiguration : IEntityTypeConfiguration<UserAppAccess>
{
    public void Configure(EntityTypeBuilder<UserAppAccess> b)
    {
        b.ToTable("UserAppAccess");
        b.HasKey(x => x.UserAppAccessId);
        b.HasIndex(x => new { x.UserId, x.AppAccessId }).IsUnique().HasFilter("[IsDeleted] = 0");
        b.HasOne(x => x.User).WithMany(x => x.UserAppAccesses)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.AppAccess).WithMany(x => x.Users)
            .HasForeignKey(x => x.AppAccessId).OnDelete(DeleteBehavior.Cascade);
    }
}
