// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { SyncSet } from '../../api/hooks.ts';

const createMutate = vi.fn();

let sets: SyncSet[] = [];
let cavePage: { id: string; name: string }[] = [];
let namesById = new Map<string, string>();
let requestedNameIds: readonly string[] = [];

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useSyncCapabilities: () => ({ data: undefined }),
    useSyncSets: () => ({ data: sets, isPending: false }),
    useCavingGroups: () => ({ data: [] }),
    useCaves: () => ({ data: { items: cavePage, page: 1, pageSize: 50, totalItems: cavePage.length } }),
    useCaveNames: (ids: readonly string[]) => {
      requestedNameIds = ids;
      return new Map([...namesById].filter(([id]) => ids.includes(id)));
    },
    useCreateSyncSet: () => ({ mutateAsync: createMutate, isPending: false }),
    useUpdateSyncSet: () => ({ mutateAsync: vi.fn(), isPending: false }),
    useDeleteSyncSet: () => ({ mutateAsync: vi.fn(), isPending: false }),
  };
});

const { default: SyncSettingsPage } = await import('./SyncSettingsPage.tsx');

function setOf(rootFeatureIds: string[]): SyncSet {
  return {
    id: 'set-1',
    name: 'Field phone',
    cavingGroupId: null,
    uploadVisibility: 'private',
    rootFeatureIds,
    settings: {},
    revision: 1,
    createdAt: '2026-08-01T00:00:00Z',
    updatedAt: '2026-08-01T00:00:00Z',
  } as unknown as SyncSet;
}

function renderPage() {
  return render(
    <App>
      <SyncSettingsPage />
    </App>,
  );
}

beforeEach(() => {
  createMutate.mockReset();
  sets = [];
  cavePage = [];
  namesById = new Map();
  requestedNameIds = [];
});

afterEach(cleanup);

describe('the caves a phone carries', () => {
  it('names a chosen cave the current page of results does not contain', async () => {
    // The cave is real and readable; it simply is not in the page the picker last fetched, which
    // is the ordinary case on an installation with more caves than one page holds.
    cavePage = [{ id: 'cave-in-page', name: 'Peștera din pagină' }];
    namesById = new Map([['cave-off-page', 'Peștera Neagră']]);
    sets = [setOf(['cave-in-page', 'cave-off-page'])];

    renderPage();

    expect(await screen.findByText('Peștera Neagră')).toBeTruthy();
    // The label reserved for a cave the caller genuinely cannot read must not be used for it.
    expect(screen.queryByText('A cave you can no longer read')).toBeNull();

    // Only the unknown one is asked for by id; the page already named the other.
    expect([...requestedNameIds]).toEqual(['cave-off-page']);
  });

  it('keeps the cannot-read label for an id the server would not name', async () => {
    cavePage = [];
    namesById = new Map();
    sets = [setOf(['cave-gone'])];

    renderPage();

    expect(await screen.findByText('A cave you can no longer read')).toBeTruthy();
  });

  it('says which part of the selection the server refused, not merely that it failed', async () => {
    createMutate.mockRejectedValue(new ApiError(403, 'sync.caving_group_forbidden'));

    renderPage();
    fireEvent.click(screen.getByTestId('sync-set-new'));
    fireEvent.change(screen.getByTestId('sync-set-name'), { target: { value: 'Club phone' } });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    // The form validates before it submits, and validation is asynchronous: under a loaded
    // suite the submit lands a beat after the click, so this waits rather than assuming.
    await waitFor(() => expect(createMutate).toHaveBeenCalled(), { timeout: 5000 });
    expect(
      await screen.findByText(
        'You are not a member of that caving group, so a device cannot create records for it.',
      ),
    ).toBeTruthy();
  });
});
