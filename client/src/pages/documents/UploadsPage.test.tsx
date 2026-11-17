// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { api } from '../../api/client.ts';

vi.mock('../../components/reslinks/permissions.ts', () => ({ useIsFullAdmin: () => false }));

const { default: UploadsPage } = await import('./UploadsPage.tsx');

/** An invented directory import, as the server describes it while it walks and once it is done. */
function batch(status: 'running' | 'completed' | 'failed', error: string | null = null) {
  return {
    id: 'batch-1',
    source: 'serverDirectory',
    status,
    label: 'Sample import',
    cabinetId: null,
    tagId: null,
    sourceDescription: null,
    totalCount: 3,
    storedCount: 2,
    skippedCount: 0,
    failedCount: 1,
    error,
    createdAt: '2026-07-15T10:00:00Z',
    completedAt: status === 'running' ? null : '2026-07-15T10:05:00Z',
  };
}

const page = <T,>(items: T[]) => ({ items, page: 1, pageSize: 50, totalItems: items.length });

let current = batch('running');
let gets: string[] = [];

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  current = batch('running');
  gets = [];
  vi.spyOn(api, 'GET').mockImplementation(((path: string) => {
    gets.push(path);
    const data =
      path === '/api/v1/upload-batches'
        ? page([current])
        : path === '/api/v1/upload-batches/{id}'
          ? current
          : page([]);
    return Promise.resolve({ data, error: undefined, response: new Response(null, { status: 200 }) });
  }) as never);
});

afterEach(async () => {
  cleanup();
  vi.restoreAllMocks();
  vi.useRealTimers();
  await i18n.changeLanguage('en');
});

function show() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <App>
        <UploadsPage />
      </App>
    </QueryClientProvider>,
  );
}

const count = (path: string) => gets.filter((p) => p === path).length;
const tick = (ms: number) => act(() => vi.advanceTimersByTimeAsync(ms));

describe('UploadsPage', () => {
  it('follows a running batch file by file, and stops asking once it has settled', async () => {
    show();
    fireEvent.click(await screen.findByText('Sample import'));
    await tick(100);
    const itemsAtOpen = count('/api/v1/upload-batches/{id}/items');

    // While the walk runs, the per-file report grows: it is asked for again, not read once.
    await tick(4100);
    expect(count('/api/v1/upload-batches/{id}/items')).toBeGreaterThan(itemsAtOpen);

    // Settled: one last look at what it did, then silence.
    current = batch('completed');
    await tick(2100);
    await tick(2100);
    const batchSettled = count('/api/v1/upload-batches/{id}');
    const itemsSettled = count('/api/v1/upload-batches/{id}/items');
    await tick(10000);
    expect(count('/api/v1/upload-batches/{id}')).toBe(batchSettled);
    expect(count('/api/v1/upload-batches/{id}/items')).toBe(itemsSettled);
  });

  it('names the counts column on its own and words a batch failure instead of showing its code', async () => {
    current = batch('failed', 'archive.unreadable');
    show();

    expect(await screen.findByRole('columnheader', { name: 'Files' })).toBeInTheDocument();
    // The failure is worded, never the bare code the server records.
    const table = screen.getByRole('table');
    expect(within(table).getByText(/The archive could not be opened/)).toBeInTheDocument();
    expect(within(table).queryByText(/archive\.unreadable/)).not.toBeInTheDocument();

    fireEvent.click(screen.getByText('Sample import'));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText('The archive could not be opened')).toBeInTheDocument();
    expect(within(dialog).queryByText('archive.unreadable')).not.toBeInTheDocument();
  });

  it('writes the start time in the language the app is shown in', async () => {
    await i18n.changeLanguage('ro');
    show();
    const when = new Date('2026-07-15T10:00:00Z').toLocaleString('ro');
    expect(await screen.findByText(when)).toBeInTheDocument();
  });
});
