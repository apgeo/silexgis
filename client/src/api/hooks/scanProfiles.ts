// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { queryKeys } from './queryKeys.ts';
import { unwrap, unwrapVoid } from './shared.ts';

export type ProcessingProfile = components['schemas']['ProcessingProfileDto'];
export type ProcessingProfileScope = components['schemas']['ProcessingProfileScope'];
export type ProcessingProfileImportReport = components['schemas']['ProcessingProfileImportReportDto'];
export type ProcessingProfileImportResult = components['schemas']['ProcessingProfileImportResultDto'];
export type ProcessingProfileImportRequest = components['schemas']['ProcessingProfileImportRequest'];
export type ProcessingProfileChoice = components['schemas']['ProcessingProfileChoiceDto'];
export type ScanStepParameterSchema = components['schemas']['ScanStepParameterSchemaDto'];

/** The scopes in the order a list shows them: the whole installation's first, one's own last. */
export const PROCESSING_PROFILE_SCOPES: readonly ProcessingProfileScope[] = ['installation', 'cavingGroup', 'personal'];

/**
 * The processing steps a profile can be kept for, in the order a person meets them: HDMapping's
 * odometry first because it is the one with many settings worth keeping, then the cloud steps.
 * Steps with no parameters (reading the header, the export, the check) have nothing to keep.
 */
export const PROFILE_STEP_IDS = [
  'hdmapping.odometry',
  'pointcloud.downsample',
  'py3dtiles.tile',
  'pointcloud.estimate',
] as const;

/** The step whose parameters HDMapping's own TOML file can carry in and out. */
export const ODOMETRY_STEP_ID = 'hdmapping.odometry';

/** Whether a profile of this step can also be written as HDMapping's own parameter file. */
export function exportsAsToml(stepId: string): boolean {
  return stepId === ODOMETRY_STEP_ID;
}

/** The address of a profile's file, `json` for SilexGIS's own and `toml` for HDMapping's. */
export function processingProfileExportUrl(id: string, format: 'json' | 'toml'): string {
  return `/api/v1/scan-profiles/${encodeURIComponent(id)}/export?format=${format}`;
}

/** The profiles the caller may read for one step (every step when none is named), built-ins included. */
export function useProcessingProfiles(stepId?: string) {
  return useQuery({
    queryKey: queryKeys.processingProfiles(stepId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/scan-profiles', { params: { query: stepId ? { stepId } : {} } })),
  });
}

/**
 * What a step's parameters may hold and its defaults. For HDMapping's odometry the server derives
 * them from what the installed program dumped, so they change only when the worker's program
 * does; kept a while rather than asked again on every editor opened.
 */
export function useScanStepParameterSchema(stepId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.scanStepSchema(stepId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/scan-profiles/schemas/{stepId}', { params: { path: { stepId: stepId! } } })),
    enabled: !!stepId,
    staleTime: 5 * 60_000,
  });
}

/** The profile the caller used last for each step, as the server remembered it from their uploads. */
export function useRememberedProcessingProfiles(stepId?: string) {
  return useQuery({
    queryKey: queryKeys.processingProfileChoices(stepId ?? ''),
    queryFn: () =>
      unwrap(api.GET('/api/v1/scan-profiles/remembered', { params: { query: stepId ? { stepId } : {} } })),
  });
}

/** Every list of profiles is asked again after a change: a change can move a profile between them. */
function useInvalidateProfiles() {
  const queryClient = useQueryClient();
  return () => void queryClient.invalidateQueries({ queryKey: queryKeys.processingProfilesAll });
}

export interface NewProcessingProfile {
  stepId: string;
  name: string;
  description: string | null;
  scope: ProcessingProfileScope;
  cavingGroupId: string | null;
  values: unknown;
}

export function useCreateProcessingProfile() {
  const invalidate = useInvalidateProfiles();
  return useMutation({
    mutationFn: (body: NewProcessingProfile) =>
      unwrap(api.POST('/api/v1/scan-profiles', { body: body as components['schemas']['ProcessingProfileCreateRequest'] })),
    onSuccess: invalidate,
  });
}

export function useUpdateProcessingProfile() {
  const invalidate = useInvalidateProfiles();
  return useMutation({
    mutationFn: ({ id, ...body }: { id: string; name: string; description: string | null; values: unknown }) =>
      unwrap(
        api.PUT('/api/v1/scan-profiles/{id}', {
          params: { path: { id } },
          body: body as components['schemas']['ProcessingProfileUpdateRequest'],
        }),
      ),
    onSuccess: invalidate,
  });
}

/**
 * A copy of a profile the caller may read, with new values when sent. This is also how a built-in
 * is "edited": built-ins never change in place, so saving one's changes to it saves a copy.
 */
export function useCopyProcessingProfile() {
  const invalidate = useInvalidateProfiles();
  return useMutation({
    mutationFn: ({
      id,
      ...body
    }: {
      id: string;
      name?: string | null;
      description?: string | null;
      scope?: ProcessingProfileScope;
      cavingGroupId?: string | null;
      values?: unknown;
    }) => unwrap(api.POST('/api/v1/scan-profiles/{id}/copy', { params: { path: { id } }, body })),
    onSuccess: invalidate,
  });
}

export function useDeleteProcessingProfile() {
  const invalidate = useInvalidateProfiles();
  return useMutation({
    mutationFn: (id: string) => unwrapVoid(api.DELETE('/api/v1/scan-profiles/{id}', { params: { path: { id } } })),
    onSuccess: invalidate,
  });
}

/**
 * Reads a TOML or JSON parameter file into a profile. With `dryRun` nothing is stored and only the
 * report comes back, which is what a screen shows before the person decides to keep it.
 */
export function useImportProcessingProfile() {
  const invalidate = useInvalidateProfiles();
  return useMutation({
    mutationFn: (body: ProcessingProfileImportRequest) =>
      unwrap(api.POST('/api/v1/scan-profiles/import', { body })),
    onSuccess: (result) => {
      if (result.profile) invalidate();
    },
  });
}
