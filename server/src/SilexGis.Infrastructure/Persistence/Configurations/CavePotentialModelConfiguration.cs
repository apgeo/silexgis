// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Potential;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class CavePotentialModelConfiguration : IEntityTypeConfiguration<CavePotentialModel>
{
    public void Configure(EntityTypeBuilder<CavePotentialModel> builder)
    {
        builder.ToTable("cave_potential_models");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(CavePotentialModelRules.MaxNameLength);
        builder.Property(x => x.Description).HasMaxLength(CavePotentialModelRules.MaxDescriptionLength);
        builder.Property(x => x.Method).HasConversion<short>();
        builder.Property(x => x.Definition).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.Visibility).HasConversion<short>();

        // Goes with its owner, as a saved view and a saved filter do: a model is one person's
        // kept recipe and holds no rows anybody else relies on. A club that dissolves leaves the
        // models shared with it standing, private to whoever wrote them.
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<CavingGroup>().WithMany().HasForeignKey(x => x.CavingGroupId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.OwnerUserId).HasDatabaseName("ix_cave_potential_models_owner_user_id");
        builder.HasIndex(x => x.CavingGroupId).HasDatabaseName("ix_cave_potential_models_caving_group_id");
    }
}
