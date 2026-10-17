// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { SyncSet } from '../../api/hooks.ts';

const createMutate = vi.fn();
const updateSpy: { current: ReturnType<typeof vi.fn> } = { current: vi.fn() };

let sets: SyncSet[] = [];
/** What the filter route will describe by id: the caves this caller may read. */
let readable = new Map<string, string>();
/** What a search for caves answers, whatever was typed. */
let searchHits: string[] = [];
const resolveCalls: { world: string; ids: readonly string[] }[] = [];
const queryBodies: unknown[] = [];

const hit = (id: string) =>
  ({ world: 'feature', id, title: readable.get(id) ?? id, subtitle: 'Cave', symbol: null, placeable: false });

vi.mock('../../api/hooks.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/hooks.ts')>('../../api/hooks.ts');
  return {
    ...actual,
    useSyncCapabilities: () => ({ data: undefined }),
    useSyncSets: () => ({ data: sets, isPending: false }),
    useCavingGroups: () => ({ data: [] }),
    useFilterVocabulary: () => ({
      data: { worlds: [{ world: 'feature', labelKey: 'x', fields: [], sorts: [] }] },
    }),
    useFilterQuery: (body: unknown, enabled: boolean) => {
      if (enabled) {
        queryBodies.push(body);
      }
      return enabled
        ? {
            data: {
              worlds: [{ world: 'feature', hits: searchHits.map(hit), total: null }],
              page: 1, pageSize: 8, counted: false,
            },
            isFetching: false,
          }
        : { data: undefined, isFetching: false };
    },
    useFilterResolve: (world: string, ids: readonly string[]) => {
      resolveCalls.push({ world, ids });
      return { data: ids.filter((id) => readable.has(id)).map(hit) };
    },
    useCreateSyncSet: () => ({ mutateAsync: createMutate, isPending: false }),
    useUpdateSyncSet: () => ({ mutateAsync: updateSpy.current, isPending: false }),
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
  readable = new Map();
  searchHits = [];
  resolveCalls.length = 0;
  queryBodies.length = 0;
});

afterEach(cleanup);

describe('the caves a phone carries', () => {
  it('names every stored cave by its id, whatever a search last found', async () => {
    // Nothing has been searched for; the caves are named because the caller can read them.
    readable = new Map([['cave-a', 'Peștera din pagină'], ['cave-b', 'Peștera Neagră']]);
    sets = [setOf(['cave-a', 'cave-b'])];

    renderPage();

    expect(await screen.findByText('Peștera Neagră')).toBeTruthy();
    expect(screen.getByText('Peștera din pagină')).toBeTruthy();
    // The label reserved for a cave the caller genuinely cannot read must not be used for either.
    expect(screen.queryByText('A cave you can no longer read')).toBeNull();
    expect(resolveCalls.some((c) => c.world === 'feature' && c.ids.includes('cave-b'))).toBe(true);
  });

  it('keeps the cannot-read label for an id the server would not name', async () => {
    readable = new Map([['cave-a', 'Peștera din pagină']]);
    sets = [setOf(['cave-a', 'cave-gone'])];

    renderPage();

    expect(await screen.findByText('A cave you can no longer read')).toBeTruthy();
    expect(screen.getByText('Peștera din pagină')).toBeTruthy();
  });

  it('searches caves only, and adds and takes away caves of a set being edited', async () => {
    const updateMutate = vi.fn().mockResolvedValue({});
    updateSpy.current = updateMutate;
    readable = new Map([['cave-a', 'Peștera Urșilor'], ['cave-b', 'Peștera Neagră'], ['cave-c', 'Peștera Vântului']]);
    searchHits = ['cave-b', 'cave-c'];
    sets = [setOf(['cave-a', 'cave-b'])];

    renderPage();
    fireEvent.click(screen.getByRole('button', { name: 'Edit' }));
    fireEvent.change(await screen.findByTestId('sync-set-caves-input'), { target: { value: 'pe' } });

    // The search is debounced; a loaded machine can take longer than the default second to answer.
    const added = await screen.findByRole('button', { name: /Peștera Vântului/ }, { timeout: 3000 });
    const asked = JSON.stringify(queryBodies.at(-1));
    expect(asked).toContain('"kind"');
    expect(asked).toContain('"cave"');

    // The stored ones are listed ticked, named by their id; the search's other find is not.
    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByRole('button', { name: /Peștera Urșilor/ })).toHaveAttribute('aria-pressed', 'true');
    expect(within(dialog).getByRole('button', { name: /Peștera Neagră/ })).toHaveAttribute('aria-pressed', 'true');
    expect(added).toHaveAttribute('aria-pressed', 'false');

    fireEvent.click(added);
    fireEvent.keyDown(within(dialog).getByRole('button', { name: /Peștera Urșilor/ }), { key: 'Enter' });
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(updateMutate).toHaveBeenCalled(), { timeout: 5000 });
    expect(updateMutate.mock.calls[0][0].body.rootFeatureIds).toEqual(['cave-b', 'cave-c']);
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
