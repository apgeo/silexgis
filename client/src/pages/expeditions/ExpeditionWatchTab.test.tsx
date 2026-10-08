// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import i18n from '../../i18n';
import type {
  ExpeditionSurfaceLog,
  ExpeditionSurfaceLogPerson,
  ExpeditionSurfaceLogTrip,
} from '../../api/hooks.ts';

const { logSpy } = vi.hoisted(() => ({ logSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useExpeditionSurfaceLog: (...args: unknown[]) => logSpy(...args),
}));

const { default: ExpeditionWatchTab } = await import('./ExpeditionWatchTab.tsx');

const CAMP = '11111111-2222-3333-4444-555555555555';
const RUNNING = 'aaaaaaaa-0000-0000-0000-000000000001';
const FINISHED = 'aaaaaaaa-0000-0000-0000-000000000002';

/** The moment every age in these tests is measured from. */
const NOW = Date.parse('2026-08-02T15:00:00Z');

function person(
  name: string,
  standing: 'underground' | 'out' | 'unheard',
  lastRecordedAt: string | null,
): ExpeditionSurfaceLogPerson {
  return {
    caverId: `caver-${name || 'nameless'}`,
    name,
    in: standing === 'underground',
    out: standing === 'out',
    lastRecordedAt,
  };
}

function trip(overrides: Partial<ExpeditionSurfaceLogTrip> = {}): ExpeditionSurfaceLogTrip {
  return {
    tripLogId: RUNNING,
    title: 'Pushing the north series',
    tripDate: '2026-08-02',
    tripDateEnd: null,
    state: 'armed',
    armedAt: '2026-08-02T08:00:00Z',
    closedAt: null,
    expectedReturnAt: '2026-08-02T18:30:00Z',
    underground: 2,
    out: 1,
    unheard: 1,
    lastRecordedAt: '2026-08-02T14:40:00Z',
    party: [
      person('Ana Invented', 'underground', '2026-08-02T14:40:00Z'),
      person('Bujor Invented', 'underground', '2026-08-02T12:00:00Z'),
      person('Carmen Invented', 'out', '2026-08-02T13:00:00Z'),
      person('Dan Invented', 'unheard', null),
    ],
    ...overrides,
  };
}

function log(overrides: Partial<ExpeditionSurfaceLog> = {}): ExpeditionSurfaceLog {
  return { trips: [trip()], truncated: false, ...overrides };
}

const answered = (data: ExpeditionSurfaceLog) => ({ data, isPending: false, error: null });

function show(active = true) {
  return render(
    <MemoryRouter>
      <ExpeditionWatchTab expeditionId={CAMP} active={active} />
    </MemoryRouter>,
  );
}

afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});
beforeEach(async () => {
  await i18n.changeLanguage('en');
  vi.spyOn(Date, 'now').mockReturnValue(NOW);
  logSpy.mockReset();
  logSpy.mockReturnValue(answered(log()));
});

describe("who is underground on a camp's trips", () => {
  it('counts each trip’s party in, out and not heard from, and names who is which', () => {
    show();

    const card = screen.getByTestId(`expedition-watch-trip-${RUNNING}`);
    expect(within(card).getByTestId('expedition-watch-count-underground').textContent).toBe('2');
    expect(within(card).getByTestId('expedition-watch-count-out').textContent).toBe('1');
    expect(within(card).getByTestId('expedition-watch-count-unheard').textContent).toBe('1');

    const ana = within(card).getByTestId('expedition-watch-person-caver-Ana Invented');
    expect(ana.textContent).toContain('Ana Invented');
    expect(within(ana).getByTestId('expedition-watch-standing-underground').textContent).toBe(
      'Underground',
    );
    const carmen = within(card).getByTestId('expedition-watch-person-caver-Carmen Invented');
    expect(within(carmen).getByTestId('expedition-watch-standing-out').textContent).toBe('Out');
    // The third state is drawn as itself. Folded into either of the others it would say that
    // somebody nobody has heard from is safely out, or is in a cave.
    const dan = within(card).getByTestId('expedition-watch-person-caver-Dan Invented');
    expect(within(dan).getByTestId('expedition-watch-standing-unheard').textContent).toBe(
      'Not heard from',
    );
  });

  it('counts the people it lists, so the figures and the names cannot disagree', () => {
    // Figures sent beside the party that do not match it. What is drawn is the count of the
    // people on the card, through the counter the trip's own tracking uses — a second set of
    // numbers above a list they contradict is the one thing this card must not be able to show.
    logSpy.mockReturnValue(
      answered(log({ trips: [trip({ underground: 9, out: 9, unheard: 9 })] })),
    );
    show();

    expect(screen.getByTestId('expedition-watch-count-underground').textContent).toBe('2');
    expect(screen.getByTestId('expedition-watch-count-out').textContent).toBe('1');
    expect(screen.getByTestId('expedition-watch-count-unheard').textContent).toBe('1');
    expect(screen.getAllByTestId('expedition-watch-standing-underground')).toHaveLength(2);
  });

  it('says how long ago the party, and each person, was last heard from', () => {
    show();

    expect(screen.getByTestId('expedition-watch-last-heard').textContent).toBe(
      'Last heard: 20 minutes ago',
    );
    const bujor = screen.getByTestId('expedition-watch-person-caver-Bujor Invented');
    expect(bujor.textContent).toContain('3 hours ago');
    // Nobody said anything about Dan, and no words are made up for a report that was never made.
    const dan = screen.getByTestId('expedition-watch-person-caver-Dan Invented');
    expect(dan.textContent).toBe('Dan InventedNot heard from');
  });

  it('says so in words when nobody on a trip has been heard from at all', () => {
    logSpy.mockReturnValue(
      answered(
        log({
          trips: [
            trip({
              lastRecordedAt: null,
              party: [person('Dan Invented', 'unheard', null)],
            }),
          ],
        }),
      ),
    );
    show();

    expect(screen.getByTestId('expedition-watch-last-heard').textContent).toBe(
      'Last heard: no word yet',
    );
  });

  it('reads a moment the answer left out altogether as silence, not as a date', () => {
    // The contract lets the moment be absent as well as empty. Handed to a date as a value it
    // would be worded as a report made at the start of 1970, or throw the whole section away.
    const silent = { ...person('Dan Invented', 'unheard', null) };
    delete silent.lastRecordedAt;
    const quiet = { ...trip({ party: [silent] }) };
    delete quiet.lastRecordedAt;
    logSpy.mockReturnValue(answered(log({ trips: [quiet] })));
    show();

    expect(screen.getByTestId('expedition-watch-last-heard').textContent).toBe(
      'Last heard: no word yet',
    );
    expect(screen.getByTestId('expedition-watch-person-caver-Dan Invented').textContent).toBe(
      'Dan InventedNot heard from',
    );
  });

  it('prints the hour a party planned to be out by as a time, and judges nothing by it', () => {
    // Four and a half hours past the planned hour. The line must read exactly as it would have
    // read before it: this section records, and whether a party is overdue is the callout's to
    // say — a count that looked like an alarm would be trusted as one.
    vi.spyOn(Date, 'now').mockReturnValue(Date.parse('2026-08-02T23:00:00Z'));
    show();

    const expected = new Date('2026-08-02T18:30:00Z').toLocaleString('en', {
      dateStyle: 'medium',
      timeStyle: 'short',
    });
    expect(screen.getByTestId('expedition-watch-expected-return').textContent).toBe(
      `Expected back: ${expected}`,
    );
    const everything = screen.getByTestId('expedition-watch-tab').textContent ?? '';
    expect(everything).not.toMatch(/overdue|late\b|alarm raised|callout/i);
  });

  it('leaves the planned hour out when the trip named none', () => {
    logSpy.mockReturnValue(answered(log({ trips: [trip({ expectedReturnAt: null })] })));
    show();

    expect(screen.queryByTestId('expedition-watch-expected-return')).toBeNull();
    // The card is otherwise whole: the absent line is the hour, not the trip.
    expect(screen.getByTestId('expedition-watch-last-heard')).toBeTruthy();
  });

  it("opens the trip on its own tracking, which is where the places are", () => {
    show();

    const link = screen.getByTestId('expedition-watch-trip-link');
    expect(link.textContent).toBe('Pushing the north series');
    expect(link.getAttribute('href')).toBe(`/trip-logs/${RUNNING}?tab=tracking`);
  });

  it('tells a running watch from one that was closed, and says when that one closed', () => {
    logSpy.mockReturnValue(
      answered(
        log({
          trips: [
            trip(),
            trip({
              tripLogId: FINISHED,
              title: 'Rigging the entrance series',
              state: 'closed',
              closedAt: '2026-08-01T19:00:00Z',
              party: [person('Elena Invented', 'out', '2026-08-01T18:50:00Z')],
            }),
          ],
        }),
      ),
    );
    show();

    const running = screen.getByTestId(`expedition-watch-trip-${RUNNING}`);
    expect(within(running).getByTestId('expedition-watch-state-armed').textContent).toBe('Tracking');
    expect(within(running).queryByTestId('expedition-watch-closed-at')).toBeNull();

    const finished = screen.getByTestId(`expedition-watch-trip-${FINISHED}`);
    expect(within(finished).getByTestId('expedition-watch-state-closed').textContent).toBe(
      'Tracking closed',
    );
    expect(within(finished).getByTestId('expedition-watch-closed-at').textContent).toContain(
      'Closed: ',
    );
    expect(within(finished).getByTestId('expedition-watch-count-out').textContent).toBe('1');
  });

  it('still counts a person it was given no name for, under words of its own', () => {
    logSpy.mockReturnValue(
      answered(log({ trips: [trip({ party: [person('', 'underground', '2026-08-02T14:40:00Z')] })] })),
    );
    show();

    expect(screen.getByTestId('expedition-watch-count-underground').textContent).toBe('1');
    expect(screen.getByTestId('expedition-watch-person-caver-nameless').textContent).toContain(
      'Unnamed person',
    );
  });

  it('says a trip has nobody on its list rather than drawing three zeroes over nothing', () => {
    logSpy.mockReturnValue(answered(log({ trips: [trip({ lastRecordedAt: null, party: [] })] })));
    show();

    expect(screen.getByTestId('expedition-watch-party-empty').textContent).toBe(
      "Nobody is on this trip's list.",
    );
    expect(screen.queryByTestId('expedition-watch-party')).toBeNull();
  });
});

describe("a camp's head count with nothing to count", () => {
  it('says what would make a trip appear', () => {
    logSpy.mockReturnValue(answered(log({ trips: [] })));
    show();

    const empty = screen.getByTestId('expedition-watch-empty');
    // Both halves: that nothing is listed is an answer about tracking and about the reader, and
    // what to do to change it. A blank pane on a camp with a party underground whose tracking
    // nobody started would be read as "everybody is out".
    expect(empty.textContent).toContain('No trip of this camp is being tracked');
    expect(empty.textContent).toContain('or none you may read');
    expect(empty.textContent).toContain('once tracking is started on its Tracking tab');
    expect(screen.queryByTestId('expedition-watch-counts')).toBeNull();
  });

  it('shows a spinner, and not the empty answer, while the answer has not arrived', () => {
    logSpy.mockReturnValue({ data: undefined, isPending: true, error: null });
    show();

    expect(screen.queryByTestId('expedition-watch-empty')).toBeNull();
    expect(screen.queryByTestId('expedition-watch-unavailable')).toBeNull();
    expect(screen.getByTestId('expedition-watch-tab').querySelector('.ant-spin')).not.toBeNull();
  });

  it('says the count could not be read, and never that nobody is underground', () => {
    logSpy.mockReturnValue({ data: undefined, isPending: false, error: new Error('boom') });
    show();

    expect(screen.getByTestId('expedition-watch-unavailable').textContent).toContain(
      'The head count could not be read.',
    );
    expect(screen.queryByTestId('expedition-watch-empty')).toBeNull();
  });
});

describe("a camp's head count that was cut short", () => {
  it('says the list is not all of them', () => {
    logSpy.mockReturnValue(answered(log({ truncated: true })));
    show();

    expect(screen.getByTestId('expedition-watch-truncated').textContent).toContain(
      'more tracked trips than this list shows',
    );
    // What did arrive is still drawn under the notice.
    expect(screen.getByTestId(`expedition-watch-trip-${RUNNING}`)).toBeTruthy();
  });

  it('says nothing of the kind when the list is whole', () => {
    show();

    expect(screen.queryByTestId('expedition-watch-truncated')).toBeNull();
  });
});

describe("a camp's head count behind another section", () => {
  it('tells the read it is not being looked at, and draws nothing', () => {
    // The camp page keeps a section mounted once it has been opened. Mounted behind the map this
    // must neither ask the server every half minute nor run a clock for ages nobody can see —
    // even though an earlier answer is still in hand.
    show(false);

    expect(logSpy).toHaveBeenCalledWith(CAMP, false);
    expect(screen.getByTestId('expedition-watch-tab').textContent).toBe('');
  });

  it('draws the count again once it is the section on screen', () => {
    const { rerender } = show(false);
    rerender(
      <MemoryRouter>
        <ExpeditionWatchTab expeditionId={CAMP} active />
      </MemoryRouter>,
    );

    expect(logSpy).toHaveBeenLastCalledWith(CAMP, true);
    expect(screen.getByTestId(`expedition-watch-trip-${RUNNING}`)).toBeTruthy();
  });
});

describe("a camp's head count in Romanian", () => {
  it('uses the words the trip’s own tracking uses for the three standings', async () => {
    await i18n.changeLanguage('ro');
    show();

    const card = screen.getByTestId(`expedition-watch-trip-${RUNNING}`);
    expect(within(card).getAllByTestId('expedition-watch-standing-underground')[0].textContent).toBe(
      'În peșteră',
    );
    expect(within(card).getByTestId('expedition-watch-standing-unheard').textContent).toBe(
      'Fără nicio veste',
    );
    expect(within(card).getByTestId('expedition-watch-expected-return').textContent).toContain(
      'Ieșire estimată: ',
    );
  });
});
