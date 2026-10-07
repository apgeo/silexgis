// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type {
  ExpeditionRoster,
  ExpeditionRosterEntry,
  ExpeditionRosterEntryWrite,
  ExpeditionRosterRole,
} from '../../api/hooks.ts';

const { rosterSpy, rolesSpy, createStay, updateStay, removeStay } = vi.hoisted(() => ({
  rosterSpy: vi.fn(),
  rolesSpy: vi.fn(),
  createStay: vi.fn(),
  updateStay: vi.fn(),
  removeStay: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  useExpeditionRoster: () => rosterSpy(),
  useExpeditionRosterRoles: () => rolesSpy(),
  useCreateExpeditionRosterEntry: () => ({ mutateAsync: createStay, isPending: false }),
  useUpdateExpeditionRosterEntry: () => ({ mutateAsync: updateStay, isPending: false }),
  useDeleteExpeditionRosterEntry: () => ({ mutateAsync: removeStay, isPending: false }),
  useCavers: () => ({ data: [] }),
}));

// The dialog's person box asks the directory a little behind the typing; here it asks at once.
vi.mock('../../hooks/useDebouncedValue.ts', () => ({
  useDebouncedValue: (value: string) => value,
}));

const { default: ExpeditionRosterTab } = await import('./ExpeditionRosterTab.tsx');

const CAMP = '77777777-8888-9999-aaaa-bbbbbbbbbbbb';

const roles: ExpeditionRosterRole[] = [
  { id: 4, code: 'member', name: 'Member', description: null, sortOrder: 1, isSeeded: true },
  { id: 1, code: 'cook', name: 'Cook', description: null, sortOrder: 3, isSeeded: true },
  { id: 2, code: 'driver', name: 'Driver', description: null, sortOrder: 5, isSeeded: true },
  // An installation's own row is shown as whoever added it wrote it — this client ships no
  // wording for a code it does not know.
  { id: 9, code: 'radio_op', name: 'Radio operator', description: null, sortOrder: 90, isSeeded: false },
];

function entry(overrides: Partial<ExpeditionRosterEntry> = {}): ExpeditionRosterEntry {
  return {
    id: 1,
    expeditionId: CAMP,
    caverId: 'caver-1',
    caverName: 'Ana Pop',
    roleId: 1,
    fromDate: '2026-07-18',
    toDate: '2026-07-25',
    note: null,
    createdAt: '2026-07-01T00:00:00Z',
    updatedAt: '2026-07-01T00:00:00Z',
    ...overrides,
  };
}

function roster(overrides: Partial<ExpeditionRoster> = {}): ExpeditionRoster {
  return { expeditionId: CAMP, entries: [entry()], people: 1, ...overrides };
}

/** The camp's own days, handed over only for somebody who may write the camp. */
const CAMP_DAYS = { startDate: '2026-07-18', endDate: '2026-08-01' };

/** As a reader sees it — the way the camp's page draws it for most people, and its write-up for all. */
function show() {
  return render(
    <App>
      <ExpeditionRosterTab expeditionId={CAMP} />
    </App>,
  );
}

/** As somebody who may write the camp sees it. */
function showToAKeeper() {
  return render(
    <App>
      <ExpeditionRosterTab expeditionId={CAMP} editable={CAMP_DAYS} />
    </App>,
  );
}

afterEach(cleanup);
beforeEach(() => {
  rolesSpy.mockReturnValue({ data: roles });
  rosterSpy.mockReturnValue({ data: roster(), isPending: false, error: null });
  createStay.mockReset().mockResolvedValue(entry());
  updateStay.mockReset().mockResolvedValue(entry());
  removeStay.mockReset().mockResolvedValue(undefined);
});

describe('who was at a camp', () => {
  it('lists each stay with the role it was under and the days it covered', () => {
    show();

    expect(screen.getByText('Ana Pop')).toBeTruthy();
    expect(screen.getByText('Cook')).toBeTruthy();
    // A stay that ran on past the day it started reads as a range.
    expect(screen.getByTestId('expedition-roster-tab').textContent).toContain('–');
  });

  it('shows one day for a stay that did not run on past the day it started', () => {
    rosterSpy.mockReturnValue({
      data: roster({ entries: [entry({ toDate: null })] }),
      isPending: false,
      error: null,
    });
    show();

    // An absent end is "they did not stay on past the day they arrived", never "the end is
    // unknown" — the same convention the camp's own dates follow.
    expect(screen.getByTestId('expedition-roster-tab').textContent).not.toContain('–');
  });

  it('reports the head count the server worked out, not the number of rows', () => {
    // The roster holds one row per person per role, so somebody who cooked and drove is two rows
    // and one person. Counting rows here would report a camp bigger than it was and raise nothing.
    rosterSpy.mockReturnValue({
      data: roster({
        entries: [entry({ id: 1, roleId: 1 }), entry({ id: 2, roleId: 2 })],
        people: 1,
      }),
      isPending: false,
      error: null,
    });
    show();

    const said = screen.getByTestId('expedition-roster-people').textContent ?? '';
    expect(said).toContain('1');
    expect(said).not.toContain('2');
  });

  it('names a role the installation added itself as that installation wrote it', () => {
    rosterSpy.mockReturnValue({
      data: roster({ entries: [entry({ roleId: 9 })] }),
      isPending: false,
      error: null,
    });
    show();

    expect(screen.getByText('Radio operator')).toBeTruthy();
  });

  it('says nobody has been recorded yet, rather than looking broken', () => {
    rosterSpy.mockReturnValue({
      data: roster({ entries: [], people: 0 }),
      isPending: false,
      error: null,
    });
    show();

    expect(screen.getByText(/Nobody has been recorded/)).toBeTruthy();
    // An empty camp is an answer: it must not also claim a head count nobody gave it.
    expect(screen.queryByTestId('expedition-roster-people')).toBeNull();
  });

  it('draws the withholding as a state of the page, not as a failure', () => {
    // The caller may read this camp and may not read people, so the server refuses outright
    // with a code of its own rather than answering with the names struck out — a struck-out
    // list would still say how many people were there and when. This is the designed answer
    // and the page says so in its own words.
    rosterSpy.mockReturnValue({
      data: undefined,
      isPending: false,
      error: new ApiError(403, 'expedition_roster.people_unreadable'),
    });
    show();

    expect(screen.getByTestId('expedition-roster-withheld')).toBeTruthy();
    expect(screen.getByText(/not shown to you/)).toBeTruthy();
    // And the positive case is the one above: the same component, given rows, shows them. The
    // withheld state must say nothing about how many people there were.
    expect(screen.queryByTestId('expedition-roster-people')).toBeNull();
    expect(screen.queryByText('Ana Pop')).toBeNull();
  });

  it('does not dress an unrelated refusal up as the withholding', () => {
    // Only the camp-readable-but-people-unreadable answer gets the explanation; anything else
    // says the roster could not be read and guesses at nothing.
    rosterSpy.mockReturnValue({
      data: undefined,
      isPending: false,
      error: new ApiError(404, 'expedition.not_found'),
    });
    show();

    expect(screen.queryByTestId('expedition-roster-withheld')).toBeNull();
    expect(screen.getByText(/could not be read/)).toBeTruthy();
  });
});

describe('keeping a camp’s roster', () => {
  it('offers a reader nothing to change it with', () => {
    // Recording, correcting and removing a stay each take the right to write the camp, and the
    // controls are drawn only for somebody the page was told holds it. A reader's roster is the
    // list and nothing else — which is also how the camp's write-up draws it.
    show();

    expect(screen.getByText('Ana Pop')).toBeTruthy();
    expect(screen.queryByTestId('expedition-stay-add')).toBeNull();
    expect(screen.queryByTestId('expedition-stay-edit-1')).toBeNull();
    expect(screen.queryByTestId('expedition-stay-remove-1')).toBeNull();
  });

  it('records a stay for somebody the directory does not hold, from the tab', async () => {
    showToAKeeper();
    fireEvent.click(screen.getByTestId('expedition-stay-add'));

    const dialog = await screen.findByRole('dialog', { name: 'Add a stay' });
    fireEvent.change(within(dialog).getByTestId('expedition-stay-person'), {
      target: { value: 'Vasile Bucătarul' },
    });
    fireEvent.click(within(dialog).getByRole('button', { name: 'OK' }));

    await vi.waitFor(() => expect(createStay).toHaveBeenCalled());
    // A name, because it was typed and not chosen; the camp's own days, because that is what a
    // new stay starts out as; the ordinary role.
    expect(createStay.mock.calls[0][0] as ExpeditionRosterEntryWrite).toEqual({
      caverId: null,
      newCaverName: 'Vasile Bucătarul',
      roleId: 4,
      fromDate: '2026-07-18',
      toDate: '2026-08-01',
      note: null,
    });
    expect(updateStay).not.toHaveBeenCalled();
  });

  it('opens a stay for correction filled in, and sends it back as the same stay', async () => {
    rosterSpy.mockReturnValue({
      data: roster({ entries: [entry({ id: 5, note: 'Left early.' })] }),
      isPending: false,
      error: null,
    });
    showToAKeeper();
    fireEvent.click(screen.getByTestId('expedition-stay-edit-5'));

    const dialog = await screen.findByRole('dialog', { name: 'Edit stay' });
    const who = within(dialog).getByTestId('expedition-stay-person') as HTMLInputElement;
    expect(who.value).toBe('Ana Pop');
    fireEvent.click(within(dialog).getByRole('button', { name: 'OK' }));

    await vi.waitFor(() => expect(updateStay).toHaveBeenCalled());
    expect(updateStay.mock.calls[0][0]).toEqual({
      entryId: 5,
      body: {
        // Still the entry it was loaded with: the name was left alone.
        caverId: 'caver-1',
        newCaverName: null,
        roleId: 1,
        fromDate: '2026-07-18',
        toDate: '2026-07-25',
        note: 'Left early.',
      },
    });
    expect(createStay).not.toHaveBeenCalled();
  });

  it('removes a stay only once that is confirmed', async () => {
    rosterSpy.mockReturnValue({
      data: roster({ entries: [entry({ id: 5 }), entry({ id: 6, roleId: 2 })], people: 1 }),
      isPending: false,
      error: null,
    });
    showToAKeeper();

    fireEvent.click(screen.getByTestId('expedition-stay-remove-6'));
    // Asked first, in words that say what goes: the stay, and not the person.
    expect(await screen.findByText(/the person stays in the list of cavers/)).toBeTruthy();
    expect(removeStay).not.toHaveBeenCalled();

    const confirm = document.querySelector('.ant-popconfirm .ant-btn-primary')!;
    fireEvent.click(confirm);
    await vi.waitFor(() => expect(removeStay).toHaveBeenCalledWith(6));
    expect(removeStay).toHaveBeenCalledTimes(1);
  });

  it('removes the stay its confirmation was opened on, though the rows move under it', async () => {
    // Three stays, and the question is open on the middle one when the list is read again a
    // row shorter — an earlier removal landing, or somebody else's. The answer belongs to the
    // stay it was asked about and not to whichever stay now sits where that one was.
    const stays = [
      entry({ id: 5, caverId: 'caver-1', caverName: 'Ana Pop' }),
      entry({ id: 6, caverId: 'caver-2', caverName: 'Bogdan Ilie' }),
      entry({ id: 7, caverId: 'caver-3', caverName: 'Carmen Radu' }),
    ];
    rosterSpy.mockReturnValue({
      data: roster({ entries: stays, people: 3 }),
      isPending: false,
      error: null,
    });
    const view = showToAKeeper();
    fireEvent.click(screen.getByTestId('expedition-stay-remove-6'));
    // The question says whose stay it is about, so it cannot come to be read as another's.
    expect(await screen.findByText(/Remove this stay of Bogdan Ilie\?/)).toBeTruthy();

    rosterSpy.mockReturnValue({
      data: roster({ entries: stays.slice(1), people: 2 }),
      isPending: false,
      error: null,
    });
    view.rerender(
      <App>
        <ExpeditionRosterTab expeditionId={CAMP} editable={CAMP_DAYS} />
      </App>,
    );

    // Still the same question, about the same person, on a list that has moved — and the only
    // one: the row it used to stand on now draws another stay, and has nothing of the old one's
    // left open on it to be pressed by mistake.
    const open = Array.from(document.querySelectorAll<HTMLElement>('.ant-popconfirm'));
    expect(open.map((popup) => popup.textContent)).toEqual([
      expect.stringContaining('Remove this stay of Bogdan Ilie?'),
    ]);
    fireEvent.click(open[0].querySelector('.ant-btn-primary')!);
    await vi.waitFor(() => expect(removeStay).toHaveBeenCalled());
    expect(removeStay).toHaveBeenCalledWith(6);
    expect(removeStay).toHaveBeenCalledTimes(1);
  });

  it('offers the first stay from a camp nobody has been recorded at', async () => {
    rosterSpy.mockReturnValue({
      data: roster({ entries: [], people: 0 }),
      isPending: false,
      error: null,
    });
    showToAKeeper();

    expect(screen.getByText(/Nobody has been recorded/)).toBeTruthy();
    fireEvent.click(screen.getByTestId('expedition-stay-add'));
    expect(await screen.findByRole('dialog', { name: 'Add a stay' })).toBeTruthy();
  });

  it('offers nothing where the roster is withheld, whoever is looking', () => {
    // Somebody who may write the camp and may not read people is refused the list. A control for
    // writing into a list they are not shown would have them working blind: the names it wrote
    // could not be read back to be checked.
    rosterSpy.mockReturnValue({
      data: undefined,
      isPending: false,
      error: new ApiError(403, 'expedition_roster.people_unreadable'),
    });
    showToAKeeper();

    expect(screen.getByTestId('expedition-roster-withheld')).toBeTruthy();
    expect(screen.queryByTestId('expedition-stay-add')).toBeNull();
  });
});
