// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type {
  PublicPastTrack,
  PublicPastTripList,
  PublicTripEnvelope,
} from '../../api/hooks.ts';
import { readPastLink } from './pastTripLink.ts';

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
let list: { data?: PublicPastTripList; isPending: boolean; isError: boolean; error?: unknown };
let trackAnswer: { data?: PublicPastTrack; isPending: boolean; isError: boolean };

/** Every read the page actually made, which is how laziness is asserted rather than asserted about. */
let listReads = 0;
let liveReads = 0;
/** What the list of parties being followed now answers once it is asked. */
let liveList: { data?: { trips: never[]; more: boolean }; isPending: boolean; isError: boolean; error?: unknown };
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
  // The parties being followed now, behind a press of their own and counted the same way.
  usePublicLiveTrips: (_token: string | undefined, enabled: boolean) => {
    if (enabled) {
      liveReads++;
    }
    return enabled ? liveList : { data: undefined, isPending: false, isError: false };
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
/** The link the page is opened under, which a test can change under a page already drawn. */
let linkToken = 'follow-token';
vi.mock('react-router-dom', () => ({
  useParams: () => ({ token: linkToken }),
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
    expedition: null,
    title: 'Peștera Demo Mare, exploration',
    tripDate: '2026-09-14',
    tripDateEnd: null,
    state: 'armed',
    armedAt: '2026-09-14T06:00:00Z',
    closedAt: null,
    expectedReturnAt: null,
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
    expedition: null,
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
const openParties = () =>
  fireEvent.click(screen.getByText('Also in this cave now', { selector: 'span' }));

beforeEach(() => {
  live = { data: envelope(), isPending: false, error: null };
  list = {
    data: {
      trips: [
        {
          tripLogId: TRIP_2019,
          expedition: null,
          title: 'Peștera Demo Mare, the 2019 push',
          tripDate: '2019-07-06',
          tripDateEnd: null,
          closedAt: '2019-07-06T18:00:00Z',
          participantCount: 4,
          playable: true,
        },
        {
          tripLogId: TRIP_EMPTY,
          expedition: null,
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
  liveReads = 0;
  liveList = { data: { trips: [], more: false }, isPending: false, isError: false };
  trackReads = [];
  given = undefined;
  address = new URLSearchParams();
  linkToken = 'follow-token';
  setAddress.mockClear();
});

afterEach(cleanup);

describe('reaching a cave’s past from a published link', () => {
  it('reads nothing of the archive until a reader opens it', () => {
    render(<PublicTripPage />);

    // The whole point of the gate: this page is opened by families on phones in numbers nobody can
    // see, and a list nobody asked for would double the cost of the cheapest surface here.
    expect(listReads).toBe(0);
    // The parties being followed now are the other half of the cave's list, behind a gate of
    // their own.
    expect(liveReads).toBe(0);
    expect(trackReads.every((asked) => asked === undefined)).toBe(true);

    // The positive twin — the same page, one press later. And one press reads one list: the
    // archive's section says nothing of who is in the cave now, so it does not ask.
    openArchive();
    expect(listReads).toBeGreaterThan(0);
    expect(liveReads).toBe(0);
    expect(screen.getByTestId('public-past-list')).toBeTruthy();
    expect(screen.queryByTestId('public-live')).toBeNull();
  });

  it('reads who else is in the cave now only when that section is opened, and not the archive with it', () => {
    render(<PublicTripPage />);
    expect(liveReads).toBe(0);

    openParties();

    expect(liveReads).toBeGreaterThan(0);
    expect(listReads).toBe(0);
    expect(screen.getByTestId('public-live')).toBeTruthy();
    expect(screen.queryByTestId('public-past-list')).toBeNull();
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

  it('says from when to when the trip on screen was followed, and never the live trip’s hours', () => {
    render(<PublicTripPage />);
    // The live trip, watched since six in the morning of 2026 and still running.
    expect(screen.getByTestId('public-trip-since').textContent).toMatch(/^Followed since /);

    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    // The replay's own span, with its own year: a header still counting the hours of the party
    // underground now, over a trip from 2019, would be the page telling two stories at once.
    const said = screen.getByTestId('public-trip-since').textContent ?? '';
    expect(said).toMatch(/^Followed .*2019/);
    expect(said).not.toMatch(/since|2026/);
    // Nothing about the page's own age over a replay — the past is not being refreshed.
    expect(screen.queryByTestId('public-trip-updated')).toBeNull();
    // And the explanation of what a place is stands over the past party as it does over the live.
    expect(screen.getByTestId('public-trip-about')).toHaveTextContent('where somebody was last reported');
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

  it('forgets where a follow pointed the camera once the reader leaves the past', () => {
    // <b>The defect this pins.</b> The follow's request was only ever set, so pressing the way back
    // handed the viewer the live survey together with the past trip's followed station — and the
    // viewer, which performs a request whenever a model finishes loading, flew the live drawing to
    // where a party of 2019 had been, or said "not in this model" about a station the live survey
    // never had. The camera has to arrive on the live party with nothing left to ask.
    address = new URLSearchParams(`past=${TRIP_2019}&team=${TEAM_B}`);
    render(<PublicTripPage />);
    expect(given?.focusRequest).toEqual({ kind: 'station', ref: 'far.end.2' });

    fireEvent.click(screen.getByTestId('public-past-back'));

    expect(given?.focusRequest).toBeUndefined();
    expect(((given?.trackedCavers ?? []) as { name: string }[]).map((caver) => caver.name)).toEqual([
      'Ana',
    ]);
  });

  it('writes the trip a reader picked into the address, so the view can be sent to somebody', () => {
    render(<PublicTripPage />);
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    const written = setAddress.mock.calls.at(-1)?.[0] as URLSearchParams;
    expect(written.get('past')).toBe(TRIP_2019);
  });

  it('makes picking a trip a step the browser’s Back button undoes', () => {
    // <b>The defect this pins.</b> The pick replaced the entry the reader was on, so Back — the
    // press somebody reaches for on finding themselves in a trip of years ago — left the page.
    render(<PublicTripPage />);
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    // A new entry, and handed to the page before the press returns: a Back pressed while the
    // browser is still busy with the survey the pick asked for must find the pick already there.
    expect(setAddress.mock.calls.at(-1)?.[1]).toEqual({ replace: false, flushSync: true });
  });

  it('adds no second step for the row of the trip the address already names', () => {
    address = new URLSearchParams(`past=${TRIP_2019}`);
    render(<PublicTripPage />);
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    expect(setAddress.mock.calls.at(-1)?.[1]).toEqual({ replace: true, flushSync: true });
  });

  it('clears the address in place when the reader leaves by the page’s own way back', () => {
    address = new URLSearchParams(`past=${TRIP_2019}&team=${TEAM_B}`);
    render(<PublicTripPage />);

    fireEvent.click(screen.getByTestId('public-past-back'));

    const written = setAddress.mock.calls.at(-1)?.[0] as URLSearchParams;
    expect(written.has('past')).toBe(false);
    expect(written.has('team')).toBe(false);
    expect(setAddress.mock.calls.at(-1)?.[1]).toEqual({ replace: true, flushSync: true });
  });

  it('returns to the party being followed now when the address stops naming a past trip', () => {
    // What Back produces after a pick: the same page, at the address it had before.
    address = new URLSearchParams(`past=${TRIP_2019}&team=${TEAM_B}`);
    const { rerender } = render(<PublicTripPage />);
    expect(screen.getByTestId('public-past-banner')).toBeTruthy();

    address = new URLSearchParams();
    rerender(<PublicTripPage />);

    expect(screen.queryByTestId('public-past-banner')).toBeNull();
    expect(((given?.trackedCavers ?? []) as { name: string }[]).map((caver) => caver.name)).toEqual([
      'Ana',
    ]);
    // And it left by reading the address, not by writing one: there is nothing to clear.
    expect(setAddress).not.toHaveBeenCalled();
  });

  it('stays in a replay the reader picked while only the page’s language changes in the address', () => {
    // The address never named the trip in this renderer, exactly as it does not for the moment
    // between a press and the router answering it. Something else changing must not read as Back.
    const { rerender } = render(<PublicTripPage />);
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));
    expect(screen.getByTestId('public-past-banner')).toBeTruthy();

    address = new URLSearchParams('lang=en');
    rerender(<PublicTripPage />);

    expect(screen.getByTestId('public-past-banner')).toBeTruthy();
  });

  it('writes whom the reader follows into the address as well, so the link sent is the view seen', async () => {
    // <b>The defect this pins.</b> Following the survey team changed the screen and left the address
    // at `?past=` alone, so a reader who copied the bar sent a link that opened the trip following
    // nobody — although the same link with the team on it, which this page reads, does work.
    render(<PublicTripPage />);
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    const choose = async (label: string) => {
      fireEvent.mouseDown(within(screen.getByTestId('public-past-follow')).getByRole('combobox'));
      const option = await waitFor(() => {
        const found = Array.from(
          document.querySelectorAll<HTMLElement>('.ant-select-item-option'),
        ).find((candidate) => candidate.textContent === label);
        expect(found).toBeDefined();
        return found!;
      });
      fireEvent.click(option);
    };
    const written = () => setAddress.mock.calls.at(-1)?.[0] as URLSearchParams;

    await choose('Survey');
    expect(written().get('past')).toBe(TRIP_2019);
    expect(written().get('team')).toBe(TEAM_B);
    expect(setAddress.mock.calls.at(-1)?.[1]).toEqual({ replace: true });

    await choose('Follow nobody');
    expect(written().get('past')).toBe(TRIP_2019);
    expect(written().has('team')).toBe(false);
    expect(written().has('caver')).toBe(false);
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

/**
 * Where the page puts the reader after a press in the picker.
 *
 * <b>The picker is the last thing on the page and the strip is the first.</b> On a phone with a
 * party between them, the only visible answer to pressing a row used to be the row's own tag
 * turning to "Playing"; the banner saying this is the past, the way back and the transport all
 * mounted a screen or more above, out of view. The test renderer lays nothing out, so what is
 * proved here is the request and its target rather than the pixels.
 */
describe('after a row is pressed at the bottom of the page', () => {
  const had = Element.prototype.scrollIntoView;
  // The element each request was made on is the mock's `this`, which vitest records as a context.
  const scrolled = vi.fn();
  const scrolledTo = () => scrolled.mock.contexts as Element[];
  beforeEach(() => {
    scrolled.mockClear();
    Element.prototype.scrollIntoView = scrolled;
  });
  afterEach(() => {
    Element.prototype.scrollIntoView = had;
  });

  it('brings the strip that says this is the past into view', () => {
    render(<PublicTripPage />);
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    expect(scrolledTo()).toHaveLength(1);
    expect(scrolledTo()[0].contains(screen.getByTestId('public-past-bar'))).toBe(true);
    expect(scrolledTo()[0].contains(screen.getByTestId('public-past-back'))).toBe(true);
  });

  it('does so again for the row already playing, since that is a press too', () => {
    render(<PublicTripPage />);
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    expect(scrolledTo()).toHaveLength(2);
  });

  // The body of a replay — its counts, its survey, its party — needs the track and a moment on the
  // clock, so it arrives a moment after the press. Scrolled to at the press itself, the strip sat
  // at the top of a page too short to scroll, and in a real browser the body arriving under it let
  // scroll anchoring carry the page back down to the pressed row, out of sight of the strip again.
  it('waits for the replay to draw its body before bringing the strip into view, and does so once', () => {
    trackAnswer = { data: undefined, isPending: true, isError: false };
    const { rerender } = render(<PublicTripPage />);
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    expect(screen.getByTestId('public-past-bar')).toBeTruthy();
    expect(screen.queryByTestId('viewer')).toBeNull();
    expect(scrolledTo()).toHaveLength(0);

    trackAnswer = { data: pastTrack(), isPending: false, isError: false };
    rerender(<PublicTripPage />);

    expect(screen.getByTestId('viewer')).toBeTruthy();
    expect(scrolledTo()).toHaveLength(1);
    expect(scrolledTo()[0].contains(screen.getByTestId('public-past-bar'))).toBe(true);

    rerender(<PublicTripPage />);
    expect(scrolledTo()).toHaveLength(1);
  });

  it('still brings the strip into view when the trip pressed could not be read', () => {
    trackAnswer = { data: undefined, isPending: true, isError: false };
    const { rerender } = render(<PublicTripPage />);
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));
    expect(scrolledTo()).toHaveLength(0);

    trackAnswer = { data: undefined, isPending: false, isError: true };
    rerender(<PublicTripPage />);

    expect(scrolledTo()).toHaveLength(1);
    expect(scrolledTo()[0].contains(screen.getByTestId('public-past-bar'))).toBe(true);
  });

  it('forgets a press whose trip the reader left before it could be drawn', () => {
    trackAnswer = { data: undefined, isPending: true, isError: false };
    const { rerender } = render(<PublicTripPage />);
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));
    fireEvent.click(screen.getByTestId('public-past-back'));

    trackAnswer = { data: pastTrack(), isPending: false, isError: false };
    rerender(<PublicTripPage />);

    expect(scrolledTo()).toHaveLength(0);
  });

  it('leaves a page that a link opened already playing where it opened', () => {
    // Opened at the top of the page in any case, and nobody pressed anything.
    address = new URLSearchParams(`past=${TRIP_2019}`);
    render(<PublicTripPage />);

    expect(screen.getByTestId('public-past-banner')).toBeTruthy();
    expect(scrolledTo()).toHaveLength(0);
  });
});

/**
 * A link that names a moment of a past trip, with or without the word that sets it playing — read
 * off the page's address when it arrives, and written by the two buttons on the replay's strip.
 *
 * <b>The page's part is the joining.</b> How the address is parsed, how a request to play is held
 * until the track lands and what the strip says after a press are each proved where they live;
 * what only the page can get wrong is handing one to the other — the address's `play` to the
 * replay, and the replay's trip, follow and moment to the link a button copies.
 */
describe('a link to a moment of a past trip', () => {
  const HALF_NINE = '2019-07-06T09:30:00Z';
  const realClipboard = Object.getOwnPropertyDescriptor(navigator, 'clipboard');

  afterEach(() => {
    if (realClipboard === undefined) {
      Reflect.deleteProperty(navigator, 'clipboard');
    } else {
      Object.defineProperty(navigator, 'clipboard', realClipboard);
    }
  });

  const clockShows = (iso: string) =>
    expect(screen.getByTestId('public-past-scrub').querySelector('[role="slider"]')).toHaveAttribute(
      'aria-valuenow',
      String(Date.parse(iso)),
    );

  it('opens at the moment named and starts playing when the address says play', () => {
    address = new URLSearchParams(`past=${TRIP_2019}&at=${HALF_NINE}&play=1`);
    render(<PublicTripPage />);

    clockShows(HALF_NINE);
    // The button offers the opposite of what the clock is doing.
    expect(screen.getByTestId('public-past-play')).toHaveAccessibleName('Pause');
  });

  it('opens at the moment named and stands still when the address does not', () => {
    // The twin of the case above: the same link without the word, and one that says no.
    address = new URLSearchParams(`past=${TRIP_2019}&at=${HALF_NINE}`);
    const first = render(<PublicTripPage />);
    clockShows(HALF_NINE);
    expect(screen.getByTestId('public-past-play')).toHaveAccessibleName('Play');
    first.unmount();

    address = new URLSearchParams(`past=${TRIP_2019}&at=${HALF_NINE}&play=0`);
    render(<PublicTripPage />);
    clockShows(HALF_NINE);
    expect(screen.getByTestId('public-past-play')).toHaveAccessibleName('Play');
  });

  it('is not honoured a second time because something else in the address changed', () => {
    // The language button rewrites the same address. A reader who paused, dragged the handle and
    // then asked for English must not be wound back to the link's moment and set going again.
    address = new URLSearchParams(`past=${TRIP_2019}&at=${HALF_NINE}&play=1`);
    const { rerender } = render(<PublicTripPage />);
    fireEvent.click(screen.getByTestId('public-past-play'));
    fireEvent.click(screen.getByTestId('public-past-report-next'));
    clockShows('2019-07-06T10:00:00Z');
    expect(screen.getByTestId('public-past-play')).toHaveAccessibleName('Play');

    address = new URLSearchParams(`past=${TRIP_2019}&at=${HALF_NINE}&play=1&lang=en`);
    rerender(<PublicTripPage />);

    clockShows('2019-07-06T10:00:00Z');
    expect(screen.getByTestId('public-past-play')).toHaveAccessibleName('Play');
  });

  it('is honoured again when the address comes to name another moment', () => {
    // The other half of the rule above: what the address says about the past did change.
    address = new URLSearchParams(`past=${TRIP_2019}&at=${HALF_NINE}`);
    const { rerender } = render(<PublicTripPage />);
    clockShows(HALF_NINE);

    address = new URLSearchParams(`past=${TRIP_2019}&at=2019-07-06T10:00:00Z`);
    rerender(<PublicTripPage />);

    clockShows('2019-07-06T10:00:00Z');
  });

  it('copies a link that opens the trip, the team followed and the moment on the clock — playing or not', async () => {
    const written: string[] = [];
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: {
        writeText: (text: string) => {
          written.push(text);
          return Promise.resolve();
        },
      },
    });
    address = new URLSearchParams(`lang=en&past=${TRIP_2019}&team=${TEAM_B}&at=${HALF_NINE}`);
    render(<PublicTripPage />);
    // A follow opened before its team appears stays at the moment the link named.
    clockShows(HALF_NINE);

    fireEvent.click(screen.getByTestId('public-past-copy-moment'));
    await waitFor(() => expect(written).toHaveLength(1));
    fireEvent.click(screen.getByTestId('public-past-copy-playing'));
    await waitFor(() => expect(written).toHaveLength(2));

    const [standing, playing] = written.map((text) => new URL(text));
    expect(standing.origin + standing.pathname).toBe(window.location.origin + window.location.pathname);
    // Read back by the same reader the page reads its own address with.
    expect(readPastLink(standing.searchParams)).toEqual({
      tripLogId: TRIP_2019,
      follow: { kind: 'team', id: TEAM_B },
      at: HALF_NINE,
      play: false,
    });
    expect(readPastLink(playing.searchParams)).toEqual({
      tripLogId: TRIP_2019,
      follow: { kind: 'team', id: TEAM_B },
      at: HALF_NINE,
      play: true,
    });
    // Whatever else the address carried goes with the link — the language above all.
    expect(standing.searchParams.get('lang')).toBe('en');
    expect(playing.searchParams.get('lang')).toBe('en');
    // And the address bar itself was given no moment by either press.
    for (const call of setAddress.mock.calls) {
      expect((call[0] as URLSearchParams).has('at')).toBe(false);
    }
  });

  it('copies the moment the reader moved the clock to, not the one the link opened at', async () => {
    const written: string[] = [];
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: {
        writeText: (text: string) => {
          written.push(text);
          return Promise.resolve();
        },
      },
    });
    address = new URLSearchParams(`past=${TRIP_2019}&at=${HALF_NINE}`);
    render(<PublicTripPage />);
    fireEvent.click(screen.getByTestId('public-past-report-next'));

    fireEvent.click(screen.getByTestId('public-past-copy-moment'));
    await waitFor(() => expect(written).toHaveLength(1));

    expect(new URL(written[0]).searchParams.get('at')).toBe('2019-07-06T10:00:00Z');
  });
});

/**
 * An archive the server refused for good, told apart from one that could not be reached.
 *
 * An installation can switch its archive off, and then the list is refused exactly as an unknown
 * link is — deliberately, so a stranger cannot tell the two apart. But the reader holding a link
 * that has just opened the trip above is not a stranger guessing, and a refusal the server settled
 * is not something trying again will change. The one sentence that must not be printed under it is
 * the invitation to try again.
 */
describe('the cave’s other past trips while one of them is on screen', () => {
  const TRIP_2021 = 'aaaaaaaa-0000-0000-0000-000000000021';
  const switchList = () => screen.queryByTestId('public-past-switch-list');

  beforeEach(() => {
    list.data?.trips.push({
      tripLogId: TRIP_2021,
      expedition: null,
      title: 'Peștera Demo Mare, the 2021 survey',
      tripDate: '2021-08-12',
      tripDateEnd: null,
      closedAt: '2021-08-12T19:00:00Z',
      participantCount: 3,
      playable: true,
    });
  });

  it('are not offered under the title while the party now is what is on screen', () => {
    render(<PublicTripPage />);
    expect(screen.queryByTestId('public-past-switch')).toBeNull();

    // The positive twin: the same page, with a past trip on screen.
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));
    expect(screen.getByTestId('public-past-switch')).toHaveTextContent(
      'Play another trip of this cave',
    );
  });

  it('are one press under the strip, shut until then, and hold every row once', () => {
    address = new URLSearchParams(`past=${TRIP_2019}`);
    render(<PublicTripPage />);

    // Shut: the only rows on the page are the ones at its foot.
    expect(switchList()).toBeNull();
    expect(screen.getAllByTestId('public-past-list')).toHaveLength(1);

    fireEvent.click(screen.getByTestId('public-past-switch'));

    const rows = switchList();
    expect(rows).not.toBeNull();
    // Directly under the strip, and before the drawing: nothing of the page stands between them.
    const strip = screen.getByTestId('public-past-bar');
    expect(strip.compareDocumentPosition(rows!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(
      rows!.compareDocumentPosition(screen.getByTestId('viewer'))
        & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
    expect(within(rows!).getByTestId(`public-past-trip-${TRIP_2019}`)).toHaveTextContent('Playing');
    expect(within(rows!).getByTestId(`public-past-trip-${TRIP_2021}`)).toBeTruthy();
    expect(within(rows!).getByTestId(`public-past-trip-${TRIP_EMPTY}`)).toHaveTextContent(
      'Nothing to play',
    );
  });

  it('play the trip pressed there as a new step in the address, and the section shuts behind the press', () => {
    address = new URLSearchParams(`past=${TRIP_2019}`);
    render(<PublicTripPage />);
    fireEvent.click(screen.getByTestId('public-past-switch'));

    fireEvent.click(within(switchList()!).getByTestId(`public-past-trip-${TRIP_2021}`));

    expect(trackReads).toContain(TRIP_2021);
    const written = setAddress.mock.calls.at(-1)?.[0] as URLSearchParams;
    expect(written.get('past')).toBe(TRIP_2021);
    expect(setAddress.mock.calls.at(-1)?.[1]).toEqual({ replace: false, flushSync: true });
    // Shut again, so the drawing of the trip just picked is what stands under the strip.
    expect(switchList()).toBeNull();
    expect(screen.getAllByTestId('public-past-list')).toHaveLength(1);
  });

  it('are read once for both places, and still read with the section at the foot shut', () => {
    address = new URLSearchParams(`past=${TRIP_2019}`);
    render(<PublicTripPage />);
    // The section at the foot was opened for the reader; they shut it.
    openArchive();
    expect(screen.queryByTestId('public-past-list')).toBeNull();

    fireEvent.click(screen.getByTestId('public-past-switch'));

    expect(within(switchList()!).getByTestId(`public-past-trip-${TRIP_2021}`)).toBeTruthy();
  });
});

describe('a second published link opened in the same tab', () => {
  const clock = () =>
    screen.getByTestId('public-past-scrub').querySelector('[role="slider"]')?.getAttribute('aria-valuenow');

  it('does not show the first link’s replay', () => {
    const { rerender } = render(<PublicTripPage />);
    openArchive();
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));
    // The replay that must not be carried over really is on screen under the first link.
    expect(screen.getByTestId('public-past-banner')).toBeTruthy();
    expect(((given?.trackedCavers ?? []) as { name: string }[]).map((caver) => caver.name)).toContain(
      'Mircea',
    );

    linkToken = 'second-link';
    rerender(<PublicTripPage />);

    expect(screen.queryByTestId('public-past-banner')).toBeNull();
    expect(screen.queryByTestId('public-past-switch')).toBeNull();
    expect(((given?.trackedCavers ?? []) as { name: string }[]).map((caver) => caver.name)).toEqual([
      'Ana',
    ]);
  });

  it('opens the past trip its own address names, from that address and not from where the first link was left', () => {
    // Both links are opened on an address naming the same trip and the same moment — two links of
    // one cave, sent by one person. What the first reader did to the clock belongs to the first.
    address = new URLSearchParams(`past=${TRIP_2019}&at=2019-07-06T09:30:00Z`);
    const { rerender } = render(<PublicTripPage />);
    fireEvent.click(screen.getByTestId('public-past-report-next'));
    expect(clock()).toBe(String(Date.parse('2019-07-06T10:00:00Z')));

    linkToken = 'second-link';
    rerender(<PublicTripPage />);

    expect(screen.getByTestId('public-past-banner')).toBeTruthy();
    expect(clock()).toBe(String(Date.parse('2019-07-06T09:30:00Z')));
  });
});

describe('an archive this installation does not offer', () => {
  it('says so rather than asking the reader to try again', () => {
    list = { data: undefined, isPending: false, isError: true, error: new ApiError(404, 'not_found') };
    render(<PublicTripPage />);
    openArchive();

    expect(screen.getByTestId('public-past-not-offered')).toHaveTextContent(
      'Past trips are not offered through this link',
    );
    expect(screen.queryByTestId('public-past-failed')).toBeNull();
    expect(screen.queryByText(/Try opening this list again/)).toBeNull();
  });

  it('keeps a list already in hand when a later read of it merely does not land', () => {
    // The list is read again when a reader comes back to it after a while. A request that did
    // not get through says nothing about the rows already shown, and must not swap them for a
    // failure under somebody who was about to press one.
    list = { ...list, isError: true, error: new TypeError('Failed to fetch') };
    render(<PublicTripPage />);
    openArchive();

    expect(screen.getByTestId(`public-past-trip-${TRIP_2019}`)).toBeEnabled();
    expect(screen.queryByTestId('public-past-failed')).toBeNull();
  });

  it('gives a list in hand up once the server refuses it for good', () => {
    // The twin: every row of it would be refused the same way when pressed, so the sentence that
    // says so replaces rows that can no longer do anything.
    list = { ...list, isError: true, error: new ApiError(404, 'not_found') };
    render(<PublicTripPage />);
    openArchive();

    expect(screen.getByTestId('public-past-not-offered')).toBeInTheDocument();
    expect(screen.queryByTestId(`public-past-trip-${TRIP_2019}`)).toBeNull();
  });

  it('still invites the reader to try again when the list merely did not land', () => {
    list = { data: undefined, isPending: false, isError: true, error: new TypeError('Failed to fetch') };
    render(<PublicTripPage />);
    openArchive();

    expect(screen.getByTestId('public-past-failed')).toHaveTextContent('Try opening this list again');
    expect(screen.queryByTestId('public-past-not-offered')).toBeNull();
    // A list that did not land plays nothing: the page goes on showing the party it was showing.
    expect(screen.queryByTestId('public-past-banner')).toBeNull();
  });

  /**
   * The link itself has been refused since the page opened — taken back, or run out — and the page
   * says so above. The cave's lists are read with that same link, so they are refused too, with
   * the very answer an archive switched off gives. Saying "this installation does not open the
   * cave's earlier trips" there claims a reason the page cannot know, and saying "this link opens
   * the trip above" is simply false; neither list may invite another try.
   */
  it('says the lists are refused with the link, not that the archive is off, once the link has ended', () => {
    live = { data: envelope(), isPending: false, error: new ApiError(404, 'tracking.share_not_found') };
    list = { data: undefined, isPending: false, isError: true, error: new ApiError(404, 'tracking.share_not_found') };
    liveList = { isPending: false, isError: true, error: new ApiError(404, 'tracking.share_not_found') };
    render(<PublicTripPage />);
    expect(screen.getByTestId('public-trip-ended')).toBeTruthy();
    openArchive();
    openParties();

    expect(screen.getByTestId('public-past-link-ended')).toHaveTextContent(
      'Past trips cannot be read through this link any more',
    );
    expect(screen.queryByTestId('public-past-not-offered')).toBeNull();
    expect(screen.queryByText(/This installation does not open/)).toBeNull();
    expect(screen.getByTestId('public-live-link-ended')).toBeTruthy();
    expect(screen.queryByTestId('public-live-failed')).toBeNull();
    expect(screen.queryByText(/Try opening this list again/)).toBeNull();
  });

  it('still reads the archive after the link has ended, since the archive outlives the live window', () => {
    // A party gone past its grace is refused on the live read and still opens the cave's earlier
    // trips — the archive answers either window. The ended notice must not be drawn over a list
    // that came back.
    live = { data: envelope(), isPending: false, error: new ApiError(404, 'tracking.share_not_found') };
    render(<PublicTripPage />);
    openArchive();

    expect(screen.getByTestId('public-past-list')).toBeTruthy();
    expect(screen.queryByTestId('public-past-link-ended')).toBeNull();
  });

  it('treats a server that is merely busy as a fault that can clear', () => {
    // 429 is the rate limiter, and the one refusal below 500 that is not final.
    list = { data: undefined, isPending: false, isError: true, error: new ApiError(429) };
    render(<PublicTripPage />);
    openArchive();

    expect(screen.getByTestId('public-past-failed')).toBeTruthy();
    expect(screen.queryByTestId('public-past-not-offered')).toBeNull();
  });
});
