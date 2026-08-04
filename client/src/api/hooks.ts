// SPDX-License-Identifier: AGPL-3.0-or-later
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { clusterCellBbox } from '../geo/cluster.ts';
import { api, ApiError } from './client.ts';
import type { components } from './schema';

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
export type TextExtractionState = components['schemas']['TextExtractionState'];
export type CabinetInfo = components['schemas']['CabinetDto'];
export type CabinetWrite = components['schemas']['CabinetWriteRequest'];
export type CabinetDocument = components['schemas']['CabinetDocumentDto'];
export type Visibility = components['schemas']['Visibility'];
export type FileConfig = components['schemas']['FileConfigDto'];
export type EntranceFeatureCollection = components['schemas']['FeatureCollection'];
export type Me = components['schemas']['MeDto'];
export type MeUpdate = components['schemas']['MeUpdateRequest'];
export type ProfileVisibility = components['schemas']['ProfileVisibilityDto'];
export type FieldVisibility = ProfileVisibility['email'];
export type UserAddress = components['schemas']['UserAddressDto'];
export type UserAddressWrite = components['schemas']['UserAddressWriteRequest'];
export type MemberSummary = components['schemas']['MemberDto'];
export type NotificationPreferences = components['schemas']['NotificationPreferencesDto'];
export type NotificationCategory = components['schemas']['NotificationCategoryDto'];
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
export type MessageTemplate = components['schemas']['MessageTemplateDto'];

// Query keys live here so invalidation stays precise.
export const queryKeys = {
  me: ['me'] as const,
  dashboardSummary: ['dashboard', 'summary'] as const,
  mapLayers: ['map-layers'] as const,
  mapConfig: ['map-config'] as const,
  taxonomy: (kind: string) => ['taxonomy', kind] as const,
  caves: (params: CaveListParams) => ['caves', 'list', params] as const,
  cave: (id: string) => ['caves', 'detail', id] as const,
  caveSummary: (id: string) => ['caves', 'summary', id] as const,
  entrances: (caveId: string) => ['entrances', caveId] as const,
  surveyModels: (caveId: string) => ['survey-models', caveId] as const,
  centerlines: (caveId: string) => ['centerlines', caveId] as const,
  search: (q: string, kind?: string) => ['search', q, kind ?? 'all'] as const,
  nominatim: (q: string) => ['nominatim', q] as const,
  features: (params: FeatureListParams) => ['features', 'list', params] as const,
  feature: (id: string) => ['features', 'detail', id] as const,
  featureParents: (id: string) => ['features', id, 'parents'] as const,
  featureChildren: (id: string, params: FeatureChildrenParams) => ['features', id, 'children', params] as const,
  featureLinks: (id: string) => ['features', id, 'links'] as const,
  featureShares: (id: string) => ['features', id, 'shares'] as const,
  geofiles: (params: GeofileListParams) => ['geofiles', 'list', params] as const,
  attachments: (entityType: string, entityId: string) => ['attachments', entityType, entityId] as const,
  fileVersions: (fileId: string) => ['file-versions', fileId] as const,
  fileConfig: ['file-config'] as const,
  document: (id: string) => ['documents', 'detail', id] as const,
  cabinets: ['cabinets'] as const,
  cabinetDocuments: (id: string, params: CabinetDocumentParams) =>
    ['cabinets', id, 'documents', params] as const,
  rasterMaps: (params: RasterMapListParams) => ['raster-maps', 'list', params] as const,
  tripLogs: (params: TripLogListParams) => ['trip-logs', 'list', params] as const,
  tripLog: (id: string) => ['trip-logs', 'detail', id] as const,
  taggings: (entityType: string, entityId: string) => ['taggings', entityType, entityId] as const,
  tags: (search: string) => ['tags', search] as const,
  cavingGroups: ['cavingGroups'] as const,
  cavers: ['cavers'] as const,
  cavingGroupMembers: (cavingGroupId: string) => ['teams', cavingGroupId, 'members'] as const,
  objectAccess: (entityType: string, entityId: string) => ['object-access', entityType, entityId] as const,
  history: (entityType: string, entityId: string) => ['history', entityType, entityId] as const,
  mfa: ['mfa'] as const,
  avatarPresets: ['avatar-presets'] as const,
  members: (params: MemberListParams) => ['members', 'list', params] as const,
  member: (id: string) => ['members', 'detail', id] as const,
  notificationPrefs: ['me', 'notifications'] as const,
  uiPreferences: ['me', 'preferences'] as const,
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
};

async function unwrap<T>(
  call: Promise<{ data?: T; error?: unknown; response: Response }>,
): Promise<T> {
  const { data, error, response } = await call;
  if (error !== undefined || data === undefined) {
    throw new ApiError(response.status, (error as { code?: string } | undefined)?.code);
  }
  return data;
}

/** unwrap for endpoints with no response body (DELETE / 204). */
async function unwrapVoid(
  call: Promise<{ error?: unknown; response: Response }>,
): Promise<void> {
  const { error, response } = await call;
  if (error !== undefined) {
    throw new ApiError(response.status, (error as { code?: string } | undefined)?.code);
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
      emailEnabled: boolean;
      digest: NotificationPreferences['digest'];
      categories: { category: NotificationCategory['category']; enabled: boolean }[];
    }) => unwrap(api.PUT('/api/v1/me/notifications', { body })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['me'] }),
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
        throw new ApiError(response.status, (error as { code?: string } | undefined)?.code);
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

export function useSurveyModels(caveId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.surveyModels(caveId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/caves/{caveId}/survey-models', { params: { path: { caveId: caveId! } } })),
    enabled: !!caveId,
    // The signed model URLs live 10 minutes; refresh before they lapse mid-view.
    staleTime: 5 * 60_000,
    refetchInterval: 8 * 60_000,
  });
}

function useInvalidateSurveyModels() {
  const queryClient = useQueryClient();
  return (caveId: string) =>
    void queryClient.invalidateQueries({ queryKey: queryKeys.surveyModels(caveId) });
}

export function useUploadSurveyModel() {
  const invalidate = useInvalidateSurveyModels();
  return useMutation({
    mutationFn: async ({ caveId, file }: { caveId: string; file: File }): Promise<SurveyModelInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
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
  return (caveId: string) =>
    void queryClient.invalidateQueries({ queryKey: queryKeys.centerlines(caveId) });
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
 * Imperative fetch used by the OpenLayers centerline loader (not a hook). The zoom decides
 * whether the server sends the splay-free skeleton or clipped full detail; `detailZoom` and
 * `maxPaths` carry the viewer's own overrides, which the server bounds.
 */
export async function fetchCenterlineFeatures(
  bbox: string,
  zoom: number,
  detailZoom?: number,
  maxPaths?: number,
): Promise<CenterlineFeatureCollection> {
  return unwrap(api.GET('/api/v1/map/cave-centerlines', {
    params: { query: { bbox, zoom, detailZoom, maxPaths } },
  }));
}

/** Imperative fetch used by the OpenLayers photo overlay loader (not a hook). */
export async function fetchPhotoFeatures(bbox: string): Promise<EntranceFeatureCollection> {
  return unwrap(api.GET('/api/v1/map/photos', { params: { query: { bbox } } }));
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
  return useQuery({
    queryKey: queryKeys.feature(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/features/{id}', { params: { path: { id: id! } } })),
    enabled: !!id,
  });
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
export type EntityType = 'feature' | 'tripLog' | 'geofile' | 'georeferencedMap' | 'mapView';
// Stored files additionally carry taggings (never attachments or grants) — the tag
// endpoints accept the extra target; the server rejects it everywhere else.
export type AttachedEntityType = EntityType | 'storedFile';
export type AttachmentRole = AttachmentInfo['role'];

export function useAttachments(entityType: AttachedEntityType, entityId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.attachments(entityType, entityId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/attachments', { params: { query: { entityType, entityId: entityId! } } })),
    enabled: !!entityId,
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
export function useHistory(entityType: string, entityId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.history(entityType, entityId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/history', {
        params: { query: { entityType, entityId: entityId!, pageSize: 100 } },
      })),
    enabled: !!entityId,
  });
}

export function useInvalidateHistory() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: ['history'] });
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
    }) => unwrap(api.PUT('/api/v1/documents/{id}', { params: { path: { id } }, body })),
    onSuccess: (_result, variables) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.document(variables.id) });
      void queryClient.invalidateQueries({ queryKey: ['cabinets'] });
      invalidateAttachments();
    },
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
 * What is on a shelf for this caller. The cabinet's own `documentCount` says how full it
 * is; this list says what of it you may read, so the two can legitimately differ.
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

export function useUploadFile() {
  return useMutation({
    mutationFn: async (file: File): Promise<FileInfo> => {
      const form = new FormData();
      form.append('file', file, file.name);
      return unwrap(api.POST('/api/v1/files', {
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
export type TripType = NonNullable<components['schemas']['TripType']>;
export type TripParticipant = components['schemas']['TripParticipantDto'];
export type TripParticipantWrite = components['schemas']['TripParticipantWrite'];
export type TagInfo = components['schemas']['TagDto'];
export type TaggingInfo = components['schemas']['TaggingDto'];

export interface TripLogListParams {
  page?: number;
  pageSize?: number;
  from?: string;
  to?: string;
  caveId?: string;
  search?: string;
}

export function useTripLogs(params: TripLogListParams) {
  return useQuery({
    queryKey: queryKeys.tripLogs(params),
    queryFn: () => unwrap(api.GET('/api/v1/trip-logs', { params: { query: params } })),
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

export function useCreateTripLog() {
  const invalidate = useInvalidateTripLogs();
  return useMutation({
    mutationFn: (body: TripLogWrite) => unwrap(api.POST('/api/v1/trip-logs', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpdateTripLog() {
  const invalidate = useInvalidateTripLogs();
  const invalidateHistory = useInvalidateHistory();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: TripLogWrite }) =>
      unwrap(api.PUT('/api/v1/trip-logs/{id}', { params: { path: { id } }, body })),
    onSuccess: () => {
      invalidate();
      invalidateHistory();
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

export function useTags(search: string) {
  return useQuery({
    queryKey: queryKeys.tags(search),
    queryFn: () => unwrap(api.GET('/api/v1/tags', { params: { query: { search: search || undefined } } })),
    staleTime: 60_000,
  });
}

export function useTaggings(entityType: AttachedEntityType, entityId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.taggings(entityType, entityId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/taggings', { params: { query: { entityType, entityId: entityId! } } })),
    enabled: !!entityId,
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

export function useCreateCavingGroup() {
  const invalidate = useInvalidateCavingGroups();
  return useMutation({
    mutationFn: (body: { name: string; type: CavingGroupInfo['type']; description: string | null; website: string | null }) =>
      unwrap(api.POST('/api/v1/caving-groups', { body })),
    onSuccess: () => invalidate(),
  });
}

export function useUpsertCavingGroupMember(cavingGroupId: string) {
  const invalidate = useInvalidateCavingGroups();
  return useMutation({
    mutationFn: (body: { caverId: string; role: CavingGroupMemberInfo['role'] }) =>
      unwrap(api.POST('/api/v1/caving-groups/{id}/members', { params: { path: { id: cavingGroupId } }, body })),
    onSuccess: () => invalidate(),
  });
}

export function useRemoveCavingGroupMember(cavingGroupId: string) {
  const invalidate = useInvalidateCavingGroups();
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
