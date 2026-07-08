// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class CaveCenterlineConfiguration : IEntityTypeConfiguration<CaveCenterline>
{
    public void Configure(EntityTypeBuilder<CaveCenterline> builder)
    {
        builder.ToTable("cave_centerlines");
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).HasMaxLength(200);
        builder.Property(x => x.Source).HasConversion<short>();
        builder.Property(x => x.LengthM).HasPrecision(12, 2);
        builder.Property(x => x.Geom).HasColumnType("geometry(MultiLineStringZ, 4326)");

        builder.HasOne<Cave>().WithMany().HasForeignKey(x => x.CaveId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<SurveyModel>().WithMany().HasForeignKey(x => x.SurveyModelId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.Geom).HasMethod("gist");
        builder.HasIndex(x => x.CaveId);
    }
}
