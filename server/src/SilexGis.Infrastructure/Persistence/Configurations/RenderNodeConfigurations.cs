// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Auth;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class RenderNodeConfiguration : IEntityTypeConfiguration<RenderNode>
{
    public const int NameMaxLength = 100;

    public void Configure(EntityTypeBuilder<RenderNode> builder)
    {
        builder.ToTable("render_nodes");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(NameMaxLength);
        builder.Property(x => x.TokenPrefix).HasMaxLength(RenderNodeTokens.PrefixLength);
        builder.Property(x => x.Worker).HasMaxLength(100);
        builder.Property(x => x.Device).HasMaxLength(200);
        builder.Property(x => x.BlenderVersions).HasMaxLength(500);
        builder.Property(x => x.SkipReason).HasMaxLength(20);

        // Who issued it is kept while the account exists; the node does not go with the account.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.CreatedBy)
            .OnDelete(DeleteBehavior.SetNull);

        // The lookup every call a node makes.
        builder.HasIndex(x => x.TokenHash).HasDatabaseName("ix_render_nodes_token_hash").IsUnique();

        // One name for one machine among those still let in; a withdrawn node's name is free again.
        builder.HasIndex(x => x.Name).HasDatabaseName("ux_render_nodes_name_active")
            .IsUnique().HasFilter("revoked_at IS NULL");
    }
}

public sealed class FlybyRenderConfiguration : IEntityTypeConfiguration<FlybyRender>
{
    public const int NameMaxLength = 200;

    public const int MessageMaxLength = 2000;

    public void Configure(EntityTypeBuilder<FlybyRender> builder)
    {
        builder.ToTable("flyby_renders");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(NameMaxLength);
        builder.Property(x => x.Plan).HasMaxLength(10);
        builder.Property(x => x.Container).HasMaxLength(10);
        builder.Property(x => x.BundleSha256).HasMaxLength(64);
        builder.Property(x => x.Phase).HasMaxLength(50);
        builder.Property(x => x.Message).HasMaxLength(MessageMaxLength);
        builder.Property(x => x.Device).HasMaxLength(200);
        builder.Property(x => x.Notices).HasMaxLength(20_000);

        // A render is its requester's; it goes with the account.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.RequestedBy)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ProcessingJob>().WithMany().HasForeignKey(x => x.ProcessingJobId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<RenderNode>().WithMany().HasForeignKey(x => x.NodeId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasMany(x => x.Files).WithOne().HasForeignKey(x => x.RenderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.ProcessingJobId).HasDatabaseName("ix_flyby_renders_processing_job_id").IsUnique();
        builder.HasIndex(x => x.RequestedBy).HasDatabaseName("ix_flyby_renders_requested_by");
        builder.HasIndex(x => x.NodeId).HasDatabaseName("ix_flyby_renders_node_id");
        builder.HasIndex(x => x.ExpiresAt).HasDatabaseName("ix_flyby_renders_expires_at");
    }
}

public sealed class FlybyRenderFileConfiguration : IEntityTypeConfiguration<FlybyRenderFile>
{
    public void Configure(EntityTypeBuilder<FlybyRenderFile> builder)
    {
        builder.ToTable("flyby_render_files");
        builder.HasKey(x => new { x.RenderId, x.Name });
        builder.Property(x => x.Name).HasMaxLength(128);
        builder.Property(x => x.Role).HasMaxLength(20);
        builder.Property(x => x.Sha256).HasMaxLength(64);
        builder.Property(x => x.StoragePath).HasMaxLength(500);
    }
}
