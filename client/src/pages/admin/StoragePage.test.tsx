// SPDX-License-Identifier: AGPL-3.0-or-later
import { App, ConfigProvider } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { StorageReport } from '../../api/hooks.ts';

const GB = 1024 ** 3;

/** An invented report: two kinds, one cave, one account, one large file, two groups. */
function invented(overrides: Partial<StorageReport> = {}): StorageReport {
  return {
    takenAt: '2026-11-08T03:00:00Z',
    storeUsedBytes: 92 * GB,
    storeCapBytes: 100 * GB,
    scanUsedBytes: 40 * GB,
    scanCapBytes: null,
    storeFreeBytes: 30 * GB,
    workerFreeBytes: 25 * GB,
    minFreeBytes: 20 * GB,
    level: 'warning',
    byFileKind: [
      { kind: 'image', bytes: 10 * GB, files: 400 },
      { kind: 'model', bytes: 82 * GB, files: 12 },
    ],
    scanByKind: [{ kind: 'point-cloud-tiles', bytes: 30 * GB, files: 3 }],
    workDirectoryBytes: 2 * GB,
    byCave: [{ id: 'c1', name: 'Invented cave', bytes: 50 * GB, files: 20 }],
    byAccount: [{ id: 'u1', name: 'Ana Example', bytes: 60 * GB, files: 30 }],
    largest: [
      {
        fileId: 'f1',
        name: 'invented.laz',
        kind: 'model',
        bytes: 8 * GB,
        documentId: 'd1',
        ownerUserId: 'u1',
        ownerName: 'Ana Example',
        caveId: 'c1',
        caveName: 'Invented cave',
        createdAt: '2026-11-01T10:00:00Z',
      },
    ],
    growth: {
      sevenDayBytes: 7 * GB,
      thirtyDayBytes: 20 * GB,
      bytesPerDay: GB,
      fullOn: '2026-11-16',
      scanBytesPerDay: 512 * 1024 ** 2,
      scansFullOn: '2026-12-20',
    },
    reclaimable: [
      { group: 'superseded-scan-products', items: 3, bytes: 12 * GB },
      { group: 'expired-uploads', items: 0, bytes: 0 },
    ],
    ...overrides,
  };
}

let report: { data: StorageReport | null | undefined; isLoading: boolean; isError: boolean };
let canExecute = true;
const measure = vi.fn();
const reclaim = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useStorageReport: () => report,
  useMeasureStorage: () => ({ mutateAsync: measure, isPending: false }),
  useReclaimStorage: () => ({ mutateAsync: reclaim, isPending: false }),
  useCapabilities: () => ({ data: { domains: { settings: canExecute ? ['read', 'execute'] : ['read'] } } }),
  hasAccessAction: (actions: string[] | undefined, action: string) => actions?.includes(action) ?? false,
}));

const { default: StoragePage } = await import('./StoragePage.tsx');

function show() {
  return render(
    <MemoryRouter>
      <ConfigProvider theme={{ token: { motion: false } }}>
        <App>
          <StoragePage />
        </App>
      </ConfigProvider>
    </MemoryRouter>,
  );
}

describe('the storage page', () => {
  beforeEach(() => {
    report = { data: invented(), isLoading: false, isError: false };
    canExecute = true;
    measure.mockReset().mockResolvedValue(invented());
    reclaim.mockReset().mockResolvedValue({ group: 'superseded-scan-products', items: 3, freedBytes: 12 * GB });
  });

  afterEach(cleanup);

  it('shows the totals against the cap, the level, the growth and the largest files', () => {
    show();
    expect(screen.getByText('92% of 100.0 GB')).toBeInTheDocument();
    expect(screen.getByTestId('storage-level')).toHaveTextContent('warning threshold');
    expect(within(screen.getByTestId('storage-growth')).getByText('7.0 GB')).toBeInTheDocument();
    expect(within(screen.getByTestId('storage-largest')).getByText('invented.laz')).toBeInTheDocument();
    expect(screen.getByText('Tilesets')).toBeInTheDocument();
  });

  it('shows the day the point clouds\' own cap fills only when there is one', () => {
    show();
    expect(screen.queryByTestId('storage-scans-full-on')).toBeNull();
    cleanup();

    report = { data: invented({ scanCapBytes: 50 * GB }), isLoading: false, isError: false };
    show();
    // A date, in whatever form the page's language writes one, not the "not in sight" wording.
    expect(screen.getByTestId('storage-scans-full-on')).toHaveTextContent('2026');
    expect(within(screen.getByTestId('storage-growth')).getByText('512.0 MB')).toBeInTheDocument();
  });

  it('offers to measure when no report has been written yet', () => {
    report = { data: null, isLoading: false, isError: false };
    show();
    expect(screen.getByTestId('storage-no-report')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('storage-measure'));
    expect(measure).toHaveBeenCalledTimes(1);
  });

  it('deletes a group only after it is confirmed, then measures again', async () => {
    show();
    // An empty group offers nothing to delete.
    expect(screen.getByTestId('storage-reclaim-expired-uploads')).toBeDisabled();

    fireEvent.click(screen.getByTestId('storage-reclaim-superseded-scan-products'));
    expect(reclaim).not.toHaveBeenCalled();
    fireEvent.click(await screen.findByRole('button', { name: 'Delete them' }));

    await waitFor(() => expect(reclaim).toHaveBeenCalledWith('superseded-scan-products'));
    await waitFor(() => expect(measure).toHaveBeenCalledTimes(1));
  });

  it('shows the figures but no delete or measure to an administrator who may only read', () => {
    canExecute = false;
    show();
    expect(screen.getByTestId('storage-reclaimable')).toBeInTheDocument();
    expect(screen.queryByTestId('storage-reclaim-superseded-scan-products')).toBeNull();
    expect(screen.queryByTestId('storage-measure')).toBeNull();
  });
});
