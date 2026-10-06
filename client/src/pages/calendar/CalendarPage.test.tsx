// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import dayjs from 'dayjs';
import { MemoryRouter, useLocation, useNavigationType } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { CalendarEntry, CalendarParams } from '../../api/hooks.ts';

const { calendarSpy, navigateSpy } = vi.hoisted(() => ({
  calendarSpy: vi.fn(),
  navigateSpy: vi.fn(),
}));

vi.mock('react-router-dom', async () => ({
  ...(await vi.importActual<typeof import('react-router-dom')>('react-router-dom')),
  useNavigate: () => navigateSpy,
}));

vi.mock('../../api/hooks.ts', () => ({
  useCalendar: (params: CalendarParams, options?: { enabled?: boolean }) =>
    calendarSpy(params, options),
  useCavingGroups: () => ({ data: [] }),
  // The pane under the record builds an OpenLayers map of its own; what it asks for is proved
  // where it lives, and here it is only required not to interfere with the record above it.
  useTripLogMap: () => ({ data: undefined }),
  useCampAreasMap: () => ({ data: undefined }),
}));

const { default: CalendarPage } = await import('./CalendarPage.tsx');

/**
 * Every member a calendar row carries, written as a map over the row's own type rather than as a
 * list of words. A member added to the row on the server, regenerated into the client's types and
 * not thought about here, fails to compile against `Record<keyof CalendarEntry, true>` — which is
 * the point: the field most likely to arrive is a cave, and widening this map is the deliberate
 * act of saying somebody decided it belongs.
 */
const rowShape: Record<keyof CalendarEntry, true> = {
  source: true,
  id: true,
  title: true,
  start: true,
  end: true,
  startTime: true,
  endTime: true,
  kind: true,
  state: true,
  placement: true,
  cavingGroupId: true,
  hasPosition: true,
};

function row(overrides: Partial<CalendarEntry> = {}): CalendarEntry {
  // No cast: the literal must satisfy the generated type exactly, so a new member on the row
  // stops the build here instead of being absorbed by an assertion nobody re-reads.
  const base: CalendarEntry = {
    source: 'tripLog',
    id: '11111111-1111-1111-1111-111111111111',
    title: 'Coiba Mare recce',
    start: '2026-09-05',
    end: null,
    startTime: null,
    endTime: null,
    kind: null,
    state: 'planned',
    placement: 'ahead',
    cavingGroupId: null,
    hasPosition: false,
  };
  return { ...base, ...overrides };
}

/** What the page last asked the server for. */
function lastParams(): CalendarParams {
  return calendarSpy.mock.calls.at(-1)![0] as CalendarParams;
}

/** Whether the page asked at all. */
function lastEnabled(): boolean {
  const options = calendarSpy.mock.calls.at(-1)![1] as { enabled?: boolean } | undefined;
  return options?.enabled ?? true;
}

function answer(entries: CalendarEntry[], omitted = 0) {
  calendarSpy.mockReturnValue({
    data: { entries, omitted },
    isFetching: false,
    isError: false,
  });
}

function fails() {
  calendarSpy.mockReturnValue({ data: undefined, isFetching: false, isError: true });
}

/**
 * The address, rendered, so a test can read where a change went and how it got there. The router
 * keeps it and the page does not, and a view that lives in the address is only testable by
 * reading it back; whether the change was a new history entry or a rewrite of the one it was made
 * on is the router's to say too.
 */
function Address() {
  const location = useLocation();
  const how = useNavigationType();
  return (
    <span data-testid="calendar-address" data-how={how}>
      {`${location.pathname}${location.search}`}
    </span>
  );
}

function show(address = '/calendar') {
  return render(
    <MemoryRouter initialEntries={[address]}>
      <CalendarPage />
      <Address />
    </MemoryRouter>,
  );
}

/** The query string the page is standing on, read back as the keys it holds. */
function addressKeys(): URLSearchParams {
  const address = screen.getByTestId('calendar-address').textContent ?? '';
  return new URLSearchParams(address.split('?')[1] ?? '');
}

/** Whether the last change made a history entry of its own or rewrote the one it was made on. */
const arrivedBy = (): string | undefined => screen.getByTestId('calendar-address').dataset.how;

/** Opens the kinds control and takes one of the words it offers. */
async function chooseKind(label: string) {
  const control = screen.getByTestId('calendar-kind-filter');
  fireEvent.mouseDown(control.querySelector('.ant-select-selector') ?? control);
  await act(async () => {
    fireEvent.click(document.querySelector(`.ant-select-item-option[title="${label}"]`)!);
  });
}

afterEach(cleanup);
beforeEach(() => {
  calendarSpy.mockReset();
  navigateSpy.mockReset();
  answer([row()]);
});

describe('the calendar record', () => {
  it('opens on a window it chose, with both ends of it, and reads both families of record', () => {
    show();

    const asked = lastParams();
    expect(asked.from).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(asked.to).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(asked.from < asked.to).toBe(true);
    // Both kinds are on, so no family is named: the answer is everything in the window.
    expect(asked.source).toBeUndefined();
    expect(screen.getByText('Coiba Mare recce')).toBeTruthy();
  });

  /**
   * The row's own title, drawn as it was written. Rewriting it for a reader who has already been
   * handed the row would withhold from them what the row's own page shows, and the decision about
   * who may see a row is taken before the row reaches this table at all.
   */
  it('draws the title as written', () => {
    answer([row({ title: 'Peștera cu Oase — derig' })]);
    show();

    expect(screen.getByText('Peștera cu Oase — derig')).toBeTruthy();
  });

  /**
   * The one thing a record of a month must not do is be quietly short. When the answer says rows
   * were left out, the page says so where somebody reading the rows will see it.
   */
  it('says how many rows the answer could not carry', () => {
    answer([row()], 12);
    show();

    expect(screen.getByTestId('calendar-omitted').textContent).toContain('12');
  });

  it('says nothing about a shortfall when there was none', () => {
    answer([row()], 0);
    show();

    expect(screen.queryByTestId('calendar-omitted')).toBeNull();
  });

  /** A date that has been put back is listed and marked, because that date is not one anybody is going on. */
  it('marks a row whose date has been abandoned', () => {
    answer([row({ state: 'delayed', placement: 'putBack' })]);
    show();

    expect(screen.getByTestId('calendar-postponed')).toBeTruthy();
  });

  /**
   * Narrowing to one family is a question the answer understands; asking for neither is not, and
   * a request that could only come back empty is not worth making.
   */
  it('names the families it wants, and asks nothing when it wants none', () => {
    show();

    fireEvent.click(screen.getByTestId('calendar-toggle-camps'));
    expect(lastParams().source).toBe('tripLog,event');

    fireEvent.click(screen.getByTestId('calendar-toggle-events'));
    expect(lastParams().source).toBe('tripLog');

    fireEvent.click(screen.getByTestId('calendar-toggle-trips'));
    expect(lastEnabled()).toBe(false);
    expect(screen.getByTestId('calendar-empty').textContent).toContain('No kind of record');
  });

  /**
   * Three families and a toggle for each, so every combination of them can be asked for — the
   * camps alone, or the events without the camps, which one toggle standing for "everything that
   * is not a trip" could not say.
   */
  it('turns each family on and off by itself', () => {
    show();

    fireEvent.click(screen.getByTestId('calendar-toggle-trips'));
    fireEvent.click(screen.getByTestId('calendar-toggle-events'));
    expect(lastParams().source).toBe('expedition');

    fireEvent.click(screen.getByTestId('calendar-toggle-events'));
    fireEvent.click(screen.getByTestId('calendar-toggle-camps'));
    expect(lastParams().source).toBe('event');

    // All three back on is the absence of the narrowing, not a list of all three.
    fireEvent.click(screen.getByTestId('calendar-toggle-trips'));
    fireEvent.click(screen.getByTestId('calendar-toggle-camps'));
    expect(lastParams().source).toBeUndefined();
  });

  /**
   * "Everything except the trips" is more than one family now that there are three, and a
   * narrowing that could only name one would drop the family it left out of a record that says
   * nothing is missing — the failure nobody sees, because an absent calendar row looks exactly
   * like a day with nothing on it.
   */
  it('keeps every non-trip family when the trips are turned off', () => {
    show();

    fireEvent.click(screen.getByTestId('calendar-toggle-trips'));
    expect(lastEnabled()).toBe(true);
    expect(lastParams().source?.split(',').sort()).toEqual(['event', 'expedition']);
  });

  /**
   * A row leads to its own record and to no other. The families are three and the addresses are
   * three; a row that opened another family's page would be a dead end on the one surface whose
   * whole purpose is leading somewhere.
   */
  it('opens each family of row at its own address', () => {
    const cases: [CalendarEntry['source'], string][] = [
      ['tripLog', '/trip-logs/'],
      ['expedition', '/expeditions/'],
      ['event', '/events/'],
    ];

    for (const [source, prefix] of cases) {
      answer([row({ source, id: '99999999-9999-9999-9999-999999999999' })]);
      show();
      fireEvent.click(screen.getByText('Coiba Mare recce'));
      expect(navigateSpy).toHaveBeenLastCalledWith(`${prefix}99999999-9999-9999-9999-999999999999`);
      cleanup();
    }
  });

  /**
   * The one source that is more than one thing says which thing it is. A permit deadline and a
   * social evening drawn as the same word would be indistinguishable to somebody scanning a month.
   */
  it('draws an event by its own kind rather than by its family', () => {
    answer([row({ source: 'event', kind: 'deadline' })]);
    show();

    expect(screen.getByText('Deadline')).toBeTruthy();
  });

  /**
   * Rows called off are in by default and are narrowed away only when somebody asks — the person
   * who was going on one is exactly the reader who most needs to find it.
   */
  it('leaves called-off rows in until they are turned off', () => {
    show();

    expect(lastParams().includeCancelled).toBeUndefined();
    fireEvent.click(screen.getByTestId('calendar-toggle-cancelled'));
    expect(lastParams().includeCancelled).toBe(false);
  });

  it('narrows the window to what is still to come when the past is turned off', () => {
    show();

    expect(lastParams().includePast).toBeUndefined();
    fireEvent.click(screen.getByTestId('calendar-toggle-past'));
    expect(lastParams().includePast).toBe(false);
  });

  /**
   * Whose rows these are is worked out from the request itself. The page sends a flag and never
   * an identifier, so there is no control here that could assemble where a named person has been.
   */
  it('asks for its own reader by a flag, and never names anybody', () => {
    show();

    fireEvent.click(screen.getByTestId('calendar-toggle-mine'));
    expect(lastParams().mine).toBe(true);
    expect(Object.keys(lastParams())).toEqual(
      expect.not.arrayContaining(['caverId', 'userId', 'participantId', 'ownerId']),
    );
  });

  /**
   * The row is a projection, not a trip: nothing derived from a cave reaches it, so nothing here
   * can draw one. A field added to the row later fails this rather than passing review.
   */
  it('carries no cave-derived field to draw', () => {
    // Asserted over the row's own type, not over a literal written next to it: a fixture can
    // only ever say what somebody typed into it, while the map is checked against the generated
    // contract and cannot fall behind it.
    const declared = Object.keys(rowShape);
    // `hasPosition` is deliberately allowed: whether there is a place is not where it is.
    expect(declared.filter((key) => /cave|geom|coordinate|latitude|longitude/i.test(key))).toEqual(
      [],
    );
    expect(declared).toEqual([
      'source',
      'id',
      'title',
      'start',
      'end',
      'startTime',
      'endTime',
      'kind',
      'state',
      'placement',
      'cavingGroupId',
      'hasPosition',
    ]);
    // And what the page is actually handed carries the same members and no others.
    expect([...Object.keys(row())].sort()).toEqual([...declared].sort());
  });

  /**
   * Both orders this page offers are asked of the server, because the rows on screen are one page
   * of a merged answer and reordering that page would arrange the wrong rows. A column identified
   * to the table only by a key reports no field when it is clicked, so the header would draw an
   * arrow for an order that was never sent — which is exactly what this pins.
   */
  it('asks the server for either order it offers', () => {
    show();

    // The table draws its header cells more than once (a measuring row sits behind the visible
    // one), so the first match is the one a reader clicks.
    const when = screen.getAllByText('When')[0];
    fireEvent.click(when);
    expect(lastParams().sort).toBe('-start');
    fireEvent.click(when);
    expect(lastParams().sort).toBeUndefined();

    const what = screen.getAllByText('What')[0];
    fireEvent.click(what);
    expect(lastParams().sort).toBe('title');
    fireEvent.click(what);
    expect(lastParams().sort).toBe('-title');
  });

  /**
   * And a fourth that gets no sentence at all. While the first answer is still coming there are
   * no rows, which is not the same as there being nothing: "nothing is recorded in these days" is
   * a claim about the days, and a question still in flight has not earned it — over the list, or
   * above a grid.
   */
  it('claims nothing about the days while the first answer is still coming', () => {
    calendarSpy.mockReturnValue({ data: undefined, isFetching: true, isError: false });
    show();
    expect(screen.queryByTestId('calendar-empty')).toBeNull();

    fireEvent.click(screen.getByText('Month'));
    expect(screen.getByTestId('calendar-grid')).toBeTruthy();
    expect(screen.queryByTestId('calendar-empty')).toBeNull();

    fireEvent.click(screen.getByText('Week'));
    expect(screen.queryByTestId('calendar-empty')).toBeNull();
  });

  /** Three situations, three sentences: only one of them is a fact about the calendar. */
  it('separates a failed read from a narrowed one and from an empty stretch of days', () => {
    fails();
    show();
    expect(screen.getByTestId('calendar-empty').textContent).toContain('could not be read');

    cleanup();
    answer([]);
    show();
    expect(screen.getByTestId('calendar-empty').textContent).toContain('Nothing is recorded');

    fireEvent.click(screen.getByTestId('calendar-toggle-mine'));
    expect(screen.getByTestId('calendar-empty').textContent).toContain('matches what you asked');
  });
});

describe('the calendar in the address', () => {
  /**
   * A calendar narrowed and read a particular way is a link: opened anywhere, it asks the server
   * the same question and draws the answer the same way. Every control is read out of the address
   * here and none of them out of a default.
   */
  it('takes its whole view from the address', () => {
    show(
      '/calendar?view=agenda&from=2026-09-01&to=2026-09-30&source=expedition,event&kind=training'
        + '&cavingGroupId=22222222-2222-2222-2222-222222222222&mine=true&includePast=false'
        + '&includeCancelled=false&sort=-title&map=false',
    );

    expect(lastParams()).toEqual({
      from: '2026-09-01',
      to: '2026-09-30',
      source: 'expedition,event',
      kind: 'training',
      cavingGroupId: '22222222-2222-2222-2222-222222222222',
      mine: true,
      includePast: false,
      includeCancelled: false,
      sort: '-title',
    });
    // The reading is the agenda's, whose rows have no header to sort by.
    expect(screen.getByTestId('calendar-agenda-row')).toBeTruthy();
    expect(screen.queryByText('When')).toBeNull();
    // And every toggle shows what the address says rather than what it would have opened on.
    const checked = (id: string) => screen.getByTestId(id).closest('label')!.className;
    expect(checked('calendar-toggle-trips')).not.toContain('checked');
    expect(checked('calendar-toggle-camps')).toContain('checked');
    expect(checked('calendar-toggle-events')).toContain('checked');
    expect(checked('calendar-toggle-past')).not.toContain('checked');
    expect(checked('calendar-toggle-cancelled')).not.toContain('checked');
    expect(checked('calendar-toggle-mine')).toContain('checked');
    expect(checked('calendar-toggle-map')).not.toContain('checked');
  });

  /** The untouched calendar has a bare address, and each choice is one readable key in it. */
  it('writes each choice into the address, and nothing for a default', () => {
    show();
    expect(screen.getByTestId('calendar-address').textContent).toBe('/calendar');

    fireEvent.click(screen.getByTestId('calendar-toggle-trips'));
    fireEvent.click(screen.getByTestId('calendar-toggle-past'));
    fireEvent.click(screen.getByTestId('calendar-toggle-mine'));

    const keys = addressKeys();
    expect(keys.get('source')).toBe('expedition,event');
    expect(keys.get('includePast')).toBe('false');
    expect(keys.get('mine')).toBe('true');
    expect([...keys.keys()].sort()).toEqual(['includePast', 'mine', 'source']);

    // Turned back, the key goes rather than being written out as its default.
    fireEvent.click(screen.getByTestId('calendar-toggle-mine'));
    expect(addressKeys().has('mine')).toBe(false);
  });

  /**
   * No family wanted is a state the reader can put the page in, so it has to survive a reload:
   * it is held in the address under a word of its own, and that word is never sent anywhere.
   */
  it('holds no family in the address and still asks the server nothing', () => {
    show('/calendar?source=none');

    expect(lastEnabled()).toBe(false);
    expect(lastParams().source).toBeUndefined();
    expect(screen.getByTestId('calendar-empty').textContent).toContain('No kind of record');
  });

  /**
   * A narrowing is a view somebody chose and is worth walking back to; which reading is on and
   * which order it is in say where the reader is standing, and walking back through each of those
   * would be walking back through every click.
   */
  it('gives a narrowing a history entry of its own, and a rearrangement none', () => {
    show();

    fireEvent.click(screen.getByTestId('calendar-toggle-cancelled'));
    expect(arrivedBy()).toBe('PUSH');

    fireEvent.click(screen.getByText('Agenda'));
    expect(arrivedBy()).toBe('REPLACE');
    expect(addressKeys().get('view')).toBe('agenda');

    fireEvent.click(screen.getByText('Record'));
    fireEvent.click(screen.getAllByText('What')[0]);
    expect(arrivedBy()).toBe('REPLACE');
    expect(addressKeys().get('sort')).toBe('title');
    // The record is the reading an address says nothing to ask for.
    expect(addressKeys().has('view')).toBe(false);
  });

  /**
   * Two changes made before the page has redrawn build on each other. The address is redrawn
   * from a navigation the router is free to defer, so a reader on a busy machine can turn two
   * switches before the first has come back; a change worked out from what the page last drew
   * would then be worked out from before the first one, and would quietly undo it.
   */
  it('keeps the first of two changes made before the page has redrawn', () => {
    show();

    act(() => {
      fireEvent.click(screen.getByTestId('calendar-toggle-camps'));
      fireEvent.click(screen.getByTestId('calendar-toggle-events'));
    });

    expect(lastParams().source).toBe('tripLog');
    expect(addressKeys().get('source')).toBe('tripLog');

    act(() => {
      fireEvent.click(screen.getByTestId('calendar-toggle-past'));
      fireEvent.click(screen.getByTestId('calendar-toggle-mine'));
      fireEvent.click(screen.getByTestId('calendar-toggle-cancelled'));
    });

    expect(lastParams()).toMatchObject({
      source: 'tripLog',
      includePast: false,
      mine: true,
      includeCancelled: false,
    });
  });

  /** An order asked for by a link shows its arrow on the column it is ordered by. */
  it('draws the order the address asks for on the column that has it', () => {
    show('/calendar?sort=-title');

    const sorted = document.querySelector('th.ant-table-column-sort');
    expect(sorted?.textContent).toContain('What');
    expect(sorted?.getAttribute('aria-sort')).toBe('descending');
  });

  /** A grid stands on the day the address names, and on today when it names none. */
  it('stands a grid on the day in the address', () => {
    show('/calendar?view=week&day=2026-03-18');

    expect(screen.getByTestId('calendar-week-day-2026-03-18')).toBeTruthy();
    expect(lastParams().from <= '2026-03-18').toBe(true);
    expect(lastParams().to >= '2026-03-18').toBe(true);

    // Moving a week is written down; coming back to this week is the absence of a day.
    fireEvent.click(screen.getByTestId('calendar-week-next'));
    expect(addressKeys().get('day')).toBe('2026-03-25');
    fireEvent.click(screen.getByTestId('calendar-week-today'));
    expect(addressKeys().has('day')).toBe(false);
  });
});

describe('the kinds of event', () => {
  /**
   * Several kinds may be chosen and are alternatives. They reach the server as one list under the
   * name the server reads them by, and the address carries the same list.
   */
  it('asks for the kinds chosen, as one list', async () => {
    show();
    expect(lastParams().kind).toBeUndefined();

    await chooseKind('Training');
    expect(lastParams().kind).toBe('training');

    await chooseKind('Deadline');
    expect(lastParams().kind).toBe('training,deadline');
    expect(addressKeys().get('kind')).toBe('training,deadline');
    // Choosing a kind names no family: the trips and the camps are asked for exactly as before.
    expect(lastParams().source).toBeUndefined();
  });

  /**
   * A kind is something only an event has, so the trips and the camps are still on the calendar
   * beside the events it narrowed — and the page says so while that is true, because a filter one
   * family in three can answer owes the other two a sentence. With the events alone on the
   * calendar there is nothing left to explain.
   */
  it('says the trips and camps are still listed while a kind is in force beside them', () => {
    show('/calendar?kind=training');
    expect(screen.getByTestId('calendar-kind-note').textContent).toContain('Trips and camps');

    fireEvent.click(screen.getByTestId('calendar-toggle-trips'));
    expect(screen.getByTestId('calendar-kind-note')).toBeTruthy();

    fireEvent.click(screen.getByTestId('calendar-toggle-camps'));
    expect(screen.queryByTestId('calendar-kind-note')).toBeNull();
    // Events of one kind and nothing else: the kind, with the other two families turned off.
    expect(lastParams().source).toBe('event');
    expect(lastParams().kind).toBe('training');
  });

  it('says nothing about kinds while none is chosen', () => {
    show();

    expect(screen.queryByTestId('calendar-kind-note')).toBeNull();
  });

  /**
   * With the events turned off there is nothing for a kind to narrow. The control is disabled
   * rather than removed and says why; the choice stays in the address, is not sent, and is back
   * in force the moment the events are.
   */
  it('cannot be chosen while events are turned off, says why, and keeps what was chosen', async () => {
    show('/calendar?kind=training');

    fireEvent.click(screen.getByTestId('calendar-toggle-events'));

    const control = screen.getByTestId('calendar-kind-filter');
    expect(control.className).toContain('ant-select-disabled');
    expect(lastParams().kind).toBeUndefined();
    expect(lastParams().source).toBe('tripLog,expedition');
    expect(addressKeys().get('kind')).toBe('training');
    expect(screen.queryByTestId('calendar-kind-note')).toBeNull();

    fireEvent.mouseEnter(control);
    expect(await screen.findByText(/Events are turned off/)).toBeTruthy();

    fireEvent.click(screen.getByTestId('calendar-toggle-events'));
    expect(screen.getByTestId('calendar-kind-filter').className).not.toContain('ant-select-disabled');
    expect(lastParams().kind).toBe('training');
  });

  /**
   * A calendar emptied by a kind has not earned "nothing is recorded in these days": that is a
   * claim about the club, and what happened is that the reader asked for less.
   */
  it('reads an empty answer under a kind as a narrowing rather than as an empty stretch of days', () => {
    answer([]);
    show('/calendar?kind=conference');

    expect(screen.getByTestId('calendar-empty').textContent).toContain('matches what you asked');
  });

  /**
   * A word a hand-written address carried that names no kind is not corrected: it is sent, the
   * server refuses it, and the control shows it as itself so there is something to let go of.
   */
  it('hands a kind it does not know to the server, and shows it so it can be removed', () => {
    fails();
    show('/calendar?kind=training,banana');

    expect(lastParams().kind).toBe('training,banana');
    expect(screen.getByTestId('calendar-empty').textContent).toContain('could not be read');

    // Read off the open list rather than off the closed control, which folds its choices into a
    // count when it has no width to draw them in.
    const control = screen.getByTestId('calendar-kind-filter');
    fireEvent.mouseDown(control.querySelector('.ant-select-selector') ?? control);
    const offered = document.querySelector('.ant-select-item-option[title="banana"]');
    expect(offered).not.toBeNull();
    expect(offered?.className).toContain('ant-select-item-option-selected');
  });
});


/**
 * The grids are drawn over the month or the year they are showing, so a fixture has to fall in
 * the panel the page opens on rather than on a date somebody wrote down once.
 */
const inThisMonth = (offsetDays: number): string =>
  dayjs().startOf('month').add(offsetDays, 'day').format('YYYY-MM-DD');

function showMonth() {
  const rendered = show();
  fireEvent.click(screen.getByText('Month'));
  return rendered;
}

/** Every chip drawn in one day's cell, in the order the grid drew them. */
function chipsOn(day: string): HTMLElement[] {
  return within(screen.getByTestId(`calendar-day-${day}`)).queryAllByTestId('calendar-chip');
}

describe('the calendar as a grid of days', () => {
  /**
   * The reason the grid overrides the height of a day's contents rather than using the calendar
   * as it ships. The shipped day cell reserves exactly three rows and scrolls the rest inside
   * itself, so a Saturday carrying four records shows three of them and hides the fourth behind a
   * scrollbar a few pixels wide — a clash the reader is never told about. The count is asserted,
   * not the presence of a cell, because three-of-four is precisely the failure.
   */
  it('shows every record on a day, and not the first three of them', () => {
    const saturday = inThisMonth(10);
    answer([
      row({ id: 'a', title: 'Coiba Mare recce', start: saturday }),
      row({ id: 'b', title: 'Huda lui Papară', start: saturday }),
      row({ id: 'c', title: 'Vântului derig', start: saturday }),
      row({ id: 'd', title: 'Scărișoara survey', start: saturday }),
    ]);
    showMonth();

    const chips = chipsOn(saturday);
    expect(chips).toHaveLength(4);
    expect(chips.map((chip) => chip.textContent)).toEqual([
      'Coiba Mare recce',
      'Huda lui Papară',
      'Vântului derig',
      'Scărișoara survey',
    ]);
  });

  /**
   * The other half of that, and the half no count can see. Nothing renders a stylesheet here, so
   * all four chips are in the document whether or not the clip is turned off — the clip is a
   * height and an overflow applied by the calendar's own styling to the box the contents sit in.
   * What this asserts is that the override reached that box: if it stops being handed over, the
   * shipped three-row clip comes back and every count above stays green while a reader loses the
   * fourth record on a day.
   */
  it('hands the height override to the box the calendar would have clipped', () => {
    const day = inThisMonth(10);
    answer([row({ start: day })]);
    showMonth();

    const box = screen.getByTestId(`calendar-day-${day}`).parentElement!;
    // The calendar's own element for a cell's contents — the one it sizes at three rows.
    expect(box.className).toContain('date-content');
    expect(box.style.height).toBe('auto');
    expect(box.style.overflowY).toBe('visible');
  });

  /**
   * A record lasting four days is drawn in all four of them, because a grid answers "what is
   * happening on this day" — and it has to read as one record doing so. Its first day is closed
   * and says its name, its last day is closed, the days between are open and draw only the rail;
   * and the name is said again on the first day of the week it runs on into, for a reader who
   * starts at that row. Said in all four it would read as four trips sharing a title.
   *
   * The month is named in the address, so the days are the same days whenever this runs: in
   * September 2026 the 4th is a Friday and the 6th, a Sunday, begins a row of the grid.
   */
  it('draws a record in every day it spans as one record, named once to a row', () => {
    answer([row({ title: 'Ponorul camp', start: '2026-09-04', end: '2026-09-07' })]);
    show('/calendar?view=month&day=2026-09-15');

    const chips = ['2026-09-04', '2026-09-05', '2026-09-06', '2026-09-07'].map((day) => {
      const chip = chipsOn(day)[0];
      expect(chip).toBeTruthy();
      return chip;
    });

    expect(chips.map((chip) => chip.dataset.span)).toEqual(['start', 'middle', 'middle', 'end']);
    expect(chips.map((chip) => chip.textContent)).toEqual(['Ponorul camp', '', 'Ponorul camp', '']);
    // What the drawing says, in words, on every day — including the two that draw no words.
    expect(chips.map((chip) => chip.getAttribute('aria-label'))).toEqual([
      'Trip — Ponorul camp, day 1 of 4',
      'Trip — Ponorul camp, day 2 of 4',
      'Trip — Ponorul camp, day 3 of 4',
      'Trip — Ponorul camp, day 4 of 4',
    ]);
    // The marks the first and last day carried before are still true of them.
    expect(chips.map((chip) => [chip.dataset.spanStart, chip.dataset.spanEnd])).toEqual([
      ['true', 'false'],
      ['false', 'false'],
      ['false', 'false'],
      ['false', 'true'],
    ]);
  });

  /** A record of one day is closed at both ends, says its name, and is day nothing of nothing. */
  it('draws a record of one day as itself', () => {
    answer([row({ title: 'Coiba Mare recce', start: '2026-09-09', startTime: '08:30:00' })]);
    show('/calendar?view=month&day=2026-09-15');

    const chip = chipsOn('2026-09-09')[0];
    expect(chip.dataset.span).toBe('single');
    expect(chip.textContent).toBe('08:30 Coiba Mare recce');
    expect(chip.getAttribute('aria-label')).toBe('Trip — Coiba Mare recce, 08:30');
  });

  /**
   * The days that draw only the rail do not say whose rail it is, so pointing at any day of a
   * record marks every day of it — and marks no other record, including one sharing those days.
   */
  it('marks every day of the record a reader is pointing at, and no other record', () => {
    answer([
      row({ id: 'camp', source: 'expedition', title: 'Ponorul camp', start: '2026-09-08', end: '2026-09-10' }),
      row({ id: 'course', source: 'event', kind: 'training', title: 'Rope course', start: '2026-09-09', end: '2026-09-10' }),
      row({ id: 'trip', title: 'Coiba Mare recce', start: '2026-09-09' }),
    ]);
    show('/calendar?view=month&day=2026-09-15');

    const linkedOn = (day: string) =>
      chipsOn(day).filter((chip) => chip.dataset.linked === 'true').map((chip) => chip.getAttribute('aria-label'));

    // Wednesday holds all three; the camp's chip there draws no words.
    const campOnWednesday = chipsOn('2026-09-09').find((chip) =>
      chip.getAttribute('aria-label')?.includes('Ponorul camp'),
    )!;
    expect(campOnWednesday.textContent).toBe('');

    fireEvent.mouseEnter(campOnWednesday);
    expect(linkedOn('2026-09-08')).toEqual(['Camp — Ponorul camp, day 1 of 3']);
    expect(linkedOn('2026-09-09')).toEqual(['Camp — Ponorul camp, day 2 of 3']);
    expect(linkedOn('2026-09-10')).toEqual(['Camp — Ponorul camp, day 3 of 3']);

    fireEvent.mouseLeave(campOnWednesday);
    expect(linkedOn('2026-09-09')).toEqual([]);

    // Reached by keyboard, the same.
    fireEvent.focus(campOnWednesday);
    expect(linkedOn('2026-09-10')).toEqual(['Camp — Ponorul camp, day 3 of 3']);
    fireEvent.blur(campOnWednesday);

    // A record of one day has no other days to be tied to.
    const trip = chipsOn('2026-09-09').find((chip) => chip.dataset.span === 'single')!;
    fireEvent.mouseEnter(trip);
    expect(linkedOn('2026-09-09')).toEqual([]);
  });

  /**
   * A day with nothing on it is an empty day and not a hole. The cell and its content box are
   * still drawn, so the month reads as a grid; what is missing is the records, not the day.
   */
  it('draws a day with nothing on it as an empty day', () => {
    const busy = inThisMonth(10);
    answer([row({ start: busy })]);
    showMonth();

    const quiet = screen.getByTestId(`calendar-day-${inThisMonth(11)}`);
    expect(quiet).toBeTruthy();
    expect(within(quiet).queryAllByTestId('calendar-chip')).toHaveLength(0);
    expect(chipsOn(busy)).toHaveLength(1);
  });

  /**
   * A day in the grid is read the same way down as a day in the week strip: what claims no time
   * of day first, in the order the answer gave, then what claims one, earliest first. The answer
   * arranges rows by the day they fall on and separates rows sharing a day only by an identifier
   * that means nothing, so there is no order inside a day for the grid to have preserved — and
   * two views of the same Saturday that disagreed about which trip came first would be worse than
   * either of them alone.
   */
  it('reads a day down by the time each record claims, whichever way the answer listed them', () => {
    const day = inThisMonth(8);
    answer([
      row({ id: 'a', title: 'Zulu', start: day, startTime: '18:00:00' }),
      row({ id: 'b', title: 'Alpha', start: day, startTime: '07:00:00' }),
      row({ id: 'c', title: 'Ponorul camp', start: day }),
    ]);
    showMonth();

    expect(chipsOn(day).map((chip) => chip.textContent)).toEqual([
      'Ponorul camp',
      '07:00 Alpha',
      '18:00 Zulu',
    ]);
  });

  /** A grid is drawn over the days it shows, so the window it asks for is the panel's own. */
  it('asks for the month it is showing rather than the window the record used', () => {
    showMonth();

    const asked = lastParams();
    expect(asked.from <= dayjs().startOf('month').format('YYYY-MM-DD')).toBe(true);
    expect(asked.to >= dayjs().endOf('month').format('YYYY-MM-DD')).toBe(true);
  });

  /**
   * A year is twelve cells an inch wide, so each says how much is in it rather than naming any of
   * it — a record that spans two months is counted in both, for the same reason it is drawn in
   * every day it covers.
   */
  it('counts a year by month rather than naming what is in it', () => {
    const day = dayjs().startOf('year').add(1, 'month');
    answer([
      row({ id: 'a', start: day.format('YYYY-MM-DD') }),
      row({ id: 'b', start: day.add(2, 'day').format('YYYY-MM-DD') }),
    ]);
    show();
    fireEvent.click(screen.getByText('Year'));

    const cell = screen.getByTestId(`calendar-month-${day.format('YYYY-MM')}`);
    expect(cell.textContent).toContain('2');
  });

  /**
   * An empty grid cannot say why it is empty. The sentence that can is drawn above it, so a read
   * that failed is never mistaken for a stretch of days with nothing on them.
   */
  it('says why a grid is empty rather than leaving blank cells to say it', () => {
    answer([]);
    showMonth();

    expect(screen.getByTestId('calendar-empty').textContent).toContain('Nothing is recorded');
  });
});

/** A day of the week the page opens on, so a fixture falls in the strip rather than beside it. */
const inThisWeek = (offsetDays: number): string =>
  dayjs().startOf('week').add(offsetDays, 'day').format('YYYY-MM-DD');

function showWeek() {
  const rendered = show();
  fireEvent.click(screen.getByText('Week'));
  return rendered;
}

/** Every chip drawn in one day's column of the strip, in the order the strip drew them. */
function chipsInColumn(day: string): HTMLElement[] {
  return within(screen.getByTestId(`calendar-week-day-${day}`)).queryAllByTestId('calendar-chip');
}

describe('the calendar as a week of days', () => {
  /**
   * The strip covers the week the day it is given falls in, and that week is the reader's
   * language's week — Monday-first in Romanian, Sunday-first in English — decided once by the
   * date library for every date this application draws. Seven columns, and the window asked for
   * is those same seven days: a strip drawn over days the answer did not cover would have columns
   * saying "nothing is happening" about days nobody asked about.
   */
  it('draws the seven days of the week it is showing, and asks for exactly those days', () => {
    showWeek();

    const days = Array.from({ length: 7 }, (_, offset) => inThisWeek(offset));
    days.forEach((day) => {
      expect(screen.getByTestId(`calendar-week-day-${day}`)).toBeTruthy();
    });
    expect(screen.queryByTestId(`calendar-week-day-${inThisWeek(7)}`)).toBeNull();
    expect(screen.queryByTestId(`calendar-week-day-${inThisWeek(-1)}`)).toBeNull();

    expect(lastParams().from).toBe(days[0]);
    expect(lastParams().to).toBe(days[6]);
  });

  /**
   * A day is read down: the things that claim no time of day first, then the ones that do, at
   * their time. Most records here claim none — a trip states the times its party was under ground
   * and a camp states days — so a column that ordered only on times would have nowhere to put
   * most of what is on it. The untimed row is asserted present, because losing it is the failure
   * this guards.
   */
  it('stacks a day by the time each record claims, and keeps the ones that claim none', () => {
    const day = inThisWeek(3);
    answer([
      row({ id: 'a', title: 'Zulu', start: day, startTime: '18:00:00' }),
      row({ id: 'b', title: 'Ponorul camp', start: day }),
      row({ id: 'c', title: 'Alpha', start: day, startTime: '07:00:00' }),
    ]);
    showWeek();

    expect(chipsInColumn(day).map((chip) => chip.textContent)).toEqual([
      'Ponorul camp',
      '07:00 Alpha',
      '18:00 Zulu',
    ]);
  });

  /**
   * A record lasting several days stands in every column it covers and reads as one record: it
   * is closed and named in the column it begins in, closed again in the column it ends in, and
   * the columns between draw its rail and no words. It states its time only where it begins — it
   * started once, not once a morning — and says which of its days each column is.
   */
  it('stands a record in every day of the week it covers, as one record', () => {
    const first = inThisWeek(1);
    const last = inThisWeek(4);
    answer([row({ title: 'Ponorul camp', start: first, end: last, startTime: '09:00:00' })]);
    showWeek();

    const marks = [1, 2, 3, 4].map((offset) => {
      const chip = chipsInColumn(inThisWeek(offset))[0];
      expect(chip).toBeTruthy();
      return [chip.textContent, chip.dataset.span, chip.getAttribute('aria-label')];
    });

    expect(marks).toEqual([
      ['09:00 Ponorul camp', 'start', 'Trip — Ponorul camp, 09:00, day 1 of 4'],
      ['', 'middle', 'Trip — Ponorul camp, day 2 of 4'],
      ['', 'middle', 'Trip — Ponorul camp, day 3 of 4'],
      ['', 'end', 'Trip — Ponorul camp, day 4 of 4'],
    ]);
  });

  /**
   * A record that began before the week on show says its name in the strip's first column: it
   * has no earlier column to have said it in. It did not begin there, so it is drawn open on the
   * side it arrived by and claims no time, and the day it is on is counted from where it began.
   */
  it('names a record that arrived from the week before in the first column', () => {
    answer([
      row({
        title: 'Ponorul camp',
        start: inThisWeek(-2),
        end: inThisWeek(1),
        startTime: '09:00:00',
      }),
    ]);
    showWeek();

    const arriving = chipsInColumn(inThisWeek(0))[0];
    expect(arriving.textContent).toBe('Ponorul camp');
    expect(arriving.dataset.span).toBe('middle');
    expect(arriving.dataset.named).toBe('true');
    expect(arriving.getAttribute('aria-label')).toBe('Trip — Ponorul camp, day 3 of 4');

    const ending = chipsInColumn(inThisWeek(1))[0];
    expect(ending.textContent).toBe('');
    expect(ending.dataset.span).toBe('end');
  });

  /** Moving a week moves the days drawn and the days asked for together. */
  it('moves a week at a time', () => {
    showWeek();
    fireEvent.click(screen.getByTestId('calendar-week-next'));

    expect(screen.getByTestId(`calendar-week-day-${inThisWeek(7)}`)).toBeTruthy();
    expect(lastParams().from).toBe(inThisWeek(7));
    expect(lastParams().to).toBe(inThisWeek(13));
  });
});

describe('the calendar as an agenda', () => {
  /**
   * The agenda is the record read forwards: the same answer, the same paging and the same click
   * through to the record, with each row drawn as one entry instead of as a set of cells to
   * compare across. The column headings go with the cells — there is nothing left to head.
   */
  it('draws each row as one entry rather than as a row of cells', () => {
    answer([row({ title: 'Coiba Mare recce', start: '2026-09-05', startTime: '08:30:00' })]);
    show();
    fireEvent.click(screen.getByText('Agenda'));

    const entry = screen.getByTestId('calendar-agenda-row');
    expect(entry.textContent).toContain('Coiba Mare recce');
    expect(entry.textContent).toContain('08:30');
    expect(entry.textContent).toContain('Trip');
    expect(screen.queryByText('When')).toBeNull();
  });

  /** The window stays the reader's own: the agenda is a way of reading the record, not of days. */
  it('keeps the window the reader picked, and the way through to the record', () => {
    answer([row({ id: 'a', source: 'expedition', title: 'Ponorul camp' })]);
    show();
    const chosenBefore = lastParams();
    fireEvent.click(screen.getByText('Agenda'));

    expect(lastParams().from).toBe(chosenBefore.from);
    expect(lastParams().to).toBe(chosenBefore.to);
    // The range control names itself on each end of the window it offers.
    expect(screen.getAllByTestId('calendar-window').length).toBeGreaterThan(0);

    fireEvent.click(screen.getByText('Ponorul camp'));
    expect(navigateSpy).toHaveBeenCalledWith('/expeditions/a');
  });
});
