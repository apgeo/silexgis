// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class SurveyTopologyConfiguration : IEntityTypeConfiguration<SurveyTopology>
{
    public void Configure(EntityTypeBuilder<SurveyTopology> builder)
    {
        builder.ToTable("survey_topology");

        // The survey model is the key, not a column beside one: a model has exactly one reading of
        // its own shape, and giving the row an identity of its own would let a second reading be
        // stored beside the first with nothing to say which is current.
        builder.HasKey(x => x.SurveyModelId);

        builder.HasOne(x => x.SurveyModel)
            .WithOne()
            .HasForeignKey<SurveyTopology>(x => x.SurveyModelId)
            .OnDelete(DeleteBehavior.Cascade);

        // Read across caves rather than only for one — how loop-rich a region's caves are is a
        // question about a set of them — so the figures are columns rather than a document.
        builder.HasIndex(x => x.CyclomaticNumber);
    }
}
