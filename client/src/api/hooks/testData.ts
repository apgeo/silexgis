// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import { retryUnlessRefused, unwrap } from './shared.ts';
import type { TestDataProfile, TestDataRun } from './types.ts';

// ---- test data: the administrators' generator of invented data ----

const overviewKey = ['test-data'] as const;

/** Whether a run or its removal is still going, so the page keeps asking while it does. */
export function testDataRunIsBusy(run: Pick<TestDataRun, 'status'>): boolean {
  return run.status === 'queued' || run.status === 'running' || run.status === 'removing';
}

/**
 * The generator's state. Asked again every two seconds while a run or a removal is going, and not
 * at all otherwise: a page left open on a finished run has nothing to watch.
 */
export function useTestDataOverview() {
  return useQuery({
    queryKey: overviewKey,
    queryFn: () => unwrap(api.GET('/api/v1/admin/test-data')),
    retry: retryUnlessRefused,
    refetchInterval: (query) => (query.state.data?.runs.some(testDataRunIsBusy) ? 2000 : false),
  });
}

/** How many rows of each table a run accounts for, and how many of them fill each column. */
export function useTestDataCoverage(runId: string | undefined) {
  return useQuery({
    queryKey: [...overviewKey, 'coverage', runId] as const,
    queryFn: () =>
      unwrap(api.GET('/api/v1/admin/test-data/runs/{id}/coverage', { params: { path: { id: runId! } } })),
    enabled: runId !== undefined,
    retry: retryUnlessRefused,
  });
}

function useAfterTestData() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: overviewKey });
  };
}

/** Starts a run; refusals (existing data to confirm, a run in progress, a bad profile) come back as ApiError. */
export function useStartTestDataRun() {
  const after = useAfterTestData();
  return useMutation({
    mutationFn: (request: { profile: TestDataProfile; confirmAlongsideExistingData: boolean }) =>
      unwrap(
        api.POST('/api/v1/admin/test-data/runs', {
          body: { profile: request.profile, profileName: null, confirmAlongsideExistingData: request.confirmAlongsideExistingData },
        }),
      ),
    onSuccess: after,
  });
}

export function useCancelTestDataRun() {
  const after = useAfterTestData();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/v1/admin/test-data/runs/{id}/cancel', { params: { path: { id } } })),
    onSuccess: after,
  });
}

export function useRemoveTestDataRun() {
  const after = useAfterTestData();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(
        api.POST('/api/v1/admin/test-data/runs/{id}/remove', { params: { path: { id } }, body: { confirm: true } }),
      ),
    onSuccess: after,
  });
}
