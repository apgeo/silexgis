// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class StorageAlertStateConfiguration : IEntityTypeConfiguration<StorageAlertState>
{
    public void Configure(EntityTypeBuilder<StorageAlertState> builder)
    {
        builder.ToTable("storage_alert_states", t =>
        {
            // Both are small enums stored as numbers; a value outside them would be a level or a
            // ceiling no code knows how to compare, so the database refuses it. Values are only
            // ever appended, and the upper bound moves with them in a migration.
            t.HasCheckConstraint("ck_storage_alert_states_limit_kind", "limit_kind >= 0 AND limit_kind <= 4");
            t.HasCheckConstraint("ck_storage_alert_states_level", "level >= 0 AND level <= 3");
        });

        // One row per ceiling: the key is the ceiling itself, which is what makes two passes
        // racing to record the same crossing collide instead of both reporting it.
        builder.HasKey(x => x.LimitKind);
        builder.Property(x => x.LimitKind).ValueGeneratedNever();
    }
}
