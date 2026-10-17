// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';

/** The membership editor of a feature set. Every name and identifier below is invented. */

const MEMBER = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const OTHER = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';

const { replaceSpy, queryCalls, stored } = vi.hoisted(() => ({
  replaceSpy: vi.fn(),
  queryCalls: [] as { body: unknown; enabled: boolean }[],
  // One array for every render, as a query cache would hand back: the drawer copies the members
  // into its draft whenever this changes.
  stored: {
    sets: [{ id: '99999999-9999-4999-8999-999999999999', name: 'Zona de test', description: null, memberCount: 1 }],
    members: [{ id: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', name: 'Peștera Deja', kind: 'cave' }],
  },
}));

const hit = (id: string, title: string, subtitle: string) =>
  ({ world: 'feature', id, title, subtitle, symbol: null, placeable: false });

vi.mock('../../api/hooks.ts', () => ({
  hasAccessAction: () => true,
  useCapabilities: () => ({ data: { domains: { featureSets: 'read,write' } } }),
  useFeatureSets: () => ({
    data: stored.sets,
    isPending: false,
  }),
  useCreateFeatureSet: () => ({ mutateAsync: vi.fn() }),
  useDeleteFeatureSet: () => ({ mutateAsync: vi.fn() }),
  useUpdateFeatureSet: () => ({ mutateAsync: vi.fn() }),
  useFeatureSetMembers: () => ({
    data: stored.members,
    isPending: false,
  }),
  useReplaceFeatureSetMembers: () => ({ mutateAsync: replaceSpy, isPending: false }),
  useFilterVocabulary: () => ({
    data: { worlds: [{ world: 'feature', labelKey: 'x', fields: [], sorts: [] }] },
  }),
  // The search answers with the member the set already holds and one other.
  useFilterQuery: (body: unknown, enabled: boolean) => {
    queryCalls.push({ body, enabled });
    return enabled
      ? {
          data: {
            worlds: [{
              world: 'feature',
              hits: [hit(MEMBER, 'Peștera Deja', 'Cave'), hit(OTHER, 'Izvorul Nou', 'Spring')],
              total: null,
            }],
            page: 1, pageSize: 6, counted: false,
          },
          isFetching: false,
        }
      : { data: undefined, isFetching: false };
  },
  useFilterResolve: () => ({ data: [] }),
}));

const { default: FeatureSetsPage } = await import('./FeatureSetsPage.tsx');

async function openMembersAndSearch() {
  render(
    <App>
      <FeatureSetsPage />
    </App>,
  );
  fireEvent.click(screen.getByRole('button', { name: 'Edit features' }));
  fireEvent.change(await screen.findByTestId('feature-set-member-picker-input'), {
    target: { value: 'iz' },
  });
  // The search is debounced; a loaded machine can take longer than the default second to answer.
  return screen.findByRole('button', { name: /Izvorul Nou/ }, { timeout: 3000 });
}

afterEach(cleanup);
beforeEach(() => {
  replaceSpy.mockReset().mockResolvedValue(undefined);
  queryCalls.length = 0;
});

describe('the feature set member picker', () => {
  it('searches the whole feature world and leaves out what the set already holds', async () => {
    await openMembersAndSearch();

    const document = JSON.stringify(queryCalls.filter((c) => c.enabled).at(-1)!.body);
    expect(document).toContain('"feature"');
    expect(document).not.toContain('"kind"');
    const offered = screen.getAllByTestId('feature-set-member-picker-row').map((r) => r.textContent);
    expect(offered).toEqual([expect.stringContaining('Izvorul Nou')]);
  });

  it('adds the pick under its row’s name and type, and saves the whole membership', async () => {
    const row = await openMembersAndSearch();
    const add = screen.getByRole('button', { name: /Add/ });
    expect(add).toBeDisabled();

    fireEvent.click(row);
    fireEvent.click(add);

    const drawer = screen.getByRole('dialog');
    expect(within(drawer).getByText('Spring')).toBeTruthy();
    expect(within(drawer).getAllByText('Izvorul Nou').length).toBeGreaterThan(0);
    // Added once: it leaves the search, and the button waits for the next pick.
    expect(screen.queryAllByTestId('feature-set-member-picker-row')).toHaveLength(0);
    expect(add).toBeDisabled();

    fireEvent.click(within(drawer).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(replaceSpy).toHaveBeenCalledWith([MEMBER, OTHER]));
  });

  it('picks a feature from the keyboard alone', async () => {
    const row = await openMembersAndSearch();

    fireEvent.keyDown(screen.getByTestId('feature-set-member-picker-input'), { key: 'ArrowDown' });
    expect(row).toHaveFocus();
    fireEvent.keyDown(row, { key: 'Enter' });

    expect(screen.getByRole('button', { name: /Add/ })).toBeEnabled();
  });
});
