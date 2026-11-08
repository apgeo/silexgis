// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { retryUnlessRefused, unwrap } from './shared.ts';

// ---- storage: how much room there is, and what fills the file store ----

export type StorageRoom = components['schemas']['StorageRoomDto'];
export type StorageShortfall = components['schemas']['StorageShortfallDto'];
export type StorageReport = components['schemas']['StorageReportDto'];
export type StorageKindTotal = components['schemas']['StorageKindTotalDto'];
export type StorageHolder = components['schemas']['StorageHolderDto'];
export type StorageLargeItem = components['schemas']['StorageLargeItemDto'];
export type StorageReclaimable = components['schemas']['StorageReclaimableDto'];
export type StorageReclaimResult = components['schemas']['StorageReclaimResultDto'];

/** How full the installation is, as answers spell it. */
export type StorageLevel = 'normal' | 'notice' | 'warning' | 'full';

/** The ceiling a shortfall is under, as answers spell it. */
export type StorageLimit = 'account' | 'installation' | 'scans' | 'storeDisk' | 'workerDisk';

export interface StorageRoomParams {
  /** What is about to be added; nothing asked while it is not known. */
  addingBytes: number | null | undefined;
  /** Checked as a point cloud's: under the scans' own cap as well. */
  scan?: boolean;
  /** Checked as a processing run's: on the worker's disk too. */
  processing?: boolean;
}

/**
 * The totals and whether something of a given size fits, asked before it is sent. Advice only: the
 * upload and the run are checked again by the server when they arrive, so a stale answer here can
 * at worst show room that has since gone.
 */
export function useStorageRoom({ addingBytes, scan = false, processing = false }: StorageRoomParams) {
  return useQuery({
    queryKey: ['storage-room', addingBytes ?? null, scan, processing] as const,
    queryFn: () =>
      unwrap(
        api.GET('/api/v1/storage/room', {
          params: { query: { addingBytes: addingBytes ?? 0, scan, processing } },
        }),
      ),
    enabled: addingBytes !== null && addingBytes !== undefined && addingBytes >= 0,
    retry: retryUnlessRefused,
    staleTime: 30_000,
  });
}

const reportKey = ['storage-report'] as const;

/**
 * The latest storage report. Answers `null` before the first has been written (the server says
 * 404 `storage.report_missing`), so the page can offer to measure now rather than show an error.
 */
export function useStorageReport() {
  return useQuery({
    queryKey: reportKey,
    queryFn: async () => {
      const { data, error, response } = await api.GET('/api/v1/admin/storage/report');
      if (response.status === 404 && (error as { code?: string } | undefined)?.code === 'storage.report_missing') {
        return null;
      }
      return unwrap(Promise.resolve({ data, error, response }));
    },
    retry: retryUnlessRefused,
  });
}

/** Measures storage now; the answer becomes the latest report. */
export function useMeasureStorage() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => unwrap(api.POST('/api/v1/admin/storage/report')),
    onSuccess: (report) => {
      queryClient.setQueryData(reportKey, report);
      void queryClient.invalidateQueries({ queryKey: ['storage-room'] });
    },
  });
}

/**
 * Deletes one group the report offers. The report itself is not rewritten by the server, so the
 * page measures again afterwards to show the room given back.
 */
export function useReclaimStorage() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (group: string) =>
      unwrap(
        api.POST('/api/v1/admin/storage/reclaim/{group}', {
          params: { path: { group } },
          body: { confirm: true },
        }),
      ),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['storage-room'] });
    },
  });
}
