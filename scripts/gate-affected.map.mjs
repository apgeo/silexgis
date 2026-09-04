// SPDX-License-Identifier: AGPL-3.0-or-later

// Which integration test classes answer for a change, by the area the change lands in.
//
// This map exists so a slice can get an early integration verdict in minutes — the classes
// that own the touched area — instead of standing behind the whole suite for every edit.
// It deliberately fails CLOSED: a path this file does not claim selects the FULL suite, an
// area mapped to an empty list selects the FULL suite, and the full suite always remains the
// final proof for a batch. The targeted tier is an early warning, never a substitute.
//
// Maintenance is enforced, not hoped for: `gate-affected.test.mjs` fails when a feature or
// area directory appears that this file does not mention, and re-derives the two
// cross-cutting lists below from the test sources so they cannot drift silently.

// Named groups, so an area that exists in three layers (Api feature, Domain area,
// Infrastructure area) names its owners once.
export const groups = {
  trips: [
    'TripAndTagTests', 'TripAttendanceLimitTests', 'TripAudienceTests',
    'TripCalloutFieldsTests', 'TripCalloutStandDownTests', 'TripCalloutSweepTests',
    'TripCaveReachTests', 'TripChecklistTickTests', 'TripInvitationTests',
    'TripInvitationSubjectTests', 'TripMeetingPointTests', 'TripParticipantRoleVocabularyTests',
    'TripPlanNotificationTests', 'TripPromotionTests', 'TripReportDocumentTests',
    'TripReportTemplateTests', 'TripRoleLinkUnitOfWorkTests', 'TripSectionSchemaTests',
    'TripStatisticsTests', 'TripTypeVocabularyTests', 'TripsOfTheCallerTests',
  ],
  events: [
    'EventAccessDomainTests', 'EventAuthoringTests', 'EventInvitationTests',
    'EventLifecycleTests', 'EventReminderSweepTests', 'EventSeriesBulkTests', 'EventSeriesTests',
    'TripInvitationSubjectTests',
  ],
  expeditions: [
    'ExpeditionDiscoveryTests', 'ExpeditionEntityTests', 'ExpeditionLeadsTests',
    'ExpeditionListFilterTests', 'ExpeditionMapTests', 'ExpeditionMembershipTests',
    'ExpeditionPolymorphicTests', 'ExpeditionReportTests', 'ExpeditionRosterEntityTests',
    'ExpeditionRosterRoleVocabularyTests', 'ExpeditionRosterTests',
    'ExpeditionSharingCascadeTests', 'ExpeditionTests', 'ExpeditionTimelineTests',
  ],
  documents: [
    'ContentMetadataTests', 'DemoPdfTests', 'DocumentAccessApiTests',
    'DocumentAccessParityTests', 'DocumentCommentApiTests', 'DocumentContentSearchSqlTests',
    'DocumentContentSearchTests', 'DocumentConversionTests', 'DocumentFilterWorldTests',
    'DocumentLanguageTests', 'DocumentMetadataTests', 'DocumentModelTests',
    'DocumentSurfaceProtectionSweepTests', 'DocumentViewerBytesTests',
    'LegacyOfficeFixtureTests', 'PageTextTests', 'PdfAndOfficeTextExtractorTests',
    'TextExtractionPipelineTests', 'TextExtractorTests',
  ],
  photos: [
    'AlbumAndPublicGalleryTests', 'PhotoBytesProtectionTests', 'PhotoGalleryTests',
    'PhotoImportTests',
  ],
  checklists: ['ChecklistAccessTests', 'ChecklistAuthoringTests', 'TripChecklistTickTests'],
  notifications: [
    'AccountEmailTests', 'AdminMessagingTests', 'CavingGroupAnnouncementPaidCapTests',
    'EmailVerificationTests', 'NotificationDeliveryTests', 'SmsNotificationChannelTests',
    'TripPlanNotificationTests',
  ],
  calendar: ['CalendarTests', 'CalendarWindowTests'],
  access: [
    'AccessApiTests', 'AccessHistoryTests', 'AccessModelTests', 'AclAndCavingGroupTests',
    'DocumentAccessApiTests', 'DocumentAccessParityTests', 'ProfileVisibilityTests',
    'ProtectionDepthTests',
  ],
  caves: [
    'AssociationDisclosureTests', 'CaveDomainTests', 'CenterlineTests', 'HypsometryTests',
    'ProtectionDepthTests', 'StructureComparisonTests', 'SurveyMeshTests', 'SurveyModelTests',
  ],
  cavers: ['CaverRosterTests', 'ProfileVisibilityTests'],
  cavingGroups: [
    'AclAndCavingGroupTests', 'CavingGroupAnnouncementPaidCapTests', 'SeededGroupUpgradeTests',
  ],
  geoFeatures: [
    'ClosestApproachTests', 'FeatureFilterCompilerTests', 'FeatureHierarchyTests',
    'FeatureIntegrityTests', 'FeatureLinkTests', 'FeatureTests', 'GeoJsonGeometryTests',
    'PolygonMorphometryTests', 'StructureComparisonTests',
  ],
  featureSets: ['FeatureIntegrityTests', 'FeatureTests'],
  featureShares: ['FeatureShareTests'],
  filters: [
    'DocumentFilterWorldTests', 'FeatureFilterCompilerTests', 'FilterEndpointTests',
    'FilterParityTests', 'FilterSpikeTests', 'FilterWorldConformanceTests',
  ],
  files: [
    'AttachmentReachListingTests', 'FileAccessBatchParityTests', 'FileAttachmentTests',
    'UploadDestinationTests',
  ],
  uploads: ['UploadDestinationTests', 'UploadLimitsAndResumeTests', 'UploadSurfaceTests'],
  geofiles: ['GeofileTests'],
  georeferencedMaps: ['GeoreferencedMapTests'],
  history: ['AccessHistoryTests', 'HistoryTests'],
  imports: [
    'ArchiveUpgradeMigrationTests', 'BulkImportTests', 'PhotoImportTests', 'StagedImportTests',
  ],
  jobs: [
    'BulkImportTests', 'DocumentConversionTests', 'EventReminderSweepTests',
    'NotificationDeliveryTests', 'TextExtractionPipelineTests', 'TripCalloutSweepTests',
  ],
  map: ['ExpeditionMapTests', 'GeoJsonGeometryTests', 'TerrainOptionsTests'],
  mapViews: ['MapViewTests'],
  me: [
    'AccountEmailTests', 'AccountSettingsTests', 'CredentialRevocationTests',
    'TwoFactorChannelTests', 'UiDefaultsTests',
  ],
  users: [
    'AuthFlowTests', 'CredentialRevocationTests', 'EmailVerificationTests', 'ExternalAuthTests',
    'MfaAndRateLimitTests', 'ProfileVisibilityTests', 'TwoFactorChannelTests',
  ],
  resLinks: ['AnnotatedTextApiTests', 'ResLinkApiTests', 'ResLinkProtectionFloorTests'],
  search: ['DocumentContentSearchSqlTests', 'DocumentContentSearchTests'],
  statistics: ['DashboardTests', 'TripStatisticsTests'],
  dashboard: ['DashboardTests'],
  tags: ['TripAndTagTests'],
  taxonomies: [
    'ExpeditionRosterRoleVocabularyTests', 'TaxonomyWideningTests', 'TermRuleSetTests',
    'TripParticipantRoleVocabularyTests', 'TripTypeVocabularyTests',
  ],
  cabinets: ['CabinetApiTests', 'CabinetTreeTests'],
  crs: ['CrsTests', 'SpatialOptionsTests', 'WorkingSridBehaviourTests'],
  exports: ['SpreadsheetWriterTests'],
  about: ['ApiSmokeTests'],
  audit: ['AccessHistoryTests', 'HistoryTests'],
  admin: ['AdminMessagingTests', 'SeededGroupUpgradeTests'],
  settings: ['AccountSettingsTests', 'TerrainOptionsTests', 'UiDefaultsTests'],
  messaging: [
    'AdminMessagingTests', 'CavingGroupAnnouncementPaidCapTests', 'NotificationDeliveryTests',
    'SmsNotificationChannelTests',
  ],
  sms: ['MfaAndRateLimitTests', 'TwoFactorChannelTests'],
  surveys: [
    'CavePassageShapeTests', 'CaveSurveyStatisticsTests', 'HypsometryTests',
    'StructureComparisonTests',
    'SurveyFormatReaderTests', 'SurveyGraphExtractorTests', 'SurveyGraphTests',
    'SurveyMeshTests', 'SurveyModelTests', 'SurveyPlacementTests',
    'SurveySegmentSubstrateTests', 'SurveySourceTests',
  ],
  // What a device asks of this installation, and what it may carry away. The protocol suites
  // and the registration and seed paths a device signs in through are one selection: they fail
  // together when the contract moves, and reading only some of them would call a broken
  // handshake a passing tier.
  sync: [
    'ContractManifestTests', 'SpeleoLocClientRegistrationTests', 'SpeleoLocDevSeedTests',
    'SyncAdministratorReadTests', 'SyncAuthTests', 'SyncPageSizeTests', 'SyncProtocolTests',
    'SyncSetTests', 'SyncUploadTests',
  ],
  // A printed code resolved by somebody with no account at all, so the anonymous surface is
  // named here with it: the landing page is the one route where getting the protection wrong
  // is visible to a stranger.
  qr: ['AnonymousSurfaceTests', 'CaveQrPublicationTests', 'PublicQrLandingTests'],
  metadata: ['ContentMetadataTests', 'DocumentMetadataTests'],
  workAreas: ['WorkAreaTests'],
  // The external cave register this installation reads from and imports out of.
  catalogue: ['SpeologieCatalogueTests'],
  // The elevation surface: the chain that builds it, the phases it runs, and the settings and
  // registration that decide whether it runs at all.
  terrain: [
    'GdalScratchDirectoryTests', 'JobQueueLaneTests', 'TerrainActivationTests',
    'TerrainBakePhaseTests', 'TerrainBuildApiTests', 'TerrainBuildHeightTests',
    'TerrainBuildPipelineTests', 'TerrainCellFetchTests', 'TerrainMapConfigTests',
    'TerrainOptionsTests', 'TerrainPipelineRegistrationTests', 'TerrainPreparePhaseTests',
    'TerrainPublishPhaseTests', 'TerrainRasterPreparationTests', 'TerrainSourceTests',
    'TerrainTileUploadTests', 'TerrainValidatePhaseTests',
  ],
};

// Path prefix (repo-relative, forward slashes) -> group name or inline class list.
// Longest matching prefix wins. A path under server/ that matches nothing here and
// nothing in `full` selects the full suite.
export const areas = {
  // Api feature slices
  'server/src/SilexGis.Api/Features/About/': 'about',
  'server/src/SilexGis.Api/Features/AccessHistory/': 'history',
  'server/src/SilexGis.Api/Features/Admin/': 'admin',
  'server/src/SilexGis.Api/Features/Attachments/': 'files',
  'server/src/SilexGis.Api/Features/Audit/': 'audit',
  'server/src/SilexGis.Api/Features/AnnotatedTexts/': 'resLinks',
  'server/src/SilexGis.Api/Features/Cabinets/': 'cabinets',
  'server/src/SilexGis.Api/Features/Calendar/': 'calendar',
  'server/src/SilexGis.Api/Features/Catalogue/': 'catalogue',
  'server/src/SilexGis.Api/Features/Cavers/': 'cavers',
  'server/src/SilexGis.Api/Features/Caves/': 'caves',
  'server/src/SilexGis.Api/Features/CavingGroups/': 'cavingGroups',
  'server/src/SilexGis.Api/Features/Checklists/': 'checklists',
  'server/src/SilexGis.Api/Features/Crs/': 'crs',
  'server/src/SilexGis.Api/Features/Dashboard/': 'dashboard',
  'server/src/SilexGis.Api/Features/Documents/': 'documents',
  'server/src/SilexGis.Api/Features/Events/': 'events',
  'server/src/SilexGis.Api/Features/Expeditions/': 'expeditions',
  'server/src/SilexGis.Api/Features/Export/': 'exports',
  'server/src/SilexGis.Api/Features/FeatureSets/': 'featureSets',
  'server/src/SilexGis.Api/Features/FeatureShares/': 'featureShares',
  'server/src/SilexGis.Api/Features/Features/': 'geoFeatures',
  'server/src/SilexGis.Api/Features/Files/': 'files',
  'server/src/SilexGis.Api/Features/Filters/': 'filters',
  'server/src/SilexGis.Api/Features/Geofiles/': 'geofiles',
  'server/src/SilexGis.Api/Features/GeoreferencedMaps/': 'georeferencedMaps',
  'server/src/SilexGis.Api/Features/History/': 'history',
  'server/src/SilexGis.Api/Features/Import/': 'imports',
  'server/src/SilexGis.Api/Features/Jobs/': 'jobs',
  'server/src/SilexGis.Api/Features/Map/': 'map',
  'server/src/SilexGis.Api/Features/MapLayers/': [],
  'server/src/SilexGis.Api/Features/MapViews/': 'mapViews',
  'server/src/SilexGis.Api/Features/Me/': 'me',
  'server/src/SilexGis.Api/Features/Notifications/': 'notifications',
  'server/src/SilexGis.Api/Features/Permissions/': 'access',
  'server/src/SilexGis.Api/Features/Photos/': 'photos',
  'server/src/SilexGis.Api/Features/ResLinks/': 'resLinks',
  'server/src/SilexGis.Api/Features/QrLanding/': 'qr',
  'server/src/SilexGis.Api/Features/Sync/': 'sync',
  'server/src/SilexGis.Api/Features/Search/': 'search',
  'server/src/SilexGis.Api/Features/Statistics/': 'statistics',
  'server/src/SilexGis.Api/Features/Tags/': 'tags',
  'server/src/SilexGis.Api/Features/Taxonomies/': 'taxonomies',
  'server/src/SilexGis.Api/Features/TripLogs/': 'trips',
  'server/src/SilexGis.Api/Features/Uploads/': 'uploads',
  'server/src/SilexGis.Api/Features/Users/': 'users',
  'server/src/SilexGis.Api/Features/WorkAreas/': 'workAreas',
  'server/src/SilexGis.Api/Features/Terrain/': 'terrain',
  // Domain areas
  'server/src/SilexGis.Domain/Auth/': 'users',
  'server/src/SilexGis.Domain/Calendar/': 'calendar',
  'server/src/SilexGis.Domain/Catalogue/': 'catalogue',
  'server/src/SilexGis.Domain/Documents/': 'documents',
  'server/src/SilexGis.Domain/Events/': 'events',
  'server/src/SilexGis.Domain/Expeditions/': 'expeditions',
  'server/src/SilexGis.Domain/Features/': 'geoFeatures',
  'server/src/SilexGis.Domain/Filters/': 'filters',
  'server/src/SilexGis.Domain/Import/': 'imports',
  'server/src/SilexGis.Domain/Map/': 'map',
  'server/src/SilexGis.Domain/Messaging/': 'messaging',
  'server/src/SilexGis.Domain/Notifications/': 'notifications',
  'server/src/SilexGis.Domain/Profiles/': 'cavers',
  'server/src/SilexGis.Domain/ResLinks/': 'resLinks',
  'server/src/SilexGis.Domain/Settings/': 'settings',
  'server/src/SilexGis.Domain/Terrain/': 'terrain',
  'server/src/SilexGis.Domain/Trips/': 'trips',
  // Infrastructure areas
  'server/src/SilexGis.Infrastructure/Catalogue/': 'catalogue',
  'server/src/SilexGis.Infrastructure/Documents/': 'documents',
  'server/src/SilexGis.Infrastructure/Email/': 'notifications',
  'server/src/SilexGis.Infrastructure/Features/': 'geoFeatures',
  'server/src/SilexGis.Infrastructure/Files/': 'files',
  'server/src/SilexGis.Infrastructure/Filters/': 'filters',
  'server/src/SilexGis.Infrastructure/Geodata/': 'geofiles',
  'server/src/SilexGis.Infrastructure/Import/': 'imports',
  'server/src/SilexGis.Infrastructure/Jobs/': 'jobs',
  'server/src/SilexGis.Infrastructure/Messaging/': 'messaging',
  'server/src/SilexGis.Infrastructure/Metadata/': 'metadata',
  'server/src/SilexGis.Infrastructure/Notifications/': 'notifications',
  'server/src/SilexGis.Infrastructure/Settings/': 'settings',
  'server/src/SilexGis.Infrastructure/Sms/': 'sms',
  'server/src/SilexGis.Infrastructure/Surveys/': 'surveys',
  'server/src/SilexGis.Infrastructure/Terrain/': 'terrain',
  'server/src/SilexGis.Infrastructure/Trips/': 'trips',
};

// Paths whose blast radius is the whole API: shared kernel, host wiring, persistence,
// identity, migrations, the test fixture itself, and build plumbing. Any touch here
// selects the full suite, whatever else the diff contains.
export const full = [
  'server/src/SilexGis.Api/Auth/',
  'server/src/SilexGis.Api/Common/',
  'server/src/SilexGis.Api/Program.cs',
  'server/src/SilexGis.Api/appsettings',
  'server/src/SilexGis.Domain/Access/',
  'server/src/SilexGis.Domain/Entities/',
  'server/src/SilexGis.Domain/Geo/',
  'server/src/SilexGis.Domain/Permissions/',
  'server/src/SilexGis.Infrastructure/DependencyInjection.cs',
  'server/src/SilexGis.Infrastructure/Identity/',
  'server/src/SilexGis.Infrastructure/Migrations/',
  'server/src/SilexGis.Infrastructure/Permissions/',
  'server/src/SilexGis.Infrastructure/Persistence/',
  'server/tests/SilexGis.Api.Tests/Fixtures/',
  'server/tests/SilexGis.Api.Tests/Support/',
  'server/Directory.Build.props',
  'server/SilexGis.slnx',
];

// The access walk and location protection are asserted suite-wide, not slice-wide: a change
// to the rules they implement must answer to every class that asserts a denial or an
// obfuscated position, whichever feature that class nominally belongs to. The two lists are
// DERIVED from the test sources by the patterns recorded below, and the test suite
// re-derives them on every run so they cannot rot as classes are added.
export const crossCutting = {
  triggers: [
    'server/src/SilexGis.Domain/Access/',
    'server/src/SilexGis.Domain/Geo/',
    'server/src/SilexGis.Domain/Permissions/',
    'server/src/SilexGis.Api/Features/Permissions/',
    'server/src/SilexGis.Infrastructure/Permissions/',
  ],
  derivation: {
    // A class is permission-cross-cutting when it asserts a 403 anywhere.
    permission: { source: 'Forbidden|Status403', flags: '' },
    // A class is location-cross-cutting when it asserts obfuscation or protected positions.
    location: { source: 'obfuscat|LocationProtect|protectedLocation|locationClass', flags: 'i' },
  },
  permissionClasses: [
    'AccessApiTests', 'AccessHistoryTests', 'AccessModelTests', 'AclAndCavingGroupTests',
    'AdminMessagingTests', 'AlbumAndPublicGalleryTests', 'AnnotatedTextApiTests', 'AuthFlowTests',
    'BulkImportTests', 'CabinetApiTests', 'CaveDomainTests', 'CaveQrPublicationTests',
    'CaverRosterTests', 'CavingGroupAnnouncementTests', 'CenterlineTests',
    'ChecklistAuthoringTests', 'DocumentAccessApiTests', 'DocumentCommentApiTests',
    'DocumentLanguageTests', 'DocumentMetadataTests', 'EventAuthoringTests',
    'EventInvitationTests', 'EventLifecycleTests', 'EventReminderSweepTests',
    'EventSeriesBulkTests', 'ExpeditionMembershipTests', 'ExpeditionReportTests',
    'ExpeditionRosterRoleVocabularyTests', 'ExpeditionRosterTests',
    'ExpeditionSharingCascadeTests', 'ExpeditionTests', 'ExpeditionTimelineTests',
    'FeatureHierarchyTests', 'FeatureLinkTests', 'FeatureShareTests', 'FeatureTests',
    'AccessHistoryTests', 'AclAndCavingGroupTests', 'AssociationDisclosureTests',
    'AttachmentReachListingTests', 'CalendarTests', 'CaveDomainTests',
    'CaveSurveyStatisticsTests', 'CenterlineTests', 'ClosestApproachTests', 'ConcurrencyTests',
    'DashboardTests', 'DocumentAccessApiTests', 'DocumentCommentNotificationTests',
    'DocumentContentSearchTests', 'DocumentSurfaceProtectionSweepTests',
    'DocumentViewerBytesTests', 'ExpeditionLeadsTests', 'ExpeditionMapTests',
    'ExpeditionReportTests', 'FeatureFilterCompilerTests', 'FeatureHierarchyTests',
    'FeatureLinkTests', 'FeatureShareTests', 'FeatureTests', 'FileAccessBatchParityTests',
    'FileAttachmentTests', 'FilterParityTests', 'FilterWorldConformanceTests', 'GeofileTests',
    'GeoreferencedMapTests', 'HistoryTests', 'HypsometryTests', 'MapViewTests',
    'NotificationHealthTests', 'PhotoBytesProtectionTests', 'PhotoImportTests',
    'PolygonMorphometryTests', 'ProtectionDepthTests', 'ResLinkApiTests',
    'ResLinkProtectionFloorTests', 'SeededGroupUpgradeTests', 'SpeleoLocDevSeedTests',
    'SpeologieCatalogueTests', 'StagedImportTests', 'StructureComparisonTests',
    'SurveyGraphTests', 'SurveyModelTests', 'SurveySegmentSubstrateTests', 'SurveySourceTests',
    'SyncPageSizeTests', 'SyncProtocolTests', 'SyncSetTests', 'SyncUploadTests',
    'TermRuleSetTests', 'TerrainActivationTests', 'TerrainBuildApiTests',
    'TerrainBuildPipelineTests', 'TerrainSourceTests', 'TextExtractionPipelineTests',
    'TripAndTagTests', 'TripAttendanceLimitTests', 'TripCalloutStandDownTests',
    'TripCalloutSweepTests', 'TripCaveReachTests', 'TripChecklistTickTests',
    'TripInvitationTests', 'TripMeetingPointTests', 'TripParticipantRoleVocabularyTests',
    'TripPlanNotificationTests', 'TripPromotionTests', 'TripReportDocumentTests',
    'TripReportTemplateTests', 'TripRoleLinkUnitOfWorkTests', 'TripStatisticsTests',
    'TripTypeVocabularyTests', 'UiDefaultsTests', 'UploadDestinationTests', 'WorkAreaTests',
  ],
  locationClasses: [
    'AccessHistoryTests', 'AclAndCavingGroupTests', 'AssociationDisclosureTests',
    'AttachmentReachListingTests', 'CalendarTests', 'CaveDomainTests', 'CavePassageShapeTests',
    'CaveSurveyStatisticsTests', 'CenterlineTests', 'ClosestApproachTests', 'ConcurrencyTests', 'DashboardTests',
    'DocumentAccessApiTests', 'DocumentCommentNotificationTests', 'DocumentContentSearchTests',
    'DocumentSurfaceProtectionSweepTests', 'DocumentViewerBytesTests', 'ExpeditionLeadsTests',
    'ExpeditionMapTests', 'ExpeditionReportTests', 'FeatureFilterCompilerTests',
    'FeatureHierarchyTests', 'FeatureLinkTests', 'FeatureShareTests', 'FeatureTests',
    'FileAccessBatchParityTests', 'FileAttachmentTests', 'FilterParityTests',
    'FilterWorldConformanceTests', 'GeofileTests', 'GeoreferencedMapTests', 'HistoryTests',
    'PhotoBytesProtectionTests', 'PhotoImportTests', 'PolygonMorphometryTests',
    'ProtectionDepthTests', 'ResLinkApiTests', 'ResLinkProtectionFloorTests',
    'SpeleoLocDevSeedTests', 'SpeologieCatalogueTests', 'StagedImportTests', 'SurveyGraphTests',
    'SurveyModelTests', 'SurveySegmentSubstrateTests', 'SurveySourceTests', 'SyncPageSizeTests',
    'SyncProtocolTests', 'SyncUploadTests', 'TripAndTagTests', 'TripCalloutSweepTests',
    'TripCaveReachTests', 'TripMeetingPointTests', 'TripPlanNotificationTests',
    'TripReportDocumentTests', 'TripRoleLinkUnitOfWorkTests', 'TripStatisticsTests',
    'WorkAreaTests',
  ],
};

// Paths outside the integration suite's blast radius: answered by another suite that the
// fast tier already runs (the client suites, the Domain and Architecture test projects, the
// deployment and gate script tests) or nothing executable. They select no integration classes.
//
// A note on bluntness that is deliberate: SilexGis.Api/Program.cs is a full-suite trigger
// even though roughly half of all commits touch it for a one-line endpoint registration.
// Content-aware exemptions were considered and rejected — a diff-hunk parser deciding what
// is "just wiring" is exactly the kind of silent narrowing this map exists to prevent.
export const outside = [
  'client/',
  'deploy/',
  'docs/',
  '.github/',
  'scripts/',
  'server/tests/SilexGis.Domain.Tests/',
  'server/tests/SilexGis.Architecture.Tests/',
];
