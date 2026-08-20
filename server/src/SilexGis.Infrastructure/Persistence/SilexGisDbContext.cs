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
    public DbSet<CavingGroup> CavingGroups => Set<CavingGroup>();

    public DbSet<CavingGroupMembership> CavingGroupMemberships => Set<CavingGroupMembership>();

    public DbSet<Caver> Cavers => Set<Caver>();

    public DbSet<CaveType> CaveTypes => Set<CaveType>();

    public DbSet<EntranceType> EntranceTypes => Set<EntranceType>();

    public DbSet<RockType> RockTypes => Set<RockType>();

    public DbSet<FeatureType> FeatureTypes => Set<FeatureType>();

    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();

    public DbSet<DocumentTypeSchema> DocumentTypeSchemas => Set<DocumentTypeSchema>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<FileAccessEvent> FileAccessEvents => Set<FileAccessEvent>();

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

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();

    public DbSet<DocumentPage> DocumentPages => Set<DocumentPage>();

    public DbSet<DocumentComment> DocumentComments => Set<DocumentComment>();

    public DbSet<TextSearchLanguage> TextSearchLanguages => Set<TextSearchLanguage>();

    public DbSet<Cabinet> Cabinets => Set<Cabinet>();

    public DbSet<CabinetDocument> CabinetDocuments => Set<CabinetDocument>();

    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();

    public DbSet<Geofile> Geofiles => Set<Geofile>();

    public DbSet<GeofileFeature> GeofileFeatures => Set<GeofileFeature>();

    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();

    public DbSet<TermRuleSet> TermRuleSets => Set<TermRuleSet>();

    public DbSet<GeofileImportSession> GeofileImportSessions => Set<GeofileImportSession>();

    public DbSet<PhotoImportSession> PhotoImportSessions => Set<PhotoImportSession>();

    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    public DbSet<ImportBatchItem> ImportBatchItems => Set<ImportBatchItem>();

    public DbSet<UploadBatch> UploadBatches => Set<UploadBatch>();

    public DbSet<UploadBatchItem> UploadBatchItems => Set<UploadBatchItem>();

    public DbSet<UploadSession> UploadSessions => Set<UploadSession>();

    public DbSet<DuplicateUploadRecord> DuplicateUploadRecords => Set<DuplicateUploadRecord>();

    public DbSet<Album> Albums => Set<Album>();

    public DbSet<AlbumItem> AlbumItems => Set<AlbumItem>();

    public DbSet<AlbumShare> AlbumShares => Set<AlbumShare>();

    public DbSet<PhotoDetails> PhotoDetails => Set<PhotoDetails>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<GeoreferencedMap> GeoreferencedMaps => Set<GeoreferencedMap>();

    public DbSet<TripType> TripTypes => Set<TripType>();

    public DbSet<TripTypeSchema> TripTypeSchemas => Set<TripTypeSchema>();

    public DbSet<TripParticipantRole> TripParticipantRoles => Set<TripParticipantRole>();

    public DbSet<TripReportTemplate> TripReportTemplates => Set<TripReportTemplate>();

    public DbSet<TripLog> TripLogs => Set<TripLog>();

    public DbSet<TripLogParticipant> TripLogParticipants => Set<TripLogParticipant>();

    public DbSet<TripInvitation> TripInvitations => Set<TripInvitation>();

    public DbSet<Expedition> Expeditions => Set<Expedition>();

    public DbSet<ExpeditionTrip> ExpeditionTrips => Set<ExpeditionTrip>();

    public DbSet<ExpeditionRosterRole> ExpeditionRosterRoles => Set<ExpeditionRosterRole>();

    /// <summary>Who was at a camp and for which days — never who was on its trips.</summary>
    public DbSet<ExpeditionRosterEntry> ExpeditionRoster => Set<ExpeditionRosterEntry>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<Tagging> Taggings => Set<Tagging>();

    public DbSet<ResLinkRelationType> ResLinkRelationTypes => Set<ResLinkRelationType>();

    public DbSet<ResLink> ResLinks => Set<ResLink>();

    public DbSet<ResLinkMember> ResLinkMembers => Set<ResLinkMember>();

    public DbSet<PermissionGroup> PermissionGroups => Set<PermissionGroup>();

    public DbSet<PermissionGroupMember> PermissionGroupMembers => Set<PermissionGroupMember>();

    public DbSet<AccessEntry> AccessEntries => Set<AccessEntry>();

    public DbSet<FeatureSet> FeatureSets => Set<FeatureSet>();

    public DbSet<FeatureSetMember> FeatureSetMembers => Set<FeatureSetMember>();

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

        // Each of these is installed because something here calls into it: geometry columns and
        // their index, the dictionary that folds diacritics inside the text-search parser, and
        // the path type the cabinet tree is stored as. An extension nobody calls does not belong
        // in the list — it is a promise to whoever reads the schema that the database is doing
        // something it is not.
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
