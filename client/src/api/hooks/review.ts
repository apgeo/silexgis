// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, ApiError } from '../client.ts';
import type { components } from '../schema';
import { queryKeys } from './queryKeys.ts';
import { retryUnlessRefused, unwrap } from './shared.ts';

export type HeldBatch = components['schemas']['HeldBatchDto'];
export type HeldLine = components['schemas']['HeldLineDto'];
export type HeldField = components['schemas']['HeldFieldDto'];
export type HeldBatchDetail = components['schemas']['HeldBatchDetailDto'];
/** An existing feature a held batch's lines name, by name and kind, as the server lets this reader see it. */
export type HeldFeature = components['schemas']['HeldFeatureDto'];
export type ReviewDecisionResult = components['schemas']['ReviewDecisionResultDto'];

/** The refusal code of a decision that reaches a row written after the uploader read it. */
export const REVIEW_CHANGED_SINCE = 'review.changed_since';
/** The refusal code of a decision that reaches something already decided or no longer open. */
export const REVIEW_NOT_PENDING = 'review.not_pending';

/** The pending batches, oldest first. Read with the review right; a refusal is the answer. */
export function useReviewQueue(page: number, pageSize: number, enabled = true) {
  return useQuery({
    queryKey: queryKeys.reviewQueue(page, pageSize),
    queryFn: () => unwrap(api.GET('/api/v1/review/batches', { params: { query: { page, pageSize } } })),
    placeholderData: keepPreviousData,
    enabled,
    // A reviewer leaves the queue open while uploads arrive.
    refetchInterval: 60_000,
  });
}

/** The caller's own held batches, newest first, decided ones included. */
export function useMyReviewBatches(page: number, pageSize: number) {
  return useQuery({
    queryKey: queryKeys.reviewMine(page, pageSize),
    queryFn: () => unwrap(api.GET('/api/v1/review/mine', { params: { query: { page, pageSize } } })),
    placeholderData: keepPreviousData,
  });
}

/** One held batch with its lines: decided ones too, so a decision stays readable. */
export function useReviewBatch(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.reviewBatch(id ?? ''),
    queryFn: () => unwrap(api.GET('/api/v1/review/batches/{id}', { params: { path: { id: id! } } })),
    enabled: Boolean(id),
  });
}

/** The sizes the server draws a held picture at: the line's thumbnail, and the larger view. */
export type HeldPreviewSize = 480 | 1200;

/**
 * A rendering of the picture a held photo or raster map keeps, as an address an image can show.
 *
 * The route needs the account's bearer, which an image element cannot send, so the bytes are read
 * through the client and handed to the image as an object address, let go when the picture leaves
 * the screen. The server draws it from the waiting bytes without any of their metadata.
 */
export function useHeldPreview(reviewId: string, size: HeldPreviewSize, enabled: boolean) {
  const query = useQuery({
    queryKey: queryKeys.heldPreview(reviewId, size),
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/review/items/{reviewId}/preview', {
          params: { path: { reviewId }, query: { size } },
          parseAs: 'blob',
        }) as Promise<{ data?: Blob; error?: unknown; response: Response }>,
      ),
    enabled,
    staleTime: Infinity,
    retry: retryUnlessRefused,
  });
  const [url, setUrl] = useState<string>();
  useEffect(() => {
    if (!query.data) {
      setUrl(undefined);
      return undefined;
    }
    const address = URL.createObjectURL(query.data);
    setUrl(address);
    return () => URL.revokeObjectURL(address);
  }, [query.data]);
  return { url, loading: query.isLoading, failed: query.isError };
}

/** What a decision on a batch or a line changes: the review, and whatever an accepted line wrote. */
function useInvalidateAfterDecision() {
  const queryClient = useQueryClient();
  return () => {
    for (const key of ['review', 'import-batches', 'features', 'caves', 'trip-logs', 'history', 'documents', 'raster-maps']) {
      void queryClient.invalidateQueries({ queryKey: [key] });
    }
  };
}

export interface BatchDecision {
  id: string;
  note?: string;
  overCurrent?: boolean;
}

export interface ItemDecision {
  reviewId: string;
  /** The subset of an edit's fields to write; left out, the whole line is decided. */
  fields?: string[];
  note?: string;
  overCurrent?: boolean;
}

function clean(note: string | undefined): string | undefined {
  const trimmed = note?.trim();
  return trimmed ? trimmed : undefined;
}

export function useAcceptReviewBatch() {
  const invalidate = useInvalidateAfterDecision();
  return useMutation({
    mutationFn: ({ id, note, overCurrent }: BatchDecision) =>
      unwrap(api.POST('/api/v1/review/batches/{id}/accept', {
        params: { path: { id } },
        body: { note: clean(note), overCurrent },
      })),
    onSettled: invalidate,
  });
}

export function useRejectReviewBatch() {
  const invalidate = useInvalidateAfterDecision();
  return useMutation({
    mutationFn: ({ id, note }: BatchDecision) =>
      unwrap(api.POST('/api/v1/review/batches/{id}/reject', {
        params: { path: { id } },
        body: { note: clean(note) },
      })),
    onSettled: invalidate,
  });
}

export function useAcceptReviewItem() {
  const invalidate = useInvalidateAfterDecision();
  return useMutation({
    mutationFn: ({ reviewId, fields, note, overCurrent }: ItemDecision) =>
      unwrap(api.POST('/api/v1/review/items/{reviewId}/accept', {
        params: { path: { reviewId } },
        body: { fields, note: clean(note), overCurrent },
      })),
    onSettled: invalidate,
  });
}

export function useRejectReviewItem() {
  const invalidate = useInvalidateAfterDecision();
  return useMutation({
    mutationFn: ({ reviewId, note }: ItemDecision) =>
      unwrap(api.POST('/api/v1/review/items/{reviewId}/reject', {
        params: { path: { reviewId } },
        body: { note: clean(note) },
      })),
    onSettled: invalidate,
  });
}

/**
 * The review ids a changed-since refusal names, or null when the error is not one.
 *
 * The ids ride in the problem's own members rather than in its sentence, so rewording the
 * sentence cannot break the screen that offers the choice.
 */
export function changedSinceIds(error: unknown): string[] | null {
  if (!(error instanceof ApiError) || error.status !== 409 || error.code !== REVIEW_CHANGED_SINCE) {
    return null;
  }
  const ids = error.problem?.reviewIds;
  return Array.isArray(ids) ? ids.filter((x): x is string => typeof x === 'string') : [];
}
