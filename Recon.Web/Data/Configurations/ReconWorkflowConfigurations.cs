using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recon.Web.Domain.Recon;

namespace Recon.Web.Data.Configurations;

public class ReconWorkflowDefinitionConfiguration : IEntityTypeConfiguration<ReconWorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<ReconWorkflowDefinition> b)
    {
        b.HasKey(x => x.ReconWorkflowDefinitionId);
        b.Property(x => x.Name).HasMaxLength(100);
        b.HasIndex(x => x.Name).IsUnique();
    }
}

public class ReconWorkflowStepConfiguration : IEntityTypeConfiguration<ReconWorkflowStep>
{
    public void Configure(EntityTypeBuilder<ReconWorkflowStep> b)
    {
        b.HasKey(x => x.ReconWorkflowStepId);
        b.Property(x => x.Label).HasMaxLength(100);
        b.HasOne(x => x.WorkflowDefinition).WithMany(x => x.Steps).HasForeignKey(x => x.ReconWorkflowDefinitionId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.ReconWorkflowDefinitionId, x.StepOrder }).IsUnique();
    }
}

public class ReconWorkflowStepRoleConfiguration : IEntityTypeConfiguration<ReconWorkflowStepRole>
{
    public void Configure(EntityTypeBuilder<ReconWorkflowStepRole> b)
    {
        b.HasKey(x => x.ReconWorkflowStepRoleId);
        b.HasOne(x => x.WorkflowStep).WithMany(x => x.Roles).HasForeignKey(x => x.ReconWorkflowStepId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.AppAccess).WithMany().HasForeignKey(x => x.AppAccessId).OnDelete(DeleteBehavior.NoAction);
        b.HasIndex(x => new { x.ReconWorkflowStepId, x.AppAccessId }).IsUnique();
    }
}

public class ReconWorkflowActorConfiguration : IEntityTypeConfiguration<ReconWorkflowActor>
{
    public void Configure(EntityTypeBuilder<ReconWorkflowActor> b)
    {
        b.HasKey(x => x.ReconWorkflowActorId);
        b.Property(x => x.Status).HasConversion<byte>();
        b.HasIndex(x => new { x.ReconRunId, x.ReconWorkflowStepId, x.AppAccessId }).IsUnique();
        b.HasIndex(x => new { x.AppAccessId, x.Status });
    }
}

public class ReconWorkflowHistoryConfiguration : IEntityTypeConfiguration<ReconWorkflowHistory>
{
    public void Configure(EntityTypeBuilder<ReconWorkflowHistory> b)
    {
        b.HasKey(x => x.ReconWorkflowHistoryId);
        b.Property(x => x.Action).HasConversion<byte>();
        b.Property(x => x.Comment).HasMaxLength(500);
        b.HasIndex(x => new { x.ReconRunId, x.ActionedDate });
    }
}
