// SPDX-License-Identifier: AGPL-3.0-or-later
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { unwrap, unwrapVoid } from './shared.ts';
import { queryKeys } from './queryKeys.ts';
import type { SpeleolocImportPreviewRequest } from './imports.ts';

export type TripRecordingListItem = components['schemas']['TripRecordingListItemDto'];
/** Where a phone recording stands: still waiting, made into a report, or made into one that needs looking at. */
export type TripRecordingStanding = components['schemas']['TripRecordingStanding'];
export type TripAttention = components['schemas']['TripAttentionDto'];
export type TripAttentionReason = components['schemas']['TripAttentionReasonDto'];

/**
 * The phone recordings of one cave the caller may read, one page at a time.
 *
 * A cave the caller may not be told about or may not place answers an empty page rather than a
 * refusal, so an empty list here is the correct picture and says nothing about whether there are
 * recordings.
 */
export function useCaveRecordings(caveId: string | undefined, page: number, pageSize: number) {
  return useQuery({
    queryKey: queryKeys.caveRecordings(caveId ?? '', page, pageSize),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/trip-recordings', {
          params: { query: { caveId: caveId!, page, pageSize } },
        }),
      ),
    enabled: Boolean(caveId),
    placeholderData: keepPreviousData,
  });
}

/**
 * The dry run of making one kept recording into a trip report by hand. A POST because the choices
 * are a body, but it writes nothing. Not retried: every refusal it answers with — already made
 * into a report, not the caller's to change, not there — is the server's settled answer.
 */
export function useRecordingImportPreview(
  recordingId: string | undefined,
  body: SpeleolocImportPreviewRequest,
  enabled = true,
) {
  return useQuery({
    queryKey: queryKeys.recordingImportPreview(recordingId ?? '', body),
    queryFn: () =>
      unwrap(
        api.POST('/api/v1/speleoloc-imports/recordings/{recordingId}/preview', {
          params: { path: { recordingId: recordingId! } },
          body,
        }),
      ),
    enabled: Boolean(recordingId) && enabled,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/**
 * Confirms the review of a kept recording. There is no saved review on this route, so the choices
 * and the decisions travel with the confirmation itself.
 */
export function useCommitRecordingImport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      recordingId,
      body,
    }: {
      recordingId: string;
      body: components['schemas']['SpeleolocImportCommitRequest'];
    }) =>
      unwrap(
        api.POST('/api/v1/speleoloc-imports/recordings/{recordingId}/commit', {
          params: { path: { recordingId } },
          body,
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['trip-logs'] });
      void queryClient.invalidateQueries({ queryKey: ['import-batches'] });
      void queryClient.invalidateQueries({ queryKey: ['trip-recordings'] });
    },
  });
}

/**
 * Says that a trip made from a phone recording no longer needs looking at. The reasons stay on
 * the trip; only the flag is lowered.
 */
export function useClearTripAttention() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (tripId: string) =>
      unwrapVoid(
        api.POST('/api/v1/trip-logs/{id}/attention/clear', { params: { path: { id: tripId } } }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['trip-logs'] });
      void queryClient.invalidateQueries({ queryKey: ['trip-recordings'] });
    },
  });
}
