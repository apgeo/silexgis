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

/** The row checkboxes, in the order the table draws them — the header's own is not one of them. */
function rowChecks() {
  return within(screen.getByTestId('trip-tracking-participants'))
    .getAllByRole('checkbox')
    .slice(1);
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
});
