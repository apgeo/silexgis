// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';

const t = i18n.getFixedT('en');

/**
 * The people page, over the real query hooks and a faked server.
 *
 * Two things here live in how the page asks, not in what it draws, so the hooks are the real
 * ones: which right decides that the delete button is offered, and what the table shows while
 * the answer to a changed search is still on its way. Every name below is invented.
 */

const get = vi.fn();
const put = vi.fn();
const post = vi.fn();
const del = vi.fn();

vi.mock('../../api/client.ts', async () => {
  const actual = await vi.importActual<typeof import('../../api/client.ts')>('../../api/client.ts');
  return {
    ...actual,
    api: {
      GET: (...args: unknown[]) => get(...args),
      PUT: (...args: unknown[]) => put(...args),
      POST: (...args: unknown[]) => post(...args),
      DELETE: (...args: unknown[]) => del(...args),
    },
  };
});

const { default: CaversPage } = await import('./CaversPage.tsx');

const ANA = { id: 'caver-ana', name: 'Ana Invented', userId: null, email: null, phone: null, notes: null, cavingGroups: [] };
const BOGDAN = { ...ANA, id: 'caver-bogdan', name: 'Bogdan Invented' };

const answer = <T,>(data: T) => ({ data, response: new Response(null, { status: 200 }) });

/** What the account holds over the roster, as the server words it. */
let rosterRights = 'read, write, delete';
/** The roster answers still owed, by the search each was asked with. */
let owed: Record<string, (rows: unknown[]) => void> = {};

beforeEach(() => {
  rosterRights = 'read, write, delete';
  owed = {};
  get.mockImplementation((path: string, init?: { params?: { query?: { search?: string } } }) => {
    if (path === '/api/v1/me/capabilities') {
      return Promise.resolve(answer({ domains: { cavers: rosterRights } }));
    }
    if (path === '/api/v1/cavers') {
      const search = init?.params?.query?.search ?? '';
      if (search === '') {
        return Promise.resolve(answer([ANA, BOGDAN]));
      }
      return new Promise((resolve) => {
        owed[search] = (rows) => resolve(answer(rows));
      });
    }
    return Promise.reject(new Error(`unexpected read of ${path}`));
  });
});

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  vi.clearAllMocks();
});

function show() {
  return render(
    <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
      <MemoryRouter>
        <App>
          <CaversPage />
        </App>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('a failed read of the roster', () => {
  it('says the people could not be read, rather than drawing an empty roster', async () => {
    const roster = get.getMockImplementation()!;
    get.mockImplementation((path: string, init?: unknown) =>
      path === '/api/v1/cavers'
        ? Promise.resolve({ data: undefined, error: { status: 500 }, response: new Response(null, { status: 500 }) })
        : roster(path, init),
    );
    show();

    expect(await screen.findByTestId('cavers-load-failed')).toBeTruthy();
  });
});

describe('who is offered the delete button', () => {
  it('offers it to an account that may delete people', async () => {
    show();

    expect(await screen.findByTestId('caver-delete-caver-ana')).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Edit' })).toHaveLength(2);
  });

  it('does not offer it to an account that may edit the roster and may not delete from it', async () => {
    // Editing asks one right and deleting another. An account holding the first alone was
    // shown a delete button whose only possible answer was a refusal.
    rosterRights = 'read, write';
    show();

    // The positive half first, so the absence below is not that of a page still loading:
    // the row's other controls are there.
    await waitFor(() => expect(screen.getAllByRole('button', { name: 'Edit' })).toHaveLength(2));
    expect(screen.queryByTestId('caver-delete-caver-ana')).not.toBeInTheDocument();
    expect(screen.queryByTestId('caver-delete-caver-bogdan')).not.toBeInTheDocument();
  });
});

describe('the name a party calls somebody by', () => {
  it('is shown beside the full name, and goes back with a save that never touched it', async () => {
    // The server writes what a save gives it. A form that drew the short name and left it out
    // of what it sends would take it off everybody whose telephone number was corrected.
    get.mockImplementation((path: string) => {
      if (path === '/api/v1/me/capabilities') {
        return Promise.resolve(answer({ domains: { cavers: rosterRights } }));
      }
      if (path === '/api/v1/cavers') {
        return Promise.resolve(answer([{ ...ANA, shortName: 'Anca' }, BOGDAN]));
      }
      return Promise.reject(new Error(`unexpected read of ${path}`));
    });
    put.mockResolvedValue(answer({ ...ANA, shortName: 'Anca' }));
    show();

    expect(await screen.findByText('(Anca)')).toBeInTheDocument();
    fireEvent.click(screen.getAllByRole('button', { name: 'Edit' })[0]);
    const field = await screen.findByTestId('caver-short-name');
    expect(field).toHaveValue('Anca');

    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    await waitFor(() => expect(put).toHaveBeenCalledOnce());
    expect(put.mock.calls[0][1].body).toMatchObject({ fullName: 'Ana Invented', shortName: 'Anca' });
  });
});

describe('the groups somebody belongs to', () => {
  const OLD = { id: 'group-old', name: 'Old Club Invented' };
  const NEW = { id: 'group-new', name: 'New Club Invented' };
  const ANA_IN_OLD = { ...ANA, cavingGroups: [{ cavingGroupId: OLD.id, name: OLD.name, role: 'member' }] };

  beforeEach(() => {
    get.mockImplementation((path: string) => {
      if (path === '/api/v1/me/capabilities') {
        return Promise.resolve(answer({ domains: { cavers: rosterRights } }));
      }
      if (path === '/api/v1/cavers') {
        return Promise.resolve(answer([ANA_IN_OLD, BOGDAN]));
      }
      if (path === '/api/v1/caving-groups') {
        return Promise.resolve(answer([OLD, NEW]));
      }
      return Promise.reject(new Error(`unexpected read of ${path}`));
    });
    put.mockResolvedValue(answer(ANA_IN_OLD));
    post.mockResolvedValue(answer({ caverId: ANA.id, name: ANA.name, userId: null, role: 'member' }));
    del.mockResolvedValue({ response: new Response(null, { status: 204 }) });
  });

  /** Opens Ana's form and its list of groups. */
  async function openGroups() {
    show();
    fireEvent.click((await screen.findAllByRole('button', { name: 'Edit' }))[0]);
    const field = await screen.findByTestId('caver-caving-groups');
    fireEvent.mouseDown(field.querySelector('.ant-select-selector') ?? field);
  }

  it('writes no roster for a save that left them alone, and warns of nothing', async () => {
    show();
    fireEvent.click((await screen.findAllByRole('button', { name: 'Edit' }))[0]);
    await screen.findByTestId('caver-caving-groups');
    expect(screen.queryByTestId('caver-caving-groups-warning')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    await waitFor(() => expect(put).toHaveBeenCalledOnce());
    expect(post).not.toHaveBeenCalled();
    expect(del).not.toHaveBeenCalled();
  });

  it('moves somebody to another group: says what that means, then joins the new and leaves the old', async () => {
    await openGroups();
    fireEvent.click(await screen.findByTitle(NEW.name));
    // The warning appears with the first difference from what is stored.
    expect(await screen.findByTestId('caver-caving-groups-warning')).toHaveTextContent(/rights/);
    fireEvent.click(document.querySelector(`.ant-select-item-option[title="${OLD.name}"]`)!);

    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    await waitFor(() => expect(del).toHaveBeenCalledOnce());
    expect(post).toHaveBeenCalledOnce();
    expect(post.mock.calls[0][0]).toBe('/api/v1/caving-groups/{id}/members');
    expect(post.mock.calls[0][1]).toMatchObject({ params: { path: { id: NEW.id } }, body: { caverId: ANA.id, role: 'member' } });
    expect(del.mock.calls[0][1]).toMatchObject({ params: { path: { id: OLD.id, caverId: ANA.id } } });
    expect(await screen.findByText('Saved.')).toBeInTheDocument();
  });

  it('names the group that could not be changed, the person having been saved', async () => {
    post.mockResolvedValue({ error: { code: 'access.forbidden' }, response: new Response(null, { status: 403 }) });
    await openGroups();
    fireEvent.click(await screen.findByTitle(NEW.name));

    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    expect(await screen.findByText(/could not be changed: New Club Invented/)).toBeInTheDocument();
    expect(put).toHaveBeenCalledOnce();
  });

  it('says why when the server names the reason a group could not be changed', async () => {
    post.mockResolvedValue({ error: { code: 'caver.grants_full_admin' }, response: new Response(null, { status: 403 }) });
    await openGroups();
    fireEvent.click(await screen.findByTitle(NEW.name));

    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    expect(await screen.findByText(/could not be changed: New Club Invented/)).toBeInTheDocument();
    expect(await screen.findByText(t('cavers.rosterProblems.grantsFullAdmin'))).toBeInTheDocument();
  });

  it('words a refused save of the person, rather than a bare failure', async () => {
    put.mockResolvedValue({ error: { code: 'caver.revokes_full_admin' }, response: new Response(null, { status: 403 }) });
    show();
    fireEvent.click((await screen.findAllByRole('button', { name: 'Edit' }))[0]);
    await screen.findByTestId('caver-caving-groups');

    fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    expect(await screen.findByText(t('cavers.rosterProblems.revokesFullAdmin'))).toBeInTheDocument();
  });
});

describe('narrowing the list', () => {
  it('keeps the rows, and a confirmation open on one of them, until the next answer has come', async () => {
    show();
    fireEvent.click(await screen.findByTestId('caver-delete-caver-ana'));
    expect(await screen.findByText('Remove this person from the roster?')).toBeInTheDocument();

    // A changed search is a new question. While its answer is on its way the page has
    // nothing new to show — and used to show nothing at all, which took the open
    // confirmation away with the row it stood on.
    fireEvent.change(screen.getByPlaceholderText('Search by name…'), { target: { value: 'ana' } });
    await waitFor(() => expect(owed.ana).toBeDefined());

    expect(screen.getByText('Ana Invented')).toBeInTheDocument();
    expect(screen.getByText('Bogdan Invented')).toBeInTheDocument();
    expect(screen.getByText('Remove this person from the roster?')).toBeInTheDocument();

    // And the answer, once it has come, is what the table shows.
    await act(async () => owed.ana([ANA]));
    await waitFor(() => expect(screen.queryByText('Bogdan Invented')).not.toBeInTheDocument());
    expect(screen.getByText('Ana Invented')).toBeInTheDocument();
  });
});

describe('the way to the phone users', () => {
  it('is offered even to an account that may only read the roster', async () => {
    rosterRights = 'read';
    show();

    const link = await screen.findByTestId('cavers-phone-users');
    expect(link.closest('a')).toHaveAttribute('href', '/cavers/phone-users');
  });
});

describe('a save with the name emptied', () => {
  it('marks the field, sends nothing and leaves no unhandled rejection', async () => {
    const unhandled: unknown[] = [];
    const record = (reason: unknown) => unhandled.push(reason);
    process.on('unhandledRejection', record);
    try {
      show();
      fireEvent.click((await screen.findAllByRole('button', { name: 'Edit' }))[0]);
      const name = await screen.findByLabelText('Name');
      await waitFor(() => expect(name).toHaveValue('Ana Invented'));
      fireEvent.change(name, { target: { value: '' } });
      fireEvent.click(screen.getByRole('button', { name: 'OK' }));
      await vi.waitFor(() => expect(name.getAttribute('aria-invalid')).toBe('true'));
      // Long enough for a rejection with no handler to be reported.
      await new Promise((resolve) => setTimeout(resolve, 50));
      expect(put).not.toHaveBeenCalled();
      expect(unhandled).toEqual([]);
    } finally {
      process.off('unhandledRejection', record);
    }
  });
});

/** A refusal as the generated client hands it back: the problem body and the response it came in. */
const refusal = (status: number, code?: string, extra: Record<string, unknown> = {}) => ({
  error: { status, ...(code ? { code } : {}), ...extra },
  response: new Response(null, { status }),
});

async function confirmIn(popoverTitle: string) {
  const title = await screen.findByText(popoverTitle);
  const popover = title.closest('.ant-popover') as HTMLElement;
  await act(async () => {
    fireEvent.click(within(popover).getByRole('button', { name: /OK|Yes/ }));
  });
}

describe('removing somebody', () => {
  it('removes the person whose row it was', async () => {
    del.mockResolvedValue({ response: new Response(null, { status: 204 }) });
    show();

    fireEvent.click(await screen.findByTestId('caver-delete-caver-bogdan'));
    await confirmIn(t('cavers.deleteConfirm'));

    await waitFor(() =>
      expect(del).toHaveBeenCalledWith('/api/v1/cavers/{id}', { params: { path: { id: 'caver-bogdan' } } }),
    );
    expect(screen.queryByText(t('cavers.deleteRefused'))).not.toBeInTheDocument();
  });

  it.each([
    ['a camp roster holds them', refusal(409, 'caver.referenced_by_expeditions'), 'cavers.deleteRefusedCamp'],
    ['it would take away full rights from the caller', refusal(403, 'caver.revokes_full_admin'), 'cavers.rosterProblems.revokesFullAdmin'],
    ['it would leave no full administrator', refusal(409, 'permission_group.last_full_admin'), 'cavers.rosterProblems.lastFullAdmin'],
    ['the server failed', refusal(500), 'cavers.deleteRefused'],
  ])('says why when %s', async (_label, answered, key) => {
    del.mockResolvedValue(answered);
    show();

    fireEvent.click(await screen.findByTestId('caver-delete-caver-ana'));
    await confirmIn(t('cavers.deleteConfirm'));

    expect(await screen.findByText(t(key))).toBeInTheDocument();
  });

  it('lists the trips that still hold the person instead of a bare refusal', async () => {
    del.mockResolvedValue(
      refusal(409, 'caver.referenced_by_trips', {
        trips: [{ id: 'trip-1', title: 'An invented descent', tripDate: '2026-05-01', onRoster: true }],
      }),
    );
    show();

    fireEvent.click(await screen.findByTestId('caver-delete-caver-ana'));
    await confirmIn(t('cavers.deleteConfirm'));

    expect(await screen.findByText(t('cavers.heldByTrips.title', { name: 'Ana Invented' }))).toBeInTheDocument();
    expect(screen.getByText('An invented descent')).toBeInTheDocument();
    expect(screen.queryByText(t('cavers.deleteRefused'))).not.toBeInTheDocument();
  });
});

describe('merging a duplicate', () => {
  async function chooseDuplicateOf(rowIndex: number, duplicate: string) {
    fireEvent.click((await screen.findAllByRole('button', { name: t('cavers.merge') }))[rowIndex]);
    const dialog = (await screen.findByText(t('cavers.mergeHint'))).closest('.ant-modal') as HTMLElement;
    fireEvent.mouseDown(within(dialog).getByRole('combobox'));
    const option = await waitFor(() => {
      const found = document.querySelector(`.ant-select-dropdown [title="${duplicate}"]`);
      expect(found).not.toBeNull();
      return found as HTMLElement;
    });
    fireEvent.click(option);
    await act(async () => {
      fireEvent.click(within(dialog).getByRole('button', { name: 'OK' }));
    });
    return dialog;
  }

  it('folds the chosen duplicate into the person whose row it was', async () => {
    post.mockResolvedValue({ data: ANA, response: new Response(null, { status: 200 }) });
    show();

    await chooseDuplicateOf(0, 'Bogdan Invented');

    await waitFor(() =>
      expect(post).toHaveBeenCalledWith('/api/v1/cavers/{id}/merge', {
        params: { path: { id: 'caver-ana' } },
        body: { sourceCaverId: 'caver-bogdan' },
      }),
    );
    expect(await screen.findByText(t('common.saved'))).toBeInTheDocument();
  });

  it.each([
    ['would hand out full rights', refusal(403, 'caver.grants_full_admin'), 'cavers.rosterProblems.grantsFullAdmin'],
    ['failed on the server', refusal(500), 'common.saveFailed'],
  ])('says so when the merge %s, and keeps the dialog open', async (_label, answered, key) => {
    post.mockResolvedValue(answered);
    show();

    const dialog = await chooseDuplicateOf(0, 'Bogdan Invented');

    expect(await screen.findByText(t(key))).toBeInTheDocument();
    expect(screen.queryByText(t('common.saved'))).not.toBeInTheDocument();
    // The page closes the dialog by unmounting it, so still being in the document is still open.
    expect(dialog).toBeInTheDocument();
    expect(screen.getByText(t('cavers.mergeHint'))).toBeInTheDocument();
  });
});

describe('adding somebody', () => {
  async function addNamed(name: string) {
    fireEvent.click(await screen.findByRole('button', { name: new RegExp(t('cavers.new')) }));
    fireEvent.change(await screen.findByLabelText(t('cavers.name')), { target: { value: name } });
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'OK' }));
    });
  }

  it('sends the person as typed, with the fields left empty sent as nothing', async () => {
    post.mockResolvedValue({ data: { ...ANA, id: 'caver-cora', name: 'Cora Invented' }, response: new Response(null, { status: 201 }) });
    show();

    await addNamed('Cora Invented');

    await waitFor(() =>
      expect(post).toHaveBeenCalledWith('/api/v1/cavers', {
        body: { fullName: 'Cora Invented', shortName: null, email: null, phone: null, notes: null },
      }),
    );
    expect(await screen.findByText(t('common.saved'))).toBeInTheDocument();
  });

  it('words a refused addition and keeps what was typed', async () => {
    post.mockResolvedValue(refusal(403, 'access_entry.exceeds_own_rights'));
    show();

    await addNamed('Cora Invented');

    expect(await screen.findByText(t('cavers.rosterProblems.exceedsOwnRights'))).toBeInTheDocument();
    expect(screen.getByLabelText(t('cavers.name'))).toHaveValue('Cora Invented');
  });
});
