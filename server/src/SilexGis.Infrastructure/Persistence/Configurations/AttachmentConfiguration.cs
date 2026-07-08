// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("attachments");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.EntityType).HasConversion<short>();
        builder.Property(x => x.Role).HasConversion<short>();
        builder.Property(x => x.Caption).HasMaxLength(500);

        builder.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SilexGisUser>().WithMany().HasForeignKey(x => x.AddedBy).OnDelete(DeleteBehavior.SetNull);

        // The polymorphic target has no FK by design; lookups are by (type, id).
        builder.HasIndex(x => new { x.EntityType, x.EntityId });
        builder.HasIndex(x => x.FileId);
    }
}
