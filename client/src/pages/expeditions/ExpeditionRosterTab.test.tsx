// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { ExpeditionRoster, ExpeditionRosterEntry, ExpeditionRosterRole } from '../../api/hooks.ts';

const { rosterSpy, rolesSpy } = vi.hoisted(() => ({ rosterSpy: vi.fn(), rolesSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useExpeditionRoster: () => rosterSpy(),
  useExpeditionRosterRoles: () => rolesSpy(),
}));

const { default: ExpeditionRosterTab } = await import('./ExpeditionRosterTab.tsx');

const CAMP = '77777777-8888-9999-aaaa-bbbbbbbbbbbb';

const roles: ExpeditionRosterRole[] = [
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

function show() {
  return render(<ExpeditionRosterTab expeditionId={CAMP} />);
}

afterEach(cleanup);
beforeEach(() => {
  rolesSpy.mockReturnValue({ data: roles });
  rosterSpy.mockReturnValue({ data: roster(), isPending: false, error: null });
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
