// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SilexGis.Domain.Entities;

namespace SilexGis.Infrastructure.Persistence.Configurations;

public sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("app_settings");
        builder.HasKey(x => x.Key);

        builder.Property(x => x.Key).HasMaxLength(64);
        builder.Property(x => x.Value).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
    }
}

public sealed class MessageTemplateConfiguration : IEntityTypeConfiguration<MessageTemplate>
{
    public void Configure(EntityTypeBuilder<MessageTemplate> builder)
    {
        builder.ToTable("message_templates");

        builder.Property(x => x.Key).HasMaxLength(64);
        builder.Property(x => x.Locale).HasMaxLength(10);
        builder.Property(x => x.Subject).HasMaxLength(300);
        // Generous but bounded: an operator writing a message longer than this has lost the plot,
        // and an unbounded column is a place for someone to put a megabyte.
        builder.Property(x => x.Body).HasMaxLength(8000);

        // One rewrite per message per language; the absence of a row is what selects the default.
        builder.HasIndex(x => new { x.Key, x.Locale }).IsUnique();
    }
}
