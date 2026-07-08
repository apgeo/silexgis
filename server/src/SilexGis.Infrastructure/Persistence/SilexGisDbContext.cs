// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Identity;

namespace SilexGis.Infrastructure.Persistence;

public class SilexGisDbContext(DbContextOptions<SilexGisDbContext> options)
    : IdentityDbContext<SilexGisUser, SilexGisRole, Guid>(options)
{
    public DbSet<Team> Teams => Set<Team>();

    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();

    public DbSet<CaveType> CaveTypes => Set<CaveType>();

    public DbSet<EntranceType> EntranceTypes => Set<EntranceType>();

    public DbSet<RockType> RockTypes => Set<RockType>();

    public DbSet<FeatureType> FeatureTypes => Set<FeatureType>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<Cave> Caves => Set<Cave>();

    public DbSet<CaveEntrance> CaveEntrances => Set<CaveEntrance>();

    public DbSet<SurfaceFeature> SurfaceFeatures => Set<SurfaceFeature>();

    public DbSet<MapLayer> MapLayers => Set<MapLayer>();

    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();

    public DbSet<Geofile> Geofiles => Set<Geofile>();

    public DbSet<GeofileFeature> GeofileFeatures => Set<GeofileFeature>();

    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<GeoreferencedMap> GeoreferencedMaps => Set<GeoreferencedMap>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasPostgresExtension("postgis");
        builder.HasPostgresExtension("unaccent");

        // Identity tables use plain names (users/roles/…), not AspNet* defaults.
        builder.Entity<SilexGisUser>().ToTable("users");
        builder.Entity<SilexGisRole>().ToTable("roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");

        builder.Entity<SilexGisUser>(u =>
        {
            u.Property(x => x.DisplayName).HasMaxLength(100);
            u.Property(x => x.Bio).HasMaxLength(2000);
            u.Property(x => x.Locale).HasMaxLength(10);
        });

        builder.ApplyConfigurationsFromAssembly(typeof(SilexGisDbContext).Assembly);
    }
}
