// SPDX-License-Identifier: AGPL-3.0-or-later
import type {
  AlbumListParams,
  CabinetDocumentParams,
  CalendarParams,
  CaveListParams,
  EventListParams,
  ExpeditionListParams,
  FeatureChildrenParams,
  FeatureListParams,
  FilterQueryBody,
  GeofileListParams,
  ImportBatchListParams,
  LibraryPhotographQuery,
  LibrarySearchQuery,
  MemberListParams,
  MyTripLogListParams,
  NotificationListParams,
  PhotoQueryParams,
  PublishedLinksParams,
  RasterMapListParams,
  ResLinkPageParams,
  SpeologieSearchParams,
  TerrainBuildPageParams,
  TrackingEventListParams,
  TripLogFacetParams,
  TripLogGroupingParams,
  TripLogListParams,
  UnfiledDocumentParams,
  UploadBatchItemParams,
} from './types.ts';

// Query keys live here so invalidation stays precise.
export const queryKeys = {
  me: ['me'] as const,
  filterVocabulary: ['filters', 'vocabulary'] as const,
  filterQuery: (body: FilterQueryBody) => ['filters', 'query', body] as const,
  filterResolve: (world: string, ids: readonly string[]) =>
    ['filters', 'resolve', world, [...ids].sort()] as const,
  dashboardSummary: ['dashboard', 'summary'] as const,
  mapLayers: ['map-layers'] as const,
  mapBackgrounds: ['map-backgrounds'] as const,
  basemap: ['basemap'] as const,
  basemapBuilds: ['basemap', 'builds'] as const,
  mapConfig: ['map-config'] as const,
  taxonomy: (kind: string) => ['taxonomy', kind] as const,
  caves: (params: CaveListParams) => ['caves', 'list', params] as const,
  cave: (id: string) => ['caves', 'detail', id] as const,
  caveSummary: (id: string) => ['caves', 'summary', id] as const,
  entrances: (caveId: string) => ['entrances', caveId] as const,
  surveyModels: (caveId: string) => ['survey-models', caveId] as const,
  surveyModel: (id: string) => ['survey-model', id] as const,
  // A survey's stations whose names begin with what somebody has typed. Under the survey's own
  // prefix, so anything that makes the survey be asked for again makes its names be asked again.
  surveyModelStationSearch: (id: string, q: string) => ['survey-model', id, 'stations', q] as const,
  surveySources: (caveId: string) => ['survey-sources', caveId] as const,
  surveyCompilations: (caveId: string) => ['survey-compilations', caveId] as const,
  caveExternalIds: (caveId: string) => ['cave-external-ids', caveId] as const,
  caveDepthPlaces: (caveId: string) => ['cave-depth-places', caveId] as const,
  centerlines: (caveId: string) => ['centerlines', caveId] as const,
  search: (q: string) => ['search', q] as const,
  nominatim: (q: string) => ['nominatim', q] as const,
  features: (params: FeatureListParams) => ['features', 'list', params] as const,
  feature: (id: string) => ['features', 'detail', id] as const,
  deletedFeatures: (page: number) => ['features', 'deleted', page] as const,
  featureParents: (id: string) => ['features', id, 'parents'] as const,
  featureChildren: (id: string, params: FeatureChildrenParams) => ['features', id, 'children', params] as const,
  featureLinks: (id: string) => ['features', id, 'links'] as const,
  featureShares: (id: string) => ['features', id, 'shares'] as const,
  caveQrPublication: (id: string) => ['caves', id, 'qr-publication'] as const,
  geofiles: (params: GeofileListParams) => ['geofiles', 'list', params] as const,
  geofile: (id: string) => ['geofiles', 'detail', id] as const,
  geofileColumns: (id: string) => ['geofile-columns', id] as const,
  termRuleSets: ['term-rule-sets', 'list'] as const,
  termRuleSet: (id: string) => ['term-rule-sets', 'detail', id] as const,
  effectiveTermRuleSet: ['term-rule-sets', 'effective'] as const,
  importSession: (geofileId: string) => ['import-session', geofileId] as const,
  // The options are part of the key: a preview is a pure function of the file and the
  // choices, so changing a rule set or the duplicate radius is a different question rather
  // than a stale answer to the same one.
  importPreview: (geofileId: string, body: unknown) => ['import-preview', geofileId, body] as const,
  tripImportSession: (fileId: string) => ['trip-import-session', fileId] as const,
  tripImportColumns: (fileId: string) => ['trip-import-columns', fileId] as const,
  // Same reasoning as the vector preview: reading a sheet is a pure function of the file and
  // the choices, so moving a column mapping or the day/month order is a different question
  // rather than a stale answer to the same one.
  tripImportPreview: (fileId: string, body: unknown) => ['trip-import-preview', fileId, body] as const,
  speleolocRecordings: (fileId: string) => ['speleoloc-recordings', fileId] as const,
  speleolocImportSession: (fileId: string) => ['speleoloc-import-session', fileId] as const,
  // The whole request is the key, as for every other dry run here: the archive is read again on
  // each preview and what comes back is a pure function of the file and the choices, so naming
  // another recording or another survey model is a different question rather than a stale answer
  // to the same one.
  speleolocImportPreview: (fileId: string, body: unknown) =>
    ['speleoloc-import-preview', fileId, body] as const,
  photoLibraryStatus: ['photo-libraries', 'status'] as const,
  // How big the protected-position decision is for a given export request. The whole request
  // is the key: it is a pure function of what would be exported, so changing a filter is a
  // different question rather than a stale answer to the same one.
  karstLinkExportPreview: (body: unknown) => ['karstlink-export-preview', body] as const,
  // The whole question is in the key — library, page, words, album, and the trip whose days it was
  // narrowed to — because every part of it changes what came back. A page held under a key that did
  // not name all of them would answer one question with another one's photographs.
  libraryPhotographs: (source: string, query: LibraryPhotographQuery) =>
    ['photo-libraries', source, 'photographs', query] as const,
  libraryPhotograph: (source: string, photographId: string) =>
    ['photo-libraries', source, 'photograph', photographId] as const,
  // Not keyed by anything but the library. A chooser's contents are a fact about the library and
  // not about the page it is drawn on, so one answer serves every narrowing the reader then tries.
  libraryAlbums: (source: string) => ['photo-libraries', source, 'albums'] as const,
  // The words are part of the question, so they are part of the key. An answer held under a key
  // that did not name them would put one search's pictures under another search's words.
  librarySearch: (source: string, query: LibrarySearchQuery) =>
    ['photo-libraries', source, 'search', query] as const,
  speologieStatus: ['speologie', 'status'] as const,
  // The whole request is the key. A catalogue search is a pure function of the term, the county
  // and the page, so changing any of them is a different question rather than a stale answer to
  // the same one — and the far end is somebody else's small service, so an answer already held
  // is one call it does not have to serve again.
  speologieSearch: (params: SpeologieSearchParams) => ['speologie', 'search', params] as const,
  speologieCave: (id: number) => ['speologie', 'cave', id] as const,
  speologieBasins: ['speologie', 'basins'] as const,
  photoImportSession: ['photo-import-session'] as const,
  // Same reasoning as the vector preview: the grouping is a pure function of the pictures and
  // the choices, so changing the clustering radius is a different question rather than a stale
  // answer to the same one.
  photoImportPreview: (body: unknown) => ['photo-import-preview', body] as const,
  photoImportTracks: ['photo-import-tracks'] as const,
  importBatches: (params: ImportBatchListParams) => ['import-batches', 'list', params] as const,
  importBatch: (id: string) => ['import-batches', 'detail', id] as const,
  importProvenance: (featureId: string) => ['import-provenance', featureId] as const,
  reviewQueue: (page: number, pageSize: number) => ['review', 'queue', page, pageSize] as const,
  reviewMine: (page: number, pageSize: number) => ['review', 'mine', page, pageSize] as const,
  reviewBatch: (id: string) => ['review', 'batch', id] as const,
  attachments: (entityType: string, entityId: string) => ['attachments', entityType, entityId] as const,
  file: (id: string) => ['files', 'detail', id] as const,
  fileVersions: (fileId: string) => ['file-versions', fileId] as const,
  fileConfig: ['file-config'] as const,
  document: (id: string) => ['documents', 'detail', id] as const,
  // Deliberately its own root rather than nested under 'documents': saving a title
  // invalidates that whole prefix, and a remark thread has no reason to be refetched
  // because somebody corrected a spelling in the metadata panel above it.
  documentComments: (id: string) => ['document-comments', id] as const,
  cabinets: ['cabinets'] as const,
  unfiledDocuments: (params: UnfiledDocumentParams) => ['documents', 'unfiled', params] as const,
  uploadBatches: (page: number, pageSize: number) => ['upload-batches', 'list', page, pageSize] as const,
  uploadBatch: (id: string) => ['upload-batches', 'detail', id] as const,
  uploadBatchItems: (id: string, params: UploadBatchItemParams) =>
    ['upload-batches', 'items', id, params] as const,
  importRoots: ['upload-batches', 'import-roots'] as const,
  photos: (params: PhotoQueryParams) => ['photos', 'list', params] as const,
  photo: (id: string) => ['photos', 'detail', id] as const,
  photoDuplicates: ['photos', 'duplicates'] as const,
  deletedPhotos: (page: number) => ['photos', 'deleted', page] as const,
  albums: (params: AlbumListParams) => ['albums', 'list', params] as const,
  album: (id: string) => ['albums', 'detail', id] as const,
  publicPhotos: (page: number) => ['public-photos', page] as const,
  sharedAlbum: (token: string) => ['public-albums', token] as const,
  cabinetDocuments: (id: string, params: CabinetDocumentParams) =>
    ['cabinets', id, 'documents', params] as const,
  rasterMaps: (params: RasterMapListParams) => ['raster-maps', 'list', params] as const,
  calendar: (params: CalendarParams) => ['calendar', params] as const,
  tripLogMap: (bbox: string, from: string, to: string) =>
    ['map', 'trip-logs', bbox, from, to] as const,
  campAreasMap: (bbox: string, from: string, to: string) =>
    ['map', 'expeditions', bbox, from, to] as const,
  tripLogs: (params: TripLogListParams) => ['trip-logs', 'list', params] as const,
  tripLogFacets: (params: TripLogFacetParams) => ['trip-logs', 'facets', params] as const,
  tripLogGrouping: (params: TripLogGroupingParams) => ['trip-logs', 'grouping', params] as const,
  tripLogStats: (params: TripLogFacetParams) => ['trip-logs', 'stats', params] as const,
  myTripLogs: (params: MyTripLogListParams) => ['trip-logs', 'mine', params] as const,
  tripLog: (id: string) => ['trip-logs', 'detail', id] as const,
  deletedTripLogs: (page: number) => ['trip-logs', 'deleted', page] as const,
  // Outside the trip prefix on purpose: it describes the installation, so no write to a trip
  // makes it stale, and every one of them would otherwise ask for it again.
  tripLogConfig: ['trip-log-config'] as const,
  tripPlanDefault: ['trip-logs', 'plan-default'] as const,
  tripInvitations: (id: string) => ['trip-logs', 'invitations', id] as const,
  tripChecklist: (id: string) => ['trip-logs', 'checklist', id] as const,
  tripTracking: (id: string) => ['trip-logs', 'tracking', id] as const,
  // Which trips were tracked on a survey model, held under the trips' prefix: it is an answer
  // about trips, so a trip or tracking write that changes one of them reaches it.
  surveyModelTrackedTrips: (surveyModelId: string) =>
    ['trip-logs', 'tracked-on-model', surveyModelId] as const,
  // The narrowing is part of the key, as everywhere else a paged read is held: a page of one
  // caver's reports is a different question from a page of everybody's, not a stale answer to it.
  tripTrackingEvents: (id: string, params: TrackingEventListParams) =>
    ['trip-logs', 'tracking-events', id, params] as const,
  // The whole log, under the same prefix so a recorded or deleted report reaches it too. Its own
  // last segment is a word rather than a narrowing, which no narrowing can collide with.
  tripTrackingEventLog: (id: string) => ['trip-logs', 'tracking-events', id, 'log'] as const,
  // The reports taken off the log, under the same prefix again: taking one off, putting one back
  // and destroying one each change this list and the log together, and one invalidation reaches
  // both. The page is a second word-led segment, so no narrowing of the log can collide with it.
  tripTrackingRemovedEvents: (id: string, page: number) =>
    ['trip-logs', 'tracking-events', id, 'removed', page] as const,
  // How many reports a trip holds about one person, kept ones included. Under the log's prefix
  // for the same reason as the kept list: every write to the log changes it.
  tripTrackingReportsHeldOf: (id: string, caverId: string) =>
    ['trip-logs', 'tracking-events', id, 'held-of', caverId] as const,
  // What the log says about each person's entry and exit, beside the roster. Under the log's
  // prefix because it is read off the log: a report recorded, corrected or taken off changes it.
  tripTrackingRosterTimes: (id: string, timeZone: string) =>
    ['trip-logs', 'tracking-events', id, 'roster-times', timeZone] as const,
  tripTrackingShares: (id: string) => ['trip-logs', 'tracking-shares', id] as const,
  // Every published link of the installation, for its administrators. Under the trips' prefix on
  // purpose: a status is read off a trip's watch and its links, so anything that writes a trip or
  // its tracking has to reach this list as well, and one prefix is how it does.
  publishedLinks: (params: PublishedLinksParams) =>
    ['trip-logs', 'published-links', params] as const,
  // The places the watch's cave has declared. Held under the trip rather than under the cave
  // because that is the identity the surface asking has: the page showing this list knows which
  // watch it is drawn on and deliberately does not know which cave the watch is anchored to.
  tripTrackingPlaces: (id: string) => ['trip-logs', 'tracking-places', id] as const,
  // What one depth means in one trip's cave. The depth is part of the key because it is the whole
  // of the question — two depths are two questions, not one answer going stale.
  tripTrackingDepth: (id: string, depthM: number) =>
    ['trip-logs', 'tracking-depth', id, depthM] as const,
  // A published trip is held under its token and not under the trip, because the page that reads
  // it has no trip id and must never be given one: the token is the whole of a follower's claim,
  // and a key naming the trip would be this browser holding an identifier the page was not sent.
  publicTrip: (token: string) => ['public-trips', token] as const,
  // The same reasoning one line up, applied to the two archive reads: both are held under the
  // token, because the token is the only identity the page holding them has. The trip id in the
  // second key is one the list itself handed over — a visitor who never opened the picker holds
  // none — so it names a row already disclosed rather than an identifier this browser was given.
  publicPastTrips: (token: string) => ['public-trips', token, 'past'] as const,
  publicPastTrack: (token: string, tripLogId: string) =>
    ['public-trips', token, 'past', tripLogId] as const,
  // The parties being followed in the same cave right now, under the token for the same reason.
  publicLiveTrips: (token: string) => ['public-trips', token, 'live'] as const,
  checklists: ['checklists'] as const,
  checklist: (id: string) => ['checklists', 'detail', id] as const,
  reportTemplates: ['report-templates'] as const,
  taggings: (entityType: string, entityId: string) => ['taggings', entityType, entityId] as const,
  tags: (search: string) => ['tags', search] as const,
  tagsBySlug: (slugs: readonly string[]) => ['tags', { slugs }] as const,
  cavingGroups: ['cavingGroups'] as const,
  syncCapabilities: ['sync', 'capabilities'] as const,
  syncSets: ['sync', 'sets'] as const,
  cavers: ['cavers'] as const,
  cavingGroupMembers: (cavingGroupId: string) => ['teams', cavingGroupId, 'members'] as const,
  cavingGroupAudience: (cavingGroupId: string) => ['teams', cavingGroupId, 'audience'] as const,
  tripStatistics: (subject: string, id: string) => ['stats', subject, id] as const,
  featureMorphometry: (id: string) => ['features', id, 'morphometry'] as const,
  caveHypsometry: (id: string) => ['caves', id, 'hypsometry'] as const,
  caveOverburden: (id: string) => ['caves', id, 'overburden'] as const,
  caveLevelBands: (id: string) => ['caves', id, 'level-bands'] as const,
  areaHypsometry: (id: string) => ['features', id, 'entrance-hypsometry'] as const,
  caveStructureComparison: (id: string, areaId: string) =>
    ['caves', id, 'structure-comparison', areaId] as const,
  areaStructureComparison: (id: string) => ['features', id, 'structure-comparison'] as const,
  areaKarstStatistics: (id: string) => ['features', id, 'karst-statistics'] as const,
  registryDistribution: (params: Record<string, unknown>) =>
    ['stats', 'registry', 'distribution', params] as const,
  registryCorrelation: (params: Record<string, unknown>) =>
    ['stats', 'registry', 'correlation', params] as const,
  registryCategories: (params: Record<string, unknown>) =>
    ['stats', 'registry', 'categories', params] as const,
  registryRegions: (params: Record<string, unknown>) =>
    ['stats', 'registry', 'regions', params] as const,
  registryClustering: (params: Record<string, unknown>) =>
    ['stats', 'registry', 'clustering', params] as const,
  mapDensity: (bbox: string, cellMetres: number | null, bandwidthMetres: number | null, areaId?: string) =>
    ['map', 'density', bbox, cellMetres, bandwidthMetres, areaId ?? null] as const,
  mapDensityStepped: (bbox: string, areaId?: string) =>
    ['map', 'density', 'stepped', bbox, areaId ?? null] as const,
  mapPointPattern: (bbox: string, simulations: number, seed: number, areaId?: string) =>
    ['map', 'point-pattern', bbox, simulations, seed, areaId ?? null] as const,
  closestApproach: (id: string, other: string) => ['caves', id, 'closest-approach', other] as const,
  objectAccess: (entityType: string, entityId: string) => ['object-access', entityType, entityId] as const,
  history: (entityType: string, entityId: string) => ['history', entityType, entityId] as const,
  mfa: ['mfa'] as const,
  avatarPresets: ['avatar-presets'] as const,
  members: (params: MemberListParams) => ['members', 'list', params] as const,
  member: (id: string) => ['members', 'detail', id] as const,
  notificationPrefs: ['me', 'notifications'] as const,
  // The inbox keeps a root of its own rather than joining the preferences under 'me': every
  // profile write invalidates that whole prefix, and a list of what happened has no reason to be
  // fetched again because somebody uploaded a new picture of themselves. Both entries share the
  // one root so marking something read can refresh the list and the badge with a single prefix.
  notificationInbox: (params: NotificationListParams) =>
    ['notification-inbox', 'list', params] as const,
  unreadNotificationCount: ['notification-inbox', 'unread-count'] as const,
  // Outside the inbox root on purpose: marking something read invalidates that whole prefix, and
  // how this installation is configured is not something a reader can change by reading.
  notificationTransport: ['notification-transport'] as const,
  uiPreferences: ['me', 'preferences'] as const,
  uiDefaults: ['ui-defaults'] as const,
  dataExport: ['me', 'data-export'] as const,
  calendarFeeds: ['me', 'calendar-feeds'] as const,
  phone: ['me', 'phone'] as const,
  adminSettings: ['admin', 'settings'] as const,
  messageTemplates: ['admin', 'message-templates'] as const,
  capabilities: ['me', 'capabilities'] as const,
  myPermissionGroups: ['me', 'permission-groups'] as const,
  effectiveAccess: (entityType: string, entityId: string, explain: boolean) =>
    ['effective-access', entityType, entityId, explain] as const,
  accessCatalog: ['access-catalog'] as const,
  permissionGroups: ['permission-groups'] as const,
  permissionGroupEntries: (id: string) => ['permission-groups', id, 'entries'] as const,
  permissionGroupMembers: (id: string) => ['permission-groups', id, 'members'] as const,
  featureSets: ['feature-sets'] as const,
  featureSetMembers: (id: string) => ['feature-sets', id, 'members'] as const,
  resLinksForTarget: (targetType: string, targetId: string, params: ResLinkPageParams) =>
    ['reslinks', 'for-target', targetType, targetId, params] as const,
  resLink: (idOrCode: string) => ['reslinks', 'detail', idOrCode] as const,
  resLinkTargets: (targetType: string, q: string) => ['reslinks', 'targets', targetType, q] as const,
  resLinkRelationTypes: ['reslinks', 'relation-types'] as const,
  resLinkPointDefault: ['reslinks', 'point-default'] as const,
  caveSurveyStatistics: (caveId: string) => ['caves', caveId, 'survey-statistics'] as const,
  caveOrientation: (caveId: string) => ['caves', caveId, 'orientation'] as const,
  caveCrossSection: (caveId: string) => ['caves', caveId, 'cross-section'] as const,
  cavePattern: (caveId: string) => ['caves', caveId, 'pattern'] as const,
  caveTopology: (caveId: string) => ['caves', caveId, 'topology'] as const,
  annotatedText: (documentId: string) => ['annotated-texts', documentId] as const,
  // One key for the whole tree: the board, the overview and the map that zooms to one area all
  // read the same answer, so they cannot disagree about which areas exist or where one of them is.
  workAreas: ['work-areas'] as const,
  processingJob: (id: number) => ['jobs', id] as const,
  // Every terrain key starts with this list key, so the mutations that invalidate it also reach
  // the paged list and each build's own detail. A key that did not would leave the page showing
  // a build's old phase for as long as its query stayed fresh.
  terrainBuilds: ['terrain', 'builds'] as const,
  terrainBuildList: (params: TerrainBuildPageParams) => ['terrain', 'builds', 'page', params] as const,
  terrainBuild: (id: string) => ['terrain', 'builds', 'detail', id] as const,
  terrainSourceDirectories: ['terrain', 'source-directories'] as const,
  terrainDerivatives: ['terrain', 'derivatives'] as const,
  expeditions: (params: ExpeditionListParams) => ['expeditions', 'list', params] as const,
  expedition: (id: string) => ['expeditions', 'detail', id] as const,
  expeditionRoster: (id: string) => ['expeditions', 'roster', id] as const,
  expeditionMap: (id: string) => ['expeditions', 'map', id] as const,
  expeditionLeads: (id: string) => ['expeditions', 'leads', id] as const,
  expeditionSurfaceLog: (id: string) => ['expeditions', 'surface-log', id] as const,
  expeditionSharing: (id: string) => ['expeditions', 'sharing', id] as const,
  events: (params: EventListParams) => ['events', 'list', params] as const,
  event: (id: string) => ['events', 'detail', id] as const,
  eventDefaults: ['events', 'defaults'] as const,
  eventInvitations: (id: string) => ['events', 'invitations', id] as const,
  accessKeys: ['access-keys'] as const,
  // Every survey-upload key starts with this one, so a rerun or a finished run that invalidates it
  // reaches the listings, the per-cave groupings and each upload's own status together.
  surveyUploads: ['survey-uploads'] as const,
  surveyUploadCapabilities: ['survey-uploads', 'capabilities'] as const,
  surveyUploadPreferences: ['survey-upload-preferences'] as const,
  surveyUpload: (id: string) => ['survey-uploads', 'detail', id] as const,
  caveSurveys: (caveId: string) => ['survey-uploads', 'cave', caveId] as const,
  publicSurveyUpload: (token: string) => ['public-survey-uploads', token] as const,
};
