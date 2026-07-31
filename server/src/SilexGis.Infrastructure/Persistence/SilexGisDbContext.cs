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

    public DbSet<Feature> Features => Set<Feature>();

    public DbSet<Cave> Caves => Set<Cave>();

    public DbSet<CaveEntrance> CaveEntrances => Set<CaveEntrance>();

    public DbSet<Centerline> Centerlines => Set<Centerline>();

    public DbSet<FeatureHierarchyEdge> FeatureHierarchyEdges => Set<FeatureHierarchyEdge>();

    public DbSet<FeatureAncestor> FeatureAncestors => Set<FeatureAncestor>();

    public DbSet<Hierarchy> Hierarchies => Set<Hierarchy>();

    public DbSet<HierarchyMembership> HierarchyMemberships => Set<HierarchyMembership>();

    public DbSet<LinkKind> LinkKinds => Set<LinkKind>();

    public DbSet<FeatureLink> FeatureLinks => Set<FeatureLink>();

    public DbSet<FeatureShare> FeatureShares => Set<FeatureShare>();

    public DbSet<MapLayer> MapLayers => Set<MapLayer>();

    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();

    public DbSet<Geofile> Geofiles => Set<Geofile>();

    public DbSet<GeofileFeature> GeofileFeatures => Set<GeofileFeature>();

    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<GeoreferencedMap> GeoreferencedMaps => Set<GeoreferencedMap>();

    public DbSet<TripLog> TripLogs => Set<TripLog>();

    public DbSet<TripLogCave> TripLogCaves => Set<TripLogCave>();

    public DbSet<TripLogParticipant> TripLogParticipants => Set<TripLogParticipant>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<Tagging> Taggings => Set<Tagging>();

    public DbSet<ObjectAcl> ObjectAcls => Set<ObjectAcl>();

    public DbSet<MapView> MapViews => Set<MapView>();

    public DbSet<SurveyModel> SurveyModels => Set<SurveyModel>();

    public DbSet<UserAddress> UserAddresses => Set<UserAddress>();

    public DbSet<UserNotificationPreference> UserNotificationPreferences => Set<UserNotificationPreference>();

    public DbSet<AccountDataExport> AccountDataExports => Set<AccountDataExport>();

    public DbSet<NotificationOutboxEntry> NotificationOutbox => Set<NotificationOutboxEntry>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasPostgresExtension("postgis");
        builder.HasPostgresExtension("unaccent");
        builder.HasPostgresExtension("ltree");

        // Identity tables use plain names (users/roles/…), not AspNet* defaults.
        builder.Entity<SilexGisUser>().ToTable("users");
        builder.Entity<SilexGisRole>().ToTable("roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");

        // User columns and every other mapping live in Configurations/, applied after the
        // renames above so their ToTable calls agree.
        builder.ApplyConfigurationsFromAssembly(typeof(SilexGisDbContext).Assembly);
    }
}
