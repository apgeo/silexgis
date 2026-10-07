// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type {
  PublicLiveTrip,
  PublicLiveTripList,
  PublicPastTrack,
  PublicPastTripList,
  PublicTripEnvelope,
} from '../../api/hooks.ts';

/**
 * Another party of the cave, watched from a published link.
 *
 * <b>Driven through the page, because the hazard is on the page.</b> Which party a view resolves
 * to is proved next door without a renderer. What only a page can get wrong is everything around
 * that answer: a title, a count, a "this trip is over" or a "stopped refreshing" still speaking
 * of the link's own trip above somebody else's party; a list no longer read once its section is
 * shut, so the party on screen quietly stops moving; a party that leaves the list and is swapped
 * for the link's own without a word.
 */

const OWN = '0195f4a2-6c3e-7b10-9f21-ab44de77c001';
const OTHER = '0195f4a2-6c3e-7b10-9f21-ab44de77c002';
const TRIP_2019 = 'aaaaaaaa-0000-0000-0000-000000000001';
const TEAM = '11111111-1111-1111-1111-111111111111';

type ListAnswer = {
  data?: PublicLiveTripList;
  isPending: boolean;
  isError: boolean;
  error?: unknown;
  /** When the last list that was read arrived, as the query reports it. */
  dataUpdatedAt?: number;
};

let live: { data?: PublicTripEnvelope; isPending: boolean; error: unknown; dataUpdatedAt?: number };
let liveList: ListAnswer;
let archive: { data?: PublicPastTripList; isPending: boolean; isError: boolean };
let trackAnswer: { data?: PublicPastTrack; isPending: boolean; isError: boolean };
/** Whether the page is asking for the list of parties at this moment, and whether it ever did. */
let liveAsked = false;
let liveReads = 0;

vi.mock('../../api/hooks.ts', () => ({
  usePublicTrip: () => live,
  useResLinksForTarget: () => ({ data: undefined, isPending: false, error: null }),
  useSurveyModel: () => ({ data: undefined, isPending: false, error: null }),
  usePublicPastTrips: (_token: string | undefined, enabled: boolean) =>
    enabled ? archive : { data: undefined, isPending: false, isError: false },
  usePublicLiveTrips: (_token: string | undefined, enabled: boolean) => {
    liveAsked = enabled;
    if (enabled) {
      liveReads++;
    }
    return enabled ? liveList : { data: undefined, isPending: false, isError: false };
  },
  usePublicPastTrack: (_token: string | undefined, tripLogId: string | undefined) =>
    tripLogId === undefined ? { data: undefined, isPending: false, isError: false } : trackAnswer,
}));

const address = new URLSearchParams();
const setAddress = vi.fn();
vi.mock('react-router-dom', () => ({
  useParams: () => ({ token: 'follow-token' }),
  useSearchParams: () => [address, setAddress],
}));

/** The viewer, faked at its contract: what it is handed is the whole of what this page owes it. */
let given: Record<string, unknown> | undefined;
/** How many viewers were built — a second one would be the survey loaded again. */
let viewersBuilt = 0;
vi.mock('../../components/caveview/CaveViewPanel.tsx', async () => {
  const { useEffect } = await import('react');
  function FakeViewer(props: Record<string, unknown>) {
    given = props;
    useEffect(() => {
      viewersBuilt++;
    }, []);
    return <div data-testid="viewer" />;
  }
  return { default: FakeViewer };
});
vi.mock('../../hooks/useIsMobile.ts', () => ({ useIsMobile: () => false }));
vi.mock('./PublicTripSheetPane.tsx', () => ({
  default: () => <div data-testid="sheet-pane" />,
}));

const { default: PublicTripPage } = await import('./PublicTripPage.tsx');

const MODEL = {
  modelUrl: 'https://example.invalid/files/own.3d?sig=1',
  format: 'survex3d',
  proj4: null,
  sourceEpsg: null,
  pictures: [],
  rasterMaps: [],
} as unknown as PublicTripEnvelope['model'];

function person(
  ordinal: number,
  label: string,
  stationName: string | null,
  flags: { in: boolean; out: boolean } = { in: true, out: false },
  positionOnOtherModel = false,
) {
  return {
    ordinal,
    label,
    teamId: null,
    stationName,
    depthM: null,
    lastRecordedAt: '2026-09-14T09:00:00Z',
    positionRecordedAt: stationName === null ? null : '2026-09-14T09:00:00Z',
    positionOnOtherModel,
    ...flags,
  };
}

function envelope(overrides: Partial<PublicTripEnvelope> = {}): PublicTripEnvelope {
  return {
    tripLogId: OWN,
    expedition: null,
    title: 'E1, the deep end',
    tripDate: '2026-09-14',
    tripDateEnd: null,
    state: 'armed',
    armedAt: '2026-09-14T06:00:00Z',
    closedAt: null,
    positionsWithheld: false,
    model: MODEL,
    teams: [],
    participants: [person(1, 'Ana', 'own.1')],
    ...overrides,
  };
}

/** A row of the list: the envelope without a survey, exactly as the server sends one. */
function row(overrides: Partial<PublicLiveTrip> = {}): PublicLiveTrip {
  return {
    tripLogId: OTHER,
    expedition: null,
    title: 'E2, the survey',
    tripDate: '2026-09-14',
    tripDateEnd: null,
    state: 'armed',
    armedAt: '2026-09-14T07:00:00Z',
    closedAt: null,
    positionsWithheld: false,
    teams: [],
    participants: [
      person(1, 'Mircea', 'other.4'),
      person(2, 'Ileana', 'other.9', { in: true, out: true }),
      person(3, 'Radu', null, { in: false, out: false }),
    ],
    ...overrides,
  };
}

function ownRow(): PublicLiveTrip {
  return row({
    tripLogId: OWN,
    title: 'E1, the deep end',
    participants: [person(1, 'Ana', 'own.1')],
  });
}

const answered = (...trips: PublicLiveTrip[]): ListAnswer => ({
  data: { trips, more: false },
  isPending: false,
  isError: false,
});

const toggleParties = () =>
  fireEvent.click(screen.getByText('Also in this cave now', { selector: 'span' }));
const openArchive = () =>
  fireEvent.click(screen.getByText('Past trips in this cave', { selector: 'span' }));
const watchOther = () => {
  toggleParties();
  fireEvent.click(screen.getByTestId(`public-live-watch-${OTHER}`));
};
const drawn = () =>
  ((given?.trackedCavers ?? []) as { name: string }[]).map((caver) => caver.name);
const count = (standing: string) => screen.getByTestId(`public-trip-count-${standing}`).textContent;

beforeEach(() => {
  live = { data: envelope(), isPending: false, error: null };
  liveList = answered(ownRow(), row());
  archive = {
    data: {
      trips: [
        {
          tripLogId: TRIP_2019,
          expedition: null,
          title: 'The 2019 push',
          tripDate: '2019-07-06',
          tripDateEnd: null,
          closedAt: '2019-07-06T18:00:00Z',
          participantCount: 1,
          playable: true,
        },
      ],
      more: false,
    },
    isPending: false,
    isError: false,
  };
  trackAnswer = {
    data: {
      tripLogId: TRIP_2019,
      expedition: null,
      title: 'The 2019 push',
      tripDate: '2019-07-06',
      tripDateEnd: null,
      armedAt: '2019-07-06T08:00:00Z',
      closedAt: '2019-07-06T18:00:00Z',
      positionsWithheld: false,
      trackTruncated: false,
      model: MODEL,
      teams: [{ id: TEAM, title: 'Advance' }],
      participants: [
        {
          ordinal: 1,
          label: 'Vlad',
          track: [
            {
              recordedAt: '2019-07-06T09:00:00Z',
              teamId: TEAM,
              stationName: 'p.g.7',
              depthM: null,
              positionOnOtherModel: false,
              in: true,
              out: false,
            },
          ],
        },
      ],
    },
    isPending: false,
    isError: false,
  };
  liveAsked = false;
  liveReads = 0;
  given = undefined;
  viewersBuilt = 0;
  setAddress.mockClear();
  document.title = '';
});

afterEach(cleanup);

describe('watching another party of the cave from a published link', () => {
  it('asks for nobody else until a reader opens the section, and offers this link’s own trip no Watch', () => {
    render(<PublicTripPage />);

    expect(liveReads).toBe(0);
    expect(screen.queryByTestId('public-watch-banner')).toBeNull();

    toggleParties();

    expect(liveReads).toBeGreaterThan(0);
    expect(screen.getByTestId(`public-live-watch-${OTHER}`)).toHaveTextContent('Watch');
    expect(screen.queryByTestId(`public-live-watch-${OWN}`)).toBeNull();
    // Opening the section changes nothing above it: the drawing is still this link's own party.
    expect(drawn()).toEqual(['Ana']);
  });

  it('draws the party whose row was pressed on this link’s own survey, under its own name', () => {
    render(<PublicTripPage />);
    const survey = given?.fileUrl;
    expect(screen.getByTestId('public-trip-title')).toHaveTextContent('E1, the deep end');
    expect(count('underground')).toBe('1');

    watchOther();

    // Title, counts, list and markers all change owner together — never one party under another's.
    expect(screen.getByTestId('public-trip-title')).toHaveTextContent('E2, the survey');
    expect(drawn()).toEqual(['Mircea', 'Ileana', 'Radu']);
    expect([count('underground'), count('out'), count('unheard')]).toEqual(['1', '1', '1']);
    expect(screen.getByTestId('public-trip-party')).toHaveTextContent('Mircea');
    expect(screen.getByTestId('public-trip-party')).not.toHaveTextContent('Ana');
    expect(screen.getByTestId('public-watch-banner')).toHaveTextContent(
      'You are watching another party of this cave',
    );
    expect(screen.getByTestId('public-watch-banner-what')).toHaveTextContent('E2, the survey');
    expect(screen.getByTestId(`public-live-watching-${OTHER}`)).toBeTruthy();

    // The same drawing, not another and not the same one loaded again.
    expect(given?.fileUrl).toBe(survey);
    expect(viewersBuilt).toBe(1);
    // The tab is the link, and so is the address: neither names the party being watched.
    expect(document.title).toBe('E1, the deep end');
    expect(setAddress).not.toHaveBeenCalled();
  });

  it('says a place measured on another survey is reported, and draws it nowhere', () => {
    liveList = answered(
      ownRow(),
      row({ participants: [person(1, 'Mircea', null, { in: true, out: false }, true)] }),
    );
    render(<PublicTripPage />);
    watchOther();

    expect(screen.getByTestId('public-trip-position-other-model')).toBeTruthy();
    expect(screen.getByTestId('public-trip-other-model')).toBeTruthy();
    const placed = (given?.trackedCavers ?? []) as { position: { kind: string } }[];
    expect(placed).toHaveLength(1);
    expect(placed.map((caver) => caver.position.kind)).not.toContain('station');
  });

  it('goes back to this link’s trip from the banner', () => {
    render(<PublicTripPage />);
    watchOther();

    fireEvent.click(screen.getByTestId('public-watch-back'));

    expect(screen.queryByTestId('public-watch-banner')).toBeNull();
    expect(screen.getByTestId('public-trip-title')).toHaveTextContent('E1, the deep end');
    expect(drawn()).toEqual(['Ana']);
    // A way back the reader took themselves is not a watch that ended under them.
    expect(screen.queryByTestId('public-watch-ended')).toBeNull();
  });

  it('goes back from this link’s own row as well, which offers it only while another party is on screen', () => {
    render(<PublicTripPage />);
    toggleParties();
    expect(screen.queryByTestId('public-live-back-own')).toBeNull();

    fireEvent.click(screen.getByTestId(`public-live-watch-${OTHER}`));
    fireEvent.click(screen.getByTestId('public-live-back-own'));

    expect(screen.queryByTestId('public-watch-banner')).toBeNull();
    expect(drawn()).toEqual(['Ana']);
    expect(screen.queryByTestId('public-live-back-own')).toBeNull();
  });

  it('goes on reading the list with its section shut while a party is watched, and stops once back', () => {
    render(<PublicTripPage />);
    watchOther();

    // Shut again by the reader: the party on screen still comes from this list.
    toggleParties();
    expect(liveAsked).toBe(true);
    expect(drawn()).toEqual(['Mircea', 'Ileana', 'Radu']);

    fireEvent.click(screen.getByTestId('public-watch-back'));
    expect(liveAsked).toBe(false);
  });

  it('follows the watched party as the list is read again', () => {
    const { rerender } = render(<PublicTripPage />);
    watchOther();
    expect(count('out')).toBe('1');

    liveList = answered(
      ownRow(),
      row({
        participants: [
          person(1, 'Mircea', 'other.4', { in: true, out: true }),
          person(2, 'Ileana', 'other.9', { in: true, out: true }),
          person(3, 'Radu', null, { in: false, out: false }),
        ],
      }),
    );
    rerender(<PublicTripPage />);

    expect(count('out')).toBe('2');
    expect(screen.getByTestId('public-watch-banner')).toBeTruthy();
  });

  it('ends the watch with a notice naming the party when it leaves the list, never by a silent swap', () => {
    const { rerender } = render(<PublicTripPage />);
    watchOther();

    liveList = answered(ownRow());
    rerender(<PublicTripPage />);

    expect(screen.queryByTestId('public-watch-banner')).toBeNull();
    expect(screen.getByTestId('public-watch-ended')).toHaveTextContent(
      'The party you were watching is no longer in the list',
    );
    expect(screen.getByTestId('public-watch-ended')).toHaveTextContent('E2, the survey');
    expect(screen.getByTestId('public-trip-title')).toHaveTextContent('E1, the deep end');
    expect(drawn()).toEqual(['Ana']);

    // Kept until the reader has seen it, and theirs to put away.
    fireEvent.click(screen.getByLabelText('Close this notice'));
    expect(screen.queryByTestId('public-watch-ended')).toBeNull();
  });

  it('does not take a read of the list that failed for the party having left, and says the page is stale', () => {
    const { rerender } = render(<PublicTripPage />);
    watchOther();
    expect(screen.queryByTestId('public-trip-stale')).toBeNull();

    // What a failed re-read looks like: the last list still in hand, and an error beside it.
    liveList = { ...liveList, isError: true, error: new ApiError(503) };
    rerender(<PublicTripPage />);

    expect(screen.getByTestId('public-watch-banner')).toBeTruthy();
    expect(screen.queryByTestId('public-watch-ended')).toBeNull();
    expect(drawn()).toEqual(['Mircea', 'Ileana', 'Radu']);
    expect(screen.getByTestId('public-trip-stale')).toBeTruthy();
  });

  it('does not call the watched party stale because this link’s own read failed', () => {
    const { rerender } = render(<PublicTripPage />);
    // The positive twin first: with the link's own party on screen, its own failed read is said.
    live = { ...live, error: new ApiError(503) };
    rerender(<PublicTripPage />);
    expect(screen.getByTestId('public-trip-stale')).toBeTruthy();

    watchOther();

    expect(screen.getByTestId('public-watch-banner')).toBeTruthy();
    expect(screen.queryByTestId('public-trip-stale')).toBeNull();
  });

  it('does not say the page will never refresh again because this link’s own read was refused, while the list still lands', () => {
    // The link's own trip stopped being published while the tab was open: its own read is
    // refused for good, and the list — which the server goes on answering — still hands over a
    // party that is underground and is read again every minute.
    live = { ...live, dataUpdatedAt: Date.now() };
    liveList = { ...answered(ownRow(), row()), dataUpdatedAt: Date.now() };
    const { rerender } = render(<PublicTripPage />);
    watchOther();

    live = { ...live, error: new ApiError(404, 'tracking.share_not_found') };
    rerender(<PublicTripPage />);

    expect(screen.getByTestId('public-watch-banner')).toBeTruthy();
    expect(screen.queryByTestId('public-trip-ended')).toBeNull();
    // Ages still run: the party on screen is being kept up with.
    expect(screen.getByTestId('public-trip-updated')).toHaveTextContent('Page updated');
    expect(drawn()).toEqual(['Mircea', 'Ileana', 'Radu']);
    // The rows are still there to be pressed, the way back among them.
    expect(screen.getByTestId('public-live-list')).toBeTruthy();

    // And a later failure of the list is still said as one — it was not silenced for good.
    liveList = { ...liveList, isError: true, error: new ApiError(503) };
    rerender(<PublicTripPage />);
    expect(screen.getByTestId('public-trip-stale')).toBeTruthy();
    expect(screen.queryByTestId('public-trip-ended')).toBeNull();

    // The final word about this link's own trip is said where it is true: over that trip.
    fireEvent.click(screen.getByTestId('public-watch-back'));
    expect(screen.getByTestId('public-trip-ended')).toHaveTextContent('will not be refreshed again');
  });

  it('says the link has stopped answering when both reads are refused for good while a party is watched', () => {
    live = { ...live, dataUpdatedAt: Date.now() };
    liveList = { ...answered(ownRow(), row()), dataUpdatedAt: Date.now() };
    const { rerender } = render(<PublicTripPage />);
    watchOther();

    live = { ...live, error: new ApiError(404, 'tracking.share_not_found') };
    liveList = { ...liveList, isError: true, error: new ApiError(404, 'tracking.share_not_found') };
    rerender(<PublicTripPage />);

    expect(screen.getByTestId('public-trip-ended')).toHaveTextContent('will not be refreshed again');
    expect(screen.queryByTestId('public-trip-stale')).toBeNull();
    expect(screen.getByTestId('public-trip-updated')).toHaveTextContent('Last read at');
  });

  it('takes a final refusal of the list as final while a party is watched, though this link’s own closed trip was never read again', () => {
    // A closed trip with nothing to re-sign is not read again, so the link's own read never
    // hears that the link was taken back. The list does — and that is the read on screen.
    live = {
      data: envelope({ state: 'closed', closedAt: '2026-09-14T12:00:00Z' }),
      isPending: false,
      error: null,
    };
    liveList = { ...answered(ownRow(), row()), dataUpdatedAt: Date.now() };
    const { rerender } = render(<PublicTripPage />);
    watchOther();

    liveList = { ...liveList, isError: true, error: new ApiError(404, 'tracking.share_not_found') };
    rerender(<PublicTripPage />);

    // Not "it starts refreshing again by itself": it will not.
    expect(screen.queryByTestId('public-trip-stale')).toBeNull();
    expect(screen.getByTestId('public-trip-ended')).toHaveTextContent('will not be refreshed again');
    expect(screen.getByTestId('public-trip-updated')).toHaveTextContent('Last read at');
    // And the list does not invite another try beneath that sentence.
    expect(screen.getByTestId('public-live-link-ended')).toBeTruthy();
    expect(screen.queryByTestId('public-live-failed')).toBeNull();

    // The twin: the same list merely failing to land is a fault that may clear.
    liveList = { ...liveList, error: new ApiError(503) };
    rerender(<PublicTripPage />);
    expect(screen.getByTestId('public-trip-stale')).toBeTruthy();
    expect(screen.queryByTestId('public-trip-ended')).toBeNull();
  });

  it('keeps the rows of the list in hand when a later read of it merely fails', () => {
    const { rerender } = render(<PublicTripPage />);
    toggleParties();
    expect(screen.getByTestId('public-live-list')).toBeTruthy();

    liveList = { ...liveList, isError: true, error: new TypeError('Failed to fetch') };
    rerender(<PublicTripPage />);

    expect(screen.getByTestId('public-live-list')).toBeTruthy();
    expect(screen.getByTestId(`public-live-watch-${OTHER}`)).toBeTruthy();
    expect(screen.queryByTestId('public-live-failed')).toBeNull();
  });

  it('gives the list up for the failure notice when there is none in hand, or the refusal is final', () => {
    liveList = { isPending: false, isError: true, error: new TypeError('Failed to fetch') };
    const { rerender } = render(<PublicTripPage />);
    toggleParties();
    expect(screen.getByTestId('public-live-failed')).toBeTruthy();

    // A list in hand, and a refusal the server settled: every row would be refused the same way.
    liveList = {
      ...answered(ownRow(), row()),
      isError: true,
      error: new ApiError(404, 'tracking.share_not_found'),
    };
    rerender(<PublicTripPage />);
    expect(screen.queryByTestId('public-live-list')).toBeNull();
    expect(screen.getByTestId('public-live-failed')).toBeTruthy();
  });

  it('says a trip is over about the party on screen, not about this link’s own', () => {
    live = {
      data: envelope({ state: 'closed', closedAt: '2026-09-14T12:00:00Z' }),
      isPending: false,
      error: null,
    };
    render(<PublicTripPage />);
    expect(screen.getByTestId('public-trip-closed')).toHaveTextContent('This trip is over');

    watchOther();

    // The party on screen is still underground: nothing may say it is finished.
    expect(screen.queryByTestId('public-trip-closed')).toBeNull();
    expect(screen.getByTestId('public-trip-state-armed')).toBeTruthy();
  });

  it('says so when it is the watched party whose trip is over, and this link’s own is not', () => {
    liveList = answered(ownRow(), row({ state: 'closed', closedAt: '2026-09-14T11:00:00Z' }));
    render(<PublicTripPage />);
    expect(screen.queryByTestId('public-trip-closed')).toBeNull();

    watchOther();

    expect(screen.getByTestId('public-trip-closed')).toHaveTextContent("This party's trip is over");
    expect(screen.getByTestId('public-trip-state-closed')).toBeTruthy();
  });

  it('says positions are withheld when they are withheld from the party on screen', () => {
    liveList = answered(ownRow(), row({ positionsWithheld: true }));
    render(<PublicTripPage />);
    expect(screen.queryByTestId('public-trip-withheld')).toBeNull();

    watchOther();
    expect(screen.getByTestId('public-trip-withheld')).toBeTruthy();

    fireEvent.click(screen.getByTestId('public-watch-back'));
    expect(screen.queryByTestId('public-trip-withheld')).toBeNull();
  });

  it('drops the watch when a past trip is picked, and returns from the past to this link’s own trip', () => {
    render(<PublicTripPage />);
    watchOther();

    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    expect(screen.getByTestId('public-past-banner')).toBeTruthy();
    expect(screen.queryByTestId('public-watch-banner')).toBeNull();
    expect(drawn()).toEqual(['Vlad']);

    fireEvent.click(screen.getByTestId('public-past-back'));

    // Not back into the watch: the way back from the past says where it goes, and goes there.
    expect(screen.queryByTestId('public-watch-banner')).toBeNull();
    expect(screen.getByTestId('public-trip-title')).toHaveTextContent('E1, the deep end');
    expect(drawn()).toEqual(['Ana']);
  });

  it('leaves a replay, address included, when a party is asked for while one is on screen', () => {
    render(<PublicTripPage />);
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));
    expect(screen.getByTestId('public-past-banner')).toBeTruthy();
    setAddress.mockClear();

    watchOther();

    expect(screen.queryByTestId('public-past-banner')).toBeNull();
    expect(screen.getByTestId('public-watch-banner')).toBeTruthy();
    expect(drawn()).toEqual(['Mircea', 'Ileana', 'Radu']);
    const written = setAddress.mock.calls.at(-1)?.[0] as URLSearchParams;
    expect(written.has('past')).toBe(false);
    expect(setAddress.mock.calls.at(-1)?.[1]).toEqual({ replace: true, flushSync: true });
  });
});
