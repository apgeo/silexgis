// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type {
  PublicPastTrack,
  PublicPastTripList,
  PublicTripEnvelope,
} from '../../api/hooks.ts';

/**
 * The cave's past, as somebody holding one published link reaches it.
 *
 * <b>Driven through the page rather than through the fold.</b> What the fold answers is proved next
 * door without a browser; what this file is for is everything the fold cannot say — that the
 * archive is not read until it is asked for, that a banner stands over a past view and says which
 * trip it is, that the way back is a different offer depending on whether there is a party to go
 * back to, and that a link in somebody's prose opens the thing it names.
 */

const TEAM_A = '11111111-1111-1111-1111-111111111111';
const TEAM_B = '22222222-2222-2222-2222-222222222222';
const TRIP_2019 = 'aaaaaaaa-0000-0000-0000-000000000001';
const TRIP_EMPTY = 'aaaaaaaa-0000-0000-0000-000000000002';

let live: { data?: PublicTripEnvelope; isPending: boolean; error: unknown };
let list: { data?: PublicPastTripList; isPending: boolean; isError: boolean };
let trackAnswer: { data?: PublicPastTrack; isPending: boolean; isError: boolean };

/** Every read the page actually made, which is how laziness is asserted rather than asserted about. */
let listReads = 0;
let trackReads: (string | undefined)[] = [];

vi.mock('../../api/hooks.ts', () => ({
  usePublicTrip: () => live,
  useResLinksForTarget: () => ({ data: undefined, isPending: false, error: null }),
  useSurveyModel: () => ({ data: undefined, isPending: false, error: null }),
  usePublicPastTrips: (_token: string | undefined, enabled: boolean) => {
    if (enabled) {
      listReads++;
    }
    return enabled ? list : { data: undefined, isPending: false, isError: false };
  },
  usePublicPastTrack: (_token: string | undefined, tripLogId: string | undefined) => {
    trackReads.push(tripLogId);
    return tripLogId === undefined
      ? { data: undefined, isPending: false, isError: false }
      : trackAnswer;
  },
}));

let address = new URLSearchParams();
const setAddress = vi.fn();
vi.mock('react-router-dom', () => ({
  useParams: () => ({ token: 'follow-token' }),
  useSearchParams: () => [address, setAddress],
}));

/** The viewer, faked at its contract: what it is handed is the whole of what this page owes it. */
let given: Record<string, unknown> | undefined;
vi.mock('../../components/caveview/CaveViewPanel.tsx', () => ({
  default: (props: Record<string, unknown>) => {
    given = props;
    return <div data-testid="viewer" />;
  },
}));
vi.mock('../../hooks/useIsMobile.ts', () => ({ useIsMobile: () => false }));
vi.mock('./PublicTripSheetPane.tsx', () => ({
  default: () => <div data-testid="sheet-pane" />,
}));

const { default: PublicTripPage } = await import('./PublicTripPage.tsx');

const MODEL = {
  modelUrl: 'https://example.invalid/files/past.3d?sig=1',
  format: 'survex3d',
  proj4: null,
  sourceEpsg: null,
  pictures: [],
  rasterMaps: [],
} as unknown as PublicTripEnvelope['model'];

function envelope(overrides: Partial<PublicTripEnvelope> = {}): PublicTripEnvelope {
  return {
    tripLogId: '0195f4a2-6c3e-7b10-9f21-ab44de77c001',
    title: 'Peștera Demo Mare, exploration',
    tripDate: '2026-09-14',
    tripDateEnd: null,
    state: 'armed',
    armedAt: '2026-09-14T06:00:00Z',
    closedAt: null,
    positionsWithheld: false,
    model: MODEL,
    teams: [{ id: TEAM_A, title: 'Advance' }],
    participants: [
      {
        ordinal: 1,
        label: 'Ana',
        teamId: TEAM_A,
        stationName: 'today.1',
        depthM: null,
        lastRecordedAt: '2026-09-14T09:00:00Z',
        positionRecordedAt: '2026-09-14T09:00:00Z',
        positionOnOtherModel: false,
        in: true,
        out: false,
      },
    ],
    ...overrides,
  };
}

function pastTrack(): PublicPastTrack {
  return {
    tripLogId: '0195f4a2-6c3e-7b10-9f21-ab44de77c002',
    title: 'Peștera Demo Mare, the 2019 push',
    tripDate: '2019-07-06',
    tripDateEnd: null,
    armedAt: '2019-07-06T08:00:00Z',
    closedAt: '2019-07-06T18:00:00Z',
    positionsWithheld: false,
    trackTruncated: false,
    model: MODEL,
    teams: [
      { id: TEAM_A, title: 'Advance' },
      { id: TEAM_B, title: 'Survey' },
    ],
    participants: [
      {
        ordinal: 1,
        label: 'Mircea',
        track: [
          {
            recordedAt: '2019-07-06T09:00:00Z',
            teamId: TEAM_A,
            stationName: 'p.g.7',
            depthM: null,
            positionOnOtherModel: false,
            in: true,
            out: false,
          },
        ],
      },
      {
        ordinal: 2,
        label: 'Ileana',
        track: [
          {
            recordedAt: '2019-07-06T10:00:00Z',
            teamId: TEAM_B,
            stationName: 'far.end.2',
            depthM: null,
            positionOnOtherModel: false,
            in: true,
            out: false,
          },
        ],
      },
    ],
  };
}

const openArchive = () =>
  fireEvent.click(screen.getByText('Past trips in this cave', { selector: 'span' }));

beforeEach(() => {
  live = { data: envelope(), isPending: false, error: null };
  list = {
    data: {
      trips: [
        {
          tripLogId: TRIP_2019,
          title: 'Peștera Demo Mare, the 2019 push',
          tripDate: '2019-07-06',
          tripDateEnd: null,
          closedAt: '2019-07-06T18:00:00Z',
          participantCount: 4,
          playable: true,
        },
        {
          tripLogId: TRIP_EMPTY,
          title: 'Peștera Demo Mare, a look at the entrance',
          tripDate: '2018-05-02',
          tripDateEnd: null,
          closedAt: '2018-05-02T12:00:00Z',
          participantCount: 2,
          playable: false,
        },
      ],
      more: true,
    },
    isPending: false,
    isError: false,
  };
  trackAnswer = { data: pastTrack(), isPending: false, isError: false };
  listReads = 0;
  trackReads = [];
  given = undefined;
  address = new URLSearchParams();
  setAddress.mockClear();
});

afterEach(cleanup);

describe('reaching a cave’s past from a published link', () => {
  it('reads nothing of the archive until a reader opens it', () => {
    render(<PublicTripPage />);

    // The whole point of the gate: this page is opened by families on phones in numbers nobody can
    // see, and a list nobody asked for would double the cost of the cheapest surface here.
    expect(listReads).toBe(0);
    expect(trackReads.every((asked) => asked === undefined)).toBe(true);

    // The positive twin — the same page, one press later.
    openArchive();
    expect(listReads).toBeGreaterThan(0);
    expect(screen.getByTestId('public-past-list')).toBeTruthy();
  });

  it('fetches no track until a trip is chosen', () => {
    render(<PublicTripPage />);
    openArchive();

    expect(trackReads.every((asked) => asked === undefined)).toBe(true);

    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));
    expect(trackReads).toContain(TRIP_2019);
  });

  it('shows a trip with nothing to play as a trip with nothing to play', () => {
    render(<PublicTripPage />);
    openArchive();

    const row = screen.getByTestId(`public-past-trip-${TRIP_EMPTY}`);
    expect(row).toBeDisabled();
    expect(screen.getByTestId(`public-past-unplayable-${TRIP_EMPTY}`)).toHaveTextContent(
      'Nothing to play',
    );

    fireEvent.click(row);
    // Nothing was asked for: offering a row and then showing an empty cave is worse than offering
    // nothing at all.
    expect(trackReads).not.toContain(TRIP_EMPTY);
    expect(screen.queryByTestId('public-past-banner')).toBeNull();
  });

  it('says there is older history without saying how much', () => {
    render(<PublicTripPage />);
    openArchive();

    // How many times a club has been into one cave is a disclosure; that there is something older
    // is not, and the server deliberately sends no count.
    expect(screen.getByTestId('public-past-more')).toHaveTextContent('There are older trips');
    expect(screen.getByTestId('public-past-more').textContent).not.toMatch(/\d/);
  });

  it('says plainly that the view is the past, and which trip it is', () => {
    render(<PublicTripPage />);
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    expect(screen.getByTestId('public-past-banner')).toHaveTextContent(
      'You are looking at a past trip',
    );
    const what = screen.getByTestId('public-past-banner-what');
    expect(what).toHaveTextContent('Peștera Demo Mare, the 2019 push');
    expect(what.textContent).toMatch(/2019/);
  });

  it('draws the past party and not the live one', () => {
    render(<PublicTripPage />);
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    // Same page, same viewer, a different party — folded through the very shape the live read
    // produces, which is why nothing downstream needed changing.
    const drawn = (given?.trackedCavers ?? []) as { name: string }[];
    expect(drawn.map((caver) => caver.name)).toEqual(['Mircea', 'Ileana']);
    expect(screen.getByTestId('public-trip-title')).toHaveTextContent('the 2019 push');
  });

  it('draws nobody at all while a chosen track is still in flight', () => {
    trackAnswer = { data: undefined, isPending: true, isError: false };
    render(<PublicTripPage />);
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    // The one sentence this feature must never produce: the people who are underground right now,
    // drawn under a strip saying this is the past.
    expect(screen.queryByTestId('public-trip-party')).toBeNull();
    expect(screen.getByTestId('public-past-track-loading')).toBeTruthy();
    expect(screen.getByTestId('public-past-banner')).toBeTruthy();
  });

  it('offers the way back to a party that is underground now', () => {
    render(<PublicTripPage />);
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    expect(screen.getByTestId('public-past-back')).toHaveTextContent('Back to the party now');
    expect(screen.queryByTestId('public-past-no-live')).toBeNull();

    fireEvent.click(screen.getByTestId('public-past-back'));
    expect(screen.queryByTestId('public-past-banner')).toBeNull();
    // The address goes with the view: a reader who leaves the past and then copies what is in the
    // bar must not be sending somebody a link back into it.
    const left = setAddress.mock.calls.at(-1)?.[0] as URLSearchParams | undefined;
    expect(left?.get('past') ?? null).toBeNull();
    expect(((given?.trackedCavers ?? []) as { name: string }[]).map((caver) => caver.name)).toEqual([
      'Ana',
    ]);
  });

  it('says there is no party to go back to when this link’s own trip is over', () => {
    live = { data: envelope({ state: 'closed', closedAt: '2026-09-14T17:00:00Z' }), isPending: false, error: null };
    render(<PublicTripPage />);
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    // The explicit second half of the request: a button promising a "now" that does not exist is
    // the thing that must not be offered.
    expect(screen.getByTestId('public-past-no-live')).toHaveTextContent(
      'no party underground to go back to',
    );
    expect(screen.getByTestId('public-past-back')).toHaveTextContent("Back to this link's trip");
    expect(screen.getByTestId('public-past-back').textContent).not.toMatch(/now/i);
  });

  it('opens the trip a hyperlink named, and follows the team it named', () => {
    address = new URLSearchParams(`past=${TRIP_2019}&team=${TEAM_B}`);
    render(<PublicTripPage />);

    expect(screen.getByTestId('public-past-banner-what')).toHaveTextContent('the 2019 push');
    expect(screen.getByTestId('public-past-banner-following')).toHaveTextContent('Survey');
    // And the camera is aimed at where that team was, rather than left wherever it opened.
    expect(given?.focusRequest).toEqual({ kind: 'station', ref: 'far.end.2' });
  });

  it('writes the trip a reader picked into the address, so the view can be sent to somebody', () => {
    render(<PublicTripPage />);
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    const written = setAddress.mock.calls.at(-1)?.[0] as URLSearchParams;
    expect(written.get('past')).toBe(TRIP_2019);
  });

  it('offers one control for following a team or a person of the trip on screen', () => {
    render(<PublicTripPage />);
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    // A team and a single caver are one question at two scales, so they are one control — "a past
    // trip / team", in the owner's words. Nobody is followed until somebody says so.
    expect(screen.getByTestId('public-past-follow')).toBeTruthy();
    expect(screen.queryByTestId('public-past-banner-following')).toBeNull();
    // The replay opens at the trip's beginning, where nothing has been reported of anybody yet.
    const drawn = given?.trackedCavers as { position: { kind: string } }[];
    expect(drawn.map((caver) => caver.position.kind)).toEqual(['unreported', 'unreported']);
  });

  it('opens a followed team where they first appear, not at an empty rail', () => {
    // A link asking for the survey team, opened at the trip's armed instant, would be a press that
    // answers with nothing visible — indistinguishable from a control that does not work.
    address = new URLSearchParams(`past=${TRIP_2019}&team=${TEAM_B}`);
    render(<PublicTripPage />);

    const drawn = given?.trackedCavers as { name: string; position: { kind: string } }[];
    expect(drawn.find((caver) => caver.name === 'Ileana')?.position).toEqual({
      kind: 'station',
      station: 'far.end.2',
    });
  });

  it('says a chosen trip could not be read, rather than drawing an empty cave', () => {
    trackAnswer = { data: undefined, isPending: false, isError: true };
    render(<PublicTripPage />);
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    expect(screen.getByTestId('public-past-track-failed')).toHaveTextContent(
      'This trip could not be read',
    );
    expect(screen.queryByTestId('public-trip-party')).toBeNull();
  });

  it('keeps the live trip’s name and standing out of the header while a track is being read', () => {
    trackAnswer = { data: undefined, isPending: true, isError: false };
    render(<PublicTripPage />);
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    // The header under a banner reading "You are looking at a past trip — Reading this trip…" must
    // not be the live trip's title beside a blue "Underground now": that is one page saying a party
    // is underground and that this is the past, in the same glance.
    expect(screen.getByTestId('public-trip-title')).toHaveTextContent('A past trip');
    expect(screen.queryByTestId('public-trip-state-armed')).toBeNull();
    expect(screen.getByTestId('public-past-banner')).toBeTruthy();
  });

  it('keeps it out permanently when the track cannot be read at all', () => {
    // A link to a trip that has since passed this installation's retention: there is no later
    // moment at which the wrong header would be replaced by a right one.
    trackAnswer = { data: undefined, isPending: false, isError: true };
    render(<PublicTripPage />);
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    expect(screen.getByTestId('public-trip-title')).not.toHaveTextContent('exploration');
    expect(screen.queryByTestId('public-trip-state-armed')).toBeNull();
  });

  it('names the live trip and its standing while the live trip is what is on screen', () => {
    render(<PublicTripPage />);

    // The twin of the two above: nothing was taken away from the page a follower opens.
    expect(screen.getByTestId('public-trip-title')).toHaveTextContent(
      'Peștera Demo Mare, exploration',
    );
    expect(screen.getByTestId('public-trip-state-armed')).toHaveTextContent('Underground now');
  });

  it('says when a trip’s record was too long to be carried whole', () => {
    trackAnswer = {
      data: { ...pastTrack(), trackTruncated: true },
      isPending: false,
      isError: false,
    };
    render(<PublicTripPage />);
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    // Otherwise the party simply stops moving part way through and the page goes on printing how
    // long ago each of them was last heard from — a party who went quiet, rather than a record
    // that ran out.
    expect(screen.getByTestId('public-past-truncated')).toBeTruthy();
  });

  it('says nothing of the kind about a trip whose record arrived whole', () => {
    render(<PublicTripPage />);
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    expect(screen.queryByTestId('public-past-truncated')).toBeNull();
    expect(screen.getByTestId('public-past-banner')).toBeTruthy();
  });
});
