// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef } from 'react';
import { keepPreviousData, useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query';
import type { QueryClient } from '@tanstack/react-query';
import { clusterCellBbox } from '../geo/cluster.ts';
import {
  defaultInboxTransport,
  inboxPollIntervalMs,
  isInboxTransport,
} from '../notifications/transport.ts';
import { api, ApiError, lastReadETag } from './client.ts';
import type { components, paths } from './schema';

export type CaveListItem = components['schemas']['CaveListItemDto'];
export type CaveDetail = components['schemas']['CaveDto'];
export type CaveWrite = components['schemas']['CaveWriteRequest'];
export type Entrance = components['schemas']['EntranceDto'];
export type EntranceWrite = components['schemas']['EntranceWriteRequest'];
export type MapLayerInfo = components['schemas']['MapLayerDto'];
export type Taxonomy = components['schemas']['TaxonomyDto'];
export type FeatureType = components['schemas']['FeatureTypeDto'];
export type DocumentType = components['schemas']['DocumentTypeDto'];
export type DocumentInfo = components['schemas']['DocumentDto'];
export type DocumentComment = components['schemas']['DocumentCommentDto'];
export type TextExtractionState = components['schemas']['TextExtractionState'];
export type CabinetInfo = components['schemas']['CabinetDto'];
export type CabinetWrite = components['schemas']['CabinetWriteRequest'];
export type CabinetDocument = components['schemas']['CabinetDocumentDto'];
export type UploadBatchInfo = components['schemas']['UploadBatchDto'];
export type UploadBatchItemInfo = components['schemas']['UploadBatchItemDto'];
export type UnfiledDocument = components['schemas']['UnfiledDocumentDto'];
export type PhotoInfo = components['schemas']['PhotoDto'];
export type PhotoCredit = components['schemas']['PhotoCreditDto'];
export type AlbumInfo = components['schemas']['AlbumDto'];
export type PublicPhoto = components['schemas']['PublicPhotoDto'];
export type DeletedPhoto = components['schemas']['DeletedPhotoDto'];
export type Visibility = components['schemas']['Visibility'];
export type FileConfig = components['schemas']['FileConfigDto'];
export type EntranceFeatureCollection = components['schemas']['FeatureCollection'];
export type Me = components['schemas']['MeDto'];
export type MeUpdate = components['schemas']['MeUpdateRequest'];
export type MeLocale = components['schemas']['MeLocaleDto'];
export type MeLocaleWrite = components['schemas']['MeLocaleWriteRequest'];
export type ProfileVisibility = components['schemas']['ProfileVisibilityDto'];
export type FieldVisibility = ProfileVisibility['email'];
export type UserAddress = components['schemas']['UserAddressDto'];
export type UserAddressWrite = components['schemas']['UserAddressWriteRequest'];
export type MemberSummary = components['schemas']['MemberDto'];
export type NotificationPreferences = components['schemas']['NotificationPreferencesDto'];
export type NotificationCategory = components['schemas']['NotificationCategoryDto'];
export type NotificationChannelCell = components['schemas']['NotificationChannelDto'];
export type NotificationChoice = components['schemas']['NotificationChannelChoice'];

/**
 * One channel of the preference matrix, as the wire spells it. The server's channel type is a
 * set of bit flags, so it crosses the boundary as a string rather than as a closed union, and a
 * cell always names exactly one of these. Written out here because the wording lookup, the
 * column order and the exhaustiveness check all need a closed list, and the generated client
 * cannot give them one.
 */
export type NotificationChannelName = 'inApp' | 'email' | 'sms';

/** Every channel, in the order a settings page reads best: the one that always works first. */
export const NOTIFICATION_CHANNELS: readonly NotificationChannelName[] = ['inApp', 'email', 'sms'];

/**
 * The name of one notification category, as the server publishes it. Named separately from the
 * row that carries it because the settings page, the opt-out landing page and the inbox all look
 * their wording up by this value alone. Non-null by construction: the generated union admits null only
 * because one response omits the category — a daily summary collects every category and names
 * none — and null is not a category anybody can be notified about.
 */
export type NotificationCategoryName = NonNullable<components['schemas']['NotificationCategory']>;

/**
 * One line of the reader's own inbox, as the server renders it.
 *
 * The wording is rendered on the server from the language the request was made in, so `title` is
 * text to show rather than a key to look up. `targetWithheld` says the reader may no longer see
 * the thing this row is about: the row is still listed — that it happened is not the secret — but
 * it carries neither the name nor the link, and has to be shown as deliberate rather than broken.
 */
export type NotificationItem = components['schemas']['NotificationDto'];

export type NotificationHealth = components['schemas']['NotificationHealthDto'];
export type NotificationDeliveryRow = components['schemas']['NotificationDeliveryDto'];
export type NotificationDeliveryStatus = components['schemas']['NotificationDeliveryStatus'];
export type NotificationDeliveryChannel = components['schemas']['NotificationChannel'];
export type NotificationRetryResult = components['schemas']['NotificationRetryDto'];
export type NotificationRetryOutcome = components['schemas']['NotificationRetryOutcome'];

/** How many lines of the reader's own inbox are still unopened. */
export type UnreadNotificationCount = components['schemas']['UnreadNotificationCountDto'];

/** What an opt-out link switched off, as the server reports it back to the landing page. */
export type UnsubscribeResult = components['schemas']['UnsubscribeResultDto'];
export type DataExport = components['schemas']['DataExportDto'];
export type MfaStatus = components['schemas']['MfaStatusDto'];
export type MfaMethod = components['schemas']['MfaMethodDto'];
export type TwoFactorMethod = components['schemas']['TwoFactorMethod'];
export type PhoneStatus = components['schemas']['PhoneStatusDto'];
export type AdminSettings = components['schemas']['AdminSettingsDto'];
export type MailSettingsWrite = components['schemas']['MailSettingsWriteRequest'];
export type SmsSettingsWrite = components['schemas']['SmsSettingsWriteRequest'];
export type SecuritySettings = components['schemas']['SecuritySettingsDto'];
export type ProtectionSettings = components['schemas']['ProtectionSettingsDto'];
export type ImportSettings = components['schemas']['ImportSettingsDto'];
export type NotificationSettings = components['schemas']['NotificationSettingsDto'];
export type AnnouncementSettings = components['schemas']['AnnouncementSettingsDto'];
export type MessageTemplate = components['schemas']['MessageTemplateDto'];
export type ResLink = components['schemas']['ResLinkDto'];
export type ResLinkMember = components['schemas']['ResLinkMemberDto'];
export type ResLinkTargetDisplay = components['schemas']['ResLinkTargetDisplayDto'];
export type ResLinkTargetHit = components['schemas']['ResLinkTargetHitDto'];
export type ResLinkRelationType = components['schemas']['ResLinkRelationTypeDto'];
export type ResLinkRelationTypeWrite = components['schemas']['ResLinkRelationTypeWriteRequest'];
export type ResLinkCreate = components['schemas']['ResLinkCreateRequest'];
export type ResLinkUpdate = components['schemas']['ResLinkUpdateRequest'];
export type ResLinkMemberAdd = components['schemas']['ResLinkMemberAddRequest'];
export type ResLinkMemberUpdate = components['schemas']['ResLinkMemberUpdateRequest'];
export type ResLinkPointDefault = components['schemas']['ResLinkPointDefaultDto'];
export type AnchorKind = components['schemas']['AnchorKind'];
export type ResLinkAnchorState = components['schemas']['ResLinkAnchorState'];
export type AnnotatedText = components['schemas']['AnnotatedTextDto'];
export type AnnotatedTextWrite = components['schemas']['AnnotatedTextWriteDto'];
export type AnnotatedTextCreate = components['schemas']['AnnotatedTextCreateRequest'];
export type ReanchorReport = components['schemas']['ReanchorReportDto'];

// Query keys live here so invalidation stays precise.
export const queryKeys = {
  me: ['me'] as const,
  filterVocabulary: ['filters', 'vocabulary'] as const,
  filterQuery: (body: FilterQueryBody) => ['filters', 'query', body] as const,
  filterResolve: (world: string, ids: readonly string[]) =>
    ['filters', 'resolve', world, [...ids].sort()] as const,
  dashboardSummary: ['dashboard', 'summary'] as const,
  mapLayers: ['map-layers'] as const,
  mapConfig: ['map-config'] as const,
  taxonomy: (kind: string) => ['taxonomy', kind] as const,
  caves: (params: CaveListParams) => ['caves', 'list', params] as const,
  cave: (id: string) => ['caves', 'detail', id] as const,
  caveSummary: (id: string) => ['caves', 'summary', id] as const,
  entrances: (caveId: string) => ['entrances', caveId] as const,
  surveyModels: (caveId: string) => ['survey-models', caveId] as const,
  surveyModel: (id: string) => ['survey-model', id] as const,
  surveySources: (caveId: string) => ['survey-sources', caveId] as const,
  centerlines: (caveId: string) => ['centerlines', caveId] as const,
  search: (q: string, kind?: string) => ['search', q, kind ?? 'all'] as const,
  nominatim: (q: string) => ['nominatim', q] as const,
  features: (params: FeatureListParams) => ['features', 'list', params] as const,
  feature: (id: string) => ['features', 'detail', id] as const,
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
  photoLibraryStatus: ['photo-libraries', 'status'] as const,
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
  tripLogs: (params: TripLogListParams) => ['trip-logs', 'list', params] as const,
  tripLogFacets: (params: TripLogFacetParams) => ['trip-logs', 'facets', params] as const,
  tripLogGrouping: (params: TripLogGroupingParams) => ['trip-logs', 'grouping', params] as const,
  tripLogStats: (params: TripLogFacetParams) => ['trip-logs', 'stats', params] as const,
  myTripLogs: (params: MyTripLogListParams) => ['trip-logs', 'mine', params] as const,
  tripLog: (id: string) => ['trip-logs', 'detail', id] as const,
  tripInvitations: (id: string) => ['trip-logs', 'invitations', id] as const,
  tripChecklist: (id: string) => ['trip-logs', 'checklist', id] as const,
  checklists: ['checklists'] as const,
  checklist: (id: string) => ['checklists', 'detail', id] as const,
  tripReportTemplates: ['trip-report-templates'] as const,
  taggings: (entityType: string, entityId: string) => ['taggings', entityType, entityId] as const,
  tags: (search: string) => ['tags', search] as const,
  cavingGroups: ['cavingGroups'] as const,
  syncCapabilities: ['sync', 'capabilities'] as const,
  syncSets: ['sync', 'sets'] as const,
  cavers: ['cavers'] as const,
  cavingGroupMembers: (cavingGroupId: string) => ['teams', cavingGroupId, 'members'] as const,
  cavingGroupAudience: (cavingGroupId: string) => ['teams', cavingGroupId, 'audience'] as const,
  tripStatistics: (subject: string, id: string) => ['stats', subject, id] as const,
  featureMorphometry: (id: string) => ['features', id, 'morphometry'] as const,
  caveHypsometry: (id: string) => ['caves', id, 'hypsometry'] as const,
  caveLevelBands: (id: string) => ['caves', id, 'level-bands'] as const,
  areaHypsometry: (id: string) => ['features', id, 'entrance-hypsometry'] as const,
  caveStructureComparison: (id: string, areaId: string) =>
    ['caves', id, 'structure-comparison', areaId] as const,
  areaStructureComparison: (id: string) => ['features', id, 'structure-comparison'] as const,
  areaKarstStatistics: (id: string) => ['features', id, 'karst-statistics'] as const,
  mapDensity: (bbox: string, cellMetres: number | null, bandwidthMetres: number | null, areaId?: string) =>
    ['map', 'density', bbox, cellMetres, bandwidthMetres, areaId ?? null] as const,
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
  expeditions: (params: ExpeditionListParams) => ['expeditions', 'list', params] as const,
  expedition: (id: string) => ['expeditions', 'detail', id] as const,
  expeditionRoster: (id: string) => ['expeditions', 'roster', id] as const,
  expeditionMap: (id: string) => ['expeditions', 'map', id] as const,
  expeditionLeads: (id: string) => ['expeditions', 'leads', id] as const,
  events: (params: EventListParams) => ['events', 'list', params] as const,
  event: (id: string) => ['events', 'detail', id] as const,
  eventDefaults: ['events', 'defaults'] as const,
  eventInvitations: (id: string) => ['events', 'invitations', id] as const,
};

async function unwrap<T>(
  call: Promise<{ data?: T; error?: unknown; response: Response }>,
): Promise<T> {
  const { data, error, response } = await call;
  if (error !== undefined || data === undefined) {
    const problem = error as { code?: string; detail?: string } | undefined;
    // The whole problem object travels, not only the two members every screen reads: a refusal
    // that carries a machine-readable fact of its own is otherwise recoverable only by matching
    // it out of the English detail sentence.
    throw new ApiError(
      response.status,
      problem?.code,
      problem?.detail,
      problem as Record<string, unknown> | undefined,
    );
  }
  return data;
}

/** unwrap for endpoints with no response body (DELETE / 204). */
async function unwrapVoid(
  call: Promise<{ error?: unknown; response: Response }>,
): Promise<void> {
  const { error, response } = await call;
  if (error !== undefined) {
    const problem = error as { code?: string; detail?: string } | undefined;
    throw new ApiError(response.status, problem?.code, problem?.detail);
  }
}

export function useMe() {
  return useQuery({
    queryKey: queryKeys.me,
    queryFn: () => unwrap(api.GET('/api/v1/me')),
    // The avatar URL carries a short-lived delivery token, so a long-cached response would
    // start 403ing in the header. Same cadence the attachment gallery refreshes on.
    staleTime: 5 * 60_000,
    refetchInterval: 8 * 60_000,
  });
}

/** Everything the settings pages write invalidates the one profile response they all read. */
function useInvalidateMe() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['me'] });
}

export function useUpdateProfile() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (body: MeUpdate) => unwrap(api.PUT('/api/v1/me', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateLocale() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (body: MeLocaleWrite) => unwrap(api.PUT('/api/v1/me/locale', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useAvatarPresets() {
  return useQuery({
    queryKey: queryKeys.avatarPresets,
    queryFn: () => unwrap(api.GET('/api/v1/avatar-presets')),
    staleTime: Infinity, // ships with the build
  });
}

export function useUploadAvatar() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (file: File) => {
      const form = new FormData();
      form.append('file', file, file.name);
      return unwrap(api.POST('/api/v1/me/avatar', {
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
    onSuccess: () => invalidate(),
  });
}

export function useSetAvatarPreset() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (preset: string) => unwrap(api.PUT('/api/v1/me/avatar', { body: { preset } })),
    onSuccess: () => invalidate(),
  });
}

export function useRemoveAvatar() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: () => unwrapVoid(api.DELETE('/api/v1/me/avatar')),
    onSuccess: () => invalidate(),
  });
}

export function useCreateAddress() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (body: UserAddressWrite) => unwrap(api.POST('/api/v1/me/addresses', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateAddress() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UserAddressWrite }) =>
      unwrap(api.PUT('/api/v1/me/addresses/{id}', { params: { path: { id } }, body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteAddress() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/me/addresses/{id}', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

export function useRequestEmailChange() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (newEmail: string) => unwrapVoid(api.POST('/api/v1/me/email/change', { body: { newEmail } })),
    onSuccess: () => invalidate(),
  });
}

export function useConfirmEmailChange() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (token: string) => unwrap(api.POST('/api/v1/me/email/confirm', { body: { token } })),
    onSuccess: () => invalidate(),
  });
}

export function useResendEmailChange() {
  return useMutation({ mutationFn: () => unwrapVoid(api.POST('/api/v1/me/email/resend')) });
}

export function useCancelEmailChange() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: () => unwrapVoid(api.DELETE('/api/v1/me/email/pending')),
    onSuccess: () => invalidate(),
  });
}

export function useVerifyEmail() {
  return useMutation({ mutationFn: () => unwrapVoid(api.POST('/api/v1/me/email/verify')) });
}

export function useChangeUsername() {
  const invalidate = useInvalidateMe();
  return useMutation({
    mutationFn: (username: string) => unwrap(api.PUT('/api/v1/me/username', { body: { username } })),
    onSuccess: () => invalidate(),
  });
}

export function useChangePassword() {
  return useMutation({
    mutationFn: (body: { currentPassword: string; newPassword: string }) =>
      unwrapVoid(api.PUT('/api/v1/me/password', { body })),
  });
}

export function useNotificationPreferences() {
  return useQuery({
    queryKey: queryKeys.notificationPrefs,
    queryFn: () => unwrap(api.GET('/api/v1/me/notifications')),
  });
}

export function useUpdateNotificationPreferences() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: {
      categories: {
        category: NotificationCategory['category'];
        channels: { channel: NotificationChannelName; choice: NotificationChoice }[];
      }[];
    }) => unwrap(api.PUT('/api/v1/me/notifications', { body })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['me'] }),
  });
}

export interface NotificationListParams {
  category?: NotificationCategoryName;
  unreadOnly?: boolean;
  page?: number;
  pageSize?: number;
}

/** The reader's own inbox, newest first. Nobody else's is reachable through it. */
export function useNotifications(params: NotificationListParams) {
  return useQuery({
    queryKey: queryKeys.notificationInbox(params),
    queryFn: () => unwrap(api.GET('/api/v1/notifications', { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

/**
 * Everything a read changes: the line's own unread mark, the page it sits on, and the count the
 * header shows. One prefix covers all three because they share a query root.
 */
function useInvalidateNotificationInbox() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['notification-inbox'] });
}

/**
 * Marks one line read. Idempotent on the server, which does not move the stamp a second time, so
 * a row that is already read can be marked again without rewriting when it was first seen.
 */
export function useMarkNotificationRead() {
  const invalidate = useInvalidateNotificationInbox();
  return useMutation({
    mutationFn: (id: number) =>
      unwrapVoid(api.POST('/api/v1/notifications/{id}/read', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

/** Marks everything the reader has not read yet, in one act. */
export function useMarkAllNotificationsRead() {
  const invalidate = useInvalidateNotificationInbox();
  return useMutation({
    mutationFn: () => unwrapVoid(api.POST('/api/v1/notifications/read-all')),
    onSuccess: () => invalidate(),
  });
}

/**
 * How many lines the reader has not opened yet, for the count the header shows.
 *
 * Its own request rather than a number read off the list, because the header is on every page and
 * the list is on one: asking for the count costs one small answer, while asking for the first page
 * of the inbox everywhere would fetch and re-render rows nobody is looking at.
 *
 * Kept current three ways, and the timer is the weakest of them. Both ways of marking something
 * read invalidate the inbox root this key sits under, so the number moves as the reader acts
 * rather than up to a minute later; returning to the tab refetches, which is what covers a browser
 * that throttles timers in a background tab; and the interval itself only has to cover a
 * notification arriving while somebody is watching a page that is not the inbox. `staleTime` is
 * left at zero for the second of those: a query still considered fresh is not refetched on focus.
 *
 * The timer is the one of the three the installation chooses: it runs while the configured
 * transport is polling, and stops when the server says it will push instead. The other two hold
 * whatever the transport is.
 */
export function useUnreadNotificationCount() {
  const transport = useInboxTransport();
  return useQuery({
    queryKey: queryKeys.unreadNotificationCount,
    queryFn: () => unwrap(api.GET('/api/v1/notifications/unread-count')),
    refetchInterval: transport === 'poll' ? inboxPollIntervalMs : false,
    refetchOnWindowFocus: true,
  });
}

/**
 * Which transport this installation has been configured for.
 *
 * The choice belongs to whoever runs the server, not to this client, so it is asked for rather
 * than compiled in. Until the answer arrives — and if it never does, because the request failed —
 * the header polls: that is the transport which needs nothing else in place, so a count is never
 * left with nothing to move it.
 *
 * It changes when an operator restarts the server with a different setting, so it is asked for
 * once and then left alone rather than re-fetched on every mount of the header.
 */
export function useInboxTransport() {
  const { data } = useQuery({
    queryKey: queryKeys.notificationTransport,
    queryFn: () => unwrap(api.GET('/api/v1/notifications/config')),
    staleTime: Infinity,
  });
  const named = data?.badgeTransport;
  return isInboxTransport(named) ? named : defaultInboxTransport;
}

/**
 * The installation's starting arrangement. A default only: the client fills gaps with it and never
 * lets it overwrite something the person chose.
 */
export function useUiDefaults() {
  return useQuery({
    queryKey: queryKeys.uiDefaults,
    queryFn: () => unwrap(api.GET('/api/v1/ui-defaults')),
    // It changes when an administrator publishes a new one, which is rare; asking again on every
    // mount would be a request per page load for an answer that is the same all day.
    staleTime: 10 * 60_000,
  });
}

export function useUiPreferences() {
  return useQuery({
    queryKey: queryKeys.uiPreferences,
    queryFn: () => unwrap(api.GET('/api/v1/me/preferences')),
  });
}

export function useUpdateUiPreferences() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (preferences: Record<string, unknown>) =>
      unwrap(api.PUT('/api/v1/me/preferences', { body: { preferences } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['me'] }),
  });
}

/**
 * The caller's newest account-data export. Polls only while one is being built, the same
 * settle pattern the geodata pages use; a missing export is a normal empty state, not an error.
 */
export function useDataExport() {
  return useQuery({
    queryKey: queryKeys.dataExport,
    queryFn: async (): Promise<DataExport | null> => {
      const { data, error, response } = await api.GET('/api/v1/me/data-export');
      if (response.status === 404) {
        return null;
      }
      if (error !== undefined || data === undefined) {
        const problem = error as { code?: string; detail?: string } | undefined;
        throw new ApiError(response.status, problem?.code, problem?.detail);
      }
      return data;
    },
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return status === 'queued' || status === 'running' ? 2000 : false;
    },
  });
}

export function useRequestDataExport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => unwrap(api.POST('/api/v1/me/data-export')),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['me', 'data-export'] }),
  });
}

export interface MemberListParams {
  search?: string;
  page?: number;
  pageSize?: number;
}

export function useMembers(params: MemberListParams) {
  return useQuery({
    queryKey: queryKeys.members(params),
    queryFn: () => unwrap(api.GET('/api/v1/members', { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

export function useMember(id: string) {
  return useQuery({
    queryKey: queryKeys.member(id),
    queryFn: () => unwrap(api.GET('/api/v1/members/{id}', { params: { path: { id } } })),
    enabled: id.length > 0,
  });
}

// ---- capabilities: what the caller may do, per resource domain ----

export type Capabilities = components['schemas']['CapabilitiesDto'];
export type AccessDomainName = components['schemas']['AccessDomain'];
export type AccessActionSet = components['schemas']['AccessAction'];
/** The individual action flags the wire's comma-joined action string is made of. */
export type AccessActionFlag =
  | 'read' | 'write' | 'delete' | 'share' | 'managePermissions'
  | 'viewExactLocation' | 'create' | 'execute';

/** Parses a comma-joined action set ("read, write") into its individual flags. */
export function parseAccessActions(actions: string | null | undefined): Set<AccessActionFlag> {
  return new Set(
    (actions ?? '')
      .split(',')
      .map((x) => x.trim())
      .filter((x): x is AccessActionFlag => x.length > 0 && x !== 'none'),
  );
}

/** True when a comma-joined action set carries the flag. */
export function hasAccessAction(
  actions: string | null | undefined,
  flag: AccessActionFlag,
): boolean {
  return parseAccessActions(actions).has(flag);
}

/**
 * The caller's domain-level rights, for hiding controls the server would refuse anyway.
 * Purely a hint: every actual decision is re-made server-side on the row in question,
 * and per-object answers come from `useEffectiveAccess` instead.
 */
export function useCapabilities() {
  return useQuery({
    queryKey: queryKeys.capabilities,
    queryFn: () => unwrap(api.GET('/api/v1/me/capabilities')),
    staleTime: 5 * 60_000,
  });
}

/** Convenience gate over `useCapabilities`: false while loading, so controls appear, never flash away. */
export function useCan(domain: AccessDomainName, action: AccessActionFlag): boolean {
  const { data } = useCapabilities();
  return hasAccessAction(data?.domains[domain], action);
}

export type MyPermissionGroup = components['schemas']['MyPermissionGroupDto'];

/** The permission groups the caller reaches — where a right of theirs comes from. */
export function useMyPermissionGroups() {
  return useQuery({
    queryKey: queryKeys.myPermissionGroups,
    queryFn: () => unwrap(api.GET('/api/v1/me/permission-groups')),
    staleTime: 5 * 60_000,
  });
}

export type EffectiveAccess = components['schemas']['EffectiveAccessDto'];
export type AccessExplanation = components['schemas']['AccessExplanationDto'];

/**
 * What the caller may do to one object — the per-object refinement of `useCapabilities`.
 * With `explain` the server also names the rule that decided each action, redacting
 * anchors the caller may not read.
 */
export function useEffectiveAccess(
  entityType: EntityType,
  entityId: string | undefined,
  options: { explain?: boolean; enabled?: boolean } = {},
) {
  const explain = options.explain ?? false;
  return useQuery({
    queryKey: queryKeys.effectiveAccess(entityType, entityId ?? '', explain),
    queryFn: () =>
      unwrap(api.GET('/api/v1/objects/{entityType}/{id}/effective-access', {
        params: { path: { entityType, id: entityId! }, query: { explain } },
      })),
    enabled: (options.enabled ?? true) && !!entityId,
    // A 403/404 is a settled answer here, not worth retrying.
    retry: false,
  });
}

export function useMapLayers() {
  return useQuery({
    queryKey: queryKeys.mapLayers,
    queryFn: () => unwrap(api.GET('/api/v1/map-layers')),
    staleTime: 5 * 60_000,
  });
}

function taxonomyQuery(path: '/api/v1/cave-types' | '/api/v1/entrance-types' | '/api/v1/rock-types', kind: string) {
  return {
    queryKey: queryKeys.taxonomy(kind),
    queryFn: () => unwrap(api.GET(path)),
    staleTime: 5 * 60_000,
  };
}

export function useCaveTypes() {
  return useQuery(taxonomyQuery('/api/v1/cave-types', 'cave-types'));
}

export function useEntranceTypes() {
  return useQuery(taxonomyQuery('/api/v1/entrance-types', 'entrance-types'));
}

export function useRockTypes() {
  return useQuery(taxonomyQuery('/api/v1/rock-types', 'rock-types'));
}

export function useFeatureTypes() {
  return useQuery({
    queryKey: queryKeys.taxonomy('feature-types'),
    queryFn: () => unwrap(api.GET('/api/v1/feature-types')),
    staleTime: 5 * 60_000,
  });
}

export type LinkKind = components['schemas']['LinkKindDto'];

/** Feature-link taxonomy (drains-to, connects-with, …). */
export function useLinkKinds() {
  return useQuery({
    queryKey: queryKeys.taxonomy('link-kinds'),
    queryFn: () => unwrap(api.GET('/api/v1/link-kinds')),
    staleTime: 5 * 60_000,
  });
}

export interface CaveListParams {
  page?: number;
  pageSize?: number;
  sort?: string;
  caveTypeId?: number;
  region?: string;
  search?: string;
  minLength?: number;
  bbox?: string;
  tag?: string;
}

export function useCaves(params: CaveListParams) {
  return useQuery({
    queryKey: queryKeys.caves(params),
    queryFn: () => unwrap(api.GET('/api/v1/caves', { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

export function useCave(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.cave(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/caves/{id}', { params: { path: { id: id! } } })),
    enabled: !!id,
  });
}

/**
 * Names for caves known only by id — the ones a stored selection points at that a paged, searched
 * list did not happen to return.
 *
 * Worth its own hook because the alternative is worse than untidy: falling back to "a cave you can
 * no longer read" for anything simply absent from the current page tells a caver their access was
 * revoked when nothing of the kind happened. Asking for each id by name distinguishes the two —
 * what comes back is named, and what genuinely cannot be read stays unnamed. A failure is not
 * retried, because the expected failure here is a definite "you cannot read this".
 */
export function useCaveNames(ids: readonly string[]) {
  return useQueries({
    queries: ids.map((id) => ({
      queryKey: queryKeys.cave(id),
      queryFn: () => unwrap(api.GET('/api/v1/caves/{id}', { params: { path: { id } } })),
      staleTime: 300_000,
      retry: false,
    })),
    combine: (results) =>
      new Map(
        results.flatMap((result) =>
          result.data ? ([[result.data.id, result.data.name]] as [string, string][]) : [],
        ),
      ),
  });
}

export type CaveSummary = components['schemas']['CaveSummaryDto'];

/** Cave header block: related-record counts, main entrance and the caller's capabilities. */
export function useCaveSummary(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.caveSummary(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/caves/{id}/summary', { params: { path: { id: id! } } })),
    enabled: !!id,
  });
}

export function useEntrances(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.entrances(caveId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/caves/{caveId}/entrances', { params: { path: { caveId: caveId! } } })),
    enabled: !!caveId,
  });
}

export type SurveyModelInfo = components['schemas']['SurveyModelDto'];
export type AuditEntry = components['schemas']['AuditEntryDto'];

/** The signed model URLs live 10 minutes; refresh before they lapse mid-view. */
const SURVEY_MODEL_URL_REFRESH_MS = 8 * 60_000;
/** How often a model whose processing has not finished yet is asked about. */
const SURVEY_MODEL_CONVERSION_POLL_MS = 2000;

/**
 * A model with work still outstanding on it. Both kinds of upload have some: a wall mesh is
 * converted into what the 3D scene draws, and a line plot is read into its stations and shots.
 * The line plot stays viewable throughout — the viewer reads the file as uploaded — so what is
 * outstanding there is the record behind it, not the picture.
 *
 * A failure counts as settled. The worker may retry it and move the row on again, but
 * the reader has been told the outcome and is owed nothing further until they act on it; asking
 * every two seconds forever on the chance somebody re-queues the job is a page that never goes
 * quiet.
 */
export function surveyModelUnsettled(status: SurveyModelInfo['status']): boolean {
  return status === 'pending' || status === 'processing';
}

/**
 * Whether the embedded survey viewer can read this model itself.
 *
 * The viewer parses the line-plot formats natively, choosing its parser by the extension of the
 * file name it is handed. A wall mesh is not one of them: it is drawn by the 3D scene, out of the
 * file the server converts it into, and handing the raw upload to the viewer produces a parse
 * failure rather than a picture.
 *
 * One home, because more than one page decides this — the cave page's model list, and the
 * popped-out survey panel, which is a separate window nobody testing the cave page would ever see.
 * Two copies diverge the first time a format is added on one side only: either a viewer is offered
 * a file it cannot parse, or a readable file is quietly skipped.
 */
export function surveyModelReadableByViewer(model: { format: SurveyModelInfo['format'] }): boolean {
  return model.format === 'lox' || model.format === 'survex3d';
}

/**
 * How often the list re-asks. Two intervals meet in this one number, which is why it is a named
 * rule and not a literal at the query: work in flight is worth a couple of seconds, and
 * once everything has settled the list must still come back before the signed URLs on it lapse.
 *
 * Exported so the rule — a list holding nothing but finished models stops watching them — can be
 * asserted directly, rather than inferred from a live query somebody would have to watch for two
 * seconds to prove it did nothing.
 */
export function surveyModelPollInterval(
  models: { status: SurveyModelInfo['status'] }[] | undefined,
): number {
  return (models ?? []).some((model) => surveyModelUnsettled(model.status))
    ? SURVEY_MODEL_CONVERSION_POLL_MS
    : SURVEY_MODEL_URL_REFRESH_MS;
}

/**
 * The survey figures a cave's page works out from its line work, dropped whenever that line work
 * changes.
 *
 * These two queries are computed per request from a cave's segments, so every upload, deletion or
 * finished background extraction that changes the segments changes the answers — but they are
 * keyed under the cave rather than under the centerlines or the survey models, so none of the
 * invalidations that refresh those lists reaches them. Without this a reader who drops a file into
 * the centerline card watches that card fill in while the two panels directly beneath it go on
 * reporting the cave as it was before the upload, with nothing on screen to say the figures are
 * stale.
 */
function invalidateCaveSurveyFigures(queryClient: QueryClient, caveId: string) {
  void queryClient.invalidateQueries({ queryKey: queryKeys.caveSurveyStatistics(caveId) });
  void queryClient.invalidateQueries({ queryKey: queryKeys.caveOrientation(caveId) });
  void queryClient.invalidateQueries({ queryKey: queryKeys.caveCrossSection(caveId) });
  void queryClient.invalidateQueries({ queryKey: queryKeys.cavePattern(caveId) });
}

/**
 * One survey model, addressed by its own id rather than found in a cave's list.
 *
 * For the places that were sent to a particular model — a pane opened from the cave page, a
 * pop-out window opened on one — and which have an id and no cave. Listing the cave's models and
 * picking through them is not an alternative: the caller does not know which cave it belongs to,
 * and the guess it would otherwise make is "the first readable one", which is how the pop-out
 * viewer behaves and is exactly the behaviour a chosen model is meant to replace.
 *
 * Refetched on the same interval as the list, because the answer carries a signed URL with a ten
 * minute life and a window left open on a model outlives it.
 *
 * A model whose cave's location is withheld from this reader answers **404**, not an empty result:
 * a cave's models are its location, so their existence is withheld along with them. Callers must
 * treat the failure as "there is nothing here for you" and not as an error worth reporting.
 */
export function useSurveyModel(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.surveyModel(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/survey-models/{id}', { params: { path: { id: id! } } })),
    enabled: !!id,
    staleTime: 5 * 60_000,
    refetchInterval: SURVEY_MODEL_URL_REFRESH_MS,
    retry: false,
  });
}

export function useSurveyModels(caveId: string | undefined) {
  const queryClient = useQueryClient();
  const query = useQuery({
    queryKey: queryKeys.surveyModels(caveId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/caves/{caveId}/survey-models', { params: { path: { caveId: caveId! } } })),
    enabled: !!caveId,
    staleTime: 5 * 60_000,
    refetchInterval: (query) => surveyModelPollInterval(query.state.data),
  });

  // Reading a line plot records the survey as the cave's own centerline, and it is a background
  // job that does it — nothing the browser did. So the only sign a page watching this list gets
  // is the moment the outstanding work stops being outstanding, and that moment is here: this is
  // the one query that keeps asking. Without this the centerline table beside it stays as it was
  // found, empty, until somebody reloads the page and wonders why the reload was needed.
  //
  // Keyed on the transition rather than the state, so a page that arrives after everything has
  // settled — the ordinary visit to a cave whose survey was read weeks ago — asks for nothing.
  const outstanding = (query.data ?? []).some((model) => surveyModelUnsettled(model.status));
  const wasOutstanding = useRef(outstanding);
  useEffect(() => {
    if (wasOutstanding.current && !outstanding && caveId) {
      void queryClient.invalidateQueries({ queryKey: queryKeys.centerlines(caveId) });
      invalidateCaveSurveyFigures(queryClient, caveId);
    }
    wasOutstanding.current = outstanding;
  }, [outstanding, caveId, queryClient]);

  return query;
}

/**
 * Imperative fetch of a cave's survey models, used by the 3D scene rather than by a component.
 *
 * The scene is not a React tree — it decides which cave's wall mesh to hold from where the
 * viewer's selection is, outside the render cycle — so it asks directly instead of mounting a
 * hook. A cave whose exact location is withheld from this caller answers with an empty list
 * rather than a refusal, which is the same answer as a cave with no models and needs no special
 * casing here.
 */
export async function fetchSurveyModels(caveId: string): Promise<SurveyModelInfo[]> {
  return unwrap(
    api.GET('/api/v1/caves/{caveId}/survey-models', { params: { path: { caveId } } }),
  );
}

function useInvalidateSurveyModels() {
  const queryClient = useQueryClient();
  return (caveId: string) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.surveyModels(caveId) });
    invalidateCaveSurveyFigures(queryClient, caveId);
  };
}

/** The multipart body the upload endpoint declares, as the generated contract states it. */
type SurveyModelUploadBody =
  paths['/api/v1/caves/{caveId}/survey-models']['post']['requestBody']['content']['multipart/form-data'];

/**
 * What an uploader may say about where a survey file's numbers sit in the world: which coordinate
 * system they are in, and what altitude the plane the file calls zero sits at.
 *
 * Either a projected system by code, or — for a file exported about a local origin, which is the
 * ordinary case — the position that origin sits at. Never both: with a code, the file's own
 * coordinates say where it is, and a second answer could only contradict the first.
 *
 * Every half is optional here, and which of them a given file must actually answer is the uploading
 * screen's to enforce, because only it knows the format: a wall mesh carries no coordinate system
 * and one of the two line-plot formats has no field for one either, so both have to be told, while
 * the other line-plot format may state its own and then needs none of this. What is left unanswered
 * is left off the request rather than sent empty.
 *
 * Taken from the generated contract rather than restated here, so that a field renamed or re-typed
 * on the server fails this build instead of failing every upload at run time. Only the file itself
 * is set aside, because it is appended separately.
 */
export type SurveySourceDeclaration = Omit<SurveyModelUploadBody, 'file'>;

export function useUploadSurveyModel() {
  const invalidate = useInvalidateSurveyModels();
  return useMutation({
    mutationFn: async ({
      caveId,
      file,
      declaration,
    }: {
      caveId: string;
      file: File;
      /** What the uploader said about where the file sits; absent when the file says it itself. */
      declaration?: SurveySourceDeclaration;
    }): Promise<SurveyModelInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      // The field names are the declaration's own keys, so the names on the wire and the names in
      // the contract are one thing rather than two lists to keep in step. Plain `String(number)`
      // writes a dot decimal whatever the reader's locale is set to, which is what the server
      // parses these as; a half that was not answered is left out rather than sent empty.
      for (const [field, value] of Object.entries(declaration ?? {})) {
        if (value !== undefined) {
          form.append(field, String(value));
        }
      }
      return unwrap(api.POST('/api/v1/caves/{caveId}/survey-models', {
        params: { path: { caveId } },
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
    onSuccess: (_, { caveId }) => invalidate(caveId),
  });
}

export function useDeleteSurveyModel() {
  const invalidate = useInvalidateSurveyModels();
  return useMutation({
    mutationFn: async ({ id }: { id: string; caveId: string }) => {
      const { error, response } = await api.DELETE('/api/v1/survey-models/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: (_, { caveId }) => invalidate(caveId),
  });
}

export type SurveySourceInfo = components['schemas']['SurveySourceDto'];
export type SurveySourceKind = SurveySourceInfo['kind'];

/**
 * The raw material a cave's compiled surveys were produced from — the survey languages, a project
 * configuration, a survey app's export bundle, and the log a compilation wrote.
 *
 * Kept apart from the models list because these are not models: nothing draws them, nothing reads
 * them, and what they are for is the day the compiled export can no longer be re-made from itself.
 */
export function useSurveySources(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.surveySources(caveId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/caves/{caveId}/survey-sources', { params: { path: { caveId: caveId! } } })),
    enabled: !!caveId,
    staleTime: 5 * 60_000,
  });
}

function useInvalidateSurveySources() {
  const queryClient = useQueryClient();
  return (caveId: string) =>
    void queryClient.invalidateQueries({ queryKey: queryKeys.surveySources(caveId) });
}

export function useUploadSurveySource() {
  const invalidate = useInvalidateSurveySources();
  return useMutation({
    mutationFn: async ({
      caveId,
      file,
      description,
    }: {
      caveId: string;
      file: File;
      description?: string;
    }): Promise<SurveySourceInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      if (description !== undefined && description.trim() !== '') {
        form.append('description', description.trim());
      }
      return unwrap(api.POST('/api/v1/caves/{caveId}/survey-sources', {
        params: { path: { caveId } },
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
    onSuccess: (_, { caveId }) => invalidate(caveId),
  });
}

export function useDeleteSurveySource() {
  const invalidate = useInvalidateSurveySources();
  return useMutation({
    mutationFn: async ({ id }: { id: string; caveId: string }) => {
      const { error, response } = await api.DELETE('/api/v1/survey-sources/{id}', {
        params: { path: { id } },
      });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: (_, { caveId }) => invalidate(caveId),
  });
}

export type CenterlineInfo = components['schemas']['CenterlineDto'];

export function useCenterlines(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.centerlines(caveId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/caves/{caveId}/centerlines', { params: { path: { caveId: caveId! } } })),
    enabled: !!caveId,
  });
}

function useInvalidateCenterlines() {
  const queryClient = useQueryClient();
  return (caveId: string) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.centerlines(caveId) });
    invalidateCaveSurveyFigures(queryClient, caveId);
  };
}

export function useUploadCenterline() {
  const invalidate = useInvalidateCenterlines();
  return useMutation({
    mutationFn: async ({ caveId, file }: { caveId: string; file: File }): Promise<CenterlineInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      return unwrap(api.POST('/api/v1/caves/{caveId}/centerlines', {
        params: { path: { caveId } },
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
    onSuccess: (_, { caveId }) => invalidate(caveId),
  });
}

export type CenterlineUpdate = components['schemas']['CenterlineUpdateRequest'];

/** Centerline metadata update, including promoting one to the cave's default shape. */
export function useUpdateCenterline() {
  const invalidate = useInvalidateCenterlines();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; caveId: string; body: CenterlineUpdate }) =>
      unwrap(api.PUT('/api/v1/centerlines/{id}', { params: { path: { id } }, body })),
    onSuccess: (_, { caveId }) => invalidate(caveId),
  });
}

export function useDeleteCenterline() {
  const invalidate = useInvalidateCenterlines();
  return useMutation({
    mutationFn: async ({ id }: { id: string; caveId: string }) => {
      const { error, response } = await api.DELETE('/api/v1/centerlines/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: (_, { caveId }) => invalidate(caveId),
  });
}

export type CenterlineFeatureCollection =
  components['schemas']['CenterlineFeatureCollection'];

/** Installation-wide map rendering limits, published by the server. */
export type MapConfig = components['schemas']['MapConfigDto'];

export function useMapConfig() {
  return useQuery({
    queryKey: queryKeys.mapConfig,
    queryFn: () => unwrap(api.GET('/api/v1/map/config')),
    staleTime: 5 * 60_000,
  });
}

/**
 * Imperative fetch used by the map centerline loaders (not a hook). The zoom decides
 * whether the server sends the splay-free skeleton or clipped full detail; `detailZoom` and
 * `maxPaths` carry the viewer's own overrides, which the server bounds.
 *
 * `z` opts into surveyed altitudes. It costs a larger payload and is answered per row — the
 * stored display skeleton is a flat shape by construction, so a row served from it comes back
 * without them and the response counts how many did. The flat map leaves it off.
 */
export async function fetchCenterlineFeatures(
  bbox: string,
  zoom: number,
  detailZoom?: number,
  maxPaths?: number,
  z?: boolean,
): Promise<CenterlineFeatureCollection> {
  return unwrap(api.GET('/api/v1/map/cave-centerlines', {
    params: { query: { bbox, zoom, detailZoom, maxPaths, z } },
  }));
}

/** Imperative fetch used by the OpenLayers photo overlay loader (not a hook). */
export async function fetchPhotoFeatures(bbox: string): Promise<EntranceFeatureCollection> {
  return unwrap(api.GET('/api/v1/map/photos', { params: { query: { bbox } } }));
}

export type TripLogFeatureCollection = components['schemas']['TripLogFeatureCollection'];

/** The narrowings the trip overlay may carry, spelled as the trip listing spells them. */
export interface TripLogMapFilter {
  from?: string;
  to?: string;
  types?: string[];
  states?: string[];
  visibilities?: string[];
  hadIncident?: boolean;
}

/**
 * Imperative fetch used by the OpenLayers trip overlay loader (not a hook).
 *
 * Every facet is sent as one comma-separated word list, which is how the trip listing spells the
 * same narrowings in its own address — so a filter carried from the list to the map arrives
 * unchanged rather than being translated into a second dialect on the way. An empty facet is
 * omitted entirely: an empty string would be a filter naming nothing, which the server is right
 * to refuse.
 */
export async function fetchTripLogFeatures(
  bbox: string,
  filter: TripLogMapFilter = {},
): Promise<TripLogFeatureCollection> {
  const list = (values?: string[]) => (values && values.length > 0 ? values.join(',') : undefined);
  return unwrap(api.GET('/api/v1/map/trip-logs', {
    params: {
      query: {
        bbox,
        from: filter.from,
        to: filter.to,
        types: list(filter.types),
        states: list(filter.states),
        visibilities: list(filter.visibilities),
        hadIncident: filter.hadIncident,
      },
    },
  }));
}

/**
 * Which neighbouring photo library a request is about, named the way the server names it.
 *
 * Taken from the generated contract rather than written here as a union of the products that
 * happen to exist today: a third one added on the server would leave a hand-written union
 * type-checking against a value it has never heard of, and failing only at runtime.
 */
export type LibraryPhotoSource = LibraryPhotoProvider['source'];
export type LibraryPhotoProvider = components['schemas']['PhotoLibraryProviderDto'];
export type LibraryPhotoStatus = components['schemas']['PhotoLibraryStatusDto'];
export type LibraryPhotoCollection = components['schemas']['LibraryPhotoFeatureCollection'];

/**
 * The photo libraries this account may see, or none.
 *
 * A query rather than an imperative fetch, unlike the map loaders below it: there is no viewport
 * in this question, so it is asked once and answered from cache while the map is panned. An
 * account outside the audience is told it may read nothing and given an empty list — the answer
 * a client needs in order to decide whether to offer the overlay at all, without being told which
 * products this installation runs.
 */
export function usePhotoLibraries() {
  return useQuery({
    queryKey: queryKeys.photoLibraryStatus,
    queryFn: () => unwrap(api.GET('/api/v1/photo-libraries/status')),
    staleTime: 5 * 60_000,
  });
}

/**
 * Imperative fetch used by the photo-library overlays (not a hook), one library per call.
 *
 * Imperative for the reason every other map loader here is: a bbox that changes with every pan is
 * an unbounded cache key, and the loader's own sequence guard is cheaper than fighting a query
 * cache for last-write-wins.
 *
 * The library is a path segment rather than a filter, because it selects which foreign
 * installation is called. One request per library, never one for both: they are separate
 * installations with separate uptime, and a joined request would be as slow as the slower of them
 * and as broken as the more broken one.
 */
export async function fetchLibraryPhotoFeatures(
  source: LibraryPhotoSource,
  bbox: string,
): Promise<LibraryPhotoCollection> {
  return unwrap(
    api.GET('/api/v1/photo-libraries/{source}/map', {
      params: { path: { source }, query: { bbox } },
    }),
  );
}

export type SearchResult = components['schemas']['SearchResultDto'];
export type SearchFeatureItem = components['schemas']['SearchFeatureItemDto'];
export type SearchTripItem = components['schemas']['SearchTripItemDto'];
export type SearchDocumentItem = components['schemas']['SearchDocumentItemDto'];

/**
 * Global search: features of every kind, trip logs, and documents matched by what their text
 * says. Results carry no coordinates — navigate to the entity (or fetch it) instead of
 * centering the map from here.
 *
 * Pass `kind` when only one kind can be picked. The server's hit budget is shared across
 * kinds, so filtering the answer here instead would let commoner kinds crowd the wanted
 * one out of the response entirely.
 *
 * The document section is the one that is paged, and this hook asks only for its first page:
 * a search box shows the best few answers, and the total that comes back with them says
 * honestly how many more there are. The two query parameters that would change that answer —
 * which page, and whether replaced revisions are searched — are deliberately not passed here,
 * because they are not in the cache key and a request that varied them would be served the
 * previous one's rows.
 */
export function useSearch(q: string, kind?: FeatureKind) {
  return useQuery({
    queryKey: queryKeys.search(q, kind),
    queryFn: () => unwrap(api.GET('/api/v1/search', { params: { query: { q, kind } } })),
    enabled: q.trim().length >= 2,
    staleTime: 30_000,
  });
}

export interface NominatimPlace {
  place_id: number;
  display_name: string;
  lon: string;
  lat: string;
}

export function useNominatim(q: string) {
  return useQuery({
    queryKey: queryKeys.nominatim(q),
    queryFn: async (): Promise<NominatimPlace[]> => {
      const url = `https://nominatim.openstreetmap.org/search?format=jsonv2&limit=5&q=${encodeURIComponent(q)}`;
      const response = await fetch(url, { headers: { Accept: 'application/json' } });
      return response.ok ? ((await response.json()) as NominatimPlace[]) : [];
    },
    enabled: q.trim().length >= 3,
    staleTime: 60_000,
    retry: false,
  });
}

/** Imperative fetch used by the OpenLayers entrance-layer loader (not a hook). */
export async function fetchEntranceFeatures(bbox: string, zoom: number, tag?: string): Promise<EntranceFeatureCollection> {
  return unwrap(api.GET('/api/v1/map/cave-entrances', { params: { query: { bbox, zoom, tag } } }));
}

/**
 * Members of a low-zoom entrance cluster: its cluster cell (+margin) fetched at
 * a zoom above the server's clustering threshold, so points come back.
 */
export function useClusterEntrances(lon: number, lat: number, zoom: number, tag?: string) {
  const bbox = clusterCellBbox(lon, lat, zoom);
  return useQuery({
    queryKey: ['map', 'cluster-entrances', bbox, tag ?? null] as const,
    queryFn: () => fetchEntranceFeatures(bbox, 14, tag),
    staleTime: 30_000,
  });
}

export type FeatureKind = components['schemas']['FeatureKind'];
export type FeatureCategory = components['schemas']['FeatureCategory'];

/** Optional filters the map feature layer applies on top of the viewport bbox. */
export interface MapFeatureFilters {
  /** Comma-separated FeatureKind names, e.g. "generic,cave". */
  kinds?: string;
  featureTypeId?: number;
  category?: FeatureCategory;
  tag?: string;
}

/**
 * Imperative fetch used by the OpenLayers feature-layer loader (not a hook). Protected
 * non-point geometry arrives with a null geometry — the layer must tolerate it.
 */
export async function fetchMapFeatures(
  bbox: string,
  filters?: MapFeatureFilters,
): Promise<EntranceFeatureCollection> {
  return unwrap(api.GET('/api/v1/map/features', { params: { query: { bbox, ...filters } } }));
}

export type FeatureListItem = components['schemas']['FeatureListItemDto'];
export type FeatureDetail = components['schemas']['FeatureDto'];
export type FeatureEnvelope = components['schemas']['FeatureEnvelopeDto'];
export type FeatureCreate = components['schemas']['FeatureCreateRequest'];
export type FeatureUpdate = components['schemas']['FeatureUpdateRequest'];

// Imperative feature calls used by the map edit controller (outside React).
export async function fetchFeature(id: string): Promise<FeatureEnvelope> {
  return unwrap(api.GET('/api/v1/features/{id}', { params: { path: { id } } }));
}

export async function createFeature(body: FeatureCreate): Promise<FeatureDetail> {
  return unwrap(api.POST('/api/v1/features', { body }));
}

export async function updateFeature(id: string, body: FeatureUpdate): Promise<FeatureDetail> {
  return unwrap(api.PUT('/api/v1/features/{id}', { params: { path: { id } }, body }));
}

export interface FeatureListParams {
  page?: number;
  pageSize?: number;
  kind?: FeatureKind;
  featureTypeId?: number;
  category?: FeatureCategory;
  bbox?: string;
  tag?: string;
  search?: string;
  /** A named set of objects — what a multi-selection asks about. Bounded by the server. */
  ids?: string[];
}

export function useFeatures(params: FeatureListParams, enabled = true) {
  return useQuery({
    queryKey: queryKeys.features(params),
    queryFn: () => unwrap(api.GET('/api/v1/features', { params: { query: params } })),
    placeholderData: keepPreviousData,
    // Pickers (parent/link target selects) pass enabled=false until they open.
    enabled,
  });
}

/** Resolves any feature id — generic, cave, entrance or centerline — to its typed envelope. */
export function useFeature(id: string | undefined) {
  return useQuery({ ...featureQuery(id ?? ''), enabled: !!id });
}

/**
 * One feature's read as a query object rather than as a hook.
 *
 * A view being told to show something is told outside React's render — it is a message arriving
 * on the workspace bus — so it cannot call a hook and must not reach for the transport either.
 * Handing out the query lets it go through the same cache under the same key, so a view already
 * showing that feature pays nothing to be told about it again.
 */
export function featureQuery(id: string) {
  return {
    queryKey: queryKeys.feature(id),
    queryFn: () => unwrap(api.GET('/api/v1/features/{id}', { params: { path: { id } } })),
  };
}

function useInvalidateFeatures() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['features'] });
}

export function useCreateFeature() {
  const invalidate = useInvalidateFeatures();
  return useMutation({
    mutationFn: (body: FeatureCreate) => createFeature(body),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateFeature() {
  const invalidate = useInvalidateFeatures();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: FeatureUpdate }) => updateFeature(id, body),
    onSuccess: () => {
      invalidate();
      invalidateHistory();
    },
  });
}

/** Soft-deletes the feature and its containment subtree. */
export function useDeleteFeature() {
  const invalidate = useInvalidateFeatures();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/features/{id}', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

export type FeatureParent = components['schemas']['FeatureParentDto'];
export type ParentEdgeWrite = components['schemas']['ParentEdgeRequest'];
export type FeatureChild = components['schemas']['FeatureChildDto'];

export function useFeatureParents(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.featureParents(id ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/features/{id}/parents', { params: { path: { id: id! } } })),
    enabled: !!id,
  });
}

/** Replaces the feature's parent edges (the DAG requires exactly one primary edge). */
export function useSetFeatureParents() {
  const invalidate = useInvalidateFeatures();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: ({ id, parents }: { id: string; parents: ParentEdgeWrite[] }) =>
      unwrap(api.PUT('/api/v1/features/{id}/parents', { params: { path: { id } }, body: { parents } })),
    onSuccess: () => {
      invalidate();
      invalidateHistory();
    },
  });
}

export interface FeatureChildrenParams {
  page?: number;
  pageSize?: number;
}

export function useFeatureChildren(id: string | undefined, params: FeatureChildrenParams = {}) {
  return useQuery({
    queryKey: queryKeys.featureChildren(id ?? '', params),
    queryFn: () =>
      unwrap(api.GET('/api/v1/features/{id}/children', {
        params: { path: { id: id! }, query: params },
      })),
    enabled: !!id,
    placeholderData: keepPreviousData,
  });
}

export type FeatureLink = components['schemas']['FeatureLinkDto'];
export type FeatureLinkWrite = components['schemas']['FeatureLinkWriteRequest'];

/** Links in both directions; protected far endpoints arrive redacted. */
export function useFeatureLinks(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.featureLinks(id ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/features/{id}/links', { params: { path: { id: id! } } })),
    enabled: !!id,
  });
}

/** Replaces the feature's outgoing links; incoming ones belong to the other feature. */
export function useSetFeatureLinks() {
  const invalidate = useInvalidateFeatures();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: ({ id, links }: { id: string; links: FeatureLinkWrite[] }) =>
      unwrap(api.PUT('/api/v1/features/{id}/links', { params: { path: { id } }, body: { links } })),
    onSuccess: () => {
      invalidate();
      invalidateHistory();
    },
  });
}

export type FeatureShare = components['schemas']['FeatureShareDto'];
export type FeatureShareCreate = components['schemas']['FeatureShareCreateRequest'];
export type FeatureShareCreated = components['schemas']['FeatureShareCreatedDto'];
export type SharedFeatureEnvelope = components['schemas']['SharedFeatureEnvelopeDto'];

/** Share-link metadata only — tokens are shown once at mint time and never again. */
export function useFeatureShares(id: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.featureShares(id ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/features/{id}/shares', { params: { path: { id: id! } } })),
    enabled: !!id && enabled,
    // Requires Share permission; a 403 is a settled answer, not worth retrying.
    retry: false,
  });
}

function useInvalidateFeatureShares() {
  const queryClient = useQueryClient();
  return (id: string) =>
    void queryClient.invalidateQueries({ queryKey: queryKeys.featureShares(id) });
}

/** Mints a share link; the response carries the one-time token. */
export function useCreateFeatureShare() {
  const invalidate = useInvalidateFeatureShares();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: FeatureShareCreate }) =>
      unwrap(api.POST('/api/v1/features/{id}/shares', { params: { path: { id } }, body })),
    onSuccess: (_, { id }) => invalidate(id),
  });
}

export function useRevokeFeatureShare() {
  const invalidate = useInvalidateFeatureShares();
  return useMutation({
    mutationFn: ({ id, shareId }: { id: string; shareId: string }) =>
      unwrapVoid(api.DELETE('/api/v1/features/{id}/shares/{shareId}', {
        params: { path: { id, shareId } },
      })),
    onSuccess: (_, { id }) => invalidate(id),
  });
}

/** Imperative fetch for the anonymous shared-feature page (no auth required for public shares). */
export async function fetchSharedFeature(token: string): Promise<SharedFeatureEnvelope> {
  return unwrap(api.GET('/api/v1/shared/features/{token}', { params: { path: { token } } }));
}

export type CaveQrPublication = components['schemas']['CaveQrPublicationDto'];
export type PublicQr = components['schemas']['PublicQrDto'];

/**
 * Whether a cave's printed codes resolve for a visitor with no account, and the record of the
 * decision that last governed it. Not published is an answer rather than an absence, so this
 * always resolves to a document for a caller allowed to ask at all.
 */
export function useCaveQrPublication(id: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.caveQrPublication(id ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/caves/{id}/qr-publication', { params: { path: { id: id! } } })),
    enabled: !!id && enabled,
    // Requires the right to share the cave; a 403 is a settled answer, not worth retrying.
    retry: false,
  });
}

function useInvalidateCaveQrPublication() {
  const queryClient = useQueryClient();
  return (id: string) =>
    void queryClient.invalidateQueries({ queryKey: queryKeys.caveQrPublication(id) });
}

/** Lets the cave's printed codes resolve for anyone. Publishing an already-published cave is the state asked for. */
export function usePublishCaveQr() {
  const invalidate = useInvalidateCaveQrPublication();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/v1/caves/{id}/qr-publication', { params: { path: { id } } })),
    onSuccess: (_, id) => invalidate(id),
  });
}

/** Stops them resolving. Withdrawing a cave nobody published is the state asked for, not an error. */
export function useRevokeCaveQr() {
  const invalidate = useInvalidateCaveQrPublication();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/caves/{id}/qr-publication', { params: { path: { id } } })),
    onSuccess: (_, id) => invalidate(id),
  });
}

/**
 * Imperative fetch for the anonymous printed-code landing page. No account is involved: a person
 * holding a phone at a cave entrance has none, which is the entire reason the route exists.
 */
export async function fetchPublicQr(code: string): Promise<PublicQr> {
  return unwrap(api.GET('/api/v1/public/qr/{code}', { params: { path: { code } } }));
}

export type GeofileInfo = components['schemas']['GeofileDto'];
export type GeofileUpdate = components['schemas']['GeofileUpdateRequest'];

export interface GeofileListParams {
  page?: number;
  pageSize?: number;
  search?: string;
}

export function useGeofiles(params: GeofileListParams, pollWhileImporting = false) {
  return useQuery({
    queryKey: queryKeys.geofiles(params),
    queryFn: () => unwrap(api.GET('/api/v1/geofiles', { params: { query: params } })),
    placeholderData: keepPreviousData,
    // Imports run in a background job — keep the table live until they settle.
    refetchInterval: pollWhileImporting
      ? (query) =>
          query.state.data?.items.some(
            (g) => g.importStatus === 'uploaded' || g.importStatus === 'importing',
          )
            ? 2000
            : false
      : false,
  });
}

function useInvalidateGeofiles() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['geofiles'] });
}

export function useUploadGeofile() {
  const invalidate = useInvalidateGeofiles();
  return useMutation({
    mutationFn: async (file: File): Promise<GeofileInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      // Multipart: hand the FormData through untouched (the browser sets the boundary).
      return unwrap(api.POST('/api/v1/geofiles', {
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
    onSuccess: () => invalidate(),
  });
}

export function useUpdateGeofile() {
  const invalidate = useInvalidateGeofiles();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: GeofileUpdate }) =>
      unwrap(api.PUT('/api/v1/geofiles/{id}', { params: { path: { id } }, body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteGeofile() {
  const invalidate = useInvalidateGeofiles();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/geofiles/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

/** Imperative fetch used by the OpenLayers geofile-layer loader (not a hook). */
export async function fetchGeofileFeatureCollection(id: string, bbox: string): Promise<EntranceFeatureCollection> {
  return unwrap(api.GET('/api/v1/map/geofiles/{id}/features', { params: { path: { id }, query: { bbox } } }));
}

export type FileInfo = components['schemas']['FileDto'];
export type AttachmentInfo = components['schemas']['AttachmentDto'];

/**
 * entityType vocabulary shared by attachments, taggings and ACLs. Any feature — generic,
 * cave, entrance or centerline — is addressed as 'feature' with its feature id. The wire
 * type is a plain string; this union is the documented set of accepted values.
 */
export type EntityType =
  | 'feature'
  | 'tripLog'
  | 'geofile'
  | 'georeferencedMap'
  | 'mapView'
  | 'expedition'
  | 'event';
// Stored files additionally carry taggings (never attachments or grants) — the tag
// endpoints accept the extra target; the server rejects it everywhere else.
export type AttachedEntityType = EntityType | 'storedFile';
export type AttachmentRole = AttachmentInfo['role'];

/**
 * @param enabled false keeps the request from being made at all — what a collapsed panel section
 * passes, so selecting an object on the map costs one small request instead of six.
 */
export function useAttachments(
  entityType: AttachedEntityType,
  entityId: string | undefined,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.attachments(entityType, entityId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/attachments', { params: { query: { entityType, entityId: entityId! } } })),
    enabled: enabled && !!entityId,
    // Delivery URLs embed 10-minute tokens; refresh the list before they lapse.
    staleTime: 5 * 60_000,
    refetchInterval: 8 * 60_000,
  });
}

function useInvalidateAttachments() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['attachments'] });
    // Attachments are audited children of their target entity, so any attach/detach/metadata
    // change surfaces in that entity's timeline — refresh it here so every caller stays in sync.
    void queryClient.invalidateQueries({ queryKey: ['history'] });
  };
}

export type HistoryEvent = components['schemas']['HistoryEventDto'];

/** Change history for an entity (incl. its children's events via audit roots). */
/** @param enabled false keeps the request from being made — see {@link useAttachments}. */
export function useHistory(entityType: string, entityId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.history(entityType, entityId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/history', {
        params: { query: { entityType, entityId: entityId!, pageSize: 100 } },
      })),
    enabled: enabled && !!entityId,
  });
}

export function useInvalidateHistory() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['history'] });
}

/**
 * One stored file, with delivery URLs minted for this caller.
 *
 * The document endpoint says what a document *is*; only this one says how to fetch its
 * bytes, and how far this caller may reach for them — the original, or renderings only.
 * Those URLs carry a ten-minute token, so the answer is refreshed well before it lapses,
 * exactly as the attachment lists do; a long read must never end in a dead link.
 */
export function useFile(id: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.file(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/files/{id}', { params: { path: { id: id! } } })),
    enabled: !!id && enabled,
    staleTime: 5 * 60_000,
    refetchInterval: 8 * 60_000,
  });
}

/**
 * Fetches a file again once the words in it have been read.
 *
 * How many pages a document has is discovered by the same pass that reads its text, and it is
 * the file — not the document — that carries the number. Only the document is watched while
 * that work is in flight; the file above is cached for minutes. Without this, someone who has
 * just uploaded a report is shown its first page and no way to reach the rest, until a refetch
 * happens to come round or they reload by hand.
 *
 * The refetch is tied to the moment the reading finishes rather than to the text being read,
 * so opening a document whose words were read long ago still costs one request, not two.
 */
export function useRefreshFileWhenTextRead(
  fileId: string | undefined,
  textExtraction: string | undefined,
) {
  const queryClient = useQueryClient();
  const wasReading = useRef(false);
  useEffect(() => {
    if (textExtraction === 'pending') {
      wasReading.current = true;
      return;
    }
    if (wasReading.current && fileId !== undefined) {
      wasReading.current = false;
      void queryClient.invalidateQueries({ queryKey: queryKeys.file(fileId) });
    }
  }, [fileId, textExtraction, queryClient]);
}

/**
 * How much of a text document is read into the page before the rest is left to a download.
 *
 * A log of a season's surveying can be tens of megabytes of plain text, and putting all of it
 * into a document that has to lay out cannot end well on a phone. The cut is stated to the
 * reader rather than made silently, because a file that appears to stop halfway through is
 * indistinguishable from a file that was truncated when it was written.
 */
export const maxInlineTextBytes = 512 * 1024;

/**
 * The text of a file, for the formats that are text.
 *
 * Fetched from the same short-lived delivery URL everything else uses, so a caller who may not
 * have the stored bytes does not get them here either — which for a text file is every caller
 * who can read the document, since a text file records no position to protect.
 *
 * Only the part that will be shown is asked for. The delivery route answers partial requests,
 * so the cut is made before the bytes cross the network rather than after: pulling a season's
 * survey log down over a phone connection in order to display its first half-megabyte would
 * spend the whole file to show a fragment of it. A server that ignored the request and sent
 * everything is still handled, because the cut is applied here as well.
 */
export function useFileText(file: FileInfo | undefined) {
  return useQuery({
    queryKey: ['file-text', file?.id ?? '', file?.contentUrl ?? ''] as const,
    queryFn: async () => {
      const response = await fetch(file!.contentUrl, {
        headers: { Range: `bytes=0-${maxInlineTextBytes - 1}` },
      });
      if (!response.ok) {
        throw new ApiError(response.status, 'file.not_found');
      }

      const bytes = (await response.arrayBuffer()).slice(0, maxInlineTextBytes);
      // A cut made in bytes can land in the middle of a character — Romanian text is full of
      // two-byte ones — so the tail is decoded as if more were coming, which drops an
      // incomplete character instead of showing it as a replacement mark.
      const text = new TextDecoder('utf-8').decode(bytes, { stream: true });
      return {
        // What was cut is a fact about the file, not about what came back: a partial answer is
        // the same length as a whole one when the file is exactly that long.
        text,
        truncated: file!.sizeBytes > maxInlineTextBytes,
      };
    },
    enabled: !!file && file.mayDownloadOriginal,
    // The bytes of one version never change, so this is refetched only because the URL that
    // reaches them expires; the key carries that URL, so a renewed one is a new request.
    staleTime: 5 * 60_000,
  });
}

/**
 * The words of one page as the server read them — the text a durable selection is measured
 * against.
 *
 * It is asked for one page at a time and only when something needs it, because that is the
 * shape of the question: a reader who selects a sentence on page nine is asking about page
 * nine, and pulling a two-hundred-page report's text to answer it would spend the document to
 * place one phrase. The delivery URL carries the token, so this is the same permission that
 * opens the page picture beside it, exercised the same way.
 *
 * A page nothing has read yet answers 404, which is a real answer and not an error to retry:
 * the reading happens in a background job, and until it has run there is nothing to match a
 * quote against.
 */
export function usePageText(pagesUrl: string | null | undefined, page: number | null) {
  return useQuery({
    queryKey: ['page-text', pagesUrl ?? '', page ?? 0] as const,
    queryFn: async () => {
      const [path, query] = pagesUrl!.split('?');
      const url = `${path.replace(/\/content$/, `/pages/${page}/text`)}?${query ?? ''}`;
      const response = await fetch(url);
      if (!response.ok) {
        throw new ApiError(response.status, 'file.page_text_not_read');
      }
      return (await response.json()) as { page: number; text: string };
    },
    enabled: !!pagesUrl && page !== null && page >= 1,
    retry: false,
    // One version's pages never change; the key carries the delivery URL, so a renewed token
    // is a new request rather than a stale answer.
    staleTime: 5 * 60_000,
  });
}

/**
 * Upload limits this installation applies. Served rather than compiled in, so a client
 * build cannot disagree with its server and let someone watch a large file transfer only
 * to be refused at the end.
 */
export function useFileConfig() {
  return useQuery({
    queryKey: queryKeys.fileConfig,
    queryFn: () => unwrap(api.GET('/api/v1/files/config')),
    staleTime: 60 * 60_000,
  });
}

/** The document kinds, each with the metadata schema its documents are described by. */
export function useDocumentTypes() {
  return useQuery({
    queryKey: queryKeys.taxonomy('document-types'),
    queryFn: () => unwrap(api.GET('/api/v1/document-types')),
    staleTime: 5 * 60_000,
  });
}

/**
 * The jobs a person may be recorded as having done on a trip: the rows that ship, which this
 * client has its own wording for, plus whatever an installation added, shown as it was written.
 */
export function useTripParticipantRoles() {
  return useQuery({
    queryKey: queryKeys.taxonomy('trip-participant-roles'),
    queryFn: () => unwrap(api.GET('/api/v1/trip-participant-roles')),
    staleTime: 5 * 60_000,
  });
}

/** A participant role as an administrator authors it. */
export interface TripParticipantRoleWrite {
  code: string;
  name: string;
  description: string | null;
  sortOrder: number;
}

function useInvalidateTripParticipantRoles() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.taxonomy('trip-participant-roles') });
    // Every roster row renders its job from this list, so a renamed or removed row leaves the
    // trips already in cache showing wording that no longer exists.
    void queryClient.invalidateQueries({ queryKey: ['trip-logs'] });
  };
}

export function useCreateTripParticipantRole() {
  const invalidate = useInvalidateTripParticipantRoles();
  return useMutation({
    mutationFn: (body: TripParticipantRoleWrite) =>
      unwrap(api.POST('/api/v1/trip-participant-roles', { body })),
    onSuccess: invalidate,
  });
}

export function useUpdateTripParticipantRole() {
  const invalidate = useInvalidateTripParticipantRoles();
  return useMutation({
    mutationFn: ({ id, ...body }: TripParticipantRoleWrite & { id: number }) =>
      unwrap(api.PUT('/api/v1/trip-participant-roles/{id}', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useDeleteTripParticipantRole() {
  const invalidate = useInvalidateTripParticipantRoles();
  return useMutation({
    mutationFn: (id: number) =>
      unwrapVoid(api.DELETE('/api/v1/trip-participant-roles/{id}', { params: { path: { id } } })),
    onSuccess: invalidate,
  });
}

/**
 * The purposes a trip may be recorded under: the rows that ship, which this client has its own
 * wording for, plus whatever an installation added, which is shown as it was written.
 */
export function useTripTypes() {
  return useQuery({
    queryKey: queryKeys.taxonomy('trip-types'),
    queryFn: () => unwrap(api.GET('/api/v1/trip-types')),
    staleTime: 5 * 60_000,
  });
}

/**
 * A trip purpose as an administrator authors it: three schemas, each arriving as the raw text
 * that was typed rather than as a parsed object, because raw text is what is edited and what
 * the server measures reports against.
 */
export interface TripTypeWrite {
  code: string;
  name: string;
  description: string | null;
  sortOrder: number;
  fieldDataSchema: string | null;
  logisticsSchema: string | null;
  safetySchema: string | null;
  /** The list trips of this purpose work through, by identity. Null names none. */
  defaultChecklistId: string | null;
}

function useInvalidateTripTypes() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.taxonomy('trip-types') });
    // A purpose's schemas decide what every trip recorded under it may say, so cached trip
    // reads are stale in a way the vocabulary list alone does not express.
    void queryClient.invalidateQueries({ queryKey: ['trip-logs'] });
  };
}

export function useCreateTripType() {
  const invalidate = useInvalidateTripTypes();
  return useMutation({
    mutationFn: (body: TripTypeWrite) => unwrap(api.POST('/api/v1/trip-types', { body })),
    onSuccess: invalidate,
  });
}

/**
 * Saves a trip purpose. Rewriting one of its schemas publishes a new version of that schema
 * server-side, so every trip already recorded keeps the version it was checked against and is
 * only re-checked when someone next edits the section it belongs to.
 */
export function useUpdateTripType() {
  const invalidate = useInvalidateTripTypes();
  return useMutation({
    mutationFn: ({ id, ...body }: TripTypeWrite & { id: number }) =>
      unwrap(api.PUT('/api/v1/trip-types/{id}', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useDeleteTripType() {
  const invalidate = useInvalidateTripTypes();
  return useMutation({
    mutationFn: (id: number) =>
      unwrapVoid(api.DELETE('/api/v1/trip-types/{id}', { params: { path: { id } } })),
    onSuccess: invalidate,
  });
}

/** A document kind as it is authored: the schema arrives as raw text, not as a parsed object. */
export interface DocumentTypeWrite {
  code: string;
  name: string;
  description: string | null;
  sortOrder: number;
  metadataSchema: string | null;
}

export function useCreateDocumentType() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: DocumentTypeWrite) => unwrap(api.POST('/api/v1/document-types', { body })),
    onSuccess: () =>
      void queryClient.invalidateQueries({ queryKey: queryKeys.taxonomy('document-types') }),
  });
}

/**
 * Saves a document kind. Rewriting the schema publishes a new schema version server-side,
 * so every document already stored keeps the version it was checked against and is only
 * re-checked when someone next edits it.
 */
export function useUpdateDocumentType() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...body }: DocumentTypeWrite & { id: number }) =>
      unwrap(api.PUT('/api/v1/document-types/{id}', { params: { path: { id } }, body })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.taxonomy('document-types') });
      // A schema change alters what every document of the kind may say, so cached
      // document reads are stale in a way the kind list alone does not express.
      void queryClient.invalidateQueries({ queryKey: ['documents'] });
    },
  });
}

/** A document: the identity behind a file, with the title and typed metadata that outlive its versions. */
export function useDocument(id: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.document(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/documents/{id}', { params: { path: { id: id! } } })),
    enabled: !!id && enabled,
    // Reading a document's text runs in a background job, so "being read" is a state that
    // resolves on its own while the panel is open. Poll only through that one state: every
    // other one is settled, and re-asking would be asking the same question forever.
    refetchInterval: (query) => (query.state.data?.textExtraction === 'pending' ? 2000 : false),
  });
}

/**
 * Updates a document's title, kind, typed metadata, read audience and club binding. A
 * null `metadata` leaves what is stored untouched — that is how a title is corrected on a
 * document whose kind has tightened its schema since the document was written.
 *
 * Visibility and the caving group decide who may read the document when no rule names it,
 * so saving them moves access: the cached lists that were filtered by that answer are
 * dropped along with the document itself.
 *
 * `language` shares the metadata carve-out: null leaves the stored code alone, because it is
 * detected from the document's own text and a title correction is not a statement about it.
 * An empty string is how it is cleared. Changing it re-indexes every page of the document, so
 * cached searches are dropped too — they were answered by the previous stemmer.
 */
export function useUpdateDocument() {
  const invalidateAttachments = useInvalidateAttachments();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...body }: {
      id: string;
      title: string;
      documentTypeId: number | null;
      metadata: Record<string, unknown> | null;
      visibility: Visibility;
      cavingGroupId: string | null;
      language: string | null;
    }) => unwrap(api.PUT('/api/v1/documents/{id}', { params: { path: { id } }, body })),
    onSuccess: (_result, variables) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.document(variables.id) });
      void queryClient.invalidateQueries({ queryKey: ['cabinets'] });
      void queryClient.invalidateQueries({ queryKey: ['search'] });
      invalidateAttachments();
    },
  });
}

/**
 * Comments are audited children of the document they sit on, so writing one changes that
 * document's timeline as well as the thread.
 */
function useInvalidateDocumentComments() {
  const queryClient = useQueryClient();
  return (documentId: string) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.documentComments(documentId) });
    void queryClient.invalidateQueries({ queryKey: ['history'] });
  };
}

/**
 * The remarks written on a document, oldest first. Reading them is reading the document:
 * the server answers a caller who may not read the document exactly as it answers one
 * asking about a document that is not there, so this hook's error carries no information
 * the document query did not already give.
 */
export function useDocumentComments(documentId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.documentComments(documentId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/documents/{documentId}/comments', {
        params: { path: { documentId: documentId! }, query: { pageSize: 200 } },
      })),
    enabled: !!documentId && enabled,
  });
}

/**
 * Posts a remark, or a reply to one. `parentId` names the remark being replied to; threads
 * are one level deep and the server refuses a reply to a reply rather than flattening it.
 */
export function useCreateDocumentComment() {
  const invalidate = useInvalidateDocumentComments();
  return useMutation({
    mutationFn: ({ documentId, ...body }: { documentId: string; parentId: string | null; body: string }) =>
      unwrap(api.POST('/api/v1/documents/{documentId}/comments', {
        params: { path: { documentId } },
        body: { ...body, anchorKind: 'whole', anchor: null, anchorFileId: null },
      })),
    onSuccess: (_result, variables) => invalidate(variables.documentId),
  });
}

/** Rewrites a remark. Only its author may, and the change is recorded in the document's history. */
export function useUpdateDocumentComment() {
  const invalidate = useInvalidateDocumentComments();
  return useMutation({
    mutationFn: ({ documentId, id, ...body }: { documentId: string; id: string; body: string }) =>
      unwrap(api.PUT('/api/v1/documents/{documentId}/comments/{id}', {
        params: { path: { documentId, id } },
        body,
      })),
    onSuccess: (_result, variables) => invalidate(variables.documentId),
  });
}

/** Removes a remark, and the replies under it: its author, or an administrator. */
export function useDeleteDocumentComment() {
  const invalidate = useInvalidateDocumentComments();
  return useMutation({
    mutationFn: ({ documentId, id }: { documentId: string; id: string }) =>
      unwrapVoid(api.DELETE('/api/v1/documents/{documentId}/comments/{id}', {
        params: { path: { documentId, id } },
      })),
    onSuccess: (_result, variables) => invalidate(variables.documentId),
  });
}

/** The whole filing tree: small by construction, so one fetch draws the sider and every breadcrumb. */
export function useCabinets(enabled = true) {
  return useQuery({
    queryKey: queryKeys.cabinets,
    queryFn: () => unwrap(api.GET('/api/v1/cabinets')),
    enabled,
    retry: false,
  });
}

/**
 * Cabinet edits move access — rules are scoped to cabinets and filing decides which of
 * them reach a document — so capabilities and effective-access answers refresh with the
 * tree, exactly as they do for feature sets.
 */
function useInvalidateCabinets() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['cabinets'] });
    // The rules editor lists cabinets as scope anchors from the catalog.
    void queryClient.invalidateQueries({ queryKey: queryKeys.accessCatalog });
    void queryClient.invalidateQueries({ queryKey: queryKeys.capabilities });
    void queryClient.invalidateQueries({ queryKey: ['effective-access'] });
  };
}

export function useCreateCabinet() {
  const invalidate = useInvalidateCabinets();
  return useMutation({
    mutationFn: (body: CabinetWrite) => unwrap(api.POST('/api/v1/cabinets', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateCabinet() {
  const invalidate = useInvalidateCabinets();
  return useMutation({
    mutationFn: ({ id, ...body }: CabinetWrite & { id: string }) =>
      unwrap(api.PUT('/api/v1/cabinets/{id}', { params: { path: { id } }, body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteCabinet() {
  const invalidate = useInvalidateCabinets();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/cabinets/{id}', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

export interface CabinetDocumentParams {
  includeSubtree?: boolean;
  page?: number;
  pageSize?: number;
}

/**
 * What is on a shelf for this caller. The cabinet's own `documentCount` is decided by the
 * same read rule as this listing and counts the same documents, so a shelf's label and its
 * contents agree. The one case where they are answering different questions is
 * `includeSubtree`: the count is always the documents filed directly on the cabinet, while
 * that switch asks the listing for everything below it as well.
 */
export function useCabinetDocuments(id: string | undefined, params: CabinetDocumentParams = {}) {
  return useQuery({
    queryKey: queryKeys.cabinetDocuments(id ?? '', params),
    queryFn: () =>
      unwrap(api.GET('/api/v1/cabinets/{id}/documents', {
        params: { path: { id: id! }, query: params },
      })),
    enabled: !!id,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/**
 * Files a document into a cabinet, or takes it out. Both directions move access — a
 * document leaving a cabinet a deny names becomes readable again — so both invalidate the
 * same caches a rule edit would.
 */
export function useFileDocument() {
  const invalidate = useInvalidateCabinets();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ cabinetId, documentId, filed }: {
      cabinetId: string;
      documentId: string;
      filed: boolean;
    }) => {
      const params = { path: { id: cabinetId, documentId } };
      return filed
        ? unwrapVoid(api.PUT('/api/v1/cabinets/{id}/documents/{documentId}', { params }))
        : unwrapVoid(api.DELETE('/api/v1/cabinets/{id}/documents/{documentId}', { params }));
    },
    onSuccess: (_result, variables) => {
      invalidate();
      void queryClient.invalidateQueries({ queryKey: queryKeys.document(variables.documentId) });
    },
  });
}

/**
 * Uploads one file.
 *
 * `allowDuplicate` is how a caller answers the one refusal a person can answer: content the
 * store already holds and this caller may read is refused with `file.duplicate` until somebody
 * says to store it anyway. It defaults to no, and the default is the point — a caller that
 * simply never asked would otherwise quietly make second copies of everything.
 */
export function useUploadFile() {
  return useMutation({
    mutationFn: async (
      { file, allowDuplicate = false }: { file: File; allowDuplicate?: boolean },
    ): Promise<FileInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      return unwrap(api.POST('/api/v1/files', {
        params: { query: { allowDuplicate } },
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
  });
}

/** Updates user-set file metadata (document date). The gallery's file DTO carries it, so refresh attachments. */
export function useUpdateFile() {
  const invalidateAttachments = useInvalidateAttachments();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, documentDate }: { id: string; documentDate: string | null }) =>
      unwrap(api.PUT('/api/v1/files/{id}', { params: { path: { id } }, body: { documentDate } })),
    onSuccess: () => {
      invalidateAttachments();
      void queryClient.invalidateQueries({ queryKey: ['file-versions'] });
    },
  });
}

export function useCreateAttachment() {
  const invalidate = useInvalidateAttachments();
  return useMutation({
    mutationFn: (body: {
      fileId: string;
      entityType: AttachedEntityType;
      entityId: string;
      role: AttachmentRole;
      caption: string | null;
      sortOrder: number;
    }) => unwrap(api.POST('/api/v1/attachments', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateAttachment() {
  const invalidate = useInvalidateAttachments();
  return useMutation({
    mutationFn: ({ id, ...body }: {
      id: string;
      role: AttachmentRole;
      caption: string | null;
      sortOrder: number;
    }) => unwrap(api.PUT('/api/v1/attachments/{id}', { params: { path: { id } }, body })),
    // Caption/role changes are audited on the target entity; useInvalidateAttachments refreshes
    // both the gallery and that timeline.
    onSuccess: () => invalidate(),
  });
}

export function useDeleteAttachment() {
  const invalidate = useInvalidateAttachments();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/attachments/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

export type FileVersionInfo = components['schemas']['FileVersionDto'];

/** Full version chain of a file (editor-only; the server 403s read-only callers). */
export function useFileVersions(fileId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.fileVersions(fileId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/files/{id}/versions', { params: { path: { id: fileId! } } })),
    enabled: !!fileId && enabled,
    staleTime: 5 * 60_000,
  });
}

export function useUploadFileVersion() {
  const invalidateAttachments = useInvalidateAttachments();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ fileId, file }: { fileId: string; file: File }): Promise<FileInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      return unwrap(api.POST('/api/v1/files/{id}/versions', {
        params: { path: { id: fileId } },
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
    // The attachment now points at the new head; both the gallery and any version list refresh.
    onSuccess: () => {
      invalidateAttachments();
      void queryClient.invalidateQueries({ queryKey: ['file-versions'] });
    },
  });
}

export function useDeleteFileVersion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/files/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['file-versions'] }),
  });
}

export type RasterMapInfo = components['schemas']['GeoreferencedMapDto'];
export type RasterMapUpdate = components['schemas']['GeoreferencedMapUpdateRequest'];

export interface RasterMapListParams {
  page?: number;
  pageSize?: number;
  caveFeatureId?: string;
}

export function useRasterMaps(params: RasterMapListParams, pollWhileProcessing = false) {
  return useQuery({
    queryKey: queryKeys.rasterMaps(params),
    queryFn: () => unwrap(api.GET('/api/v1/georeferenced-maps', { params: { query: params } })),
    placeholderData: keepPreviousData,
    // COG normalization runs in a background job; the signed CogUrl also has a
    // 10-minute lifetime, so refresh periodically either way.
    refetchInterval: pollWhileProcessing
      ? (query) =>
          query.state.data?.items.some(
            (m) => m.status === 'uploaded' || m.status === 'processing',
          )
            ? 2000
            : 8 * 60_000
      : 8 * 60_000,
  });
}

function useInvalidateRasterMaps() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['raster-maps'] });
}

export function useUploadRasterMap() {
  const invalidate = useInvalidateRasterMaps();
  return useMutation({
    mutationFn: async (file: File): Promise<RasterMapInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      return unwrap(api.POST('/api/v1/georeferenced-maps', {
        body: form as never,
        bodySerializer: (b: unknown) => b as FormData,
      }));
    },
    onSuccess: () => invalidate(),
  });
}

export function useUpdateRasterMap() {
  const invalidate = useInvalidateRasterMaps();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: RasterMapUpdate }) =>
      unwrap(api.PUT('/api/v1/georeferenced-maps/{id}', { params: { path: { id } }, body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteRasterMap() {
  const invalidate = useInvalidateRasterMaps();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/georeferenced-maps/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

export type TripLogInfo = components['schemas']['TripLogDto'];
export type TripLogWrite = components['schemas']['TripLogWriteRequest'];
export type TripType = components['schemas']['TripTypeDto'];
export type TripParticipant = components['schemas']['TripParticipantDto'];
export type TripParticipantRole = components['schemas']['TripParticipantRoleDto'];
export type TripParticipantWrite = components['schemas']['TripParticipantWrite'];
export type ActivityState = components['schemas']['ActivityState'];
export type TagInfo = components['schemas']['TagDto'];
export type TaggingInfo = components['schemas']['TaggingDto'];

export interface TripLogListParams {
  page?: number;
  pageSize?: number;
  from?: string;
  to?: string;
  caveId?: string;
  search?: string;
  /**
   * The camp's own trip list. It is filtered like every other listing, so the same camp lists
   * different trips to different people; a camp the caller may not read answers as though it
   * gathered nothing rather than refusing, so an id cannot be probed for existence here.
   */
  expeditionId?: string;
  /**
   * Area features the trips name, comma-separated. Alternatives: a trip naming any of them is
   * kept, and each one reaches everything the containment hierarchy puts inside it. Like the cave
   * and the camp, an area this caller may not read answers with an empty page rather than a
   * refusal, so an id cannot be probed for existence — and one such id empties the whole answer
   * rather than being dropped from the list.
   */
  areaIds?: string;
  /**
   * People on the roster, comma-separated and alternatives to each other. The answer is still only
   * the trips this caller may read, so it never assembles where a person has been out of trips the
   * asker cannot open — which is why the caller's own list of trips, whose whole point is that it
   * names nobody, has no such member.
   */
  participantIds?: string;
  /** Trip type ids, comma-separated. Alternatives: a trip of any of them is kept. */
  types?: string;
  /** Lifecycle words, comma-separated, spelled the way the contract spells them. */
  states?: string;
  /** Audience words, comma-separated, spelled the way the contract spells them. */
  visibilities?: string;
  /** Whether something went wrong. Omitted means no opinion, not "no". */
  hadIncident?: boolean;
  /**
   * `date`, `title`, `created` or `updated`, a leading minus for descending. The order is the
   * server's: re-sorting the page it handed back would reorder one page, which is a different
   * and wrong answer as soon as there is more than one.
   */
  sort?: string;
}

/** One option a filter panel may offer, and how many trips it would leave. */
export type TripFacetValue = components['schemas']['TripFacetValueDto'];

/** What every option in the trip listing's filter panel would leave, for this caller. */
export type TripListFacets = components['schemas']['TripListFacetsDto'];

/**
 * Everything the counts are asked about. It is the listing's own parameters minus the three that
 * change no count — which page, how big, and in what order — so a panel and the page it sits over
 * are demonstrably asking one question.
 */
export type TripLogFacetParams = Omit<TripLogListParams, 'page' | 'pageSize' | 'sort'>;

export function useTripLogs(params: TripLogListParams) {
  return useQuery({
    queryKey: queryKeys.tripLogs(params),
    queryFn: () => unwrap(api.GET('/api/v1/trip-logs', { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

/**
 * How many trips each filter option would leave, counted over the trips this caller may read.
 *
 * Its own request rather than a shape of the listing's, because it is a different question about
 * the same query and every caller who only wants rows would otherwise pay for the counts. Keeping
 * the last answer on screen matters more here than anywhere: the panel is what somebody is
 * clicking, and options that vanish and return under the cursor make it unusable.
 */
export function useTripLogFacets(params: TripLogFacetParams) {
  return useQuery({
    queryKey: queryKeys.tripLogFacets(params),
    queryFn: () => unwrap(api.GET('/api/v1/trip-logs/facets', { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

/** One slice of a trip listing, and what it holds. */
export type TripGroup = components['schemas']['TripGroupDto'];

/** A trip listing broken into slices, over the trips this caller may read. */
export type TripListGrouping = components['schemas']['TripListGroupingDto'];

/**
 * What the slices are cut from and what they are cut by. The narrowings are the listing's own,
 * minus the three that change no slice — which page, how big, and in what order — so the shape
 * above the table and the table itself are demonstrably about one set of trips.
 */
export type TripLogGroupingParams = TripLogFacetParams & {
  /** `year`, `type`, `state`, `visibility`, `incident`, `area` or `participant`. */
  groupBy?: string;
  /** The second level. Must differ from the first, which the server refuses rather than ignores. */
  thenBy?: string;
};

/**
 * A trip listing broken into slices.
 *
 * Its own request rather than a shape of the page's, for the same reason the counts are: it is a
 * different question about the same query, and nobody who only wants rows should pay for it. The
 * previous answer is kept on screen while a new one is fetched, because the panel is what
 * somebody is clicking and slices that vanish and return under the cursor make it unusable.
 */
export function useTripLogGrouping(params: TripLogGroupingParams, enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.tripLogGrouping(params),
    queryFn: () => unwrap(api.GET('/api/v1/trip-logs/grouping', { params: { query: params } })),
    placeholderData: keepPreviousData,
    enabled,
  });
}

/** One year of a filtered trip listing, and how much ground had been covered by the end of it. */
export type TripStatsYear = components['schemas']['TripStatsYearDto'];

/** How the filtered trips break down along one dimension, longest first. */
export type TripStatsBreakdown = components['schemas']['TripStatsBreakdownDto'];

/** What a filtered trip listing adds up to, over the trips this caller may read. */
export type TripStats = components['schemas']['TripStatsDto'];

/**
 * What the filtered trips add up to.
 *
 * It takes the listing's own narrowings and nothing else, so "the current filter" means one thing
 * on the insights page and on the list. A figure worked out from a second, similar-looking filter
 * would disagree with the table under the one condition nobody checks by eye: a reader who may
 * open only part of the archive. The previous answer is kept while a new one loads, because the
 * scope toggle and the filter are things somebody is clicking and charts that blank out and
 * return under the cursor read as breakage.
 */
export function useTripLogStats(params: TripLogFacetParams, enabled = true) {
  return useQuery({
    queryKey: queryKeys.tripLogStats(params),
    queryFn: () => unwrap(api.GET('/api/v1/trip-logs/stats', { params: { query: params } })),
    placeholderData: keepPreviousData,
    enabled,
  });
}

/**
 * What the caller asked of their own list of trips. There is deliberately no member naming a
 * person: whose trips these are is worked out on the server from whoever is making the request,
 * and a parameter for it would let somebody assemble where a named person has been out of trips
 * they may never open. Adding one here would be the first half of undoing that.
 */
export interface MyTripLogListParams {
  page?: number;
  pageSize?: number;
  /** Inclusive, `YYYY-MM-DD`. Omitted, the server starts the window at today. */
  from?: string;
  /** Inclusive, `YYYY-MM-DD`. Omitted, the window has no far end. */
  to?: string;
  /** A lifecycle state spelled the way the contract spells it; an unknown word is refused. */
  state?: ActivityState;
}

/**
 * The trips the signed-in account is on, soonest first.
 *
 * Its own key rather than a shape of the trip list's, because it is a different question with a
 * different answer for every reader, and because it goes stale as dates pass rather than as
 * people edit. The key sits under the trip prefix so writing a trip re-reads it for free.
 */
export function useMyTripLogs(params: MyTripLogListParams) {
  return useQuery({
    queryKey: queryKeys.myTripLogs(params),
    queryFn: () => unwrap(api.GET('/api/v1/trip-logs/mine', { params: { query: params } })),
    // Paging or narrowing keeps the rows on screen while the next answer arrives, rather than
    // emptying the table under whoever is reading it.
    placeholderData: keepPreviousData,
  });
}

export function useTripLog(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.tripLog(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/trip-logs/{id}', { params: { path: { id: id! } } })),
    enabled: !!id,
  });
}

function useInvalidateTripLogs() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['trip-logs'] });
}

/**
 * The same invalidation, but handed back so a caller can wait for the re-read it starts.
 *
 * A write on the trip is checked against the version the caller last *read*, and only a read
 * records a version. So the moment a write succeeds, the version this caller holds is one behind
 * the one their own write produced, and a second write sent before the re-read lands is refused
 * as a conflict — with two saves on the same card a second apart, which is ordinary use, not a
 * race anybody would think to look for. Refusing it is right: the caller really is writing
 * against a version that has moved. What is wrong is answering "saved" while that is still true.
 *
 * So a write on the trip is not finished until the trip has been read back. The cost is that the
 * confirmation waits for the read, which takes as long as it takes; the alternative is a second
 * save that fails for a reason nobody can act on. This would be unnecessary if a write handed
 * back the version it produced, and it is only needed on the trip's own writes — a write on
 * something beside the trip carries no precondition on it.
 */
function useReadTripLogsBack() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: ['trip-logs'] });
}

export function useCreateTripLog() {
  const invalidate = useInvalidateTripLogs();
  return useMutation({
    mutationFn: (body: TripLogWrite) => unwrap(api.POST('/api/v1/trip-logs', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateTripLog() {
  const readBack = useReadTripLogsBack();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: TripLogWrite }) =>
      unwrap(api.PUT('/api/v1/trip-logs/{id}', { params: { path: { id } }, body })),
    onSuccess: () => {
      invalidateHistory();
      // Handed back rather than started and forgotten: the next write on this trip is checked
      // against the version this one produced, and only the read records it.
      return readBack();
    },
  });
}

export function useDeleteTripLog() {
  const invalidate = useInvalidateTripLogs();
  return useMutation({
    mutationFn: (id: string) => unwrapVoid(api.DELETE('/api/v1/trip-logs/{id}', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

export type TripReportTemplate = components['schemas']['TripReportTemplateDto'];

/** The layouts a write-up may be built in. Every account may read them: choosing one is not editing one. */
export function useTripReportTemplates(enabled = true) {
  return useQuery({
    queryKey: queryKeys.tripReportTemplates,
    queryFn: () => unwrap(api.GET('/api/v1/trip-report-templates')),
    enabled,
  });
}

export type TripReportTemplateWrite = components['schemas']['TripReportTemplateRequest'];

function useInvalidateTripReportTemplates() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: queryKeys.tripReportTemplates });
}

/**
 * Stores a layout. A body whose lines cannot be read is refused by the server, which says which
 * line is at fault — the message is passed through rather than replaced, because the person
 * editing the layout is the only one who can act on it.
 */
export function useCreateTripReportTemplate() {
  const invalidate = useInvalidateTripReportTemplates();
  return useMutation({
    mutationFn: (body: TripReportTemplateWrite) =>
      unwrap(api.POST('/api/v1/trip-report-templates', { body })),
    onSuccess: invalidate,
  });
}

export function useUpdateTripReportTemplate() {
  const invalidate = useInvalidateTripReportTemplates();
  return useMutation({
    mutationFn: ({ id, ...body }: TripReportTemplateWrite & { id: string }) =>
      unwrap(api.PUT('/api/v1/trip-report-templates/{id}', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useDeleteTripReportTemplate() {
  const invalidate = useInvalidateTripReportTemplates();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/trip-report-templates/{id}', { params: { path: { id } } })),
    onSuccess: invalidate,
  });
}

/**
 * Writes the trip up and files the document in the trip's report slot.
 *
 * The document is built server-side, so nothing here hands it any of the trip's content — an
 * argument carrying what to write would be a second place the question of who may see what is
 * answered. What is filed is not this caller's own copy: a file attached to a trip is reachable
 * by everybody who may read that trip, so the server builds the filed one from the reading any
 * account has. The fuller copy is the download.
 */
export function useKeepTripReport() {
  const invalidateAttachments = useInvalidateAttachments();
  return useMutation({
    mutationFn: ({ id, templateId }: { id: string; templateId?: string }) =>
      unwrap(
        api.POST('/api/v1/trip-logs/{id}/report', {
          params: { path: { id }, query: templateId ? { templateId } : {} },
        }),
      ),
    onSuccess: () => invalidateAttachments(),
  });
}

/**
 * Moving a trip to another lifecycle state, through the one route that names the state it moves
 * to rather than a verb per move.
 *
 * It is a write on the trip and is checked against the version the user was looking at, so it
 * carries the precondition the detail read captured. Which moves are legal from which state is
 * the server's to decide — the control only offers the ones a reader would expect, and a request
 * the rules refuse comes back as a conflict rather than being prevented here.
 */
export function useMoveTripLog() {
  const readBack = useReadTripLogsBack();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: ({ id, state }: { id: string; state: ActivityState }) => {
      const etag = lastReadETag(`/api/v1/trip-logs/${id}`);
      return unwrap(
        api.POST('/api/v1/trip-logs/{id}/state', {
          params: { path: { id } },
          headers: etag ? { 'If-Match': etag } : undefined,
          body: { state },
        }),
      );
    },
    onSuccess: () => {
      invalidateHistory();
      // As on the trip's own update: a move is checked against the version last read, so the
      // move is not finished until the version it produced has been read.
      return readBack();
    },
  });
}

export type TripCalloutState = components['schemas']['TripCalloutState'];

export type TripCalloutArrangement = components['schemas']['TripCalloutRequest'];

/**
 * Arranges, changes or calls off the check that notices if a party does not come back.
 *
 * Part of planning the trip, so it is offered to whoever may change the trip and carries the
 * precondition header every other write to a trip carries — two people arranging different hours
 * is exactly the lost update it exists to catch. Clearing the alarm time is how the whole
 * arrangement is called off; there is no separate route for that, and none is wanted.
 *
 * The answer is the trip as it now stands, so the page redraws from it directly.
 */
export function useArrangeTripCallout() {
  const readBack = useReadTripLogsBack();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: ({ id, ...body }: { id: string } & TripCalloutArrangement) => {
      const etag = lastReadETag(`/api/v1/trip-logs/${id}`);
      return unwrap(
        api.POST('/api/v1/trip-logs/{id}/callout', {
          params: { path: { id } },
          headers: etag ? { 'If-Match': etag } : undefined,
          body,
        }),
      );
    },
    onSuccess: () => {
      invalidateHistory();
      // Checked against the version last read, as the trip's own update is, so the write is not
      // finished until the version it produced has been read back.
      return readBack();
    },
  });
}

/**
 * Says the party is out, which stops the overdue check.
 *
 * No precondition header, unlike every other write to a trip, and that is the server's rule
 * rather than an omission here: there is one value it can write, everybody entitled to call it is
 * saying the same thing, and a stale version would refuse the message that says people are safe.
 * The answer is the trip as it now stands, so the page redraws from it directly.
 */
export function useStandDownTripCallout() {
  const readBack = useReadTripLogsBack();
  return useMutation({
    mutationFn: ({ id }: { id: string }) =>
      unwrap(
        api.POST('/api/v1/trip-logs/{id}/callout/stand-down', { params: { path: { id } } }),
      ),
    onSuccess: () => readBack(),
  });
}

export type TripInvitationInfo = components['schemas']['TripInvitationDto'];
export type TripInvitationList = components['schemas']['TripInvitationListDto'];
export type TripInvitationAnswer = components['schemas']['TripInvitationResponse'];

/**
 * Everybody on a trip's list and what each has said, in the order the server put them in.
 *
 * Three things on this answer are the server's conclusions and not raw rows: the place each
 * person holds in the order people answered in, whether they hold one of the trip's places or
 * are waiting for one, and whether this caller may write an answer for that person. Each has a
 * rule behind it that the client has no way to evaluate — the ordering skips anybody who has not
 * said yes, a hand-picked person keeps their place even past the limit, and answering for
 * somebody else depends on rights over the trip. Rendering them as they arrive is the whole
 * point; re-deriving any of them here would be a second copy of a rule free to drift from the
 * one that is enforced.
 *
 * The list is deliberately unpaged: a place in an order computed over some of the rows would not
 * be a place in the order at all.
 */
export function useTripInvitations(tripLogId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.tripInvitations(tripLogId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/trip-logs/{tripLogId}/invitations', {
          params: { path: { tripLogId: tripLogId! } },
        }),
      ),
    enabled: !!tripLogId && enabled,
  });
}

function useInvalidateTripInvitations() {
  const queryClient = useQueryClient();
  const invalidateHistory = useInvalidateHistory();
  return (tripLogId: string) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.tripInvitations(tripLogId) });
    // A row here is an audit child of the trip, so writing one moves the trip's own timeline.
    invalidateHistory();
  };
}

/**
 * Puts somebody on the trip's list. The person is named by their entry in the club's directory
 * and never by a bare name — a list of people to be told about a trip that could hold text
 * nobody can resolve would be a list nobody can act on — so an id the directory does not know
 * is refused rather than created.
 */
export function useInviteToTrip() {
  const invalidate = useInvalidateTripInvitations();
  return useMutation({
    mutationFn: ({ tripLogId, caverId }: { tripLogId: string; caverId: string }) =>
      unwrap(
        api.POST('/api/v1/trip-logs/{tripLogId}/invitations', {
          params: { path: { tripLogId } },
          body: { caverId },
        }),
      ),
    onSuccess: (_data, { tripLogId }) => invalidate(tripLogId),
  });
}

/**
 * Records what one person says about coming.
 *
 * The note travels with the answer and is replaced by it: an answer given without words is an
 * answer without words, not an answer still wearing the previous one's. So a cleared note is
 * sent as an explicit absence rather than omitted.
 */
export function useAnswerTripInvitation() {
  const invalidate = useInvalidateTripInvitations();
  return useMutation({
    mutationFn: ({
      tripLogId,
      caverId,
      response,
      note,
    }: {
      tripLogId: string;
      caverId: string;
      response: TripInvitationAnswer;
      note: string | null;
    }) =>
      unwrap(
        api.PUT('/api/v1/trip-logs/{tripLogId}/invitations/{caverId}/response', {
          params: { path: { tripLogId, caverId } },
          body: { response, note },
        }),
      ),
    onSuccess: (_data, { tripLogId }) => invalidate(tripLogId),
  });
}

/** Picks one person out for the trip, or puts them back in the order. The order itself is unchanged. */
export function useSelectForTrip() {
  const invalidate = useInvalidateTripInvitations();
  return useMutation({
    mutationFn: ({
      tripLogId,
      caverId,
      selected,
    }: {
      tripLogId: string;
      caverId: string;
      selected: boolean;
    }) =>
      unwrap(
        api.PUT('/api/v1/trip-logs/{tripLogId}/invitations/{caverId}/selection', {
          params: { path: { tripLogId, caverId } },
          body: { selected },
        }),
      ),
    onSuccess: (_data, { tripLogId }) => invalidate(tripLogId),
  });
}

/**
 * Takes somebody off the list entirely, answer and all. For a person put on it by mistake —
 * recording a "no" in their name instead would be writing down words they never said.
 */
export function useRemoveTripInvitation() {
  const invalidate = useInvalidateTripInvitations();
  return useMutation({
    mutationFn: ({ tripLogId, caverId }: { tripLogId: string; caverId: string }) =>
      unwrapVoid(
        api.DELETE('/api/v1/trip-logs/{tripLogId}/invitations/{caverId}', {
          params: { path: { tripLogId, caverId } },
        }),
      ),
    onSuccess: (_data, { tripLogId }) => invalidate(tripLogId),
  });
}

export type TripPromotion = components['schemas']['TripPromotionDto'];

/**
 * Writes everybody holding a place on the trip into the trip's own list of people.
 *
 * A deliberate act and not a consequence of the trip having happened: who turned up is not who
 * said they would, and a roster nobody wrote is one an audit trail cannot account for.
 *
 * It moves the trip itself — its people change and its version with them — so the whole trip
 * prefix is invalidated rather than the list alone, and the re-read is waited for. Leaving the
 * trip as it was read would let a later save carry the roster somebody saw before this ran, pass
 * its precondition, and quietly undo every row written here; not waiting would leave the same
 * window open for as long as the re-read takes, with a confirmation already on screen.
 */
export function usePromoteTripInvitations() {
  const readBack = useReadTripLogsBack();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: ({ tripLogId }: { tripLogId: string }) =>
      unwrap(
        api.POST('/api/v1/trip-logs/{tripLogId}/invitations/promote', {
          params: { path: { tripLogId } },
        }),
      ),
    onSuccess: () => {
      invalidateHistory();
      // Waited for, as on the trip's own writes: this stamps the trip row, so a save sent
      // between the confirmation and the re-read would be refused against the version it moved.
      return readBack();
    },
  });
}

export type EventInvitationInfo = components['schemas']['EventInvitationDto'];
export type EventInvitationList = components['schemas']['EventInvitationListDto'];

/**
 * Everybody on a club event's list and what each has said, in the order the server put them in.
 *
 * The same rows a trip's answers are, read through the shape that names an event: there is one
 * answering mechanism and one table behind both. As on a trip, the place each person holds, in or
 * waiting, and whether this caller may write an answer for them are the server's conclusions and
 * never re-derived here.
 *
 * A kind of event that nobody comes to — a deadline — takes no answers at all and refuses this
 * whole group under its own code, so nothing asks for a list it has no way to hold.
 */
export function useEventInvitations(eventId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.eventInvitations(eventId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/events/{eventId}/invitations', {
          params: { path: { eventId: eventId! } },
        }),
      ),
    enabled: !!eventId && enabled,
  });
}

function useInvalidateEventInvitations() {
  const queryClient = useQueryClient();
  const invalidateHistory = useInvalidateHistory();
  return (eventId: string) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.eventInvitations(eventId) });
    // A row here is an audit child of the event, so writing one moves the event's own timeline.
    invalidateHistory();
  };
}

/**
 * Puts somebody on an event's list. The person is named by their entry in the club's directory
 * and never by a bare name, exactly as on a trip: a list of people to be told about something
 * that could hold text nobody can resolve would be a list nobody can act on.
 */
export function useInviteToEvent() {
  const invalidate = useInvalidateEventInvitations();
  return useMutation({
    mutationFn: ({ eventId, caverId }: { eventId: string; caverId: string }) =>
      unwrap(
        api.POST('/api/v1/events/{eventId}/invitations', {
          params: { path: { eventId } },
          body: { caverId },
        }),
      ),
    onSuccess: (_data, { eventId }) => invalidate(eventId),
  });
}

/**
 * Records what one person says about coming to an event. The note travels with the answer and is
 * replaced by it, so a cleared note is sent as an explicit absence rather than omitted.
 */
export function useAnswerEventInvitation() {
  const invalidate = useInvalidateEventInvitations();
  return useMutation({
    mutationFn: ({
      eventId,
      caverId,
      response,
      note,
    }: {
      eventId: string;
      caverId: string;
      response: TripInvitationAnswer;
      note: string | null;
    }) =>
      unwrap(
        api.PUT('/api/v1/events/{eventId}/invitations/{caverId}/response', {
          params: { path: { eventId, caverId } },
          body: { response, note },
        }),
      ),
    onSuccess: (_data, { eventId }) => invalidate(eventId),
  });
}

/** Picks one person out for the event, or puts them back in the order. The order is unchanged. */
export function useSelectForEvent() {
  const invalidate = useInvalidateEventInvitations();
  return useMutation({
    mutationFn: ({
      eventId,
      caverId,
      selected,
    }: {
      eventId: string;
      caverId: string;
      selected: boolean;
    }) =>
      unwrap(
        api.PUT('/api/v1/events/{eventId}/invitations/{caverId}/selection', {
          params: { path: { eventId, caverId } },
          body: { selected },
        }),
      ),
    onSuccess: (_data, { eventId }) => invalidate(eventId),
  });
}

/**
 * Takes somebody off an event's list entirely, answer and all. For a person put on it by mistake —
 * recording a "no" in their name instead would be writing down words they never said.
 */
export function useRemoveEventInvitation() {
  const invalidate = useInvalidateEventInvitations();
  return useMutation({
    mutationFn: ({ eventId, caverId }: { eventId: string; caverId: string }) =>
      unwrapVoid(
        api.DELETE('/api/v1/events/{eventId}/invitations/{caverId}', {
          params: { path: { eventId, caverId } },
        }),
      ),
    onSuccess: (_data, { eventId }) => invalidate(eventId),
  });
}

export function useTags(search: string) {
  return useQuery({
    queryKey: queryKeys.tags(search),
    queryFn: () => unwrap(api.GET('/api/v1/tags', { params: { query: { search: search || undefined } } })),
    staleTime: 60_000,
  });
}

/** @param enabled false keeps the request from being made — see {@link useAttachments}. */
export function useTaggings(
  entityType: AttachedEntityType,
  entityId: string | undefined,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.taggings(entityType, entityId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/taggings', { params: { query: { entityType, entityId: entityId! } } })),
    enabled: enabled && !!entityId,
  });
}

function useInvalidateTaggings() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['taggings'] });
    void queryClient.invalidateQueries({ queryKey: ['tags'] });
    // Taggings are audited children of the tagged entity — refresh its timeline too.
    void queryClient.invalidateQueries({ queryKey: ['history'] });
  };
}

export function useCreateTagging() {
  const invalidate = useInvalidateTaggings();
  return useMutation({
    mutationFn: (body: { tagName: string; entityType: AttachedEntityType; entityId: string }) =>
      unwrap(api.POST('/api/v1/taggings', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteTagging() {
  const invalidate = useInvalidateTaggings();
  return useMutation({
    mutationFn: async (id: number) => {
      const { error, response } = await api.DELETE('/api/v1/taggings/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

export type CavingGroupInfo = components['schemas']['CavingGroupDto'];
export type CavingGroupMemberInfo = components['schemas']['CavingGroupMemberDto'];
export type CaverInfo = components['schemas']['CaverDto'];
export type ObjectAccessEntry = components['schemas']['ObjectAccessEntryDto'];
export type ObjectAccessEntryWrite = components['schemas']['ObjectAccessEntryWrite'];

export function useCavingGroups() {
  return useQuery({
    queryKey: queryKeys.cavingGroups,
    queryFn: () => unwrap(api.GET('/api/v1/caving-groups')),
    staleTime: 60_000,
  });
}

export function useCavingGroupMembers(cavingGroupId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.cavingGroupMembers(cavingGroupId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/caving-groups/{id}/members', { params: { path: { id: cavingGroupId! } } })),
    enabled: !!cavingGroupId,
  });
}

function useInvalidateCavingGroups() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['cavingGroups'] });
}

/**
 * What a roster edit changes, which is more than the directory row.
 *
 * The list of clubs carries each one's member count, so it has to be refetched — but so does the
 * roster the edit was made in, and so does how many people an announcement to that club would
 * reach. Refetching only the directory leaves the drawer showing the roster as it was before the
 * edit that was just made in it.
 */
function useInvalidateCavingGroupRoster(cavingGroupId: string) {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['cavingGroups'] });
    void queryClient.invalidateQueries({ queryKey: queryKeys.cavingGroupMembers(cavingGroupId) });
    void queryClient.invalidateQueries({ queryKey: queryKeys.cavingGroupAudience(cavingGroupId) });
  };
}

export function useCreateCavingGroup() {
  const invalidate = useInvalidateCavingGroups();
  return useMutation({
    mutationFn: (body: { name: string; type: CavingGroupInfo['type']; description: string | null; website: string | null }) =>
      unwrap(api.POST('/api/v1/caving-groups', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpsertCavingGroupMember(cavingGroupId: string) {
  const invalidate = useInvalidateCavingGroupRoster(cavingGroupId);
  return useMutation({
    mutationFn: (body: { caverId: string; role: CavingGroupMemberInfo['role'] }) =>
      unwrap(api.POST('/api/v1/caving-groups/{id}/members', { params: { path: { id: cavingGroupId } }, body })),
    onSuccess: () => invalidate(),
  });
}

/**
 * How many people an announcement to this caving group would reach, asked before one is written.
 *
 * Not the roster's size: the members with no account have nowhere to receive anything and the
 * person asking is never told their own announcement. It is the server's own count rather than
 * one this page works out, so what somebody is shown and what is sent cannot drift apart.
 *
 * Left disabled for anyone who may not write to the group, so no 403 is provoked by opening a
 * page: only the people the list already marks as able to announce ever ask.
 */
export function useCavingGroupAnnouncementAudience(cavingGroupId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.cavingGroupAudience(cavingGroupId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/caving-groups/{id}/announcements/audience', {
          params: { path: { id: cavingGroupId! } },
        }),
      ),
    enabled: !!cavingGroupId,
  });
}

export function useAnnounceToCavingGroup(cavingGroupId: string) {
  return useMutation({
    mutationFn: (body: { message: string }) =>
      unwrap(
        api.POST('/api/v1/caving-groups/{id}/announcements', {
          params: { path: { id: cavingGroupId } },
          body,
        }),
      ),
  });
}

export function useRemoveCavingGroupMember(cavingGroupId: string) {
  const invalidate = useInvalidateCavingGroupRoster(cavingGroupId);
  return useMutation({
    mutationFn: async (caverId: string) => {
      const { error, response } = await api.DELETE('/api/v1/caving-groups/{id}/members/{caverId}', {
        params: { path: { id: cavingGroupId, caverId } },
      });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

/** The roster of people, optionally filtered by name; `unlinked` narrows to those with no account. */
export function useCavers(search?: string, unlinked?: boolean) {
  return useQuery({
    queryKey: [...queryKeys.cavers, search ?? '', unlinked ?? false],
    queryFn: () => unwrap(api.GET('/api/v1/cavers', { params: { query: { search, unlinked } } })),
    staleTime: 30_000,
  });
}

export function useCaver(id: string | undefined) {
  return useQuery({
    queryKey: [...queryKeys.cavers, id],
    queryFn: () => unwrap(api.GET('/api/v1/cavers/{id}', { params: { path: { id: id! } } })),
    enabled: !!id,
  });
}

function useInvalidateCavers() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.cavers });
    // A roster edit can change how a person is named on trips and group pages too.
    void queryClient.invalidateQueries({ queryKey: ['cavingGroups'] });
  };
}

export function useCreateCaver() {
  const invalidate = useInvalidateCavers();
  return useMutation({
    mutationFn: (body: { fullName: string; email: string | null; phone: string | null; notes: string | null }) =>
      unwrap(api.POST('/api/v1/cavers', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateCaver(id: string) {
  const invalidate = useInvalidateCavers();
  return useMutation({
    mutationFn: (body: { fullName: string; email: string | null; phone: string | null; notes: string | null }) =>
      unwrap(api.PUT('/api/v1/cavers/{id}', { params: { path: { id } }, body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteCaver() {
  const invalidate = useInvalidateCavers();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/cavers/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

export function useMergeCavers(id: string) {
  const invalidate = useInvalidateCavers();
  return useMutation({
    mutationFn: (sourceCaverId: string) =>
      unwrap(api.POST('/api/v1/cavers/{id}/merge', { params: { path: { id } }, body: { sourceCaverId } })),
    onSuccess: () => invalidate(),
  });
}

export function useLinkCaverAccount(id: string) {
  const invalidate = useInvalidateCavers();
  return useMutation({
    mutationFn: (userId: string) =>
      unwrap(api.POST('/api/v1/cavers/{id}/account-link', { params: { path: { id } }, body: { userId } })),
    onSuccess: () => invalidate(),
  });
}

export function useUnlinkCaverAccount(id: string) {
  const invalidate = useInvalidateCavers();
  return useMutation({
    mutationFn: async () => {
      const { error, response } = await api.DELETE('/api/v1/cavers/{id}/account-link', {
        params: { path: { id } },
      });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

/** The rules written directly onto one object (server-side: ManagePermissions). */
export function useObjectAccess(entityType: EntityType, entityId: string | undefined, enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.objectAccess(entityType, entityId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/objects/{entityType}/{id}/access', {
        params: { path: { entityType, id: entityId! } },
      })),
    enabled: enabled && !!entityId,
    retry: false,
  });
}

export function useReplaceObjectAccess(entityType: EntityType, entityId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (entries: ObjectAccessEntryWrite[]) =>
      unwrap(api.PUT('/api/v1/objects/{entityType}/{id}/access', {
        params: { path: { entityType, id: entityId } },
        body: { entries },
      })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['object-access'] });
      // The rules just written decide what everyone may do here, this caller included.
      void queryClient.invalidateQueries({ queryKey: ['effective-access'] });
    },
  });
}

// ---- the permission model's own surface: rulesets, trustees, feature sets ----

export type PermissionGroup = components['schemas']['PermissionGroupDto'];
export type PermissionGroupMember = components['schemas']['PermissionGroupMemberDto'];
export type AccessEntry = components['schemas']['AccessEntryDto'];
export type AccessEntryWrite = components['schemas']['AccessEntryWrite'];
export type AccessCatalog = components['schemas']['AccessCatalogDto'];
export type AccessCatalogDomain = components['schemas']['AccessCatalogDomainDto'];
export type AccessCatalogScope = components['schemas']['AccessCatalogScopeDto'];
export type AccessScopeKind = components['schemas']['AccessScopeKind'];
export type AccessEffect = components['schemas']['AccessEffect'];
export type AccessSubjectKind = components['schemas']['AccessSubjectKind'];
export type AccessPreview = components['schemas']['AccessPreviewDto'];
export type AccessPreviewExplanation = components['schemas']['AccessPreviewExplanationDto'];
export type FeatureSetInfo = components['schemas']['FeatureSetDto'];
export type FeatureSetMember = components['schemas']['FeatureSetMemberDto'];

/**
 * The rule editor's vocabulary: which scopes each domain accepts and the actions valid in
 * each, generated server-side from the validator so the editor can never drift from it.
 */
export function useAccessCatalog(enabled = true) {
  return useQuery({
    queryKey: queryKeys.accessCatalog,
    queryFn: () => unwrap(api.GET('/api/v1/permission-groups/catalog')),
    enabled,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

export function usePermissionGroups(enabled = true) {
  return useQuery({
    queryKey: queryKeys.permissionGroups,
    queryFn: () => unwrap(api.GET('/api/v1/permission-groups')),
    enabled,
    retry: false,
  });
}

/**
 * Everything the model's admin surface writes moves authorization, so every mutation
 * refreshes the caller's own capabilities along with the edited resource.
 */
function useInvalidatePermissionModel() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['permission-groups'] });
    void queryClient.invalidateQueries({ queryKey: queryKeys.capabilities });
    void queryClient.invalidateQueries({ queryKey: queryKeys.myPermissionGroups });
    void queryClient.invalidateQueries({ queryKey: ['effective-access'] });
  };
}

export function useCreatePermissionGroup() {
  const invalidate = useInvalidatePermissionModel();
  return useMutation({
    mutationFn: (body: { name: string; description: string | null }) =>
      unwrap(api.POST('/api/v1/permission-groups', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdatePermissionGroup(id: string) {
  const invalidate = useInvalidatePermissionModel();
  return useMutation({
    mutationFn: (body: { name: string; description: string | null }) =>
      unwrap(api.PUT('/api/v1/permission-groups/{id}', { params: { path: { id } }, body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeletePermissionGroup() {
  const invalidate = useInvalidatePermissionModel();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/permission-groups/{id}', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

export function usePermissionGroupEntries(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.permissionGroupEntries(id ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/permission-groups/{id}/entries', { params: { path: { id: id! } } })),
    enabled: !!id,
    retry: false,
  });
}

export function useReplacePermissionGroupEntries(id: string) {
  const invalidate = useInvalidatePermissionModel();
  return useMutation({
    mutationFn: (entries: AccessEntryWrite[]) =>
      unwrap(api.PUT('/api/v1/permission-groups/{id}/entries', {
        params: { path: { id } },
        body: { entries },
      })),
    onSuccess: () => invalidate(),
  });
}

export function usePermissionGroupMembers(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.permissionGroupMembers(id ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/permission-groups/{id}/members', { params: { path: { id: id! } } })),
    enabled: !!id,
    retry: false,
  });
}

export function useAddPermissionGroupMember(id: string) {
  const invalidate = useInvalidatePermissionModel();
  return useMutation({
    mutationFn: (body: { memberKind: AccessSubjectKind; memberId: string }) =>
      unwrapVoid(api.POST('/api/v1/permission-groups/{id}/members', {
        params: { path: { id } },
        body,
      })),
    onSuccess: () => invalidate(),
  });
}

export function useRemovePermissionGroupMember(id: string) {
  const invalidate = useInvalidatePermissionModel();
  return useMutation({
    mutationFn: ({ memberKind, memberId }: { memberKind: AccessSubjectKind; memberId: string }) =>
      unwrapVoid(api.DELETE('/api/v1/permission-groups/{id}/members/{memberKind}/{memberId}', {
        params: { path: { id, memberKind, memberId } },
      })),
    onSuccess: () => invalidate(),
  });
}

/**
 * What the model would answer for somebody else — the look an editor takes before saving
 * a deny. A mutation rather than a query: it is asked deliberately, per subject.
 */
export function useAccessPreview() {
  return useMutation({
    mutationFn: (body: { subjectKind: AccessSubjectKind; subjectId: string }) =>
      unwrap(api.POST('/api/v1/permission-groups/preview', { body })),
  });
}

export function useFeatureSets(enabled = true) {
  return useQuery({
    queryKey: queryKeys.featureSets,
    queryFn: () => unwrap(api.GET('/api/v1/feature-sets')),
    enabled,
    retry: false,
  });
}

/** Feature-set edits move access (rules hang on sets), so capabilities refresh too. */
function useInvalidateFeatureSets() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['feature-sets'] });
    // The rules editor lists sets as scope anchors from the catalog.
    void queryClient.invalidateQueries({ queryKey: queryKeys.accessCatalog });
    void queryClient.invalidateQueries({ queryKey: queryKeys.capabilities });
    void queryClient.invalidateQueries({ queryKey: ['effective-access'] });
  };
}

export function useCreateFeatureSet() {
  const invalidate = useInvalidateFeatureSets();
  return useMutation({
    mutationFn: (body: { name: string; description: string | null }) =>
      unwrap(api.POST('/api/v1/feature-sets', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateFeatureSet(id: string) {
  const invalidate = useInvalidateFeatureSets();
  return useMutation({
    mutationFn: (body: { name: string; description: string | null }) =>
      unwrap(api.PUT('/api/v1/feature-sets/{id}', { params: { path: { id } }, body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteFeatureSet() {
  const invalidate = useInvalidateFeatureSets();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/feature-sets/{id}', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

/** The set's features the caller may read — the count on the set itself can be larger. */
export function useFeatureSetMembers(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.featureSetMembers(id ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/feature-sets/{id}/members', { params: { path: { id: id! } } })),
    enabled: !!id,
    retry: false,
  });
}

export function useReplaceFeatureSetMembers(id: string) {
  const invalidate = useInvalidateFeatureSets();
  return useMutation({
    mutationFn: (featureIds: string[]) =>
      unwrapVoid(api.PUT('/api/v1/feature-sets/{id}/members', {
        params: { path: { id } },
        body: { featureIds },
      })),
    onSuccess: () => invalidate(),
  });
}

export type UserSummary = components['schemas']['UserSummaryDto'];

export function useUserSearch(q: string) {
  return useQuery({
    queryKey: ['user-search', q] as const,
    queryFn: () => unwrap(api.GET('/api/v1/users/search', { params: { query: { q } } })),
    enabled: q.trim().length >= 2,
    staleTime: 30_000,
  });
}

export type DashboardSummary = components['schemas']['DashboardSummaryDto'];
export type DashboardActivityItem = components['schemas']['DashboardActivityItemDto'];
export type DashboardActivityKind = components['schemas']['DashboardActivityKind'];

export function useDashboardSummary() {
  return useQuery({
    queryKey: queryKeys.dashboardSummary,
    queryFn: () => unwrap(api.GET('/api/v1/dashboard/summary')),
    // Counts and the activity feed move as other people edit; a short window keeps the
    // page from refetching on every visit without going stale over a working session.
    staleTime: 60_000,
  });
}

export type MapViewInfo = components['schemas']['MapViewDto'];
export type MapViewWrite = components['schemas']['MapViewWriteRequest'];

export function useMapViews() {
  return useQuery({
    queryKey: ['map-views'] as const,
    queryFn: () => unwrap(api.GET('/api/v1/map-views')),
  });
}

function useInvalidateMapViews() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['map-views'] });
}

export function useCreateMapView() {
  const invalidate = useInvalidateMapViews();
  return useMutation({
    mutationFn: (body: MapViewWrite) => unwrap(api.POST('/api/v1/map-views', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteMapView() {
  const invalidate = useInvalidateMapViews();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/map-views/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => invalidate(),
  });
}

export function useShareMapView() {
  const invalidate = useInvalidateMapViews();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/v1/map-views/{id}/share', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

/** Imperative fetch for the anonymous shared-view page (no auth attached needed). */
export async function fetchSharedView(token: string) {
  return unwrap(api.GET('/api/v1/shared/views/{token}', { params: { path: { token } } }));
}

export function useMfaStatus() {
  return useQuery({
    queryKey: queryKeys.mfa,
    queryFn: () => unwrap(api.GET('/api/v1/me/mfa')),
  });
}

export function usePhoneStatus() {
  return useQuery({
    queryKey: queryKeys.phone,
    queryFn: () => unwrap(api.GET('/api/v1/me/phone')),
  });
}

/** Admin-only; the query is left disabled for anyone else so no 403 is provoked. */
export function useAdminSettings(enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.adminSettings,
    queryFn: () => unwrap(api.GET('/api/v1/admin/settings')),
    enabled,
  });
}

export function useMessageTemplates(enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.messageTemplates,
    queryFn: () => unwrap(api.GET('/api/v1/admin/message-templates')),
    enabled,
  });
}

function useInvalidateCaves() {
  const queryClient = useQueryClient();
  return (caveId?: string) => {
    void queryClient.invalidateQueries({ queryKey: ['caves'] });
    if (caveId) {
      void queryClient.invalidateQueries({ queryKey: queryKeys.entrances(caveId) });
    }
  };
}

export function useCreateCave() {
  const invalidate = useInvalidateCaves();
  return useMutation({
    mutationFn: (body: CaveWrite) => unwrap(api.POST('/api/v1/caves', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateCave(id: string) {
  const invalidate = useInvalidateCaves();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: (body: CaveWrite) =>
      unwrap(api.PUT('/api/v1/caves/{id}', { params: { path: { id } }, body })),
    onSuccess: () => {
      invalidate(id);
      invalidateHistory();
    },
  });
}

export function useDeleteCave() {
  const invalidate = useInvalidateCaves();
  return useMutation({
    mutationFn: (id: string) => unwrapVoid(api.DELETE('/api/v1/caves/{id}', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

/** Non-hook variant for flows where the cave id is only known at submit time. */
export async function createEntranceFor(caveId: string, body: EntranceWrite) {
  return unwrap(api.POST('/api/v1/caves/{caveId}/entrances', { params: { path: { caveId } }, body }));
}

export function useCreateEntrance(caveId: string) {
  const invalidate = useInvalidateCaves();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: (body: EntranceWrite) =>
      unwrap(api.POST('/api/v1/caves/{caveId}/entrances', { params: { path: { caveId } }, body })),
    onSuccess: () => {
      invalidate(caveId);
      invalidateHistory();
    },
  });
}

export function useUpdateEntrance(caveId: string) {
  const invalidate = useInvalidateCaves();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: EntranceWrite }) =>
      unwrap(api.PUT('/api/v1/cave-entrances/{id}', { params: { path: { id } }, body })),
    onSuccess: () => {
      invalidate(caveId);
      invalidateHistory();
    },
  });
}

export function useDeleteEntrance(caveId: string) {
  const invalidate = useInvalidateCaves();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/cave-entrances/{id}', { params: { path: { id } } });
      if (error !== undefined) {
        throw new Error(`API error ${response.status}`);
      }
    },
    onSuccess: () => {
      invalidate(caveId);
      invalidateHistory();
    },
  });
}

// ---------------------------------------------------------------------------
// Resource links
// ---------------------------------------------------------------------------

export interface ResLinkPageParams {
  page?: number;
  pageSize?: number;
  /**
   * A relation code, narrowing the panel to links of that one relation — how a page asks
   * for a single role rather than reading every link and sorting them itself. Omitted or
   * blank means every relation, untyped links included; a code no relation type carries is
   * refused by the server rather than answered with an empty page.
   */
  relation?: string;
}

/**
 * Links incident to one entity — the per-entity panel query. `totalItems` is the badge
 * count and is legitimately 0 with an empty page: a link reachable only through a member
 * whose location is protected from this caller is withheld whole, so two callers can
 * honestly see different counts for the same entity. Never reconcile it against another
 * source.
 */
export function useResLinksForTarget(
  targetType: string,
  targetId: string,
  params: ResLinkPageParams = {},
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.resLinksForTarget(targetType, targetId, params),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/reslinks/for-target', {
          params: { query: { type: targetType, id: targetId, ...params } },
        }),
      ),
    enabled: enabled && Boolean(targetId),
    // Deliberately no placeholder from the previous key: this query is keyed by the entity,
    // and a section that showed the last entity's links under this one's heading would not
    // just be stale — the rows elide the entity whose page they are on, so the previous
    // entity's own chip would appear as a sibling of one it is not linked to.
  });
}

/** One link by id or by its 8-character short code — the endpoint tells them apart by shape. */
export function useResLink(idOrCode: string, enabled = true) {
  return useQuery({
    queryKey: queryKeys.resLink(idOrCode),
    queryFn: () => unwrap(api.GET('/api/v1/reslinks/{idOrCode}', { params: { path: { idOrCode } } })),
    enabled: enabled && Boolean(idOrCode),
    retry: false,
  });
}

/** The picker feed. A blank query is answered with an empty list without touching the database. */
export function useResLinkTargets(targetType: string, q: string, enabled = true) {
  return useQuery({
    queryKey: queryKeys.resLinkTargets(targetType, q),
    queryFn: () =>
      unwrap(api.GET('/api/v1/reslinks/targets/search', { params: { query: { type: targetType, q } } })),
    enabled: enabled && q.trim().length > 0,
  });
}

/**
 * The audience a new GPS point would take for this caller if the form names none. The rule
 * reads the caller's caving-group roster, which nothing else publishes, so the audience is
 * asked for rather than guessed: a guess about who can see a cave position is worse than
 * no notice at all.
 */
export function useResLinkPointDefault(enabled = true) {
  return useQuery({
    queryKey: queryKeys.resLinkPointDefault,
    queryFn: () => unwrap(api.GET('/api/v1/reslinks/point-default')),
    enabled,
    staleTime: 5 * 60_000,
  });
}

export function useResLinkRelationTypes(enabled = true) {
  return useQuery({
    queryKey: queryKeys.resLinkRelationTypes,
    queryFn: () => unwrap(api.GET('/api/v1/reslinks/relation-types')),
    enabled,
    staleTime: 5 * 60_000,
  });
}

/**
 * Every link read hangs off one prefix, and a write to one link can change what any
 * entity's panel shows, so writes invalidate the lot. Write responses are deliberately
 * not seeded into the cache: the server answers an author with the membership they just
 * asserted, uncut, while the next ordinary read may withhold part of it — priming from
 * the echo would show the author a link that does not exist for them on refresh.
 */
export function useInvalidateResLinks() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['reslinks'] });
}

export function useCreateResLink() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: (body: ResLinkCreate) => unwrap(api.POST('/api/v1/reslinks', { body })),
    onSuccess: invalidate,
  });
}

export function useUpdateResLink() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: ResLinkUpdate }) =>
      unwrap(api.PATCH('/api/v1/reslinks/{id}', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useDeleteResLink() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: (id: string) => unwrapVoid(api.DELETE('/api/v1/reslinks/{id}', { params: { path: { id } } })),
    onSuccess: invalidate,
  });
}

export function useAddResLinkMember() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: ResLinkMemberAdd }) =>
      unwrap(api.POST('/api/v1/reslinks/{id}/members', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useUpdateResLinkMember() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: ({ id, memberId, body }: { id: string; memberId: string; body: ResLinkMemberUpdate }) =>
      unwrap(
        api.PATCH('/api/v1/reslinks/{id}/members/{memberId}', {
          params: { path: { id, memberId } },
          body,
        }),
      ),
    onSuccess: invalidate,
  });
}

export function useDeleteResLinkMember() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: ({ id, memberId }: { id: string; memberId: string }) =>
      unwrapVoid(
        api.DELETE('/api/v1/reslinks/{id}/members/{memberId}', { params: { path: { id, memberId } } }),
      ),
    onSuccess: invalidate,
  });
}

/**
 * Adds a relation to the installation's vocabulary. The whole link prefix is invalidated
 * rather than only the vocabulary list, because every link row carries its relation inline
 * and would otherwise keep rendering the wording that was current when it was fetched.
 */
export function useCreateResLinkRelationType() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: (body: ResLinkRelationTypeWrite) =>
      unwrap(api.POST('/api/v1/reslinks/relation-types', { body })),
    onSuccess: invalidate,
  });
}

export function useUpdateResLinkRelationType() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: ({ id, body }: { id: number; body: ResLinkRelationTypeWrite }) =>
      unwrap(api.PATCH('/api/v1/reslinks/relation-types/{id}', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useDeleteResLinkRelationType() {
  const invalidate = useInvalidateResLinks();
  return useMutation({
    mutationFn: (id: number) =>
      unwrapVoid(api.DELETE('/api/v1/reslinks/relation-types/{id}', { params: { path: { id } } })),
    onSuccess: invalidate,
  });
}

// ---------------------------------------------------------------------------
// Staged vector import: the club's term rules, the review of a loaded file, and
// the confirmations it produced.
// ---------------------------------------------------------------------------

export type TermRuleSetInfo = components['schemas']['TermRuleSetDto'];
export type TermRuleSetDetail = components['schemas']['TermRuleSetDetailDto'];
export type TermRule = components['schemas']['TermRule'];
export type TermRuleDocument = components['schemas']['TermRuleDocument'];
export type TermRuleSetCreate = components['schemas']['TermRuleSetCreateRequest'];
export type TermRuleSetWrite = components['schemas']['TermRuleSetWriteRequest'];
export type TermRuleSetScopeWrite = components['schemas']['TermRuleSetScopeRequest'];
export type TermRuleSetImport = components['schemas']['TermRuleSetImportRequest'];
export type TermRuleScope = components['schemas']['TermRuleScope'];
export type TermMatchMode = components['schemas']['TermMatchMode'];
export type TermStripMode = components['schemas']['TermStripMode'];
export type ImportTargetKind = components['schemas']['ImportTargetKind'];
export type ImportOptions = components['schemas']['ImportOptions'];
export type ImportDecision = components['schemas']['ImportDecision'];
export type ImportCandidate = components['schemas']['ImportCandidateDto'];
export type ImportPreview = components['schemas']['ImportPreviewDto'];
export type ImportPreviewRequest = components['schemas']['ImportPreviewRequest'];
export type ImportSession = components['schemas']['ImportSessionDto'];
export type ImportCommitResult = components['schemas']['ImportCommitResultDto'];
export type ImportBatch = components['schemas']['ImportBatchDto'];
export type ImportFailure = components['schemas']['ImportFailureDto'];
export type ImportBatchDetail = components['schemas']['ImportBatchDetailDto'];
export type ImportProvenance = components['schemas']['ImportProvenanceDto'];
export type GeofileSourceOptions = components['schemas']['GeofileSourceOptions'];

export function useTermRuleSets() {
  return useQuery({
    queryKey: queryKeys.termRuleSets,
    queryFn: () => unwrap(api.GET('/api/v1/term-rule-sets')),
  });
}

/** The set an import starts from when the reviewer names none. */
export function useEffectiveTermRuleSet() {
  return useQuery({
    queryKey: queryKeys.effectiveTermRuleSet,
    queryFn: () => unwrap(api.GET('/api/v1/term-rule-sets/effective')),
  });
}

export function useTermRuleSet(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.termRuleSet(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/term-rule-sets/{id}', { params: { path: { id: id! } } })),
    enabled: Boolean(id),
  });
}

/**
 * The whole prefix, not one row: which set applies to somebody is derived from all of them,
 * so promoting one changes what another account's next import starts from.
 */
function useInvalidateTermRuleSets() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['term-rule-sets'] });
}

export function useCreateTermRuleSet() {
  const invalidate = useInvalidateTermRuleSets();
  return useMutation({
    mutationFn: (body: TermRuleSetCreate) => unwrap(api.POST('/api/v1/term-rule-sets', { body })),
    onSuccess: invalidate,
  });
}

export function useUpdateTermRuleSet() {
  const invalidate = useInvalidateTermRuleSets();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: TermRuleSetWrite }) =>
      unwrap(api.PUT('/api/v1/term-rule-sets/{id}', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useDeleteTermRuleSet() {
  const invalidate = useInvalidateTermRuleSets();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/term-rule-sets/{id}', { params: { path: { id } } })),
    onSuccess: invalidate,
  });
}

export function useSetTermRuleSetScope() {
  const invalidate = useInvalidateTermRuleSets();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: TermRuleSetScopeWrite }) =>
      unwrap(api.POST('/api/v1/term-rule-sets/{id}/scope', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useImportTermRuleSet() {
  const invalidate = useInvalidateTermRuleSets();
  return useMutation({
    mutationFn: (body: TermRuleSetImport) =>
      unwrap(api.POST('/api/v1/term-rule-sets/import', { body })),
    onSuccess: invalidate,
  });
}

export function useImportSession(geofileId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.importSession(geofileId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/geofiles/{geofileId}/import/session', {
          params: { path: { geofileId: geofileId! } },
        }),
      ),
    enabled: Boolean(geofileId),
  });
}

/**
 * Saves the review as the reviewer works. Deliberately does not invalidate the session
 * query: the browser already holds what it just sent, and refetching would make every tick
 * of the table fight the answer coming back.
 */
export function useSaveImportSession() {
  return useMutation({
    mutationFn: ({
      geofileId,
      body,
    }: {
      geofileId: string;
      body: { options: ImportOptions; decisions: Record<string, ImportDecision> };
    }) =>
      unwrap(
        api.PUT('/api/v1/geofiles/{geofileId}/import/session', {
          params: { path: { geofileId } },
          body,
        }),
      ),
  });
}

/** The dry run. A POST because the options are a body, but it creates nothing. */
export function useImportPreview(
  geofileId: string | undefined,
  body: ImportPreviewRequest,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.importPreview(geofileId ?? '', body),
    queryFn: () =>
      unwrap(
        api.POST('/api/v1/geofiles/{geofileId}/import/preview', {
          params: { path: { geofileId: geofileId! } },
          body,
        }),
      ),
    enabled: Boolean(geofileId) && enabled,
    placeholderData: keepPreviousData,
  });
}

export function useCommitImport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      geofileId,
      body,
    }: {
      geofileId: string;
      body: {
        options: ImportOptions;
        selection: number[];
        decisions: Record<string, ImportDecision>;
        withoutReview: boolean;
      };
    }) =>
      unwrap(
        api.POST('/api/v1/geofiles/{geofileId}/import/commit', {
          params: { path: { geofileId } },
          body,
        }),
      ),
    onSuccess: () => {
      // Only the review is spent here. The caves, entrances and features do not exist yet —
      // the confirmation was queued — so invalidating the registry now would refetch it early
      // and show the reader an unchanged map as though nothing had been created.
      // `useProcessingJob` invalidates the rest when the job finishes.
      void queryClient.invalidateQueries({ queryKey: ['import-session'] });
    },
  });
}

export type ProcessingJob = components['schemas']['ProcessingJobDto'];

/** Whether a job is still going, which is the only thing worth asking again about. */
export function jobUnsettled(status: ProcessingJob['status'] | undefined): boolean {
  return status === 'queued' || status === 'running';
}

/**
 * One job, polled while it is unfinished.
 *
 * A caller may always read a job they asked for, so this needs no right of its own — the server
 * answers a job belonging to somebody else exactly as it answers one that never existed.
 */
export function useProcessingJob(jobId: number | undefined) {
  const queryClient = useQueryClient();
  return useQuery({
    queryKey: queryKeys.processingJob(jobId ?? 0),
    queryFn: async () => {
      const job = await unwrap(
        api.GET('/api/v1/jobs/{id}', { params: { path: { id: jobId! } } }),
      );
      if (!jobUnsettled(job.status)) {
        // The moment the work is actually done — not when it was asked for. Everything a
        // confirmation creates lands at once, so the surfaces that show it go stale together.
        void queryClient.invalidateQueries({ queryKey: ['features'] });
        void queryClient.invalidateQueries({ queryKey: ['caves'] });
        void queryClient.invalidateQueries({ queryKey: ['import-batches'] });
      }
      return job;
    },
    enabled: jobId !== undefined,
    // Stopped by the answer rather than by a timer: a settled job is asked about no more.
    refetchInterval: (query) => (jobUnsettled(query.state.data?.status) ? 1500 : false),
    retry: false,
  });
}

// ---------------------------------------------------------------------------
// A club's trip spreadsheet into trips
// ---------------------------------------------------------------------------

export type TripImportOptions = components['schemas']['TripImportOptions'];
export type TripImportDecision = components['schemas']['TripImportDecision'];
export type TripImportRowAction = components['schemas']['TripImportRowAction'];
export type TripImportSession = components['schemas']['TripImportSessionDto'];
export type TripImportColumns = components['schemas']['TripImportColumnsDto'];
export type TripImportPreview = components['schemas']['TripImportPreviewDto'];
export type TripImportPreviewRequest = components['schemas']['TripImportPreviewRequest'];
export type TripImportRow = components['schemas']['TripImportRowDto'];
export type TripImportProblem = components['schemas']['TripImportProblemDto'];
export type TripImportProposals = components['schemas']['TripImportProposalsDto'];
export type TripImportPersonMatch = components['schemas']['TripImportPersonMatch'];
export type TripImportFeatureMatch = components['schemas']['TripImportFeatureMatch'];
export type TripImportTermMatch = components['schemas']['TripImportTermMatch'];
export type TripImportCommitResult = components['schemas']['TripImportCommitResultDto'];
export type TripCsvField = components['schemas']['TripCsvField'];
export type TripCsvDateOrder = components['schemas']['TripCsvDateOrder'];
export type TripCsvDateOrderSource = components['schemas']['TripCsvDateOrderSource'];
export type TripCsvDiagnosticCode = components['schemas']['TripCsvDiagnosticCode'];

/**
 * The saved review of one uploaded sheet. Answers with defaults rather than a 404 when
 * nobody has reviewed this file yet, so the screen has something to open with.
 */
export function useTripImportSession(fileId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.tripImportSession(fileId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/trip-imports/{fileId}/session', { params: { path: { fileId: fileId! } } }),
      ),
    enabled: Boolean(fileId),
  });
}

/**
 * Saves the review as the reviewer works. Deliberately does not invalidate the session
 * query: the browser already holds what it just sent, and refetching would make every tick
 * of the table fight the answer coming back.
 */
export function useSaveTripImportSession() {
  return useMutation({
    mutationFn: ({
      fileId,
      body,
    }: {
      fileId: string;
      body: { options: TripImportOptions; decisions: Record<string, TripImportDecision> };
    }) =>
      unwrap(
        api.PUT('/api/v1/trip-imports/{fileId}/session', { params: { path: { fileId } }, body }),
      ),
  });
}

/**
 * The sheet's own header line, for the mapping controls. Read from the stored file rather
 * than from a parse, so it still answers when the parse itself failed — which is exactly
 * when somebody needs to re-point a column.
 */
export function useTripImportColumns(fileId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.tripImportColumns(fileId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/trip-imports/{fileId}/columns', { params: { path: { fileId: fileId! } } }),
      ),
    enabled: Boolean(fileId),
    // A refusal here is the server's settled answer about this file, and asking again three
    // times only holds the screen in its loading state through the whole backoff.
    retry: false,
  });
}

/** The dry run. A POST because the options are a body, but it creates nothing. */
export function useTripImportPreview(
  fileId: string | undefined,
  body: TripImportPreviewRequest,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.tripImportPreview(fileId ?? '', body),
    queryFn: () =>
      unwrap(
        api.POST('/api/v1/trip-imports/{fileId}/preview', {
          params: { path: { fileId: fileId! } },
          body,
        }),
      ),
    enabled: Boolean(fileId) && enabled,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/**
 * Confirms the review. The options travel with it rather than being read back from the saved
 * row: a second tab left open on different choices must not decide what a thousand trips
 * become.
 */
export function useCommitTripImport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      fileId,
      body,
    }: {
      fileId: string;
      body: {
        options: TripImportOptions;
        lines: number[];
        decisions: Record<string, TripImportDecision>;
      };
    }) =>
      unwrap(
        api.POST('/api/v1/trip-imports/{fileId}/commit', { params: { path: { fileId } }, body }),
      ),
    onSuccess: () => {
      // A confirmation writes trips, may add area and cave features, records a batch that can
      // be undone, and spends the review that produced it — five surfaces go stale at once.
      void queryClient.invalidateQueries({ queryKey: ['trip-logs'] });
      void queryClient.invalidateQueries({ queryKey: ['features'] });
      void queryClient.invalidateQueries({ queryKey: ['caves'] });
      void queryClient.invalidateQueries({ queryKey: ['import-batches'] });
      void queryClient.invalidateQueries({ queryKey: ['trip-import-session'] });
    },
  });
}

// ---------------------------------------------------------------------------
// Photographs into the registry
// ---------------------------------------------------------------------------

export type PhotoImportOptions = components['schemas']['PhotoImportOptions'];
export type PhotoDecision = components['schemas']['PhotoDecision'];
export type PhotoCandidate = components['schemas']['PhotoCandidateDto'];
export type PhotoCandidateMember = components['schemas']['PhotoCandidateMemberDto'];
export type PhotoNearby = components['schemas']['PhotoNearbyDto'];
export type PhotoPreview = components['schemas']['PhotoPreviewDto'];
export type PhotoPreviewRequest = components['schemas']['PhotoPreviewRequest'];
export type PhotoSession = components['schemas']['PhotoSessionDto'];
export type PhotoTrackOption = components['schemas']['PhotoTrackOptionDto'];
export type PhotoPosition = components['schemas']['PhotoPositionDto'];
export type PhotoPositionSource = components['schemas']['PhotoPositionSource'];
export type PhotoExif = components['schemas']['PhotoExif'];
export type PositionConfidenceBand = components['schemas']['PositionConfidenceBand'];

export function usePhotoImportSession() {
  return useQuery({
    queryKey: queryKeys.photoImportSession,
    queryFn: () => unwrap(api.GET('/api/v1/photo-import/session')),
  });
}

/**
 * Saves the review as the reviewer works. Deliberately does not invalidate the session query,
 * for the same reason the vector one does not: the browser already holds what it just sent.
 */
export function useSavePhotoImportSession() {
  return useMutation({
    mutationFn: (body: {
      fileIds: string[];
      options: PhotoImportOptions;
      decisions: Record<string, PhotoDecision>;
    }) => unwrap(api.PUT('/api/v1/photo-import/session', { body })),
  });
}

/** Groups the drop into places. A POST because the options are a body, but it creates nothing. */
export function usePhotoImportPreview(body: PhotoPreviewRequest, enabled = true) {
  return useQuery({
    queryKey: queryKeys.photoImportPreview(body),
    queryFn: () => unwrap(api.POST('/api/v1/photo-import/preview', { body })),
    enabled: enabled && body.fileIds.length > 0,
    placeholderData: keepPreviousData,
  });
}

export function useCommitPhotoImport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: {
      options: PhotoImportOptions;
      fileIds: string[];
      selection: string[];
      decisions: Record<string, PhotoDecision>;
    }) => unwrap(api.POST('/api/v1/photo-import/commit', { body })),
    onSuccess: () => {
      // A confirmation creates objects, hangs pictures on them and spends the review that
      // produced them, so every surface built from any of those goes stale at once.
      void queryClient.invalidateQueries({ queryKey: ['features'] });
      void queryClient.invalidateQueries({ queryKey: ['caves'] });
      void queryClient.invalidateQueries({ queryKey: ['attachments'] });
      void queryClient.invalidateQueries({ queryKey: ['import-batches'] });
      void queryClient.invalidateQueries({ queryKey: ['photo-import-session'] });
    },
  });
}

/** Uploaded tracks a picture with no fix of its own can be placed against. */
export function usePhotoImportTracks(enabled = true) {
  return useQuery({
    queryKey: queryKeys.photoImportTracks,
    queryFn: () => unwrap(api.GET('/api/v1/photo-import/tracks')),
    enabled,
  });
}

/**
 * Gives a picture a position by hand, or forgets one. `replaceRecordedFix` has to be true when
 * the camera recorded a fix — the server refuses otherwise, so that a drag cannot quietly
 * replace a measurement.
 */
export function useSetPhotoPosition() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      id,
      position,
      replaceRecordedFix,
    }: {
      id: string;
      position: number[] | null;
      replaceRecordedFix: boolean;
    }) =>
      unwrap(
        api.PUT('/api/v1/files/{id}/position', {
          params: { path: { id } },
          body: { position, replaceRecordedFix },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['attachments'] });
      void queryClient.invalidateQueries({ queryKey: ['photo-import-preview'] });
    },
  });
}

/**
 * Moves an object to where one of its pictures was taken. An ordinary geometry write: it needs
 * write access to the object and lands in that object's own history.
 */
export function useFeaturePositionFromPhoto() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ featureId, fileId }: { featureId: string; fileId: string }) =>
      unwrap(
        api.POST('/api/v1/features/{id}/position-from-photo', {
          params: { path: { id: featureId } },
          body: { fileId },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['features'] });
      void queryClient.invalidateQueries({ queryKey: ['caves'] });
      void queryClient.invalidateQueries({ queryKey: ['history'] });
    },
  });
}

export interface ImportBatchListParams {
  geofileId?: string;
  page?: number;
  pageSize?: number;
}

export function useImportBatches(params: ImportBatchListParams) {
  return useQuery({
    queryKey: queryKeys.importBatches(params),
    queryFn: () => unwrap(api.GET('/api/v1/import-batches', { params: { query: params } })),
    placeholderData: keepPreviousData,
  });
}

export function useImportBatch(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.importBatch(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/import-batches/{id}', { params: { path: { id: id! } } })),
    enabled: Boolean(id),
  });
}

export function useRevertImportBatch() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/v1/import-batches/{id}/revert', { params: { path: { id } } })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['import-batches'] });
      void queryClient.invalidateQueries({ queryKey: ['features'] });
      void queryClient.invalidateQueries({ queryKey: ['caves'] });
    },
  });
}

/**
 * Where an object came from. Absent for anything nobody imported, which is most of them —
 * hence no retry: a 404 here is the normal answer, not a failure worth asking again about.
 */
export function useImportProvenance(featureId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.importProvenance(featureId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/features/{featureId}/import-provenance', {
          params: { path: { featureId: featureId! } },
        }),
      ),
    enabled: Boolean(featureId) && enabled,
    retry: false,
  });
}

// ---------------------------------------------------------------------------
// The Romanian community cave catalogue (speologie.org)
// ---------------------------------------------------------------------------

export type SpeologieStatus = components['schemas']['SpeologieStatusDto'];
export type SpeologieCave = components['schemas']['SpeologieCaveDto'];
export type SpeologieSearchResult = components['schemas']['SpeologieSearchDto'];
export type SpeologieDecision = components['schemas']['SpeologieDecisionDto'];
export type SpeologieAction = components['schemas']['SpeologieAction'];
export type SpeologieImportResult = components['schemas']['SpeologieImportResultDto'];
export type SpeologieBasin = components['schemas']['SpeologieBasinDto'];

export type SpeologieSearchParams = {
  q?: string;
  county?: string;
  basin?: number;
  page?: number;
  pageSize?: number;
};

/**
 * The catalogue's hydrographic basin tree, which its own programmatic interface does not publish —
 * this installation carries a copy so a cave's basin number can be read as a place.
 *
 * Held for the session: it is a table shipped with the application rather than an answer about
 * anything, so re-fetching it on every visit to the screen would be asking the server to repeat
 * itself six hundred times over.
 */
export function useSpeologieBasins() {
  return useQuery({
    queryKey: queryKeys.speologieBasins,
    queryFn: () => unwrap(api.GET('/api/v1/catalogue/speologie/basins')),
    staleTime: Infinity,
    gcTime: Infinity,
  });
}

/**
 * Whether this installation has been given a key for the catalogue at all. Reaches nothing, so
 * it is safe to ask on every visit; the screens use it to say "your administrator has not set
 * this up" rather than to fail on the first search.
 */
export function useSpeologieStatus() {
  return useQuery({
    queryKey: queryKeys.speologieStatus,
    queryFn: () => unwrap(api.GET('/api/v1/catalogue/speologie/status')),
    staleTime: 300_000,
  });
}

/**
 * Searches the catalogue. Deliberately not fired until there is something to search for: the
 * server refuses a search that names neither a term nor a county, and asking anyway would cost
 * a round trip to be told so.
 */
export function useSpeologieSearch(params: SpeologieSearchParams, enabled = true) {
  const hasQuestion = Boolean(params.q?.trim()) || Boolean(params.county?.trim());
  return useQuery({
    queryKey: queryKeys.speologieSearch(params),
    queryFn: () => unwrap(api.GET('/api/v1/catalogue/speologie/caves', { params: { query: params } })),
    enabled: enabled && hasQuestion,
    placeholderData: keepPreviousData,
    // The far end is a volunteer-run service the server already throttles itself against.
    // Repeating a search it has just answered helps nobody.
    staleTime: 300_000,
    retry: false,
  });
}

/**
 * One catalogue cave in full, including the description already converted to the plain text an
 * import would store — the same conversion, so what is previewed is what would be kept.
 */
export function useSpeologieCave(id: number | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.speologieCave(id ?? 0),
    queryFn: () =>
      unwrap(api.GET('/api/v1/catalogue/speologie/caves/{id}', { params: { path: { id: id! } } })),
    enabled: Boolean(id) && enabled,
    staleTime: 300_000,
    retry: false,
  });
}

/**
 * Several catalogue caves in full, one query each.
 *
 * One query per cave rather than one for the lot, because each answer carries a description and a
 * description in this catalogue can run to a megabyte — a batched answer would be one enormous
 * payload that is either wholly there or wholly missing. Separate queries also mean a cave the
 * catalogue has withdrawn fails on its own instead of taking the other nine with it. The server
 * throttles itself against the far end, so these queue rather than arriving together.
 */
export function useSpeologieCaves(ids: readonly number[], enabled = true) {
  return useQueries({
    queries: ids.map((id) => ({
      queryKey: queryKeys.speologieCave(id),
      queryFn: () =>
        unwrap(api.GET('/api/v1/catalogue/speologie/caves/{id}', { params: { path: { id } } })),
      enabled,
      staleTime: 300_000,
      retry: false,
    })),
  });
}

export function useImportFromSpeologie() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: {
      selection: number[];
      decisions: Record<string, SpeologieDecision>;
      visibility: Visibility;
      cavingGroupId: string | null;
      locationProtected: boolean;
      parentId: string | null;
    }) => unwrap(api.POST('/api/v1/catalogue/speologie/import', { body })),
    onSuccess: () => {
      // Caves and features appear, a batch appears in the undo history, and every catalogue
      // search now has a different answer to "is this one already here".
      void queryClient.invalidateQueries({ queryKey: ['features'] });
      void queryClient.invalidateQueries({ queryKey: ['caves'] });
      void queryClient.invalidateQueries({ queryKey: ['import-batches'] });
      void queryClient.invalidateQueries({ queryKey: ['speologie'] });
    },
  });
}

export function useGeofile(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.geofile(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/geofiles/{id}', { params: { path: { id: id! } } })),
    enabled: Boolean(id),
  });
}

/**
 * The header of a delimited upload, so a wrong coordinate-column guess can be corrected.
 * Only a delimited upload has one — callers must not ask about anything else, because the
 * refusal is a browser console error on every review of a GPX.
 */
export function useGeofileColumns(geofileId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.geofileColumns(geofileId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/geofiles/{id}/columns', { params: { path: { id: geofileId! } } })),
    enabled: Boolean(geofileId) && enabled,
    retry: false,
  });
}

export function useReimportGeofile() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, sourceOptions }: { id: string; sourceOptions?: GeofileSourceOptions }) =>
      unwrap(
        api.POST('/api/v1/geofiles/{id}/reimport', {
          params: { path: { id } },
          body: { sourceOptions: sourceOptions ?? null },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['geofiles'] });
      void queryClient.invalidateQueries({ queryKey: ['import-preview'] });
      void queryClient.invalidateQueries({ queryKey: ['geofile-columns'] });
    },
  });
}

export interface UnfiledDocumentParams {
  uploadBatchId?: string;
  page?: number;
  pageSize?: number;
}

/**
 * The inbox: documents filed nowhere.
 *
 * "Unfiled" is a query rather than a shelf — a document with no cabinet row — which is what
 * keeps filing a pure addition and leaving the inbox automatic. Nothing here is a way to see
 * anything: it is filtered by the same read rule as every other document listing, and an
 * unfiled document starts private because it has no cabinet for a rule to reach it through.
 */
export function useUnfiledDocuments(params: UnfiledDocumentParams = {}, enabled = true) {
  return useQuery({
    queryKey: queryKeys.unfiledDocuments(params),
    queryFn: () => unwrap(api.GET('/api/v1/documents/unfiled', { params: { query: params } })),
    enabled,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/**
 * Files and unfiles many documents at once.
 *
 * The answer is a partial result rather than all-or-nothing, and the caller has to read it: a
 * selection of two hundred documents will, on a real archive, contain one somebody else owns.
 */
export function useBulkFiling() {
  const invalidate = useInvalidateCabinets();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: {
      documentIds: string[];
      fileIntoCabinetIds?: string[];
      unfileFromCabinetIds?: string[];
    }) =>
      unwrap(api.POST('/api/v1/cabinets/filing', {
        // Both lists are always sent: the contract distinguishes "no shelves named" from
        // "this field was not mentioned", and only the first of those is a thing to ask for.
        body: {
          documentIds: request.documentIds,
          fileIntoCabinetIds: request.fileIntoCabinetIds ?? [],
          unfileFromCabinetIds: request.unfileFromCabinetIds ?? [],
        },
      })),
    onSuccess: () => {
      invalidate();
      void queryClient.invalidateQueries({ queryKey: ['documents'] });
    },
  });
}

/** The caller's own drops, newest first. */
export function useUploadBatches(page = 1, pageSize = 20, enabled = true) {
  return useQuery({
    queryKey: queryKeys.uploadBatches(page, pageSize),
    queryFn: () =>
      unwrap(api.GET('/api/v1/upload-batches', { params: { query: { page, pageSize } } })),
    enabled,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/**
 * One drop and its counts.
 *
 * Polled while the work is still running, because an archive expansion and a directory import
 * happen in the background and the page has nothing else to learn from.
 */
export function useUploadBatch(id: string | undefined, poll = false) {
  return useQuery({
    queryKey: queryKeys.uploadBatch(id ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/upload-batches/{id}', { params: { path: { id: id! } } })),
    enabled: Boolean(id),
    refetchInterval: poll ? 2000 : false,
    retry: false,
  });
}

export interface UploadBatchItemParams {
  outcome?: 'pending' | 'stored' | 'skipped' | 'failed';
  page?: number;
  pageSize?: number;
}

/** The per-file report of one drop. */
export function useUploadBatchItems(
  id: string | undefined,
  params: UploadBatchItemParams = {},
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.uploadBatchItems(id ?? '', params),
    queryFn: () =>
      unwrap(api.GET('/api/v1/upload-batches/{id}/items', {
        params: { path: { id: id! }, query: params },
      })),
    enabled: Boolean(id) && enabled,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/** Opens a drop, so everything uploaded into it can be found together afterwards. */
export function useOpenUploadBatch() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: { label?: string | null; cabinetId?: string | null; tagName?: string | null }) =>
      unwrap(api.POST('/api/v1/upload-batches', {
        body: {
          label: request.label ?? null,
          cabinetId: request.cabinetId ?? null,
          tagName: request.tagName ?? null,
        },
      })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['upload-batches'] }),
  });
}

/** Closes a drop; nothing more can be counted into it. */
export function useCloseUploadBatch() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/v1/upload-batches/{id}/close', { params: { path: { id } } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['upload-batches'] }),
  });
}

/**
 * The directories this installation may import from.
 *
 * Empty means the operator has not switched the feature on — which the page states rather than
 * offering a field that refuses everything.
 */
export function useImportRoots(enabled = true) {
  return useQuery({
    queryKey: queryKeys.importRoots,
    queryFn: () => unwrap(api.GET('/api/v1/upload-batches/import-roots')),
    enabled,
    retry: false,
    staleTime: 60 * 60_000,
  });
}

/** Starts a background import from a directory the server itself can reach. */
export function useImportDirectory() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: {
      path: string;
      label?: string | null;
      cabinetId?: string | null;
      tagName?: string | null;
    }) =>
      unwrap(api.POST('/api/v1/upload-batches/import-directory', {
        body: {
          path: request.path,
          label: request.label ?? null,
          cabinetId: request.cabinetId ?? null,
          tagName: request.tagName ?? null,
        },
      })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['upload-batches'] });
      void queryClient.invalidateQueries({ queryKey: ['cabinets'] });
    },
  });
}

/** What the gallery can be narrowed by. Every field is optional and they combine. */
export interface PhotoQueryParams {
  caveId?: string;
  featureId?: string;
  tripLogId?: string;
  caverId?: string;
  tagId?: number;
  albumId?: string;
  uploadBatchId?: string;
  camera?: string;
  /** The map extent, as west,south,east,north. */
  bbox?: string;
  unplaced?: boolean;
  from?: string;
  to?: string;
  search?: string;
  page?: number;
  pageSize?: number;
}

/**
 * The gallery.
 *
 * Page-sized queries rather than one growing list: a club archive runs to tens of thousands of
 * photographs, and the grid is what decides how many are on screen.
 */
export function usePhotos(params: PhotoQueryParams = {}, enabled = true) {
  return useQuery({
    queryKey: queryKeys.photos(params),
    queryFn: () => unwrap(api.GET('/api/v1/photos', { params: { query: params } })),
    enabled,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

export function usePhoto(documentId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.photo(documentId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/photos/{documentId}', { params: { path: { documentId: documentId! } } })),
    enabled: Boolean(documentId),
    retry: false,
  });
}

/** Everything a photograph, an album or an attachment listing might now show differently. */
function useInvalidatePhotos() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: ['photos'] });
    void queryClient.invalidateQueries({ queryKey: ['albums'] });
    void queryClient.invalidateQueries({ queryKey: ['attachments'] });
    void queryClient.invalidateQueries({ queryKey: ['documents'] });
  };
}

export function useUpdatePhotoCredit() {
  const invalidate = useInvalidatePhotos();
  return useMutation({
    mutationFn: ({ documentId, ...body }: {
      documentId: string;
      photographerCaverId?: string | null;
      photographerName?: string | null;
      caption?: string | null;
      licenceCode?: string | null;
      placeName?: string | null;
    }) =>
      unwrap(api.PUT('/api/v1/photos/{documentId}/credit', {
        params: { path: { documentId } },
        body: {
          photographerCaverId: body.photographerCaverId ?? null,
          photographerName: body.photographerName ?? null,
          caption: body.caption ?? null,
          licenceCode: body.licenceCode ?? null,
          placeName: body.placeName ?? null,
        },
      })),
    onSuccess: () => invalidate(),
  });
}

/**
 * One operation over a selection.
 *
 * The answer is a partial result and callers have to read it: a selection of two hundred
 * photographs will, on a real archive, contain one somebody else owns.
 */
export function usePhotoBulk() {
  const invalidate = useInvalidatePhotos();
  return useMutation({
    mutationFn: (body: {
      documentIds: string[];
      addTagIds?: number[];
      removeTagIds?: number[];
      visibility?: string;
      rotateQuarterTurns?: number;
      attachEntityType?: string;
      attachEntityId?: string;
      addToAlbumId?: string;
      delete?: boolean;
    }) => unwrap(api.POST('/api/v1/photos/bulk', { body: body as never })),
    onSuccess: () => invalidate(),
  });
}

export function usePhotoDuplicates(enabled = true) {
  return useQuery({
    queryKey: queryKeys.photoDuplicates,
    queryFn: () => unwrap(api.GET('/api/v1/photos/duplicates')),
    enabled,
    retry: false,
  });
}

export function useDeletedPhotos(page = 1, enabled = true) {
  return useQuery({
    queryKey: queryKeys.deletedPhotos(page),
    queryFn: () =>
      unwrap(api.GET('/api/v1/photos/deleted', { params: { query: { page, pageSize: 50 } } })),
    enabled,
    retry: false,
  });
}

export function useRestorePhoto() {
  const invalidate = useInvalidatePhotos();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (documentId: string) =>
      unwrapVoid(api.POST('/api/v1/photos/{documentId}/restore', {
        params: { path: { documentId } },
      })),
    onSuccess: () => {
      invalidate();
      void queryClient.invalidateQueries({ queryKey: ['photos', 'deleted'] });
    },
  });
}

/** Publishing to the internet — a full administrator's decision, and not the read audience. */
export function useSetPhotoPublic() {
  const invalidate = useInvalidatePhotos();
  return useMutation({
    mutationFn: ({ documentId, published }: { documentId: string; published: boolean }) =>
      unwrapVoid(api.PUT('/api/v1/photos/{documentId}/public', {
        params: { path: { documentId }, query: { published } },
      })),
    onSuccess: () => invalidate(),
  });
}

export interface AlbumListParams {
  subjectEntityId?: string;
  page?: number;
  pageSize?: number;
}

export function useAlbums(params: AlbumListParams = {}, enabled = true) {
  return useQuery({
    queryKey: queryKeys.albums(params),
    queryFn: () => unwrap(api.GET('/api/v1/albums', { params: { query: params } })),
    enabled,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

export function useAlbum(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.album(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/albums/{id}', { params: { path: { id: id! } } })),
    enabled: Boolean(id),
    retry: false,
  });
}

export interface AlbumWrite {
  title: string;
  description?: string | null;
  visibility: string;
  cavingGroupId?: string | null;
  subjectEntityType?: string | null;
  subjectEntityId?: string | null;
}

function albumBody(body: AlbumWrite) {
  return {
    title: body.title,
    description: body.description ?? null,
    visibility: body.visibility,
    cavingGroupId: body.cavingGroupId ?? null,
    subjectEntityType: body.subjectEntityType ?? null,
    subjectEntityId: body.subjectEntityId ?? null,
  } as never;
}

function useInvalidateAlbums() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['albums'] });
}

export function useCreateAlbum() {
  const invalidate = useInvalidateAlbums();
  return useMutation({
    mutationFn: (body: AlbumWrite) => unwrap(api.POST('/api/v1/albums', { body: albumBody(body) })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateAlbum() {
  const invalidate = useInvalidateAlbums();
  return useMutation({
    mutationFn: ({ id, ...body }: AlbumWrite & { id: string }) =>
      unwrap(api.PUT('/api/v1/albums/{id}', { params: { path: { id } }, body: albumBody(body) })),
    onSuccess: () => invalidate(),
  });
}

export function useDeleteAlbum() {
  const invalidate = useInvalidateAlbums();
  return useMutation({
    mutationFn: (id: string) => unwrapVoid(api.DELETE('/api/v1/albums/{id}', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

export function useAddAlbumItems() {
  const invalidate = useInvalidatePhotos();
  return useMutation({
    mutationFn: ({ id, documentIds }: { id: string; documentIds: string[] }) =>
      unwrapVoid(api.POST('/api/v1/albums/{id}/items', {
        params: { path: { id } },
        body: documentIds as never,
      })),
    onSuccess: () => invalidate(),
  });
}

export function useRemoveAlbumItem() {
  const invalidate = useInvalidatePhotos();
  return useMutation({
    mutationFn: ({ id, documentId }: { id: string; documentId: string }) =>
      unwrapVoid(api.DELETE('/api/v1/albums/{id}/items/{documentId}', {
        params: { path: { id, documentId } },
      })),
    onSuccess: () => invalidate(),
  });
}

/** Moves a picture to sit after another, or to the start when nothing is named. */
export function useReorderAlbum() {
  const invalidate = useInvalidatePhotos();
  return useMutation({
    mutationFn: ({ id, documentId, afterDocumentId }: {
      id: string;
      documentId: string;
      afterDocumentId?: string | null;
    }) =>
      unwrapVoid(api.POST('/api/v1/albums/{id}/reorder', {
        params: { path: { id } },
        body: { documentId, afterDocumentId: afterDocumentId ?? null },
      })),
    onSuccess: () => invalidate(),
  });
}

export function useSetAlbumCover() {
  const invalidate = useInvalidateAlbums();
  return useMutation({
    mutationFn: ({ id, documentId }: { id: string; documentId: string | null }) =>
      unwrapVoid(api.PUT('/api/v1/albums/{id}/cover', {
        params: { path: { id }, query: documentId ? { documentId } : {} },
      })),
    onSuccess: () => invalidate(),
  });
}

/** Mints a share link. The token comes back once and is never recoverable afterwards. */
export function useShareAlbum() {
  const invalidate = useInvalidateAlbums();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/v1/albums/{id}/share', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

export function useRevokeAlbumShare() {
  const invalidate = useInvalidateAlbums();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/albums/{id}/share', { params: { path: { id } } })),
    onSuccess: () => invalidate(),
  });
}

/** The object's headline picture — the one a list, a card or a popup shows. */
export function useSetPrimaryAttachment() {
  const invalidate = useInvalidatePhotos();
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, primary }: { id: string; primary: boolean }) =>
      unwrapVoid(api.PUT('/api/v1/attachments/{id}/primary', {
        params: { path: { id }, query: { primary } },
      })),
    onSuccess: () => {
      invalidate();
      // The cave summary carries the headline picture, so the card and the panel beside the map
      // both read a stale one until this is dropped.
      void queryClient.invalidateQueries({ queryKey: ['caves'] });
    },
  });
}

/** The installation's curated public gallery. Reachable without signing in. */
export function usePublicPhotos(page = 1) {
  return useQuery({
    queryKey: queryKeys.publicPhotos(page),
    queryFn: () =>
      unwrap(api.GET('/api/v1/public/photos', { params: { query: { page, pageSize: 60 } } })),
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/** One album, opened by its share link. Reachable without signing in. */
export function useSharedAlbum(token: string | undefined) {
  return useQuery({
    queryKey: queryKeys.sharedAlbum(token ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/public/albums/{token}', { params: { path: { token: token! } } })),
    enabled: Boolean(token),
    retry: false,
  });
}

// ---------------------------------------------------------------------------
// The one filter route, and the two that describe it
// ---------------------------------------------------------------------------

export type FilterQueryBody = components['schemas']['FilterQueryRequest'];
export type FilterQueryResult = components['schemas']['FilterQueryResponse'];
export type FilterVocabularyResult = components['schemas']['FilterVocabularyResponse'];
export type FilterHitDto = components['schemas']['FilterHitDto'];

/**
 * What each kind of object can be asked, for this caller.
 *
 * Cached hard. It changes when an administrator edits the taxonomy, not while somebody is typing,
 * and every control that draws a filter reads it — so refetching per mount would put a request
 * behind every dropdown on the page.
 */
export function useFilterVocabulary() {
  return useQuery({
    queryKey: queryKeys.filterVocabulary,
    queryFn: () => unwrap(api.GET('/api/v1/filters/vocabulary')),
    staleTime: 10 * 60_000,
  });
}

/**
 * Runs a filter.
 *
 * `keepPreviousData` is what makes a type-ahead readable: without it the list empties on every
 * keystroke and the rows jump, which reads as the control losing what it had found.
 */
export function useFilterQuery(body: FilterQueryBody, enabled = true) {
  return useQuery({
    queryKey: queryKeys.filterQuery(body),
    queryFn: () => unwrap(api.POST('/api/v1/filters/query', { body })),
    enabled,
    placeholderData: keepPreviousData,
  });
}

/**
 * Describes rows somebody already chose.
 *
 * An id the caller may not see comes back missing rather than refused, so the caller of this hook
 * must treat an absent row as "cannot show this" — never as an error, and never by falling back to
 * printing the identifier, which would put a raw uuid on screen where a name belongs.
 */
export function useFilterResolve(world: string, ids: readonly string[]) {
  return useQuery({
    queryKey: queryKeys.filterResolve(world, ids),
    queryFn: () => unwrap(api.POST('/api/v1/filters/resolve', {
      body: { world, ids: [...ids] },
    })),
    enabled: ids.length > 0,
    // Names change rarely and this is asked every time a form with a stored value opens.
    staleTime: 5 * 60_000,
  });
}

// ---------------------------------------------------------------------------
// Trip statistics
// ---------------------------------------------------------------------------

/**
 * What a person, a cave, a club or a camp adds up to across trips.
 *
 * Every figure is counted over the trips the caller may read, so two people legitimately see
 * different totals for the same subject. That is a fact about the answer, not about the request —
 * whatever shows these has to say so on the screen beside them, or the difference is reported as a
 * bug and repaired by removing the filter.
 */
export type TripStatistics = components['schemas']['TripStatisticsDto'];

/** Which thing is being added up. */
export type StatisticsSubject = 'caver' | 'cave' | 'cavingGroup' | 'expedition';

export function useTripStatistics(
  subject: StatisticsSubject,
  id: string | undefined,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.tripStatistics(subject, id ?? ''),
    queryFn: () => {
      const path = { id: id! };
      // A named branch per subject rather than a trailing fallthrough: a subject added to the
      // union without a branch of its own would otherwise be asked about as a club, and answered.
      switch (subject) {
        case 'caver':
          return unwrap(api.GET('/api/v1/stats/cavers/{id}', { params: { path } }));
        case 'cave':
          return unwrap(api.GET('/api/v1/stats/caves/{id}', { params: { path } }));
        case 'cavingGroup':
          return unwrap(api.GET('/api/v1/stats/caving-groups/{id}', { params: { path } }));
        case 'expedition':
          return unwrap(api.GET('/api/v1/stats/expeditions/{id}', { params: { path } }));
      }
    },
    enabled: enabled && !!id,
    // Derived on every request from trips that change slowly; a page revisited within the minute
    // does not need to ask again.
    staleTime: 30_000,
    // A caller who may not read the subject is refused, and the surface simply does not appear.
    retry: false,
  });
}

// ---------------------------------------------------------------------------
// Cave survey statistics
// ---------------------------------------------------------------------------

/** What a cave's line work measures, and how it compares with what the record claims. */
export type CaveSurveyStatistics = components['schemas']['CaveStatisticsDto'];

/** Which way and how steeply a cave's passages run. */
export type CaveOrientation = components['schemas']['CaveOrientationDto'];

/** One sector of a rose, or one band of a dip histogram. The range comes from the response. */
export type OrientationBin = components['schemas']['OrientationBin'];

/** How steep the passages are, or absent when the line work carries no altitudes. */
export type DipSummary = components['schemas']['DipSummary'];

/** A computed figure set against the one typed into the record. */
export type MorphometryComparison = components['schemas']['MorphometryComparison'];

/**
 * Which body of line work a survey statistic was measured from.
 *
 * This is not decoration. `surveyFlags` means the surveyor's own per-leg flags decided what
 * counts, which is what these statistics are defined as; `skeletonHeuristic` means the shape of a
 * stored centerline was used to guess the same thing, which keeps most of the length but is a
 * different measurement. Comparing one cave measured the first way against another measured the
 * second, as though they were the same figure, is the mistake this field exists to prevent —
 * so whatever renders these numbers has to say which one it got.
 */
export type SurveySegmentBasis = components['schemas']['SurveySegmentBasis'];

export function useCaveSurveyStatistics(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.caveSurveyStatistics(caveId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/caves/{id}/statistics', { params: { path: { id: caveId! } } })),
    enabled: !!caveId,
    // Recomputed per request from line work that changes only when a survey is uploaded.
    staleTime: 5 * 60_000,
    // A cave the caller may not read — or may read but not place exactly — is refused with the
    // same answer as a cave that does not exist, and asking again will not change it.
    retry: false,
  });
}

/** How big a cave's passages are, from the wall distances recorded at its stations. */
export type CaveCrossSection = components['schemas']['CaveCrossSectionDto'];

/** The sizes, distributions, volume and vertical slices of one cave's passages. */
export type CrossSectionSummary = components['schemas']['CrossSectionSummary'];

/** A five-number summary plus the mean, for one kind of measurement. */
export type CrossSectionDistribution = components['schemas']['CrossSectionDistribution'];

/** One vertical slice of a cave, with the passage sizes found in it. */
export type CrossSectionElevationBand = components['schemas']['CrossSectionElevationBand'];

/** What kind of cave a survey's shape suggests, with every rule applied to reach it. */
export type CavePattern = components['schemas']['CavePatternDto'];

/** The suggestion itself: the pattern, the scores, the rules and the caveats. */
export type PatternSuggestion = components['schemas']['PatternSuggestion'];

/** One rule, what it did, what it read and what it would have counted towards. */
export type PatternRuleTrace = components['schemas']['PatternRuleTrace'];

/** Which pattern a cave's measurements suggest. */
export type SpeleogeneticPatternKind = components['schemas']['SpeleogeneticPatternKind'];

/** Which rule a trace entry describes. */
export type PatternRule = components['schemas']['PatternRule'];

/** Which measured figure a rule read. */
export type PatternFigure = components['schemas']['PatternFigure'];

/** Whether a rule fired, stayed silent, or had nothing to read. */
export type PatternRuleOutcome = components['schemas']['PatternRuleOutcome'];

/** Something a reader has to know before using a pattern suggestion. */
export type PatternCaveat = components['schemas']['PatternCaveat'];

export function useCaveCrossSection(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.caveCrossSection(caveId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/caves/{id}/cross-section', { params: { path: { id: caveId! } } })),
    enabled: !!caveId,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

export function useCavePattern(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.cavePattern(caveId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/caves/{id}/pattern', { params: { path: { id: caveId! } } })),
    enabled: !!caveId,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

/** Where a cave's passage sits vertically, and the levels it appears to be cut at. */
export type CaveHypsometry = components['schemas']['CaveHypsometryDto'];

/** Where the entrances under an area sit vertically. */
export type AreaHypsometry = components['schemas']['AreaHypsometryDto'];

/** The histogram and the levels proposed from it. */
export type ElevationBandProposal = components['schemas']['ElevationBandProposal'];

/** One interval of height in an elevation histogram. */
export type ElevationBin = components['schemas']['ElevationBin'];

/** One proposed level. */
export type ElevationBand = components['schemas']['ElevationBand'];

/** What somebody decided one cave's levels are, or the fact that nobody has. */
export type CaveLevelBands = components['schemas']['CaveLevelBandsDto'];

/** One level of a saved reading, as a person wrote it down. */
export type SavedElevationBand = components['schemas']['SavedElevationBand'];

/** A cave's passage trends against the structure mapped around it. */
export type CaveStructureComparison = components['schemas']['CaveStructureComparisonDto'];

/** An area's depression alignments against the structure mapped in it. */
export type AreaStructureComparison = components['schemas']['AreaStructureComparisonDto'];

/** How far apart two roses are. */
export type RoseDivergence = components['schemas']['RoseDivergence'];

export function useCaveHypsometry(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.caveHypsometry(caveId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/caves/{id}/hypsometry', { params: { path: { id: caveId! } } })),
    enabled: !!caveId,
    staleTime: 5 * 60_000,
    // A cave this caller may read but not place exactly is refused with the same answer as one
    // that does not exist. Retrying asks the same question again.
    retry: false,
  });
}

export function useAreaHypsometry(areaId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.areaHypsometry(areaId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/features/{id}/entrance-hypsometry', {
          params: { path: { id: areaId! } },
        }),
      ),
    enabled: !!areaId && enabled,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

export function useCaveLevelBands(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.caveLevelBands(caveId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/caves/{id}/level-bands', { params: { path: { id: caveId! } } })),
    enabled: !!caveId,
    retry: false,
  });
}

export function useSaveCaveLevelBands(caveId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { bands: SavedElevationBand[]; note: string | null }) =>
      unwrap(
        api.PUT('/api/v1/caves/{id}/level-bands', {
          params: { path: { id: caveId } },
          body,
        }),
      ),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: queryKeys.caveLevelBands(caveId) }),
  });
}

export function useClearCaveLevelBands(caveId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () =>
      unwrap(api.DELETE('/api/v1/caves/{id}/level-bands', { params: { path: { id: caveId } } })),
    onSuccess: () =>
      queryClient.invalidateQueries({ queryKey: queryKeys.caveLevelBands(caveId) }),
  });
}

/**
 * A cave's passage rose against the structure mapped around it.
 *
 * `areaId` scopes the structure to an area of the containment hierarchy instead of a buffer; it is
 * part of the query key because the two scopes are two different answers to two different
 * questions, and caching them together would show one under the other's heading.
 */
export function useCaveStructureComparison(caveId: string | undefined, areaId?: string) {
  return useQuery({
    queryKey: queryKeys.caveStructureComparison(caveId ?? '', areaId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/caves/{id}/structure-comparison', {
          // The contract names its query parameters as the request record does; spelled here the
          // way the generated types spell them rather than lower-cased and left to the server's
          // case-insensitive binding to rescue.
          params: { path: { id: caveId! }, query: areaId ? { AreaId: areaId } : {} },
        }),
      ),
    enabled: !!caveId,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

export function useAreaStructureComparison(areaId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.areaStructureComparison(areaId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/features/{id}/structure-comparison', {
          params: { path: { id: areaId! } },
        }),
      ),
    enabled: !!areaId && enabled,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

/** What one karst area adds up to, over the caves declared to be in it. */
export type AreaKarstStatistics = components['schemas']['AreaKarstStatisticsDto'];

/** One reading behind the karstification index, present or absent. */
export type KarstificationComponent = components['schemas']['KarstificationComponentDto'];

/** One cave standing at an end of a range in an area. */
export type AreaCaveExtreme = components['schemas']['AreaCaveExtremeDto'];

/**
 * Counts, densities, totals and a classed index for one area.
 *
 * `retry: false` for the reason every location-protected statistic here has it: an area this
 * caller may not read answers exactly as one that is not there, so asking again asks the same
 * refused question and only delays the empty state.
 */
export function useAreaKarstStatistics(areaId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.areaKarstStatistics(areaId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/features/{id}/karst-statistics', {
          params: { path: { id: areaId! } },
        }),
      ),
    enabled: !!areaId && enabled,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

export type DensityGrid = components['schemas']['DensityGridDto'];
export type DensityCell = components['schemas']['DensityCellDto'];
export type PointPattern = components['schemas']['PointPatternDto'];

export interface DensityQuery {
  bbox: string;
  /**
   * Omitted on the first request on purpose. The finest cell an installation will publish is its
   * location-protection grid, which the client does not know and must not guess: asking without a
   * cell size gets the floor and the payload states what it was, so a control can offer multiples
   * of a real number instead of discovering the edge by being refused.
   */
  cellMetres?: number;
  bandwidthMetres?: number;
  areaId?: string;
}

/**
 * How thickly cave entrances sit over a window, as a grid and as a smoothed surface.
 *
 * <p>
 * `retry` is off on purpose. The two interesting failures here are refusals with a stable code —
 * a cell finer than the location-protection grid, and a window that would be more cells than one
 * answer holds — and neither becomes true on a second attempt. Retrying them only delays the
 * message the control needs to show.
 * </p>
 */
export function useMapDensity(query: DensityQuery | undefined) {
  return useQuery({
    queryKey: queryKeys.mapDensity(
      query?.bbox ?? '',
      query?.cellMetres ?? null,
      query?.bandwidthMetres ?? null,
      query?.areaId,
    ),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/map/density', {
          params: {
            query: {
              bbox: query!.bbox,
              cellMetres: query!.cellMetres,
              bandwidthMetres: query!.bandwidthMetres,
              areaId: query!.areaId,
            },
          },
        }),
      ),
    enabled: !!query,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

export interface PointPatternQuery {
  bbox: string;
  simulations: number;
  seed: number;
  areaId?: string;
}

/**
 * Whether those entrances are arranged more thickly, more evenly, or more directionally than
 * chance would arrange them. The seed travels in the query key as well as in the request, so two
 * readers looking at the same window and the same seed are looking at the same band.
 */
export function useMapPointPattern(query: PointPatternQuery | undefined) {
  return useQuery({
    queryKey: queryKeys.mapPointPattern(
      query?.bbox ?? '',
      query?.simulations ?? 0,
      query?.seed ?? 0,
      query?.areaId,
    ),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/map/point-pattern', {
          params: {
            query: {
              bbox: query!.bbox,
              simulations: query!.simulations,
              seed: query!.seed,
              areaId: query!.areaId,
            },
          },
        }),
      ),
    enabled: !!query,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

export function useCaveOrientation(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.caveOrientation(caveId ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/caves/{id}/orientation', { params: { path: { id: caveId! } } })),
    enabled: !!caveId,
    staleTime: 5 * 60_000,
    retry: false,
  });
}

export type TerrainBuild = components['schemas']['TerrainBuildDto'];
export type TerrainBuildDetail = components['schemas']['TerrainBuildDetailDto'];
export type TerrainBuildSource = components['schemas']['TerrainBuildSourceDto'];
export type TerrainBuildSourceRequest = components['schemas']['TerrainBuildSourceRequest'];
export type TerrainBuildSubmitRequest = components['schemas']['TerrainBuildSubmitRequest'];
export type TerrainRasterUpload = components['schemas']['TerrainRasterUploadDto'];
export type TerrainBuildStatus = components['schemas']['TerrainBuildStatus'];
export type TerrainBuildPhase = components['schemas']['TerrainBuildPhase'];
export type TerrainBuildSourceKind = components['schemas']['TerrainBuildSourceKind'];
export type TerrainHeightDatum = components['schemas']['TerrainHeightDatum'];

export interface TerrainBuildPageParams {
  page: number;
  pageSize: number;
}

/** Whether a build is still going, which is the only thing worth asking the server again about. */
export function terrainBuildUnsettled(status: TerrainBuildStatus): boolean {
  return status === 'queued' || status === 'running';
}

/**
 * How long to wait before asking again, or `false` for "stop asking".
 *
 * Exported and pure so the rule that a settled build is left alone can be asserted directly,
 * rather than inferred from a live query that would have to be watched for two seconds to prove
 * it did nothing.
 */
export function terrainBuildPollInterval(
  status: TerrainBuildStatus | undefined,
): number | false {
  return status !== undefined && terrainBuildUnsettled(status) ? 2000 : false;
}

/**
 * The same rule for a page of builds: ask again only while something on it is unfinished.
 *
 * A rule of its own rather than the one above applied to a row, and exported for the same reason:
 * a list left open on nothing but finished builds must stop asking, and that is only provable by
 * naming the rule the list actually uses.
 */
export function terrainListPollInterval(
  items: { status: TerrainBuildStatus }[] | undefined,
): number | false {
  return (items ?? []).some((build) => terrainBuildUnsettled(build.status)) ? 2000 : false;
}

/**
 * Every build this installation has made, newest first.
 *
 * A bake takes eleven seconds over a pilot rectangle and hours over a country, so the list keeps
 * asking while anything on it is unfinished and stops the moment nothing is — the interval
 * disables itself rather than being cleared by whoever navigated away.
 */
export function useTerrainBuilds(params: TerrainBuildPageParams, enabled = true) {
  return useQuery({
    queryKey: queryKeys.terrainBuildList(params),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/terrain/builds', {
          params: { query: { page: params.page, pageSize: params.pageSize } },
        }),
      ),
    enabled,
    placeholderData: keepPreviousData,
    refetchInterval: (query) => terrainListPollInterval(query.state.data?.items),
    // Newest first is the server's own ordering; there is nothing to sort by here.
  });
}

// ---------------------------------------------------------------------------
// Expeditions
// ---------------------------------------------------------------------------

export type ExpeditionInfo = components['schemas']['ExpeditionDto'];

/**
 * How the camp list is narrowed. The window asks what a camp overlapped rather than what it
 * started inside, so a fortnight camp running across the end of a month is in both months; the
 * word is looked for in the name; the state is one of the camp lifecycle's own, and a word the
 * server does not have is refused rather than ignored.
 */
export interface ExpeditionListParams {
  page?: number;
  pageSize?: number;
  from?: string;
  to?: string;
  search?: string;
  state?: string;
}

/** Camps, most recent first, narrowed by the filters the list offers. */
export function useExpeditions(params: ExpeditionListParams = {}) {
  return useQuery({
    queryKey: queryKeys.expeditions(params),
    queryFn: () => unwrap(api.GET('/api/v1/expeditions', { params: { query: params } })),
    // Paging or retyping a filter keeps the rows on screen while the next answer arrives, rather
    // than emptying the table under whoever is reading it.
    placeholderData: keepPreviousData,
  });
}

/**
 * One camp. A camp the caller may not read answers exactly as one that does not exist does —
 * the server spells both `expedition.not_found` — so the page has no way to tell them apart and
 * must not try: an address that answered differently for the two would be an address anybody
 * could probe for the existence of a camp they cannot see.
 */
export function useExpedition(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.expedition(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/expeditions/{id}', { params: { path: { id: id! } } })),
    enabled: !!id,
    // A refusal here is a settled answer about the caller, not a transient failure: retrying it
    // three times only delays the page saying so.
    retry: false,
  });
}

export type ExpeditionRoster = components['schemas']['ExpeditionRosterDto'];
export type ExpeditionRosterEntry = components['schemas']['ExpeditionRosterEntryDto'];
export type ExpeditionRosterRole = components['schemas']['ExpeditionRosterRoleDto'];

/** What somebody may be recorded as having been at a camp as. Seeded rows plus an installation's own. */
export function useExpeditionRosterRoles() {
  return useQuery({
    queryKey: queryKeys.taxonomy('expedition-roster-roles'),
    queryFn: () => unwrap(api.GET('/api/v1/expedition-roster-roles')),
    staleTime: 5 * 60_000,
  });
}

/**
 * Who was at a camp, and for which days.
 *
 * Two rights, not one: the right to read the camp *and* the right to read people. A caller
 * holding the first and not the second is refused outright, with a code of its own, rather than
 * being handed rows with the names struck out — a struck-out list still says how many people were
 * there and when. That refusal is a designed answer and the surface showing it renders it as a
 * state of the page, which is why it must not be retried: the identical request cannot produce
 * anything else, and three attempts only hold the screen in its loading state meanwhile.
 */
export function useExpeditionRoster(expeditionId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.expeditionRoster(expeditionId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/expeditions/{expeditionId}/roster', {
          params: { path: { expeditionId: expeditionId! } },
        }),
      ),
    enabled: !!expeditionId,
    retry: false,
  });
}

/**
 * One build with its sources and the tail of what the tool itself said.
 *
 * The log tail and the source list live only on this response, so a running build is watched
 * through here rather than through the list — and only while it is running.
 */
export function useTerrainBuild(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.terrainBuild(id ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/terrain/builds/{id}', { params: { path: { id: id! } } })),
    enabled: !!id,
    refetchInterval: (query) => terrainBuildPollInterval(query.state.data?.build.status),
  });
}

/**
 * Starts a build over a rectangle.
 *
 * The answer is the build row, not the queue row, so the list is asked again rather than seeded:
 * the server decides where a new build sorts and what phase it starts in.
 */
export function useSubmitTerrainBuild() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: TerrainBuildSubmitRequest) =>
      unwrap(api.POST('/api/v1/terrain/builds', { body: request })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.terrainBuilds });
    },
  });
}

/**
 * Everything one camp draws on a map, as one answer.
 *
 * Three kinds of shape come back together — the camp's working area, the sketches of the member
 * trips this caller may read, and the entrances of the caves those trips name whose positions
 * this caller may see — because they are governed by three different rules and only the server
 * can apply them. Nothing here filters what arrives: whatever reached the client was cleared to.
 *
 * Fetched only once the map tab is the one on screen, so opening a camp does not pay for a map
 * nobody looked at.
 */
export function useExpeditionMap(expeditionId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.expeditionMap(expeditionId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/expeditions/{id}/map', { params: { path: { id: expeditionId! } } })),
    enabled: !!expeditionId && enabled,
    retry: false,
  });
}

export type ExpeditionLeads = components['schemas']['ExpeditionLeadsDto'];
export type ExpeditionLeadGroup = components['schemas']['ExpeditionLeadGroupDto'];
export type ExpeditionLead = components['schemas']['ExpeditionLeadDto'];

/**
 * What a camp's trips left open, grouped by whether each way on is still going.
 *
 * Nothing here is derived on the client and nothing here is filtered on it: which leads are on
 * the board is decided per lead on the server, against the same rule any single place is judged
 * by, and a lead this caller may not place exactly never arrives. So the board is drawn from what
 * came back and the count beside it is the count of what came back — two readers of the same camp
 * see different boards, and the page says so rather than leaving the numbers to imply otherwise.
 */
export function useExpeditionLeads(expeditionId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.expeditionLeads(expeditionId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/expeditions/{id}/leads', { params: { path: { id: expeditionId! } } })),
    enabled: !!expeditionId,
    retry: false,
  });
}

export type ChecklistInfo = components['schemas']['ChecklistDto'];
export type ChecklistItemInfo = components['schemas']['ChecklistItemDto'];
export type ChecklistWrite = components['schemas']['ChecklistWriteRequest'];
export type TripChecklistInfo = components['schemas']['TripChecklistDto'];
export type TripChecklistItemInfo = components['schemas']['TripChecklistItemDto'];

/**
 * The lists this caller may read, lines included.
 *
 * There is one kind of list. A list an administrator publishes for the whole installation
 * arrives here beside a caver's own — it is the same row with a wider audience — so nothing
 * here sorts them into two groups or asks which is "the default".
 */
export function useChecklists() {
  return useQuery({
    queryKey: queryKeys.checklists,
    queryFn: () => unwrap(api.GET('/api/v1/checklists')),
  });
}

function useInvalidateChecklists() {
  const queryClient = useQueryClient();
  return (id?: string) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.checklists });
    if (id) {
      void queryClient.invalidateQueries({ queryKey: queryKeys.checklist(id) });
    }
    // A trip's reading of how settled it is comes from these rows, so changing a list moves it.
    void queryClient.invalidateQueries({ queryKey: ['trip-logs'] });
  };
}

export function useCreateChecklist() {
  const invalidate = useInvalidateChecklists();
  return useMutation({
    mutationFn: (body: ChecklistWrite) => unwrap(api.POST('/api/v1/checklists', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateChecklist() {
  const invalidate = useInvalidateChecklists();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: ChecklistWrite }) =>
      unwrap(api.PUT('/api/v1/checklists/{id}', { params: { path: { id } }, body })),
    onSuccess: (_data, variables) => invalidate(variables.id),
  });
}

export function useDeleteChecklist() {
  const invalidate = useInvalidateChecklists();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/checklists/{id}', {
        params: { path: { id } },
      });
      if (error) {
        throw new ApiError(response.status, error);
      }
    },
    onSuccess: () => invalidate(),
  });
}

/**
 * Sends one raster up, and answers the reference a build declares it by.
 *
 * Nothing is invalidated: an uploaded file is not a build and does not appear anywhere until a
 * submit names it.
 */
export function useUploadTerrainRaster() {
  return useMutation({
    mutationFn: async (file: File): Promise<TerrainRasterUpload> => {
      const form = new FormData();
      form.append('file', file, file.name);
      // Multipart: hand the FormData through untouched (the browser sets the boundary).
      return unwrap(
        api.POST('/api/v1/terrain/rasters', {
          body: form as never,
          bodySerializer: (b: unknown) => b as FormData,
        }),
      );
    },
  });
}

/**
 * The directories on the server this installation may read rasters from.
 *
 * Refused outright to anyone who is not a full administrator, because the answer is the operator's
 * own directory layout — a fact about the machine rather than about anything in it. Callers gate
 * on that before enabling this, so the request is never made and never refused; `retry` is off
 * regardless, since a refusal will not become an acceptance by being asked twice.
 */
export function useTerrainSourceDirectories(enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.terrainSourceDirectories,
    queryFn: () => unwrap(api.GET('/api/v1/terrain/source-directories')),
    enabled,
    retry: false,
    staleTime: 5 * 60_000,
  });
}

/**
 * The list one trip works through, its lines, who has confirmed each and when, and how much of
 * it is settled.
 *
 * The count comes back on the answer rather than being worked out here. It is the server's
 * reading of the lines and the confirmations, and a second count computed in the browser would
 * be a copy free to disagree with it — over a list whose lines this caller may not even have
 * been sent all of.
 *
 * A trip whose purpose names no list, and a trip naming one this caller may not read, answer the
 * same way: no list. That is deliberate on the server, and nothing here tries to tell them apart.
 */
export function useTripChecklist(tripLogId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.tripChecklist(tripLogId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/trip-logs/{tripLogId}/checklist', {
          params: { path: { tripLogId: tripLogId! } },
        }),
      ),
    enabled: !!tripLogId && enabled,
  });
}

function useInvalidateTripChecklist() {
  const queryClient = useQueryClient();
  return (tripLogId: string) => {
    void queryClient.invalidateQueries({ queryKey: queryKeys.tripChecklist(tripLogId) });
    // The trip's own row carries the same figure, so a confirmation moves both.
    void queryClient.invalidateQueries({ queryKey: queryKeys.tripLog(tripLogId) });
  };
}

/**
 * Confirms one line as settled for this trip, or takes the confirmation back.
 *
 * Confirming what is already confirmed changes nothing: the record is of who first said so and
 * when, and an answer that moved every time somebody reopened the page would answer a different
 * question.
 */
export function useSetTripChecklistItem() {
  const invalidate = useInvalidateTripChecklist();
  return useMutation({
    mutationFn: async ({
      tripLogId,
      itemId,
      ticked,
    }: {
      tripLogId: string;
      itemId: string;
      ticked: boolean;
    }) => {
      const params = { path: { tripLogId, itemId } };
      if (!ticked) {
        const { error, response } = await api.DELETE(
          '/api/v1/trip-logs/{tripLogId}/checklist/items/{itemId}',
          { params },
        );
        if (error) {
          throw new ApiError(response.status, error);
        }
        return;
      }

      await unwrap(api.PUT('/api/v1/trip-logs/{tripLogId}/checklist/items/{itemId}', { params }));
    },
    onSuccess: (_data, variables) => invalidate(variables.tripLogId),
  });
}

export type CalendarEntry = components['schemas']['CalendarEntryDto'];
export type CalendarResult = components['schemas']['CalendarResultDto'];
export type CalendarSource = components['schemas']['CalendarSource'];
export type CalendarPlacement = components['schemas']['CalendarPlacement'];

/**
 * What the caller asked of the calendar.
 *
 * The window is required, both ends of it, and that is the whole reason this answer can be one
 * merged list rather than an approximation: within a bounded window each source's readable rows
 * are a finite set, so merging them is exact. An unbounded question would have to read each
 * source ahead and hope.
 *
 * There is deliberately no member naming a person. `mine` means whoever is making the request and
 * is worked out on the server from the request itself; a parameter carrying somebody's identifier
 * would let a reader assemble where a named person has been out of rows they may never open.
 * `cavingGroupId` names a group and not a person, and it can only ever narrow what the reader
 * could already read.
 */
export interface CalendarParams {
  /** Inclusive, `YYYY-MM-DD`. Required. */
  from: string;
  /** Inclusive, `YYYY-MM-DD`. Required. */
  to: string;
  /**
   * The families of dated record wanted, comma-separated. Omitted means all of them, which is
   * the point of the surface; naming several is how "everything except one family" is asked for,
   * which a single word cannot express once there are more than two families.
   */
  source?: string;
  /** A lifecycle state spelled the way the contract spells it. */
  state?: ActivityState;
  /** One group's calendar: the trips it is running and the camps it owns. */
  cavingGroupId?: string;
  /** The rows the signed-in account is on. Takes no argument, and never will. */
  mine?: boolean;
  /** False narrows the window to begin no earlier than today, in the server's clock. */
  includePast?: boolean;
  /** False leaves out the rows that were called off. They are in by default. */
  includeCancelled?: boolean;
  /** One of the orders the server knows; anything else falls back to the calendar's own. */
  sort?: string;
}

/**
 * The dated records the reader may open whose days fall in one window.
 *
 * Its own key rather than a shape of any list's: it spans two families of row, so writing either
 * of them should re-read it, and it goes stale as days pass rather than only as people edit.
 */
export function useCalendar(params: CalendarParams, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: queryKeys.calendar(params),
    queryFn: () => unwrap(api.GET('/api/v1/calendar', { params: { query: params } })),
    enabled: options?.enabled ?? true,
    // Changing the window or a toggle keeps the rows on screen while the next answer arrives,
    // rather than emptying the record under whoever is reading it.
    placeholderData: keepPreviousData,
  });
}

/**
 * Where the trips in a window of days went, for one rectangle of the world.
 *
 * **Two answers, correlated here rather than joined on the server, and that is a decision.** The
 * calendar answers which dated records fall in a window and carries no position of any kind; this
 * answers which trip shapes fall inside a rectangle over the same days. Asking one endpoint for
 * both would put a third expression of who may read what beside the two that already exist — the
 * calendar's merge and this map's own filter — and the two would then have to be kept saying the
 * same thing forever. They already agree by construction: both walk the same visibility rule over
 * the same table, so nothing can arrive on one side that the other would have refused. Matching
 * them on the identifier each carries is therefore exact, and costs one request that was going to
 * be made anyway.
 *
 * The rectangle is the caller's own view. There is deliberately no way to ask for the whole world:
 * the answer is capped, and a capped answer over the world is a scatter of whichever rows sorted
 * first, drawn as though it were everything in view.
 */
export function useTripLogMap(
  bbox: string | undefined,
  from: string,
  to: string,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.tripLogMap(bbox ?? '', from, to),
    queryFn: () =>
      unwrap(api.GET('/api/v1/map/trip-logs', { params: { query: { bbox: bbox!, from, to } } })),
    enabled: enabled && !!bbox,
    retry: false,
    // Panning keeps the shapes already drawn on screen while the next rectangle is answered,
    // rather than blanking the map under whoever is moving it.
    placeholderData: keepPreviousData,
  });
}

// ---------------------------------------------------------------------------
// Calendar events
// ---------------------------------------------------------------------------

export type EventInfo = components['schemas']['EventDto'];
export type EventWrite = components['schemas']['EventWriteRequest'];
export type EventKind = components['schemas']['EventKind'];
export type EventDefaults = components['schemas']['EventDefaultsDto'];
/** How a repeating event comes round. A closed list the server steps by once, at creation. */
export type EventRecurrenceFrequency = NonNullable<
  components['schemas']['EventRecurrenceFrequency']
>;

/**
 * How the event list is narrowed. The window asks what an event *overlapped* rather than what it
 * started inside, so a training weekend running across the end of a month is in both months; the
 * word is looked for in the title; and a kind or a state the server does not have is refused
 * rather than quietly answered with an empty page.
 */
export interface EventListParams {
  page?: number;
  pageSize?: number;
  from?: string;
  to?: string;
  search?: string;
  kind?: string;
  state?: string;
  /**
   * One repeating event's occurrences, by the key they share. A run is not a thing of its own —
   * it is the ordinary events carrying this key — so it is asked for as a narrowing of the list
   * and answered through the same visibility walk as every other narrowing.
   */
  seriesId?: string;
}

/** The events this reader may open, narrowed by the filters the list offers. */
export function useEvents(params: EventListParams = {}) {
  return useQuery({
    queryKey: queryKeys.events(params),
    queryFn: () => unwrap(api.GET('/api/v1/events', { params: { query: params } })),
    // Paging or retyping a filter keeps the rows on screen while the next answer arrives, rather
    // than emptying the table under whoever is reading it.
    placeholderData: keepPreviousData,
  });
}

/**
 * One event. An event the caller may not read answers exactly as one that does not exist does —
 * the server spells both `event.not_found` — so the page has no way to tell them apart and must
 * not try: an address that answered differently for the two would be one anybody could probe for
 * the existence of an event they cannot see.
 */
export function useEvent(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.event(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/events/{id}', { params: { path: { id: id! } } })),
    enabled: !!id,
    // A refusal here is a settled answer about the caller, not a transient failure: retrying it
    // three times only delays the page saying so.
    retry: false,
  });
}

/**
 * Everything that changes which elevation model the 3D scene draws.
 *
 * The invalidation is the load-bearing part, not decoration. What the scene draws arrives with the
 * map configuration, which is treated as fresh for five minutes — so without this, choosing terrain
 * answers immediately, changes nothing on screen, and the wait looks like a bad bake rather than a
 * cached answer. The scene picks the new ground up on its own once the configuration is re-read:
 * each build is served from an address of its own, and a terrain source whose address has changed
 * is what makes the engine load one at all.
 */
export function useChooseTerrainBuild() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/v1/terrain/builds/{id}/active', { params: { path: { id } } })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.mapConfig });
      void queryClient.invalidateQueries({ queryKey: queryKeys.terrainBuilds });
    },
  });
}

/** Stops drawing a build, which leaves the scene on bare ground until something else is chosen. */
export function useStopDrawingTerrainBuild() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.DELETE('/api/v1/terrain/builds/{id}/active', { params: { path: { id } } })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.mapConfig });
      void queryClient.invalidateQueries({ queryKey: queryKeys.terrainBuilds });
    },
  });
}

/**
 * The audience a new event would get if its author names none.
 *
 * Read from the server rather than worked out here, because the write applies the same rule: a
 * form that guessed would be guessing about who can read something, and the two answers would be
 * free to disagree the day either changed.
 */
export function useEventDefaults(enabled = true) {
  return useQuery({
    queryKey: queryKeys.eventDefaults,
    queryFn: () => unwrap(api.GET('/api/v1/events/defaults')),
    enabled,
  });
}

/**
 * Re-reads everything a write to an event changes, and hands back the promise rather than
 * starting it and forgetting it.
 *
 * The promise matters on the writes the server checks a precondition on. No write answers with
 * the version it produced, so the token the next write must carry is only recorded by a read —
 * and a mutation that settled before that read left the buttons live while the cached token was
 * still the one from before. The second click then carries a spent version and is refused for a
 * move that was perfectly legal.
 */
function useInvalidateEvents() {
  const queryClient = useQueryClient();
  return (id?: string) => {
    const pending = [
      queryClient.invalidateQueries({ queryKey: ['events'] }),
      // An event is a row on the calendar, so writing one moves what that window answers.
      queryClient.invalidateQueries({ queryKey: ['calendar'] }),
    ];
    if (id) {
      pending.push(queryClient.invalidateQueries({ queryKey: queryKeys.event(id) }));
    }
    return Promise.all(pending);
  };
}

export function useCreateEvent() {
  const invalidate = useInvalidateEvents();
  return useMutation({
    mutationFn: (body: EventWrite) => unwrap(api.POST('/api/v1/events', { body })),
    onSuccess: () => invalidate(),
  });
}

/**
 * Stores an edited event. The server requires the version the form was loaded against; a full
 * update is on the path the detail read captured that version under, so the precondition is
 * threaded onto it without this call having to say so.
 */
export function useUpdateEvent() {
  const invalidate = useInvalidateEvents();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: EventWrite }) =>
      unwrap(api.PUT('/api/v1/events/{id}', { params: { path: { id } }, body })),
    // Handed back rather than started and forgotten: the next write on this event is checked
    // against the version this one produced, and only the read records it.
    onSuccess: (_data, variables) => invalidate(variables.id),
  });
}

export function useDeleteEvent() {
  const invalidate = useInvalidateEvents();
  return useMutation({
    mutationFn: async (id: string) => {
      const { error, response } = await api.DELETE('/api/v1/events/{id}', {
        params: { path: { id } },
      });
      if (error) {
        throw new ApiError(response.status, error);
      }
    },
    onSuccess: () => invalidate(),
  });
}

export type EventSeriesEditResult = components['schemas']['EventSeriesEditResultDto'];
export type EventSeriesDeleteResult = components['schemas']['EventSeriesDeleteResultDto'];

/**
 * Applies one edit to this occurrence of a repeating event and to every later one of its series.
 *
 * The precondition is set here by hand rather than left to the replay that threads it onto an
 * ordinary update. That replay is keyed by the path a version was read under, and this write is
 * addressed to a sub-path of the event rather than to the event itself, so nothing would be sent
 * and a server that requires one would refuse every edit. It is honestly a precondition over the
 * occurrence on the screen alone — one token cannot speak for a set — and it is still worth
 * carrying: it says the evening the author was looking at has not moved underneath them.
 */
export function useEditEventSeriesFollowing() {
  const invalidate = useInvalidateEvents();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: EventWrite }) => {
      const etag = lastReadETag(`/api/v1/events/${id}`);
      return unwrap(
        api.PUT('/api/v1/events/{id}/series/following', {
          params: { path: { id } },
          headers: etag ? { 'If-Match': etag } : undefined,
          body,
        }),
      );
    },
    // Every occurrence of the series is an ordinary event on its own page and its own row of the
    // calendar, so an edit reaching a dozen of them has moved a dozen things this cache holds.
    onSuccess: (_data, variables) => invalidate(variables.id),
  });
}

/**
 * Removes a build and the disk it was keeping.
 *
 * The map configuration goes with it even though the build being drawn cannot be deleted: another
 * account may have chosen different terrain since this list was read, and a delete is the moment
 * this browser is talking to the server anyway.
 */
export function useDeleteTerrainBuild() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/terrain/builds/{id}', { params: { path: { id } } })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.terrainBuilds });
      void queryClient.invalidateQueries({ queryKey: queryKeys.mapConfig });
    },
  });
}

/**
 * The measured shape of a drawn outline — a doline's area, how round it is, how long and how wide,
 * which way it lies, and where its middle is.
 *
 * Every length is metres and every area square metres. They are measured in the installation's
 * working coordinate system rather than in the degrees the outline is stored in, because a degree
 * is not a unit of length and its size on the ground changes with latitude.
 */
export type FeatureMorphometry = components['schemas']['FeatureMorphometryDto'];

/**
 * Asks for one outline's measurements.
 *
 * A reader who may see the feature but may not be told where it is gets no answer at all — the
 * server spells that as "no such feature", because a shape and a bearing place a doline as surely
 * as a coordinate does. So a failure here means the card simply does not appear; it is never an
 * empty set of figures, which would read as "this doline has no size".
 */
export function useFeatureMorphometry(id: string | undefined, enabled = true) {
  return useQuery({
    queryKey: queryKeys.featureMorphometry(id ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/features/{id}/morphometry', { params: { path: { id: id! } } })),
    enabled: enabled && !!id,
    // Measured from geometry that only changes when somebody redraws the outline.
    staleTime: 60_000,
    retry: false,
  });
}

/**
 * Calls off this occurrence of a repeating event and every later one, keeping any that has
 * already begun.
 *
 * It answers with both halves — what went and what stayed — because the half that stays is the
 * surprising one: an occurrence that has already happened is the record of an evening and of who
 * said they would come, and is never removed by an act aimed at the rest of the run.
 */
export function useDeleteEventSeriesFollowing() {
  const invalidate = useInvalidateEvents();
  return useMutation({
    mutationFn: async (id: string) => {
      const { data, error, response } = await api.DELETE('/api/v1/events/{id}/series/following', {
        params: { path: { id } },
      });
      if (error) {
        throw new ApiError(response.status, error);
      }
      return data as EventSeriesDeleteResult;
    },
    onSuccess: () => invalidate(),
  });
}

export type SyncSet = components['schemas']['SyncSetDto'];
export type SyncSetWrite = components['schemas']['SyncSetWriteRequest'];
export type SyncCapabilities = components['schemas']['SyncCapabilitiesDto'];

/**
 * What this installation's sync protocol can do. Read by the settings page for the same reason a
 * phone reads it first: the parts of the protocol a server actually serves are announced by name,
 * so a page can say what a device will and will not be able to do here instead of guessing.
 */
export function useSyncCapabilities() {
  return useQuery({
    queryKey: queryKeys.syncCapabilities,
    queryFn: () => unwrap(api.GET('/api/v1/sync/capabilities')),
    staleTime: 300_000,
  });
}

/**
 * How close two caves come to each other: the shortest line between their line work in three
 * dimensions, split into its horizontal and vertical parts, with a bearing.
 *
 * `absence` says why there is no measurement when there is none, and is never blank — a cave with
 * no line work and a cave whose line work was drawn in plan with no depths are different answers,
 * and both are different from "these two have not been compared".
 */
export type ClosestApproach = components['schemas']['ClosestApproachDto'];

/**
 * Asks how close two caves come.
 *
 * Withheld entirely unless this caller may place both caves exactly — not rounded, not snapped.
 * The refusal is spelled "no such cave", so a guarded cave and a cave that never existed answer
 * alike and the query simply fails; whatever shows this shows nothing rather than a blank number.
 */
export function useClosestApproach(id: string | undefined, other: string | undefined) {
  return useQuery({
    queryKey: queryKeys.closestApproach(id ?? '', other ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/caves/{id}/closest-approach/{other}', {
          params: { path: { id: id!, other: other! } },
        }),
      ),
    enabled: !!id && !!other && id !== other,
    staleTime: 60_000,
  });
}

/**
 * Moves an event to another lifecycle state — one endpoint naming the state to move to rather
 * than a verb per move.
 *
 * The server requires the version the caller was looking at, so the move carries the token the
 * detail read captured: two people announcing and un-announcing the same evening otherwise land
 * in whichever order the database happens to see. Which moves are legal from which state is the
 * server's to decide; the control only offers the ones a reader would expect, and one the table
 * refuses comes back as a conflict rather than being prevented here.
 */
export function useMoveEvent() {
  const invalidate = useInvalidateEvents();
  return useMutation({
    mutationFn: ({ id, state }: { id: string; state: ActivityState }) => {
      const etag = lastReadETag(`/api/v1/events/${id}`);
      return unwrap(
        api.POST('/api/v1/events/{id}/state', {
          params: { path: { id } },
          headers: etag ? { 'If-Match': etag } : undefined,
          body: { state },
        }),
      );
    },
    // As on the event's own update: a move is checked against the version last read, so the move
    // is not finished until the version it produced has been read.
    onSuccess: (_data, variables) => invalidate(variables.id),
  });
}

// ---------------------------------------------------------------------------
// Work areas
// ---------------------------------------------------------------------------

export type WorkArea = components['schemas']['WorkAreaDto'];
export type WorkAreaCollection = components['schemas']['WorkAreaCollectionDto'];

/**
 * Every work area this caller may read, as one answer.
 *
 * There are tens of these and not thousands — a club works the ground it can reach — so the whole
 * tree is fetched once and levelled in the browser. That is what lets the dashboard list, the
 * overview map and the zoom-to-area link share a cache entry instead of asking three times and
 * risking three different answers.
 */
export function useWorkAreas(enabled = true) {
  return useQuery({
    queryKey: queryKeys.workAreas,
    queryFn: () => unwrap(api.GET('/api/v1/work-areas', {})),
    enabled,
    retry: false,
  });
}

// --- link-annotated text ------------------------------------------------------------------

/**
 * One annotated text's blocks, with the file its anchors are measured against.
 *
 * Keyed by the document rather than by the file: a revision replaces the bytes, and a reader
 * following a link into this document names the document, which is the identity that survives
 * every edit. `retry: false` because the two ways this fails — the document is not one of these,
 * and the caller may not read it — are both settled answers that asking again cannot change.
 */
export function useAnnotatedText(documentId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.annotatedText(documentId ?? ''),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/annotated-texts/{documentId}', {
          params: { path: { documentId: documentId! } },
        }),
      ),
    enabled: Boolean(documentId),
    retry: false,
  });
}

export function useCreateAnnotatedText() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: AnnotatedTextCreate) => unwrap(api.POST('/api/v1/annotated-texts', { body })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['documents'] });
      void queryClient.invalidateQueries({ queryKey: ['cabinets'] });
    },
  });
}

/**
 * The caller's own sync sets. This listing is never widened: it answers with the sets the signed-in
 * account owns and no others, whatever an installation allows an administrator to read one set by.
 */
export function useSyncSets() {
  return useQuery({
    queryKey: queryKeys.syncSets,
    queryFn: () => unwrap(api.GET('/api/v1/sync/sets')),
  });
}

export function useCreateSyncSet() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: SyncSetWrite) => unwrap(api.POST('/api/v1/sync/sets', { body })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.syncSets }),
  });
}

export function useUpdateSyncSet() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: SyncSetWrite }) =>
      unwrap(api.PUT('/api/v1/sync/sets/{id}', { params: { path: { id } }, body })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.syncSets }),
  });
}

/**
 * Replaces the body with a new revision.
 *
 * Both link caches are dropped as well as the text's own, because rewriting the words re-measures
 * every anchor over them: a panel still holding the previous answer would draw highlights at the
 * offsets they had before the edit — which is exactly the silent mis-highlighting the re-measuring
 * exists to prevent, reintroduced on this side of the wire.
 */
export function useReplaceAnnotatedText() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ documentId, blocks }: { documentId: string; blocks: AnnotatedText['blocks'] }) =>
      unwrap(
        api.PUT('/api/v1/annotated-texts/{documentId}', {
          params: { path: { documentId } },
          body: { blocks },
        }),
      ),
    onSuccess: (_result, { documentId }) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.annotatedText(documentId) });
      void queryClient.invalidateQueries({ queryKey: ['reslinks'] });
      void queryClient.invalidateQueries({ queryKey: ['documents'] });
    },
  });
}

/**
 * Revokes a selection. Nothing that was synced is touched — a sync set names caves, it never owned
 * any of them — so what this ends is a device's licence to keep asking for them.
 */
export function useDeleteSyncSet() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/sync/sets/{id}', { params: { path: { id } } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.syncSets }),
  });
}
