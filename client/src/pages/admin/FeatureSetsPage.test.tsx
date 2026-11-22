// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import { afterRejectionsAreReported } from '../../testEventLoop.ts';

/** The membership editor of a feature set. Every name and identifier below is invented. */

const MEMBER = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const OTHER = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const TYPED = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';

const { replaceSpy, createSpy, refetchMembers, queryCalls, stored, failedRead } = vi.hoisted(() => ({
  // Which read did not land, switched on by the test that needs it.
  failedRead: { sets: false, members: false, refetch: vi.fn() },
  replaceSpy: vi.fn(),
  refetchMembers: vi.fn(),
  createSpy: vi.fn(),
  queryCalls: [] as { body: unknown; enabled: boolean }[],
  // One array for every render, as a query cache would hand back: the drawer copies the members
  // into its draft whenever this changes.
  stored: {
    membersFailed: false,
    sets: [{ id: '99999999-9999-4999-8999-999999999999', name: 'Zona de test', description: null, memberCount: 1 }],
    members: [
      { id: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa', name: 'Peștera Deja', kind: 'cave', typeName: null },
      { id: 'cccccccc-cccc-4ccc-8ccc-cccccccccccc', name: 'Dolina Mare', kind: 'generic', typeName: 'Dolină test' },
    ],
  },
}));

const hit = (id: string, title: string, subtitle: string, symbol: string) =>
  ({ world: 'feature', id, title, subtitle, symbol, placeable: false });

vi.mock('../../api/hooks.ts', () => ({
  hasAccessAction: () => true,
  useCapabilities: () => ({ data: { domains: { featureSets: 'read,write' } } }),
  useFeatureSets: () =>
    failedRead.sets
      ? { data: undefined, isPending: false, isError: true, error: new TypeError('offline'), refetch: failedRead.refetch }
      : { data: stored.sets, isPending: false },
  useCreateFeatureSet: () => ({ mutateAsync: createSpy, isPending: false }),
  useDeleteFeatureSet: () => ({ mutateAsync: vi.fn() }),
  useUpdateFeatureSet: () => ({ mutateAsync: vi.fn() }),
  useFeatureSetMembers: () =>
    failedRead.members
      ? { data: undefined, isPending: false, isError: true, error: new TypeError('offline'), refetch: failedRead.refetch }
      : {
          data: stored.membersFailed ? undefined : stored.members,
          isPending: false,
          isError: stored.membersFailed,
          error: stored.membersFailed ? new TypeError('offline') : null,
          refetch: refetchMembers,
        },
  useReplaceFeatureSetMembers: () => ({ mutateAsync: replaceSpy, isPending: false }),
  useFeatureTypes: () => ({ data: [{ id: 7, code: 'spring', name: 'Izvor test' }] }),
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
              hits: [
                hit(MEMBER, 'Peștera Deja', 'Cave', 'cave'),
                hit(OTHER, 'Izvorul Nou', 'Spring · Peștera Deja · Bihor', 'spring'),
              ],
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
  createSpy.mockReset().mockResolvedValue(undefined);
  refetchMembers.mockReset();
  stored.membersFailed = false;
  queryCalls.length = 0;
  failedRead.sets = false;
  failedRead.members = false;
  failedRead.refetch.mockReset();
});

describe('failed reads of feature sets', () => {
  it('says the list of sets could not be read instead of showing none, and offers Retry', () => {
    failedRead.sets = true;
    render(
      <App>
        <FeatureSetsPage />
      </App>,
    );
    expect(screen.getByTestId('feature-sets-load-failed')).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: i18n.t('common.retry') }));
    expect(failedRead.refetch).toHaveBeenCalledTimes(1);
  });

  it('will not save a membership it never read, which would replace the real one', async () => {
    failedRead.members = true;
    render(
      <App>
        <FeatureSetsPage />
      </App>,
    );
    fireEvent.click(screen.getByRole('button', { name: 'Edit features' }));
    const drawer = await screen.findByRole('dialog');
    expect(within(drawer).getByTestId('feature-set-members-load-failed')).toBeTruthy();
    expect(within(drawer).queryByText(i18n.t('featureSets.noMembers'))).toBeNull();
    expect(within(drawer).getByRole('button', { name: 'Save' })).toBeDisabled();
    fireEvent.click(within(drawer).getByRole('button', { name: i18n.t('common.retry') }));
    expect(failedRead.refetch).toHaveBeenCalledTimes(1);
  });
});

describe('the feature set member drawer when the members cannot be read', () => {
  it('says so, offers a retry, and never saves an empty membership over the stored one', async () => {
    stored.membersFailed = true;
    render(
      <App>
        <FeatureSetsPage />
      </App>,
    );
    fireEvent.click(screen.getByRole('button', { name: 'Edit features' }));
    const drawer = await screen.findByRole('dialog');

    expect(
      within(drawer).getByText('The set’s members could not be read, so its membership cannot be changed yet.'),
    ).toBeTruthy();
    fireEvent.click(within(drawer).getByRole('button', { name: 'Retry' }));
    expect(refetchMembers).toHaveBeenCalledTimes(1);

    const save = within(drawer).getByRole('button', { name: 'Save' });
    expect(save).toBeDisabled();
    fireEvent.click(save);
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(replaceSpy).not.toHaveBeenCalled();
  });
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
    // Named by its feature type, as a saved member is — not by the search row's context line.
    expect(within(drawer).getByText('Izvor test')).toBeTruthy();
    expect(within(drawer).queryByText(/Spring/)).toBeNull();
    expect(within(drawer).getAllByText('Izvorul Nou').length).toBeGreaterThan(0);
    // Added once: it leaves the search, and the button waits for the next pick.
    expect(screen.queryAllByTestId('feature-set-member-picker-row')).toHaveLength(0);
    expect(add).toBeDisabled();

    fireEvent.click(within(drawer).getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(replaceSpy).toHaveBeenCalledWith([MEMBER, TYPED, OTHER]));
  });

  it('names a saved member by its feature type, and one with only a kind by its kind', async () => {
    await openMembersAndSearch();
    const drawer = screen.getByRole('dialog');
    expect(within(drawer).getByText('Dolină test')).toBeTruthy();
    expect(within(drawer).getByText('Cave')).toBeTruthy();
  });

  it('picks a feature from the keyboard alone', async () => {
    const row = await openMembersAndSearch();

    fireEvent.keyDown(screen.getByTestId('feature-set-member-picker-input'), { key: 'ArrowDown' });
    expect(row).toHaveFocus();
    fireEvent.keyDown(row, { key: 'Enter' });

    expect(screen.getByRole('button', { name: /Add/ })).toBeEnabled();
  });
});

describe('creating a feature set', () => {
  it('names a refused name in words rather than as a failed save', async () => {
    createSpy.mockRejectedValue(new ApiError(400, 'feature_set.name_invalid'));
    render(
      <App>
        <FeatureSetsPage />
      </App>,
    );

    fireEvent.click(screen.getByRole('button', { name: /New feature set/ }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.change(within(dialog).getByLabelText('Name'), { target: { value: '***' } });
    fireEvent.click(within(dialog).getByRole('button', { name: 'OK' }));

    await waitFor(() => expect(createSpy).toHaveBeenCalledWith({ name: '***', description: null }));
    expect(await screen.findByText('A feature set name needs at least one letter or digit.')).toBeTruthy();
    expect(screen.queryByText('The operation failed. Please try again.')).toBeNull();
  });

  it('OK with no name marks the field, sends nothing and leaves no unhandled rejection', async () => {
    createSpy.mockClear();
    const unhandled: unknown[] = [];
    const record = (reason: unknown) => unhandled.push(reason);
    process.on('unhandledRejection', record);
    try {
      render(
        <App>
          <FeatureSetsPage />
        </App>,
      );
      fireEvent.click(screen.getByRole('button', { name: /New feature set/ }));
      const dialog = await screen.findByRole('dialog');
      const name = within(dialog).getByLabelText('Name');
      fireEvent.click(within(dialog).getByRole('button', { name: 'OK' }));
      await vi.waitFor(() => expect(name.getAttribute('aria-invalid')).toBe('true'));
      await afterRejectionsAreReported();
      expect(createSpy).not.toHaveBeenCalled();
      expect(unhandled).toEqual([]);
    } finally {
      process.off('unhandledRejection', record);
    }
  });
});
