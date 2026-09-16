// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi, type MockInstance } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
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
const setLabel = vi.fn();

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
  // The pictures linked to the model's stations, asked for on that same surface and only once the
  // model has been opened — which these tests never do, there being no model on the watch.
  useResLinksForTarget: () => ({ data: undefined, isPending: false, error: null }),
  surveyModelReadableByViewer: (m: { format: string }) => m.format === 'lox' || m.format === 'survex3d',
  // Publishing the trip: this tab mounts the card that offers it, and what the card does has its
  // own tests. Nothing here has published anything, so the list is empty and neither write is
  // reached.
  useTripTrackingShares: () => ({ data: [], error: null }),
  useMintTripTrackingShare: () => ({ mutateAsync: vi.fn(), isPending: false }),
  useRevokeTripTrackingShare: () => ({ mutateAsync: vi.fn(), isPending: false }),
  // Naming somebody as a follower of the published page sees them. The dialog that does it has its
  // own tests; what this suite asks is whether the tab reaches it at all, which nothing did before.
  useSetTrackingParticipantLabel: () => ({ mutateAsync: setLabel, isPending: false }),
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
    // What the published page would call the party. The panel that mints a link words its notice
    // from this, so the fixture states it rather than leaving the surface to guess.
    publishesRealNames: true,
    teams: [],
    participants: [
      {
        caverId: ANA,
        teamId: null,
        lastKind: 'atStation',
        lastRecordedAt: '2026-09-12T07:00:00Z',
        positionRecordedAt: '2026-09-12T07:00:00Z',
        stationName: 'P12',
        depthM: 84,
        in: true,
        out: false,
        label: null,
      },
      {
        caverId: BOGDAN,
        teamId: null,
        lastKind: 'entered',
        lastRecordedAt: '2026-09-12T06:30:00Z',
        positionRecordedAt: null,
        stationName: null,
        depthM: null,
        in: true,
        out: false,
        label: null,
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
  setLabel.mockReset().mockResolvedValue({ caverId: ANA, label: null });
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
            positionRecordedAt: null,
            // Reported at a station, and the station kept back — the shape the server sends to a
            // reader without the right to place the cave.
            stationName: null,
            depthM: null,
            in: true,
            out: false,
            label: null,
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
            positionRecordedAt: null,
            stationName: null,
            depthM: null,
            in: true,
            out: false,
            label: null,
          },
          {
            caverId: CARMEN,
            teamId: null,
            lastKind: null,
            lastRecordedAt: null,
            positionRecordedAt: null,
            stationName: null,
            depthM: null,
            in: false,
            out: false,
            label: null,
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
            positionRecordedAt: null,
            stationName: null,
            depthM: null,
            in: true,
            out: false,
            label: null,
          },
          {
            caverId: BOGDAN,
            teamId: null,
            // A station report always carries a station, so an empty one here can only be a
            // withholding — and that one is still said outright.
            lastKind: 'atStation',
            lastRecordedAt: '2026-09-12T07:00:00Z',
            positionRecordedAt: null,
            stationName: null,
            depthM: null,
            in: true,
            out: false,
            label: null,
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

      // Taken as the row rather than by searching for the name: the name is on the row twice on
      // purpose — once as who they are and once as what the published page will call them — and
      // that is the contrast the public-name field exists to draw.
      const row = table.querySelector('tbody tr') as HTMLElement;
      expect(within(row).getAllByText('Ana Popescu').length).toBeGreaterThan(0);
      for (const label of ['On the public page', 'Team', 'Last report', 'Last heard', 'Where']) {
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
              positionRecordedAt: null,
              stationName: null,
              depthM: null,
              in: true,
              out: false,
              label: null,
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

  /**
   * <b>One failed poll used to destroy the whole watch.</b> The read refreshes itself every thirty
   * seconds while a party is underground, and TanStack Query reports a failed *refresh* by setting
   * the error while still holding the answer it had. Taking that as "there is nothing here" tore
   * down the configuration, the share panel, the table, the log, the report being typed off a phone
   * call, and the survey model — whose viewer is keyed on the file it was handed, so the recovery
   * was to download and parse the entire survey again. The tab already handles its own event query
   * this way; this is the same idiom on the read the whole surface stands on.
   */
  describe('a poll that fails', () => {
    const failedRefresh = () =>
      trackingQuery.mockReturnValue({
        data: state(),
        isPending: false,
        isFetching: false,
        error: new Error('Failed to fetch'),
        refetch: vi.fn(),
      });

    it('keeps the whole watch on screen and says only that it has stopped refreshing', () => {
      failedRefresh();
      show();

      // Everything a failed poll used to take away, asserted one by one rather than as a spot
      // check: each of these is a separate thing somebody loses on a surface being read during a
      // callout.
      expect(screen.getByTestId('trip-tracking-state')).toBeTruthy();
      expect(screen.getByTestId('trip-tracking-publish')).toBeTruthy();
      expect(screen.getByTestId('trip-tracking-participants')).toHaveTextContent('Ana Popescu');
      expect(screen.getByTestId('trip-tracking-events')).toBeTruthy();
      expect(screen.getByTestId('trip-tracking-record')).toBeTruthy();

      expect(screen.getByTestId('trip-tracking-stale')).toHaveTextContent(
        'This watch has stopped refreshing',
      );
      // And the refusal is not dressed up as news about the trip: the hard error belongs to the
      // case below, where there is genuinely nothing to show.
      expect(screen.queryByText("This trip's tracking could not be read.")).toBeNull();
    });

    /**
     * The half of it that is not about pixels. A coordinator taking word off a phone call has
     * ticked who it is about and typed half a note; the poll fails behind them; and before this
     * split, both were gone — along with the survey model, whose viewer is keyed on the file it
     * was handed, so the recovery was to fetch and parse the whole survey again.
     */
    it('keeps a half-typed report and the ticks that say who it is about', () => {
      const view = show();
      fireEvent.click(rowChecks()[0]);
      fireEvent.change(screen.getByTestId('trip-tracking-note'), {
        target: { value: 'out at 14:00, all well' },
      });
      expect(screen.getByTestId('trip-tracking-record')).toHaveTextContent('Record for 1 selected');

      failedRefresh();
      view.rerender(
        <App>
          <TripTrackingTab trip={trip()} canEdit />
        </App>,
      );

      expect(screen.getByTestId('trip-tracking-stale')).toBeTruthy();
      expect(screen.getByTestId('trip-tracking-record')).toHaveTextContent('Record for 1 selected');
      expect(screen.getByTestId('trip-tracking-note')).toHaveValue('out at 14:00, all well');
    });

    it('offers a retry that re-reads rather than leaving the half minute to run out', () => {
      const refetch = vi.fn();
      trackingQuery.mockReturnValue({
        data: state(),
        isPending: false,
        isFetching: false,
        error: new Error('Failed to fetch'),
        refetch,
      });
      show();

      fireEvent.click(screen.getByTestId('trip-tracking-retry'));

      expect(refetch).toHaveBeenCalledTimes(1);
    });

    /**
     * The twin, and the reason the split is a split rather than a deletion: a read that has
     * genuinely never answered has no watch to keep on screen, and saying "this has stopped
     * refreshing" over an empty tab would name a state the reader cannot act on.
     */
    it('still says the watch could not be read when there is no watch at all', () => {
      trackingQuery.mockReturnValue({
        data: undefined,
        isPending: false,
        isFetching: false,
        error: new Error('403'),
        refetch: vi.fn(),
      });
      show();

      expect(screen.getByText("This trip's tracking could not be read.")).toBeTruthy();
      expect(screen.queryByTestId('trip-tracking-stale')).toBeNull();
      expect(screen.queryByTestId('trip-tracking-participants')).toBeNull();
      // And no reason is invented for it: nothing here was told why, and a guess offered as an
      // explanation is worst on the surface least able to afford one.
      expect(screen.queryByText(/no longer yours to read/)).toBeNull();
    });

    it('says nothing about refreshing while the reads are going through', () => {
      show();

      expect(screen.queryByTestId('trip-tracking-stale')).toBeNull();
      expect(screen.queryByTestId('trip-tracking-retry')).toBeNull();
    });

    /**
     * <b>A poll that was refused is not a poll that did not arrive, and keeping the whole watch on
     * screen must not turn the first into the second.</b> The retry policy stops asking at a 4xx,
     * so a trip that has been deleted, or one this account may no longer read, sets the error on
     * the very first failed poll with the last answer still in hand. Worded as a dropped
     * connection it would tell a coordinator watching a party underground that the figures in
     * front of them are about to become current, under a Retry that is refused on every press —
     * and they would keep pressing it, during a callout, instead of doing the one thing that would
     * help. What the server settled is said as settled.
     */
    const refusedBy = (error: unknown) =>
      trackingQuery.mockReturnValue({
        data: state(),
        isPending: false,
        isFetching: false,
        error,
        refetch: vi.fn(),
      });

    it('says a refusal the server settled as an answer rather than as a dropped connection', () => {
      refusedBy(new ApiError(404, 'trip_log.not_found'));
      show();

      expect(screen.getByTestId('trip-tracking-refused')).toHaveTextContent(
        'This trip is no longer there, or is no longer yours to read.',
      );
      expect(screen.queryByTestId('trip-tracking-stale')).toBeNull();
      // The control that would be refused every time it was pressed is not offered at all.
      expect(screen.queryByTestId('trip-tracking-retry')).toBeNull();
      // And it is still not a demolition: everything the split was built to keep is kept.
      expect(screen.getByTestId('trip-tracking-participants')).toHaveTextContent('Ana Popescu');
      expect(screen.getByTestId('trip-tracking-record')).toBeTruthy();
    });

    // The one settled refusal that arrives with no code of its own, and the only one the reader
    // can put right in the next ten seconds — so it is named rather than lumped in with the rest.
    it('names a sign-in that has lapsed rather than blaming the connection for it', () => {
      refusedBy(new ApiError(401));
      show();

      expect(screen.getByTestId('trip-tracking-refused')).toHaveTextContent(
        'You are no longer signed in',
      );
      expect(screen.queryByTestId('trip-tracking-retry')).toBeNull();
    });

    /**
     * The twin, and the reason this is a fork rather than a replacement: a connection that dropped
     * really does come back by itself, and saying "open the trip again" at every poll a phone
     * misses in a valley would be the same error pointing the other way.
     */
    it('still calls a poll that did not get through a poll that did not get through', () => {
      failedRefresh();
      show();

      expect(screen.getByTestId('trip-tracking-stale')).toBeTruthy();
      expect(screen.getByTestId('trip-tracking-retry')).toBeTruthy();
      expect(screen.queryByTestId('trip-tracking-refused')).toBeNull();
    });

    // A rate limit is the one client refusal that clears by itself, and the read goes on retrying
    // through it. A watch that declared itself over on a 429 would end during the one minute a
    // coordinator is reloading it hardest.
    it('waits a rate limit out instead of declaring the watch over', () => {
      refusedBy(new ApiError(429, undefined, 'slow down'));
      show();

      expect(screen.getByTestId('trip-tracking-stale')).toBeTruthy();
      expect(screen.getByTestId('trip-tracking-retry')).toBeTruthy();
      expect(screen.queryByTestId('trip-tracking-refused')).toBeNull();
    });

    // The first-read half of the same distinction: there is no watch to keep, but there is still a
    // reason, and "could not be read" on its own leaves somebody reloading a trip that is gone.
    it('says why when the very first read is the one that is refused', () => {
      trackingQuery.mockReturnValue({
        data: undefined,
        isPending: false,
        isFetching: false,
        error: new ApiError(404, 'trip_log.not_found'),
        refetch: vi.fn(),
      });
      show();

      expect(screen.getByText("This trip's tracking could not be read.")).toBeTruthy();
      expect(screen.getByText('This trip is no longer there, or is no longer yours to read.')).toBeTruthy();
    });
  });

  /**
   * <b>The coordinator's own screen had the worst time sense in the product.</b> A follower without
   * an account was told "3 hours ago"; the person who has to notice that nobody has heard from a
   * team since noon was given clock times and left to subtract them, during a callout. And nothing
   * counted the people nobody had heard from at all.
   */
  describe('how long it has been', () => {
    /** Ten o'clock, so the fixture's reports are three hours and a half hour old. */
    const AT_TEN = Date.parse('2026-09-12T10:00:00Z');

    function party() {
      return state({
        participants: [
          {
            caverId: ANA,
            teamId: null,
            lastKind: 'atStation',
            lastRecordedAt: '2026-09-12T07:00:00Z',
            positionRecordedAt: '2026-09-12T07:00:00Z',
            stationName: 'P12',
            depthM: null,
            in: true,
            out: false,
            label: null,
          },
          {
            caverId: BOGDAN,
            teamId: null,
            lastKind: 'exited',
            lastRecordedAt: '2026-09-12T09:30:00Z',
            positionRecordedAt: null,
            stationName: null,
            depthM: null,
            in: false,
            out: true,
            label: null,
          },
          {
            // Nobody has said a single word about her. This is the row the whole change is for.
            caverId: CARMEN,
            teamId: null,
            lastKind: null,
            lastRecordedAt: null,
            positionRecordedAt: null,
            stationName: null,
            depthM: null,
            in: false,
            out: false,
            label: null,
          },
        ],
      });
    }

    /** jsdom's clock is the real one, so the moment every age is measured from is said out loud. */
    let clock: MockInstance<typeof Date.now>;

    beforeEach(() => {
      clock = vi.spyOn(Date, 'now').mockReturnValue(AT_TEN);
      trackingQuery.mockReturnValue({
        data: party(),
        isPending: false,
        isFetching: false,
        error: null,
        refetch: vi.fn(),
      });
    });

    // Restored one by one rather than through a blanket restore, which would also reset the module
    // mocks this whole suite is built on.
    afterEach(() => clock.mockRestore());

    it('says how long ago somebody was heard from rather than at what time', () => {
      show(false);
      const table = screen.getByTestId('trip-tracking-participants');

      expect(within(table).getByText('3 hours ago')).toBeTruthy();
      expect(within(table).getByText('30 minutes ago')).toBeTruthy();
      // The exact moment is not lost, only moved off the first reading of the row — the same
      // arrangement the followed page uses.
      expect(within(table).getByText('3 hours ago')).toHaveAttribute(
        'title',
        expect.stringContaining('2026'),
      );
    });

    /**
     * Silence is not an age. Every set of words for "how long ago was the report that never
     * arrived" — "just now", "0 minutes ago", an epoch date — is a claim this watch was never
     * given, and on this table it would be read as word having come in.
     */
    it('draws a person nobody has reported as silent rather than as freshly heard from', () => {
      show(false);
      const table = screen.getByTestId('trip-tracking-participants');

      expect(within(table).getByTestId('trip-tracking-never-heard')).toHaveTextContent(
        'No word yet',
      );
      // The twin: the two people who *have* been heard from are still given their ages, so this
      // cannot pass by drawing everybody as silent.
      expect(within(table).getAllByTestId('trip-tracking-never-heard')).toHaveLength(1);
      expect(within(table).queryByText('0 minutes ago')).toBeNull();
    });

    /**
     * The count that was missing, and the three states it is made of. A watch that said
     * "1 underground, 1 out" over a party of three would be leaving out the one person the
     * coordinator most needs to think about — and doing it silently, because the figures look
     * complete.
     */
    it('counts the people nobody has heard from beside the other two', () => {
      show(false);

      expect(screen.getByTestId('trip-tracking-count-underground')).toHaveTextContent('1');
      expect(screen.getByTestId('trip-tracking-count-out')).toHaveTextContent('1');
      expect(screen.getByTestId('trip-tracking-count-unheard')).toHaveTextContent('1');
      expect(screen.getByTestId('trip-tracking-counts')).toHaveTextContent('Not heard from');
    });

    it('keeps the three states apart on the rows as well as in the counts', () => {
      show(false);
      const table = screen.getByTestId('trip-tracking-participants');

      expect(within(table).getAllByTestId('trip-tracking-standing-underground')).toHaveLength(1);
      expect(within(table).getAllByTestId('trip-tracking-standing-out')).toHaveLength(1);
      expect(within(table).getAllByTestId('trip-tracking-standing-unheard')).toHaveLength(1);
      expect(within(table).getByTestId('trip-tracking-standing-unheard')).toHaveTextContent(
        'Not heard from',
      );
    });

    it('says it on a phone too, where the fields are stacked down the row', () => {
      narrow = true;
      show(false);
      const table = screen.getByTestId('trip-tracking-participants');

      expect(within(table).getByText('3 hours ago')).toBeTruthy();
      expect(within(table).getByTestId('trip-tracking-never-heard')).toBeTruthy();
      expect(screen.getByTestId('trip-tracking-count-unheard')).toHaveTextContent('1');
    });
  });

  /**
   * <b>How old the place is, which is a different question from how long ago somebody spoke.</b>
   *
   * The defect, in the shape it happens: a team is reported at P12 at seven, and at five to ten the
   * radio carries "all fine, coming out in an hour". The second report says nothing about where
   * anybody is — but it was the only moment this table had, so the station from seven o'clock was
   * drawn against a timestamp five minutes old, and a coordinator deciding whether that team is
   * overdue read a three-hour-old position as fresh.
   */
  describe('how old the place is', () => {
    /** Ten o'clock again, so the station below is three hours old and the note five minutes. */
    const AT_TEN = Date.parse('2026-09-12T10:00:00Z');

    /**
     * Ana, placed at seven and heard from at five to ten; Bogdan, whose position was kept from
     * this reader; Carmen, whom nothing has placed at all.
     */
    function party(positionsWithheld = false) {
      return state({
        positionsWithheld,
        participants: [
          {
            caverId: ANA,
            teamId: null,
            // The last word is a note — it carries no place, which is exactly why the two moments
            // on this row are three hours apart.
            lastKind: 'note',
            lastRecordedAt: '2026-09-12T09:55:00Z',
            positionRecordedAt: '2026-09-12T07:00:00Z',
            stationName: 'P12',
            depthM: null,
            in: true,
            out: false,
            label: null,
          },
          {
            caverId: BOGDAN,
            teamId: null,
            // A station report with no station on it: a withholding, and the server sends no
            // moment for it either — the position's age is as much of it as the place.
            lastKind: 'atStation',
            lastRecordedAt: '2026-09-12T09:40:00Z',
            positionRecordedAt: null,
            stationName: null,
            depthM: null,
            in: true,
            out: false,
            label: null,
          },
          {
            caverId: CARMEN,
            teamId: null,
            lastKind: 'entered',
            lastRecordedAt: '2026-09-12T06:30:00Z',
            positionRecordedAt: null,
            stationName: null,
            depthM: null,
            in: true,
            out: false,
            label: null,
          },
        ],
      });
    }

    let clock: MockInstance<typeof Date.now>;

    beforeEach(() => {
      clock = vi.spyOn(Date, 'now').mockReturnValue(AT_TEN);
      trackingQuery.mockReturnValue({
        data: party(),
        isPending: false,
        isFetching: false,
        error: null,
        refetch: vi.fn(),
      });
    });

    afterEach(() => clock.mockRestore());

    it('dates the station from the report that placed somebody, not from a later note', () => {
      show(false);

      expect(screen.getByTestId(`trip-tracking-position-age-${ANA}`)).toHaveTextContent(
        'Reported 3 hours ago',
      );
      // The assertion the whole change is: the note that arrived five minutes ago did not re-date
      // the station. Said as a refusal *and* as the number that replaced it, so it cannot pass by
      // the age having gone missing altogether.
      expect(screen.getByTestId(`trip-tracking-position-age-${ANA}`)).not.toHaveTextContent(
        '5 minutes ago',
      );
    });

    /**
     * <b>Two ages, two facts, two labels — never one.</b> Somebody heard from five minutes ago and
     * last placed three hours ago is the row a coordinator has to read as two things: word is
     * getting through, and nobody has said where they are since seven. Collapsing those into one
     * figure would have to lie about whichever of them it dropped.
     */
    it('keeps the last word and the position as two ages that can disagree', () => {
      show(false);
      const table = screen.getByTestId('trip-tracking-participants');

      expect(within(table).getByText('5 minutes ago')).toBeTruthy();
      expect(screen.getByTestId(`trip-tracking-position-age-${ANA}`)).toHaveTextContent(
        '3 hours ago',
      );
      // Under the headings that name them, so neither age can be read against the other's
      // question. Asked for as a set: a table told to keep its own overflow clones every column
      // title into a hidden measure row, so one heading is two nodes.
      expect(within(table).getAllByText('Last heard').length).toBeGreaterThan(0);
      expect(within(table).getAllByText('Where').length).toBeGreaterThan(0);
      // And the exact moment of the position is kept for whoever wants the clock rather than the
      // gap — the same arrangement the last word has.
      expect(screen.getByTestId(`trip-tracking-position-age-${ANA}`)).toHaveAttribute(
        'title',
        expect.stringContaining('2026'),
      );
    });

    it('says both of them on a phone, where the fields are stacked down the row', () => {
      narrow = true;
      show(false);
      const table = screen.getByTestId('trip-tracking-participants');

      expect(screen.getByTestId(`trip-tracking-position-age-${ANA}`)).toHaveTextContent(
        'Reported 3 hours ago',
      );
      expect(within(table).getByText('5 minutes ago')).toBeTruthy();
    });

    /**
     * <b>Silence keeps no age, and a withheld position least of all.</b> Nothing placed Carmen, and
     * Bogdan's position is one this reader may not be told — the read carries no moment for either,
     * deliberately, because which of the two it is is itself something that cannot be disclosed.
     * The one repair that must never be made is filling that gap from the last word: both of them
     * were heard from within the hour, and an age drawn from that would say a position was reported
     * when none was — or would date a position somebody may not learn.
     */
    it('gives a position nobody drew no age, withheld or never reported', () => {
      trackingQuery.mockReturnValue({
        data: party(true),
        isPending: false,
        isFetching: false,
        error: null,
        refetch: vi.fn(),
      });
      show(false);

      expect(screen.queryByTestId(`trip-tracking-position-age-${BOGDAN}`)).toBeNull();
      expect(screen.queryByTestId(`trip-tracking-position-age-${CARMEN}`)).toBeNull();
      // The positive twins. The withholding is still said in words where the place would be, so
      // the absence above is an age that is missing and not a row that has gone quiet; and the one
      // person who was placed still carries hers, so this cannot pass by never drawing an age.
      expect(screen.getByTestId('trip-tracking-position-withheld')).toBeTruthy();
      expect(screen.getByTestId(`trip-tracking-position-age-${ANA}`)).toHaveTextContent(
        '3 hours ago',
      );
    });

    /**
     * The same refusal where the moment does arrive beside an absence.
     *
     * A withheld position and one nobody reported are indistinguishable by design, so the read
     * sends no moment for either — but the age is drawn beside the place rather than off the row,
     * and this is what pins that. A future read that sent a moment for a position it would not
     * disclose must not be able to date the withholding with it: the reader would learn that a
     * position exists and when it was reported, which is half of what was being kept from them.
     */
    it('draws no age beside a withheld position even if the read carries one', () => {
      const withheldButDated = party(true);
      trackingQuery.mockReturnValue({
        data: {
          ...withheldButDated,
          participants: withheldButDated.participants.map((person) =>
            person.caverId === BOGDAN
              ? { ...person, positionRecordedAt: '2026-09-12T07:00:00Z' }
              : person,
          ),
        },
        isPending: false,
        isFetching: false,
        error: null,
        refetch: vi.fn(),
      });
      show(false);

      expect(screen.queryByTestId(`trip-tracking-position-age-${BOGDAN}`)).toBeNull();
      expect(screen.getByTestId('trip-tracking-position-withheld')).toBeTruthy();
      // The twin again: an age is still drawn where a place was.
      expect(screen.getByTestId(`trip-tracking-position-age-${ANA}`)).toBeTruthy();
    });
  });

  /**
   * <b>The one thing that keeps a named person off a public page, and it had no user interface at
   * all.</b> The route existed, the generated client carried it, and nothing in the application
   * called it — while the installation's own setting publishes real names, so every member of every
   * followed trip is named and this is their only opt-out.
   */
  describe('what the published page calls each person', () => {
    it('says what the page will call somebody, per person, before a link is minted', () => {
      trackingQuery.mockReturnValue({
        data: state({
          participants: [
            {
              caverId: ANA,
              teamId: null,
              lastKind: 'entered',
              lastRecordedAt: '2026-09-12T06:30:00Z',
              positionRecordedAt: null,
              stationName: null,
              depthM: null,
              in: true,
              out: false,
              // Asked to be kept off the page, and this is the record of it.
              label: 'A club member',
            },
            {
              caverId: BOGDAN,
              teamId: null,
              lastKind: 'entered',
              lastRecordedAt: '2026-09-12T06:30:00Z',
              positionRecordedAt: null,
              stationName: null,
              depthM: null,
              in: true,
              out: false,
              label: null,
            },
          ],
        }),
        isPending: false,
        isFetching: false,
        error: null,
        refetch: vi.fn(),
      });
      show(false);

      expect(screen.getByTestId(`trip-tracking-public-name-${ANA}`)).toHaveTextContent(
        'A club member',
      );
      // The twin, and the contrast the column exists to draw: his name goes out as it stands.
      expect(screen.getByTestId(`trip-tracking-public-name-${BOGDAN}`)).toHaveTextContent(
        'Bogdan Ilie',
      );
    });

    it('says a party the page will number rather than name', () => {
      trackingQuery.mockReturnValue({
        data: state({ publishesRealNames: false }),
        isPending: false,
        isFetching: false,
        error: null,
        refetch: vi.fn(),
      });
      show(false);

      expect(screen.getByTestId(`trip-tracking-public-name-${ANA}`)).toHaveTextContent(
        'A place in the party',
      );
      expect(screen.getByTestId('trip-tracking-names-setting')).toHaveTextContent(
        'This installation publishes places in the party',
      );
    });

    /**
     * What the installation does is stated on the tab itself, not only on the panel that mints the
     * link: a reader deciding whether somebody needs a caption is reading the table, and a rule
     * they have to go and find somewhere else is a rule they apply after the link has gone out.
     */
    it('says what this installation publishes, and what a caption does to it', () => {
      show();

      expect(screen.getByTestId('trip-tracking-names-setting')).toHaveTextContent(
        'This installation publishes real names.',
      );
      expect(
        screen.getByText(/outranks the setting above in both directions/),
      ).toBeInTheDocument();
    });

    /**
     * <b>The one part of this column that is a good guess rather than a fact, said on the page.</b>
     * The names in the table are the ones this application uses — an account's own display name
     * where one is set — while the published page prints the name the club's roster holds. The two
     * start out identical and part company the moment a member chooses a display name, and this is
     * the surface whose entire job is to answer what a follow link will print *before* the link is
     * minted. Left unsaid, the cell and the dialog would both state a string as fact and the page
     * would then print another.
     */
    it('says the name shown here is not exactly the name the published page prints', () => {
      show(false);

      expect(screen.getByTestId('trip-tracking-roster-name')).toHaveTextContent(
        "prints the name on the club's roster",
      );
    });

    /**
     * The twin, and not a symmetry for its own sake: on an installation that numbers its party no
     * name of any kind goes out, so a caveat about which name would be a worry about nothing —
     * offered on every trip, beside the sentence that has just said no names are published.
     */
    it('says nothing about roster names where no name is published at all', () => {
      trackingQuery.mockReturnValue({
        data: state({ publishesRealNames: false }),
        isPending: false,
        isFetching: false,
        error: null,
        refetch: vi.fn(),
      });
      show(false);

      expect(screen.queryByTestId('trip-tracking-roster-name')).toBeNull();
      expect(screen.getByTestId('trip-tracking-names-setting')).toHaveTextContent(
        'This installation publishes places in the party',
      );
    });

    it('reaches the write that nothing in this application reached before', async () => {
      show();

      fireEvent.click(screen.getByTestId(`trip-tracking-public-name-edit-${ANA}`));
      fireEvent.change(await screen.findByTestId('trip-tracking-public-name-input'), {
        target: { value: 'A club member' },
      });
      fireEvent.click(screen.getByTestId('trip-tracking-public-name-save'));

      await waitFor(() => expect(setLabel).toHaveBeenCalledTimes(1));
      expect(setLabel.mock.calls[0][0]).toEqual({
        tripLogId: 'trip-1',
        caverId: ANA,
        label: 'A club member',
      });
    });

    it('takes a caption back off again', async () => {
      trackingQuery.mockReturnValue({
        data: state({
          participants: [
            {
              caverId: ANA,
              teamId: null,
              lastKind: 'entered',
              lastRecordedAt: '2026-09-12T06:30:00Z',
              positionRecordedAt: null,
              stationName: null,
              depthM: null,
              in: true,
              out: false,
              label: 'A club member',
            },
          ],
        }),
        isPending: false,
        isFetching: false,
        error: null,
        refetch: vi.fn(),
      });
      show();

      fireEvent.click(screen.getByTestId(`trip-tracking-public-name-edit-${ANA}`));
      fireEvent.click(await screen.findByTestId('trip-tracking-public-name-clear'));

      await waitFor(() => expect(setLabel).toHaveBeenCalledTimes(1));
      expect(setLabel.mock.calls[0][0]).toMatchObject({ caverId: ANA, label: null });
    });

    /**
     * A reader who may not write the trip still has to be able to see what the page says about
     * people — that is the reading half — but is offered nothing to change it with.
     */
    it('shows a reader who may not write the trip the answer and no control', () => {
      show(false);

      expect(screen.getByTestId(`trip-tracking-public-name-${ANA}`)).toBeTruthy();
      expect(screen.queryByTestId(`trip-tracking-public-name-edit-${ANA}`)).toBeNull();
    });

    // The control is pressed by the same finger as everything else on this surface.
    it('sizes the control on the pointer rather than on the width', () => {
      const { unmount } = show();
      expect(screen.getByTestId(`trip-tracking-public-name-edit-${ANA}`)).toHaveClass('ant-btn-sm');
      unmount();

      coarse = true;
      show();
      expect(screen.getByTestId(`trip-tracking-public-name-edit-${ANA}`)).toHaveClass('ant-btn-lg');
    });
  });
});
