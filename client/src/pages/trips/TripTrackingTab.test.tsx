// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TrackingState, TripLogInfo } from '../../api/hooks.ts';

const ANA = '11111111-1111-1111-1111-111111111111';
const BOGDAN = '22222222-2222-2222-2222-222222222222';
const CARMEN = '33333333-3333-3333-3333-333333333333';

const trackingQuery = vi.fn();
const eventsQuery = vi.fn();
const recordEvents = vi.fn();
const resolveDepth = vi.fn();
const deleteEvent = vi.fn();
const setTracking = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  TRACKING_EVENT_KINDS: ['entered', 'atStation', 'atDepth', 'note', 'exited'],
  useTripTracking: () => trackingQuery(),
  useTripTrackingEvents: () => eventsQuery(),
  useRecordTrackingEvents: () => ({ mutateAsync: recordEvents, isPending: false }),
  useResolveTrackingDepth: () => ({ mutateAsync: resolveDepth, isPending: false }),
  useDeleteTrackingEvent: () => ({ mutateAsync: deleteEvent, isPending: false }),
  useSetTripTracking: () => ({ mutateAsync: setTracking, isPending: false }),
  useCreateTrackingTeam: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useRenameTrackingTeam: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useDeleteTrackingTeam: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useSurveyModelsForCaves: () => ({ data: [], isPending: false }),
  useCaveNames: () => new Map<string, string>(),
  // The watch on the survey model it is resolved against. These tests set no model on the watch,
  // so nothing is asked for and nothing is offered; that surface has its own tests. The whole log
  // the replay reads is asked for on the same surface and is not asked for here either.
  useSurveyModel: () => ({ data: undefined, isPending: false }),
  useTripTrackingEventLog: () => ({ data: undefined, isPending: true, error: null }),
  surveyModelReadableByViewer: (m: { format: string }) => m.format === 'lox' || m.format === 'survex3d',
}));

// What decides how big every target on this surface is drawn, and how much room the selection
// column is given. Mocked rather than driven by a media query, as the rest of this application
// tests its finger layouts; false by default, which is the machine every other test here is being
// read on.
let coarse = false;
vi.mock('../../hooks/useCoarsePointer.ts', () => ({ useCoarsePointer: () => coarse }));

// The other axis, and deliberately not the same one: how much room there is across is what decides
// whether five columns can stand side by side. False by default — the desk this suite is read on.
let narrow = false;
vi.mock('../../hooks/useIsMobile.ts', () => ({ useIsMobile: () => narrow }));

const { default: TripTrackingTab } = await import('./TripTrackingTab.tsx');

function state(overrides: Partial<TrackingState> = {}): TrackingState {
  return {
    state: 'armed',
    surveyModelId: null,
    referenceStationName: null,
    depthFilter: [],
    armedAt: '2026-09-12T06:00:00Z',
    closedAt: null,
    positionsWithheld: false,
    teams: [],
    participants: [
      {
        caverId: ANA,
        teamId: null,
        lastKind: 'atStation',
        lastRecordedAt: '2026-09-12T07:00:00Z',
        stationName: 'P12',
        depthM: 84,
        out: false,
      },
      {
        caverId: BOGDAN,
        teamId: null,
        lastKind: 'entered',
        lastRecordedAt: '2026-09-12T06:30:00Z',
        stationName: null,
        depthM: null,
        out: false,
      },
    ],
    ...overrides,
  };
}

function trip(overrides: Partial<TripLogInfo> = {}): TripLogInfo {
  return {
    id: 'trip-1',
    title: 'Digging weekend',
    caveIds: [],
    participants: [
      { caverId: ANA, name: 'Ana Popescu' },
      { caverId: BOGDAN, name: 'Bogdan Ilie' },
      { caverId: CARMEN, name: 'Carmen Radu' },
    ],
    ...overrides,
  } as unknown as TripLogInfo;
}

function show(canEdit = true) {
  return render(
    <App>
      <TripTrackingTab trip={trip()} canEdit={canEdit} />
    </App>,
  );
}

/**
 * The row checkboxes, in the order the table draws them.
 *
 * Every checkbox inside the table is one of these. Choosing everybody is said above the table in
 * words rather than by a checkbox in its header, so nothing has to be sliced off the front — and a
 * slice here is exactly what would hide the duplicate this file now asserts cannot exist.
 */
function rowChecks() {
  return within(screen.getByTestId('trip-tracking-participants')).getAllByRole('checkbox');
}

beforeEach(() => {
  trackingQuery.mockReturnValue({
    data: state(),
    isPending: false,
    error: null,
    refetch: vi.fn(),
  });
  eventsQuery.mockReturnValue({ data: { items: [], page: 1, pageSize: 20, totalItems: 0 }, isPending: false });
  recordEvents.mockReset().mockResolvedValue([{}, {}]);
  resolveDepth.mockReset().mockResolvedValue([]);
  deleteEvent.mockReset().mockResolvedValue(undefined);
  coarse = false;
  narrow = false;
  setTracking.mockReset().mockResolvedValue(state());
});

afterEach(cleanup);

describe('TripTrackingTab', () => {
  /**
   * The rule this surface exists to get right. A station name is location data and is kept from a
   * reader who may not place the cave — it arrives as an absence, exactly as it does for somebody
   * nobody has reported yet. Drawn as an empty cell it would say "nobody knows where this caver
   * is", which on a page somebody opens during a callout is the worst sentence it could say.
   */
  it('says a withheld position was withheld, and never draws it as nobody knowing', () => {
    trackingQuery.mockReturnValue({
      data: state({
        positionsWithheld: true,
        participants: [
          {
            caverId: ANA,
            teamId: null,
            lastKind: 'atStation',
            lastRecordedAt: '2026-09-12T07:00:00Z',
            // Reported at a station, and the station kept back — the shape the server sends to a
            // reader without the right to place the cave.
            stationName: null,
            depthM: null,
            out: false,
          },
        ],
      }),
      isPending: false,
      error: null,
      refetch: vi.fn(),
    });
    show(false);

    expect(screen.getByTestId('trip-tracking-position-withheld')).toHaveTextContent(
      'Not shown to you',
    );
    // And said once for the whole page as well, because a reader has to learn that they are being
    // shown less than exists rather than infer it from the shape of what is missing.
    expect(screen.getByTestId('trip-tracking-positions-withheld')).toBeTruthy();
  });

  /**
   * The other half of the same rule, and the one it would be easy to lose while fixing the first:
   * somebody nobody has reported at all has no position to withhold, so marking them withheld
   * would invent a secret and tell a co-ordinator the page is hiding something it is not.
   */
  it('does not call an unreported caver withheld, even while other positions are being withheld', () => {
    trackingQuery.mockReturnValue({
      data: state({
        positionsWithheld: true,
        participants: [
          {
            caverId: ANA,
            teamId: null,
            lastKind: 'atDepth',
            lastRecordedAt: '2026-09-12T07:00:00Z',
            stationName: null,
            depthM: null,
            out: false,
          },
          {
            caverId: CARMEN,
            teamId: null,
            lastKind: null,
            lastRecordedAt: null,
            stationName: null,
            depthM: null,
            out: false,
          },
        ],
      }),
      isPending: false,
      error: null,
      refetch: vi.fn(),
    });
    show(false);

    expect(screen.getAllByTestId('trip-tracking-position-withheld')).toHaveLength(1);
  });

  /**
   * The third case, and the ordinary one five minutes after a party goes in: everybody's only
   * report is "went in", which carries no place at all. The withholding flag is raised by the
   * configuration as well as by positions, so on a protected cave every one of those rows would
   * otherwise be tagged with a sentence that states, as fact, that a position exists and is being
   * kept back — for people nobody has placed. A page read during a callout does not get to invent
   * that.
   */
  it('does not state a position was withheld when the last report carried no position at all', () => {
    trackingQuery.mockReturnValue({
      data: state({
        positionsWithheld: true,
        participants: [
          {
            caverId: ANA,
            teamId: null,
            // Went in, and nothing since: there is no position on the server to keep back.
            lastKind: 'entered',
            lastRecordedAt: '2026-09-12T06:30:00Z',
            stationName: null,
            depthM: null,
            out: false,
          },
          {
            caverId: BOGDAN,
            teamId: null,
            // A station report always carries a station, so an empty one here can only be a
            // withholding — and that one is still said outright.
            lastKind: 'atStation',
            lastRecordedAt: '2026-09-12T07:00:00Z',
            stationName: null,
            depthM: null,
            out: false,
          },
        ],
      }),
      isPending: false,
      error: null,
      refetch: vi.fn(),
    });
    show(false);

    expect(screen.getAllByTestId('trip-tracking-position-withheld')).toHaveLength(1);
    expect(screen.getByTestId('trip-tracking-position-maybe-withheld')).toHaveTextContent(
      'Not shown, or not reported',
    );
  });

  /**
   * A log that could not be read is not an empty log. Under the table's own empty text, a refused
   * or dropped request says "nothing has been reported yet" directly beneath a participants table
   * showing cavers at stations — a failure to learn something drawn as a fact about the world, on
   * the surface that is the record of what came in over the radio.
   */
  it('says the reports could not be read rather than that nothing was reported', () => {
    eventsQuery.mockReturnValue({
      data: undefined,
      isPending: false,
      error: new Error('the log could not be read'),
    });
    show();

    expect(screen.getByTestId('trip-tracking-events-unavailable')).toBeTruthy();
    expect(screen.queryByText('Nothing has been reported yet.')).toBeNull();
  });

  // Reports land on an armed watch and on no other, so a form offered on a watch nobody started
  // is a form whose every use is refused — at the moment somebody is relaying word out of a cave.
  it('offers the report form only once tracking has been started', () => {
    trackingQuery.mockReturnValue({
      data: state({ state: 'off', armedAt: null }),
      isPending: false,
      error: null,
      refetch: vi.fn(),
    });
    const { unmount } = show();

    expect(screen.getByTestId('trip-tracking-not-armed')).toHaveTextContent(
      'Tracking is not on for this trip',
    );
    expect(screen.queryByTestId('trip-tracking-record')).toBeNull();
    unmount();

    trackingQuery.mockReturnValue({
      data: state(),
      isPending: false,
      error: null,
      refetch: vi.fn(),
    });
    show();
    expect(screen.getByTestId('trip-tracking-record')).toBeTruthy();
    expect(screen.queryByTestId('trip-tracking-not-armed')).toBeNull();
  });

  /**
   * A party that reached a station reached it together, at one moment. One request per person
   * would put that moment on the log several times with nothing tying the rows together, and the
   * log is what a rescue reads.
   */
  it('records one report for every caver the table has selected, in a single request', async () => {
    show();

    const checks = rowChecks();
    fireEvent.click(checks[0]);
    fireEvent.click(checks[1]);

    fireEvent.click(screen.getByTestId('trip-tracking-record'));

    await waitFor(() => expect(recordEvents).toHaveBeenCalledTimes(1));
    expect(recordEvents.mock.calls[0][0]).toMatchObject({
      tripLogId: 'trip-1',
      caverIds: [ANA, BOGDAN],
      kind: 'entered',
    });
  });

  it('will not record a report about nobody', () => {
    show();

    expect(screen.getByTestId('trip-tracking-nobody')).toBeTruthy();
    fireEvent.click(screen.getByTestId('trip-tracking-record'));
    expect(recordEvents).not.toHaveBeenCalled();
  });

  /**
   * A depth becomes whichever station is nearest under a filter and a datum somebody set earlier,
   * and that decision is worth watching being made: it is how a position lands on the log naming a
   * place the party is not. The preview shows the candidates and how far off each is while the
   * number can still be changed.
   */
  it('shows which stations a depth could mean before the depth is recorded', async () => {
    resolveDepth.mockResolvedValue([
      { stationName: 'P12', surveyName: 'entrance series', depthM: 118, deltaM: 2 },
      { stationName: 'Q4', surveyName: null, depthM: 131, deltaM: 11 },
    ]);
    show();

    const kind = within(screen.getByTestId('trip-tracking-kind')).getByRole('combobox');
    await act(async () => {
      fireEvent.mouseDown(kind);
    });
    await act(async () => {
      fireEvent.click(await screen.findByTitle('At a depth'));
    });

    fireEvent.change(screen.getByTestId('trip-tracking-depth'), { target: { value: '120' } });

    fireEvent.click(screen.getByTestId('trip-tracking-depth-preview'));

    await waitFor(() => expect(resolveDepth).toHaveBeenCalledTimes(1));
    expect(resolveDepth.mock.calls[0][0]).toMatchObject({ tripLogId: 'trip-1', depthM: 120 });

    const candidates = await screen.findByTestId('trip-tracking-depth-candidates');
    expect(candidates).toHaveTextContent('P12');
    // How far off each one is, because "the nearest station" is only reassuring with the distance
    // beside it — eleven metres out is a different claim from two.
    expect(candidates).toHaveTextContent('2 m off');
    expect(candidates).toHaveTextContent('Q4');
  });

  // Corrections are delete-and-say-again. Nothing on this surface edits a report, so the delete
  // has to be reachable or a wrong position stays on the log for good.
  it('takes a wrong report off the log rather than offering to edit it', async () => {
    eventsQuery.mockReturnValue({
      data: {
        items: [
          {
            id: 'event-1',
            caverId: ANA,
            teamId: null,
            kind: 'atStation',
            surveyModelId: null,
            stationName: 'P12',
            depthEnteredM: null,
            note: null,
            recordedAt: '2026-09-12T07:00:00Z',
          },
        ],
        page: 1,
        pageSize: 20,
        totalItems: 1,
      },
      isPending: false,
    });
    show();

    fireEvent.click(screen.getByTestId('trip-tracking-event-delete-event-1'));
    fireEvent.click(await screen.findByText('OK'));

    await waitFor(() => expect(deleteEvent).toHaveBeenCalledTimes(1));
    expect(deleteEvent.mock.calls[0][0]).toMatchObject({ tripLogId: 'trip-1', eventId: 'event-1' });
  });

  it('shows a report whose position was withheld as withheld on the log too', () => {
    trackingQuery.mockReturnValue({
      data: state({ positionsWithheld: true }),
      isPending: false,
      error: null,
      refetch: vi.fn(),
    });
    eventsQuery.mockReturnValue({
      data: {
        items: [
          {
            id: 'event-1',
            caverId: ANA,
            teamId: null,
            // A station report always carries a station, so an empty one here is a withholding
            // and cannot be anything else.
            kind: 'atStation',
            surveyModelId: null,
            stationName: null,
            depthEnteredM: null,
            note: null,
            recordedAt: '2026-09-12T07:00:00Z',
          },
        ],
        page: 1,
        pageSize: 20,
        totalItems: 1,
      },
      isPending: false,
    });
    show(false);

    const log = screen.getByTestId('trip-tracking-events');
    expect(within(log).getByTestId('trip-tracking-position-withheld')).toBeTruthy();
  });

  it('offers a reader who may not write the trip nothing to write with', () => {
    show(false);

    expect(screen.queryByTestId('trip-tracking-record')).toBeNull();
    expect(screen.queryByTestId('trip-tracking-arm')).toBeNull();
    expect(screen.queryByTestId('trip-tracking-event-delete-event-1')).toBeNull();
    // But the watch itself is drawn: who is in and who is out is news to whoever is reading.
    expect(screen.getByTestId('trip-tracking-state')).toHaveTextContent('Tracking');
  });

  /**
   * What this surface is read and written on, and what it takes to be readable there.
   *
   * <b>A table that is told to keep its own overflow keeps it, which is not the same as showing
   * it.</b> Left to burst out, the log's inner table came to 543px inside a 364px card on a 412px
   * screen and the whole page gained that width, so reading where somebody was and pressing Save
   * became two views of the page 334px apart. Scrolled inside itself instead, the page stops
   * moving — and the columns past the fold are still past the fold: measured at rest, the
   * participants' "Where" began 139px beyond the right edge and the log's delete control 457px
   * beyond it, inside scrollers that give no sign they have more to the right. So where there is
   * no room across, the columns are stacked down the row instead and every one of them is on
   * screen without a gesture.
   *
   * <b>And the target for choosing who a report is about was the smallest thing on the page.</b>
   * Selecting cavers is the first act here and antd draws a checkbox for a cursor; the cell it
   * sits in is the target instead, which is why the column is asked for a width a finger fits in.
   */
  describe('read on a phone', () => {
    it('keeps a wide table\'s overflow inside the table rather than handing it to the page', () => {
      show();

      for (const id of ['trip-tracking-participants', 'trip-tracking-events']) {
        // The class antd puts on a table that has been told to scroll horizontally. Asserted
        // rather than the pixel width, because the width that overflows depends on the data and
        // the question is whether the overflow has somewhere of its own to go.
        expect(screen.getByTestId(id).className).toContain('ant-table-scroll-horizontal');
      }
    });

    it('stacks a caver\'s columns down the row where there is no room across', () => {
      narrow = true;
      show();
      const table = screen.getByTestId('trip-tracking-participants');

      // Nothing stands side by side any more, so there is no sideways overflow to keep — and a
      // table that still scrolled sideways would be the defect this replaces.
      expect(table.className).not.toContain('ant-table-scroll-horizontal');
      // The headings go with it: every field carries its own name on the row, and a row of
      // headings would be the same words again on the axis there is least of.
      expect(table.querySelector('thead')).toBeNull();

      const row = within(table).getByText('Ana Popescu').closest('tr') as HTMLElement;
      for (const label of ['Team', 'Last report', 'When', 'Where']) {
        expect(within(row).getByText(label)).toBeTruthy();
      }
      // Including the one the whole surface is read for, and the one that carries whether a
      // position was withheld.
      expect(within(row).getByText('P12 · 84 m')).toBeTruthy();
    });

    it('says a withheld position on the row itself rather than past the right edge', () => {
      narrow = true;
      trackingQuery.mockReturnValue({
        data: state({
          positionsWithheld: true,
          participants: [
            {
              caverId: ANA,
              teamId: null,
              lastKind: 'atStation',
              lastRecordedAt: '2026-09-12T07:00:00Z',
              stationName: null,
              depthM: null,
              out: false,
            },
          ],
        }),
        isPending: false,
        error: null,
        refetch: vi.fn(),
      });
      show(false);

      const row = screen
        .getByTestId('trip-tracking-participants')
        .querySelector('tbody tr') as HTMLElement;
      expect(within(row).getByTestId('trip-tracking-position-withheld')).toBeTruthy();
    });

    it('puts the control that corrects a report on the report it corrects', () => {
      narrow = true;
      eventsQuery.mockReturnValue({
        data: {
          items: [
            {
              id: 'event-1',
              caverId: ANA,
              teamId: null,
              kind: 'note',
              surveyModelId: null,
              stationName: null,
              depthEnteredM: null,
              note: 'radio check',
              recordedAt: '2026-09-12T07:00:00Z',
            },
          ],
          page: 1,
          pageSize: 20,
          totalItems: 1,
        },
        isPending: false,
      });
      show();

      const row = screen
        .getByTestId('trip-tracking-events')
        .querySelector('tbody tr') as HTMLElement;
      // In the row's own cell rather than in a sixth column of its own, which is where it began
      // 457px past the right edge of a 364px scroller.
      expect(within(row).getByTestId('trip-tracking-event-delete-event-1')).toBeTruthy();
      expect(row.querySelectorAll('td')).toHaveLength(1);
    });

    it('gives the selection column room for a finger, and leaves it alone for a cursor', () => {
      const { container, unmount } = show();
      const widthOf = (root: HTMLElement) =>
        root.querySelector('colgroup col')?.getAttribute('style') ?? '';
      expect(widthOf(container)).not.toContain('48px');
      unmount();

      coarse = true;
      const finger = show();
      expect(widthOf(finger.container)).toContain('48px');
    });
  });

  /**
   * Choosing everybody at once — the act that decides who a report is about, said in one place.
   *
   * <b>It is not antd's header checkbox, and the reason is a defect rather than a preference.</b>
   * A table told to keep its own overflow is given a measure row, and the measure row clones every
   * column's title into a hidden cell — so the header's live "Select all" was minted a second time
   * inside a `height: 0` box marked `aria-hidden`. Measured at 1600x1000 on a mouse: two checkboxes
   * named "Select all", the second reachable by Tab with no visible focus anywhere on the page, and
   * Space on it silently selected the whole party. A stacked phone layout has no header to put one
   * in either, so one control above the table answers both.
   */
  describe('choosing everybody', () => {
    it('leaves nothing that can be reached by keyboard inside a row hidden from everybody', () => {
      const { container } = show();

      // The general rule rather than a spot check: anything a browser will focus, inside a subtree
      // that says it is not there, is a stop with nothing at the end of it.
      const reachable = Array.from(container.querySelectorAll('[aria-hidden="true"]')).flatMap(
        (hidden) =>
          Array.from(
            hidden.querySelectorAll('input, button, select, textarea, a[href], [tabindex]'),
          ).filter((element) => !(element as HTMLInputElement).disabled),
      );
      expect(reachable).toHaveLength(0);
    });

    it('offers exactly one way to select everybody, and says what it does in words', () => {
      show();

      // One control, named, outside the table — the table holds a checkbox per caver and nothing
      // else. Two people are on this watch.
      expect(rowChecks()).toHaveLength(2);
      expect(screen.getByTestId('trip-tracking-select-all').closest('label')).toHaveTextContent(
        'Select everybody (2)',
      );
    });

    it('selects the whole party and lets it go again', () => {
      show();
      const everybody = screen.getByTestId('trip-tracking-select-all');

      fireEvent.click(everybody);
      expect(screen.getByTestId('trip-tracking-record')).toHaveTextContent('Record for 2 selected');

      fireEvent.click(everybody);
      expect(screen.getByTestId('trip-tracking-record')).toHaveTextContent('Record for 0 selected');
    });

    it('offers it on a phone too, where there is no header at all', () => {
      narrow = true;
      show();

      expect(screen.getByTestId('trip-tracking-select-all')).toBeTruthy();
    });

    it('offers nothing to select with to a reader who may not write the trip', () => {
      show(false);

      expect(screen.queryByTestId('trip-tracking-select-all')).toBeNull();
    });
  });

  /**
   * The one control that takes a report off a log nothing can edit. It was a bare 14px icon — the
   * smallest thing on the page — and in portrait it sat 147px off the right edge, so correcting a
   * mistyped position meant dragging the whole page sideways to reach a target a finger covers
   * entirely.
   */
  describe('drawn for a finger', () => {
    const oneReport = () =>
      eventsQuery.mockReturnValue({
        data: {
          items: [
            {
              id: 'event-1',
              caverId: ANA,
              teamId: null,
              kind: 'note',
              surveyModelId: null,
              stationName: null,
              depthEnteredM: null,
              note: 'radio check',
              recordedAt: '2026-09-12T07:00:00Z',
            },
          ],
          page: 1,
          pageSize: 20,
          totalItems: 1,
        },
        isPending: false,
      });

    it('makes taking a wrong report off the log a button, sized for whatever is pressing it', () => {
      oneReport();
      coarse = true;
      show();

      const remove = screen.getByTestId('trip-tracking-event-delete-event-1');
      // A button rather than an icon with a role attached: what changed is that there is a
      // control with its own box, not only a glyph.
      expect(remove.tagName).toBe('BUTTON');
      expect(remove).toHaveClass('ant-btn-lg');
    });

    it('keeps the dense log where there is a mouse', () => {
      oneReport();
      show();

      expect(screen.getByTestId('trip-tracking-event-delete-event-1')).toHaveClass('ant-btn-sm');
    });

    it('sizes the form a party is actually recorded with, fields and all', () => {
      // This is the surface somebody uses standing outside a cave with one hand free, so every
      // part of it is on the same branch: the kind of report, the note typed into it, the moment
      // it names, and the two buttons that send it.
      coarse = true;
      show();

      expect(screen.getByTestId('trip-tracking-record')).toHaveClass('ant-btn-lg');
      expect(screen.getByTestId('trip-tracking-mark-out')).toHaveClass('ant-btn-lg');
      expect(screen.getByTestId('trip-tracking-kind')).toHaveClass('ant-select-lg');
      expect(screen.getByTestId('trip-tracking-note')).toHaveClass('ant-input-lg');
      expect(
        screen.getByTestId('trip-tracking-recorded-at').closest('.ant-picker'),
      ).toHaveClass('ant-picker-large');
    });

    it('keeps the dense form where there is a mouse', () => {
      show();

      expect(screen.getByTestId('trip-tracking-record')).not.toHaveClass('ant-btn-lg');
      expect(screen.getByTestId('trip-tracking-kind')).not.toHaveClass('ant-select-lg');
      expect(
        screen.getByTestId('trip-tracking-recorded-at').closest('.ant-picker'),
      ).not.toHaveClass('ant-picker-large');
    });

    it('still deletes when the finger-sized control is the one pressed', async () => {
      // The point of all of this is that the act still happens. Driven through the enlarged
      // control and its enlarged confirmation, which are different elements from the ones the
      // desk layout draws.
      oneReport();
      coarse = true;
      show();

      fireEvent.click(screen.getByTestId('trip-tracking-event-delete-event-1'));
      fireEvent.click(await screen.findByText('OK'));

      await waitFor(() => expect(deleteEvent).toHaveBeenCalledTimes(1));
      expect(deleteEvent.mock.calls[0][0]).toMatchObject({ eventId: 'event-1' });
    });
  });


  /**
   * The calendar behind "when it was said", which is the one panel on this page that is not drawn
   * inside it.
   *
   * Sized for a finger it is around 730px tall, and antd anchors it to the field and offers it
   * whichever side of that field has more room — without regard to whether the panel fits there.
   * A field halfway down a phone has about four hundred pixels either way, so the panel was drawn
   * with three hundred of it off the top of the screen, taking the month, the year and every arrow
   * that walks backwards through time with it. On a control whose only purpose is naming a moment
   * in the past, that is the whole control.
   */
  describe('the relayed-time calendar', () => {
    /** jsdom lays nothing out and scrolls nothing, so both have to be said out loud. */
    function fieldAt(bottom: number) {
      const field = screen.getByTestId('trip-tracking-recorded-at') as HTMLInputElement;
      field.getBoundingClientRect = () => ({ bottom }) as DOMRect;
      field.focus();
      return field;
    }

    let scrolled: ReturnType<typeof vi.fn<(arg?: boolean | ScrollIntoViewOptions) => void>>;

    beforeEach(() => {
      scrolled = vi.fn<(arg?: boolean | ScrollIntoViewOptions) => void>();
      Element.prototype.scrollIntoView = scrolled;
    });

    it('puts the field where the enlarged panel has room before opening it', () => {
      coarse = true;
      show();
      const field = fieldAt(500);

      fireEvent.mouseDown(field);
      fireEvent.click(field);

      expect(scrolled).toHaveBeenCalled();
      expect(scrolled.mock.instances[0]).toBe(field);
      expect(scrolled.mock.calls[0][0]).toMatchObject({ block: 'start' });
    });

    it('leaves a field that already has room where it is', () => {
      // A page that scrolls when it did not need to is the same surprise as one that does not
      // scroll when it did.
      coarse = true;
      show();
      const field = fieldAt(80);

      fireEvent.mouseDown(field);
      fireEvent.click(field);

      expect(scrolled).not.toHaveBeenCalled();
    });

    it('never moves the page under a mouse', () => {
      // On a cursor the calendar is antd's own size and fits beside the field wherever the field
      // is, so a page that jumped when a date picker opened would be a defect rather than a fix.
      show();
      const field = fieldAt(500);

      fireEvent.mouseDown(field);
      fireEvent.click(field);

      expect(scrolled).not.toHaveBeenCalled();
    });
  });

});
