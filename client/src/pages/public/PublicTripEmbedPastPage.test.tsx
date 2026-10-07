// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type {
  PublicPastTrack,
  PublicPastTripList,
  PublicTripEnvelope,
} from '../../api/hooks.ts';
import { EMBED_CHANNEL, EMBED_PROTOCOL } from './publicTripEmbed.ts';

/**
 * The cave's past inside somebody else's article.
 *
 * <b>Two things are proved here and nowhere else.</b> That the frame's own chrome stays one line
 * until there is something to say — a club pasted a box, not a page — and that the conversation
 * with the document around it carries the past honestly: a link can open a trip, follow a team in
 * it and wind its clock, and every announcement says which trip the party it names belongs to. An
 * article printing "Ana is at the sump" from a message it believed was live would be saying
 * something false about somebody's whereabouts.
 */

const TEAM_A = '11111111-1111-1111-1111-111111111111';
const TEAM_B = '22222222-2222-2222-2222-222222222222';
const TRIP_2019 = 'aaaaaaaa-0000-0000-0000-000000000001';

let live: { data?: PublicTripEnvelope; isPending: boolean; error: unknown };
let list: { data?: PublicPastTripList; isPending: boolean; isError: boolean; error?: unknown };
let trackAnswer: { data?: PublicPastTrack; isPending: boolean; isError: boolean };
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
  // The parties being followed now, behind the same press as the archive and counted the same way.
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
// The frame's address, which is where an article says what language it is written in.
const address = new URLSearchParams();
const setAddress = vi.fn();
vi.mock('react-router-dom', () => ({
  useParams: () => ({ token: 'follow-token' }),
  useSearchParams: () => [address, setAddress],
}));
// Whether a finger is driving the frame. False by default — the desk this suite is read on.
let coarse = false;
vi.mock('../../hooks/useCoarsePointer.ts', () => ({ useCoarsePointer: () => coarse }));

let given: Record<string, unknown> | undefined;
vi.mock('../../components/caveview/CaveViewPanel.tsx', () => ({
  default: (props: Record<string, unknown>) => {
    given = props;
    return <div data-testid="viewer" />;
  },
}));
/** The sheet pane, faked at its contract: what it is handed is the whole of what the frame owes it. */
let sheetPane: Record<string, unknown> | undefined;
vi.mock('./PublicTripSheetPane.tsx', () => ({
  default: (props: Record<string, unknown>) => {
    sheetPane = props;
    return <div data-testid="sheet-pane" data-active={String(props.active)} />;
  },
}));

const { default: PublicTripEmbedPage } = await import('./PublicTripEmbedPage.tsx');

const model = {
  format: 'survex3d' as const,
  modelUrl: '/api/v1/files/abc/content?token=first',
  meshUrl: null,
  anchorLongitude: null,
  anchorLatitude: null,
  anchorHeightM: null,
  sourceEpsg: null,
  proj4: null,
  pictures: [],
  rasterMaps: [],
};

function envelope(overrides: Partial<PublicTripEnvelope> = {}): PublicTripEnvelope {
  return {
    tripLogId: '0195f4a2-6c3e-7b10-9f21-ab44de77c001',
    expedition: null,
    title: 'Peștera Demo Mare',
    tripDate: '2026-09-14',
    tripDateEnd: null,
    state: 'armed',
    armedAt: '2026-09-14T06:00:00Z',
    closedAt: null,
    positionsWithheld: false,
    model,
    teams: [],
    participants: [
      {
        ordinal: 1,
        label: 'Ana',
        teamId: null,
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
    model,
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

function fakeParent() {
  const sent: { message: Record<string, unknown>; origin: string }[] = [];
  const parent = {
    postMessage: (message: Record<string, unknown>, origin: string) => sent.push({ message, origin }),
  } as unknown as Window;
  Object.defineProperty(window, 'parent', { value: parent, configurable: true });
  return { parent, sent };
}

function deliver(source: Window, origin: string, data: unknown) {
  const event = new MessageEvent('message', { data, origin });
  Object.defineProperty(event, 'source', { value: source });
  act(() => {
    window.dispatchEvent(event);
  });
}

const hello = { silexgis: EMBED_CHANNEL, v: EMBED_PROTOCOL, type: 'hello' };
const focus = (
  kind: string,
  ref: string,
  extra: Record<string, unknown> = {},
) => ({ silexgis: EMBED_CHANNEL, v: EMBED_PROTOCOL, type: 'focus', target: { kind, ref }, ...extra });

const readies = (sent: { message: Record<string, unknown> }[]) =>
  sent.filter((posted) => posted.message.type === 'ready').map((posted) => posted.message);
const focused = (sent: { message: Record<string, unknown> }[]) =>
  sent.filter((posted) => posted.message.type === 'focused').map((posted) => posted.message);

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
      ],
      more: false,
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
  sheetPane = undefined;
  coarse = false;
});

afterEach(cleanup);

describe('the archive inside a framed viewer', () => {
  it('adds one line of chrome and reads nothing until it is pressed', () => {
    render(<PublicTripEmbedPage />);

    // The frame a club pasted is a box, sometimes 260px tall. A list standing open in it would
    // leave the drawing a strip.
    expect(screen.queryByTestId('public-past-list')).toBeNull();
    expect(listReads).toBe(0);
    // The parties being followed now are the other half of the same sheet, behind the same press.
    expect(liveReads).toBe(0);

    fireEvent.click(screen.getByTestId('public-past-open'));
    expect(screen.getByTestId('public-past-list')).toBeTruthy();
    expect(listReads).toBeGreaterThan(0);
    expect(screen.getByTestId('public-live')).toBeTruthy();
    expect(liveReads).toBeGreaterThan(0);
  });

  it('says the archive is not offered rather than asking for another try, when the server refused it for good', () => {
    // An installation with its archive switched off refuses the list exactly as it refuses an
    // unknown link. Inside somebody's article that answer is final, and a sheet inviting the
    // reader to open it again would be inviting them to wait for something that cannot happen.
    list = { data: undefined, isPending: false, isError: true, error: new ApiError(404, 'not_found') };
    render(<PublicTripEmbedPage />);
    fireEvent.click(screen.getByTestId('public-past-open'));

    expect(screen.getByTestId('public-past-not-offered')).toHaveTextContent('not offered');
    expect(screen.queryByTestId('public-past-failed')).toBeNull();
  });

  it('says the lists are refused with the link, not that the archive is off, once the link has ended', () => {
    // The link was taken back or ran out while the frame was open, and the banner says so. The
    // cave's lists are read with the same link and refused with it, in the very words an archive
    // switched off answers — so the sheet must claim neither reason, nor invite another try.
    live = { data: envelope(), isPending: false, error: new ApiError(404, 'tracking.share_not_found') };
    list = { data: undefined, isPending: false, isError: true, error: new ApiError(404, 'tracking.share_not_found') };
    liveList = { isPending: false, isError: true, error: new ApiError(404, 'tracking.share_not_found') };
    render(<PublicTripEmbedPage />);
    expect(screen.getByTestId('public-trip-ended')).toBeTruthy();
    fireEvent.click(screen.getByTestId('public-past-open'));

    expect(screen.getByTestId('public-past-link-ended')).toBeTruthy();
    expect(screen.queryByTestId('public-past-not-offered')).toBeNull();
    expect(screen.getByTestId('public-live-link-ended')).toBeTruthy();
    expect(screen.queryByText(/Try opening this list again/)).toBeNull();
  });

  it('invites another try when the list merely did not land, and plays nothing in its place', () => {
    // A phone that lost its signal for the one request, not an archive switched off: the sheet
    // says so and asks for another try, and the frame goes on showing the party it was showing.
    list = { data: undefined, isPending: false, isError: true, error: new TypeError('Failed to fetch') };
    render(<PublicTripEmbedPage />);
    fireEvent.click(screen.getByTestId('public-past-open'));

    expect(screen.getByTestId('public-past-failed')).toHaveTextContent('Try opening this list again');
    expect(screen.queryByTestId('public-past-not-offered')).toBeNull();
    expect(screen.queryByTestId('public-past-banner')).toBeNull();
    expect(screen.getByTestId('public-trip-embed')).not.toHaveClass('public-trip-embed-past');
    expect(trackReads.every((tripLogId) => tripLogId === undefined)).toBe(true);
  });

  it('plays a trip a reader picked from the sheet, and closes it behind them', () => {
    render(<PublicTripEmbedPage />);
    fireEvent.click(screen.getByTestId('public-past-open'));
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    expect(screen.getByTestId('public-past-banner')).toHaveTextContent(
      'You are looking at a past trip',
    );
    expect(((given?.trackedCavers ?? []) as { name: string }[]).map((caver) => caver.name)).toEqual(
      ['Mircea', 'Ileana'],
    );
  });

  it('hands the drawing the replay’s own marker timing, and the live party none', () => {
    // The club's own viewer places the party under a drag and slides it one tick while playing;
    // this frame is the same replay and must move the same way.
    render(<PublicTripEmbedPage />);
    expect(given?.markerMoveMs).toBeUndefined();

    fireEvent.click(screen.getByTestId('public-past-open'));
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));
    expect(given?.markerMoveMs).toBe(0);

    fireEvent.click(screen.getByTestId('public-past-play'));
    expect(given?.markerMoveMs).toBeGreaterThan(0);

    fireEvent.click(screen.getByTestId('public-past-play'));
    expect(given?.markerMoveMs).toBe(0);
  });

  it('says nothing about a missing drawing while the chosen trip is still being read', () => {
    trackAnswer = { data: undefined, isPending: true, isError: false };
    render(<PublicTripEmbedPage />);
    fireEvent.click(screen.getByTestId('public-past-open'));
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    // There is no model in hand for the whole of a track's fetch. Asserting that this trip has no
    // survey drawing — inside somebody's article, beside a strip still saying the trip is being
    // read — is a statement the frame has no grounds for.
    expect(screen.queryByText(/no survey drawing/i)).toBeNull();
    expect(screen.getByTestId('public-past-track-loading')).toBeTruthy();
    // And the signal that costs no height is still on the frame a reader scrolled the strip out of.
    expect(screen.getByTestId('public-trip-embed').className).toContain('embed-past');
  });

  /**
   * Not even for one render.
   *
   * The address the viewer is handed is held still across re-signings, and the frame reads its
   * absence as "this trip has no drawing". Settled a render late, that hold was empty for the whole
   * of the render in which an address first arrived — so the sentence was committed into the
   * document over a trip that has a drawing, and replaced a moment later. Whether a browser paints
   * that frame is up to its scheduling; that the document held it is not, and it is what is
   * checked: every node the frame ever put in, including the ones taken out again.
   */
  describe('never commits the no-drawing sentence over a trip that has a drawing', () => {
    const everSaidNoDrawing = (records: MutationRecord[]) =>
      records.some((record) =>
        [...Array.from(record.addedNodes), ...Array.from(record.removedNodes)].some((node) =>
          /no survey drawing/i.test(node.textContent ?? ''),
        ),
      );

    it('as the frame first draws the live trip', () => {
      const observer = new MutationObserver(() => {});
      observer.observe(document.body, { childList: true, subtree: true });
      render(<PublicTripEmbedPage />);
      const records = observer.takeRecords();
      observer.disconnect();

      expect(everSaidNoDrawing(records)).toBe(false);
      expect(screen.getByTestId('viewer')).toBeTruthy();
    });

    it('as a past trip with its own survey lands', () => {
      const track = pastTrack();
      trackAnswer = {
        data: { ...track, model: { ...model, modelUrl: '/api/v1/files/old/content?token=first' } },
        isPending: false,
        isError: false,
      };
      render(<PublicTripEmbedPage />);
      fireEvent.click(screen.getByTestId('public-past-open'));

      const observer = new MutationObserver(() => {});
      observer.observe(document.body, { childList: true, subtree: true });
      fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));
      const records = observer.takeRecords();
      observer.disconnect();

      expect(everSaidNoDrawing(records)).toBe(false);
      expect(given?.fileUrl).toBe('/api/v1/files/old/content?token=first');
    });
  });

  it('says nothing about a missing drawing when the chosen trip could not be read at all', () => {
    trackAnswer = { data: undefined, isPending: false, isError: true };
    render(<PublicTripEmbedPage />);
    fireEvent.click(screen.getByTestId('public-past-open'));
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    // Permanent, where the case above is momentary: nothing later replaces the false sentence.
    expect(screen.queryByText(/no survey drawing/i)).toBeNull();
    expect(screen.getByTestId('public-past-track-failed')).toBeTruthy();
  });

  it('still says a trip has no drawing when the trip on screen really has none', () => {
    // The twin of the two above, and the reason that branch exists: an empty box on somebody's
    // website reads as a broken embed.
    live = { data: envelope({ model: null }), isPending: false, error: null };
    render(<PublicTripEmbedPage />);

    expect(screen.getByTestId('public-trip-embed-failure')).toHaveTextContent(/no survey drawing/i);
  });

  it('marks the whole frame while the past is on screen, at no cost in height', () => {
    render(<PublicTripEmbedPage />);
    expect(screen.getByTestId('public-trip-embed').className).not.toContain('embed-past');

    fireEvent.click(screen.getByTestId('public-past-open'));
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));
    // A reader who scrolled the strip out of view still cannot mistake a past trip for a live one.
    expect(screen.getByTestId('public-trip-embed').className).toContain('embed-past');
  });
});

/**
 * The frame's own buttons, sized on the pointer as every control on the strip beside them is.
 *
 * The strip that plays a past trip grows its controls for a finger; the button in its place while
 * the live trip is on screen did not, so on a phone the one way into the archive was a
 * twenty-four-pixel target — and so was the one way to try again when the trip did not arrive.
 */
describe('the frame’s buttons under a finger', () => {
  it('grows the way into the archive for a finger', () => {
    coarse = true;
    render(<PublicTripEmbedPage />);

    expect(screen.getByTestId('public-past-open').className).toContain('ant-btn-lg');
  });

  it('keeps it at the mouse size under a mouse', () => {
    render(<PublicTripEmbedPage />);

    expect(screen.getByTestId('public-past-open').className).toContain('ant-btn-sm');
  });

  it('grows the way to try again for a finger, when the trip did not arrive', () => {
    coarse = true;
    live = { data: undefined, isPending: false, error: new TypeError('Failed to fetch') };
    render(<PublicTripEmbedPage />);

    expect(screen.getByTestId('public-trip-retry').className).toContain('ant-btn-lg');
  });
});

describe('an article driving the cave’s past through the frame', () => {
  it('opens the past trip a link named', () => {
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);

    deliver(parent, 'https://club.example.org', focus('trip', TRIP_2019));

    expect(trackReads).toContain(TRIP_2019);
    expect(screen.getByTestId('public-past-banner-what')).toHaveTextContent('the 2019 push');
    expect(focused(sent).at(-1)).toMatchObject({ target: { kind: 'trip' }, found: true });
  });

  it('follows a team named beside a trip, in one press', () => {
    const { parent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);

    deliver(parent, 'https://club.example.org', focus('team', TEAM_B, { trip: TRIP_2019 }));

    expect(screen.getByTestId('public-past-banner-following')).toHaveTextContent('Survey');
    // And the camera goes where that team was, rather than being left at the model's opening view.
    expect(given?.focusRequest).toEqual({ kind: 'station', ref: 'far.end.2' });
  });

  it('leaves the reader on the sheet they opened while the followed party moves', async () => {
    // The sheets are handed the same followed station the 3D scene is, so whichever pane is open
    // is the one following. A frame that re-selected the 3D pane at each report of the followed
    // party would make the scanned maps unreachable for the whole of a followed replay — the one
    // path that threading could never be seen on.
    const source = pastTrack();
    trackAnswer = {
      data: {
        ...source,
        model: {
          ...model,
          rasterMaps: [
            {
              title: 'Plan sheet',
              viewKind: 'plan',
              imageUrl: '/api/v1/files/sheet-1/thumbnail?size=1200&token=sig',
              points: [{ station: 'far.end.2', x: 0.25, y: 0.75 }],
            },
          ],
        },
        participants: source.participants.map((person) =>
          person.ordinal !== 2
            ? person
            : {
                ...person,
                track: [
                  ...person.track,
                  {
                    recordedAt: '2019-07-06T11:00:00Z',
                    teamId: TEAM_B,
                    stationName: 'far.end.9',
                    depthM: null,
                    positionOnOtherModel: false,
                    in: true,
                    out: false,
                  },
                ],
              },
        ),
      },
      isPending: false,
      isError: false,
    };
    const { parent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);
    deliver(parent, 'https://club.example.org', focus('team', TEAM_B, { trip: TRIP_2019 }));

    act(() => {
      screen.getByRole('tab', { name: /Plan sheet/ }).click();
    });
    expect(await screen.findByTestId('sheet-pane')).toHaveAttribute('data-active', 'true');

    deliver(parent, 'https://club.example.org', focus('moment', '2019-07-06T11:30:00Z'));

    expect(screen.getByTestId('sheet-pane')).toHaveAttribute('data-active', 'true');
    // And it is following: the sheet is told where the team went, on the pane the reader is on.
    expect(sheetPane?.followStation).toBe('far.end.9');
  });

  it('takes an article’s own way back out of the past', () => {
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);
    deliver(parent, 'https://club.example.org', focus('trip', TRIP_2019));

    deliver(parent, 'https://club.example.org', focus('trip', 'live'));

    expect(screen.queryByTestId('public-past-banner')).toBeNull();
    expect(((given?.trackedCavers ?? []) as { name: string }[]).map((caver) => caver.name)).toEqual([
      'Ana',
    ]);
    expect(focused(sent).at(-1)).toMatchObject({ found: true });
  });

  it('takes a follow’s camera request out with the trip it was made on', () => {
    // The follow's request was only ever set, so the way back handed the viewer the live survey
    // together with a station of the 2019 push, to be flown to as soon as that survey was up.
    const { parent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);
    deliver(parent, 'https://club.example.org', focus('team', TEAM_B, { trip: TRIP_2019 }));
    expect(given?.focusRequest).toEqual({ kind: 'station', ref: 'far.end.2' });

    deliver(parent, 'https://club.example.org', focus('trip', 'live'));

    expect(given?.focusRequest).toBeUndefined();
  });

  it('answers a link about a place exactly once, however many trips follow it', () => {
    // A request a link made carries the article's callback. Left standing across a trip change it
    // was performed again on the next survey and the article was told `focused` a second time, for
    // a link nobody had just pressed, about a drawing the link was never written against.
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);
    deliver(parent, 'https://club.example.org', focus('station', 'today.1'));
    expect(given?.focusRequest).toMatchObject({ kind: 'station', ref: 'today.1' });
    const asked = focused(sent).length;

    deliver(parent, 'https://club.example.org', focus('trip', TRIP_2019));
    expect(given?.focusRequest).toBeUndefined();

    deliver(parent, 'https://club.example.org', focus('trip', 'live'));
    expect(given?.focusRequest).toBeUndefined();
    // The two trip links were each answered; the station link was not answered again.
    expect(focused(sent).length).toBe(asked + 2);
    expect(focused(sent).slice(asked).every((posted) => (posted.target as { kind: string }).kind === 'trip')).toBe(true);
  });

  it('says which trip every announced party belongs to', () => {
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);

    // While the live trip is on screen there is no `past` member at all, which is exactly what
    // every announcement meant before the archive existed — an article written against the older
    // vocabulary goes on reading the party as it did.
    expect(readies(sent).at(-1)?.past).toBeUndefined();

    deliver(parent, 'https://club.example.org', focus('trip', TRIP_2019));

    const announced = readies(sent).at(-1);
    expect(announced?.past).toMatchObject({
      tripLogId: TRIP_2019,
      title: 'Peștera Demo Mare, the 2019 push',
    });
    expect(((announced?.party ?? []) as { name: string }[]).map((person) => person.name)).toEqual([
      'Mircea',
      'Ileana',
    ]);
  });

  it('refuses a moment over a live trip rather than pretending to honour it', () => {
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);

    deliver(parent, 'https://club.example.org', focus('moment', '2026-09-14T09:00:00Z'));

    // There is no clock to move on a live trip, and `found: false` is what lets an article grey
    // such a link out instead of offering one that does nothing.
    expect(focused(sent).at(-1)).toMatchObject({ target: { kind: 'moment' }, found: false });
  });

  it('winds the clock of the trip already playing', () => {
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);
    deliver(parent, 'https://club.example.org', focus('trip', TRIP_2019));

    deliver(parent, 'https://club.example.org', focus('moment', '2019-07-06T09:30:00Z'));

    expect(focused(sent).at(-1)).toMatchObject({ target: { kind: 'moment' }, found: true });
    // At half past nine only the first report has been made: the second person is listed and
    // unplaced, never standing where they were reported half an hour later.
    const drawn = (given?.trackedCavers ?? []) as { name: string; position: { kind: string } }[];
    expect(drawn.map((caver) => caver.position.kind)).toEqual(['station', 'unreported']);
  });

  it('holds a moment pressed while the trip playing is still being read, and winds to it when it lands', () => {
    // The press arrives before the response it belongs to. Refused then, an article that greys a
    // link out on `found: false` disabled one that would have worked a second later.
    trackAnswer = { data: undefined, isPending: true, isError: false };
    const { parent, sent } = fakeParent();
    const { rerender } = render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);
    deliver(parent, 'https://club.example.org', focus('trip', TRIP_2019));

    deliver(parent, 'https://club.example.org', focus('moment', '2019-07-06T09:30:00Z'));

    expect(focused(sent).at(-1)).toMatchObject({ target: { kind: 'moment' }, found: true });

    trackAnswer = { data: pastTrack(), isPending: false, isError: false };
    rerender(<PublicTripEmbedPage />);

    // Half past nine, as asked: the first report made and the second not yet.
    const drawn = (given?.trackedCavers ?? []) as { name: string; position: { kind: string } }[];
    expect(drawn.map((caver) => caver.position.kind)).toEqual(['station', 'unreported']);
  });

  it('moves the clock to a caver named beside the trip already open, as it does without the trip', () => {
    // Two spellings of one link: an article names a caver with the trip beside it, or the caver
    // alone once the trip is open. The replay stands at its start, before this person was placed
    // anywhere, so following them there is a camera with nothing to aim at — both spellings move
    // the clock to where they first appear, or one of them answers `found` and does nothing.
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);
    deliver(parent, 'https://club.example.org', focus('trip', TRIP_2019));
    const before = (given?.trackedCavers ?? []) as { position: { kind: string } }[];
    expect(before.map((caver) => caver.position.kind)).toEqual(['unreported', 'unreported']);

    deliver(parent, 'https://club.example.org', focus('caver', '2', { trip: TRIP_2019 }));

    expect(focused(sent).at(-1)).toMatchObject({ target: { kind: 'caver' }, found: true });
    // Ten o'clock, where Ileana was first reported — and Mircea, reported at nine, placed too.
    const drawn = (given?.trackedCavers ?? []) as { position: { kind: string } }[];
    expect(drawn.map((caver) => caver.position.kind)).toEqual(['station', 'station']);
    expect(given?.focusRequest).toEqual({ kind: 'station', ref: 'far.end.2' });
  });

  it('resolves a place in the party against the trip on screen, not against today’s', () => {
    const { parent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);
    deliver(parent, 'https://club.example.org', focus('trip', TRIP_2019));

    deliver(parent, 'https://club.example.org', focus('caver', '1'));

    // Reading it off the live trip would fly the camera to a station somebody is standing at today
    // under the name of somebody from six years ago.
    expect(given?.focusRequest).toMatchObject({ kind: 'station', ref: 'p.g.7' });
  });

  it('flies to a station named beside a trip, in the same one press', () => {
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);

    // The sentence the snippet a club is handed writes out for them: "the sump on the 2019 push".
    deliver(
      parent,
      'https://club.example.org',
      focus('station', 'far.end.2', { trip: TRIP_2019 }),
    );

    expect(trackReads).toContain(TRIP_2019);
    expect(given?.focusRequest).toMatchObject({ kind: 'station', ref: 'far.end.2' });
    // And the article is told nothing yet: whether that survey holds the station is the drawing's
    // answer, and it has not given one. An eager `found: true` here is a link the host page cannot
    // grey out however wrong it turns out to be.
    expect(focused(sent)).toEqual([]);
  });

  it('flies to a survey named beside a trip too', () => {
    const { parent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);

    deliver(
      parent,
      'https://club.example.org',
      focus('survey', 'galeria-nord', { trip: TRIP_2019 }),
    );

    expect(given?.focusRequest).toMatchObject({ kind: 'survey', ref: 'galeria-nord' });
  });

  it('refuses a place named beside a trip that cannot be read', () => {
    trackAnswer = { data: undefined, isPending: false, isError: true };
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);

    deliver(
      parent,
      'https://club.example.org',
      focus('station', 'far.end.2', { trip: TRIP_2019 }),
    );

    // The twin of the two above. There will never be a drawing to answer with, and an article left
    // waiting for a `focused` that never comes cannot tell that from a link still in flight.
    expect(focused(sent).at(-1)).toMatchObject({ target: { kind: 'station' }, found: false });
  });

  it('answers a team named on the live trip as a place, not as a follow nothing can call off', () => {
    live = {
      data: envelope({
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
      }),
      isPending: false,
      error: null,
    };
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);

    deliver(parent, 'https://club.example.org', focus('team', TEAM_A));

    // There is no clock on the live trip, so a team is a place — answered through the derivation
    // that picks which member speaks for a team on a replay, so the two readings agree. Kept a
    // follow, it would re-aim the camera at every position report with no strip on screen to stop
    // it: the strip carrying the stop-following control is only drawn over the past.
    expect(given?.focusRequest).toMatchObject({ kind: 'station', ref: 'today.1' });
    expect(screen.queryByTestId('public-past-banner')).toBeNull();
    expect(focused(sent)).toEqual([]);
  });

  it('does not re-announce the party into the article five times a second while a replay plays', () => {
    vi.useFakeTimers();
    try {
      const { parent, sent } = fakeParent();
      render(<PublicTripEmbedPage />);
      deliver(parent, 'https://club.example.org', hello);
      deliver(parent, 'https://club.example.org', focus('trip', TRIP_2019));
      const announcements = readies(sent).length;
      const opening = screen.getByTestId('public-past-clock').textContent;

      act(() => {
        screen.getByTestId('public-past-play').click();
      });
      act(() => {
        vi.advanceTimersByTime(1000);
      });

      // The clock ran — five ticks of it — and nobody reported anything in that minute of 2019.
      expect(screen.getByTestId('public-past-clock').textContent).not.toBe(opening);
      // So the article, whose handler prints where people are, hears nothing. Compared on the
      // moment instead, the whole party would be posted into somebody else's page 300 times a
      // minute, on a phone, for a party that has not moved.
      expect(readies(sent).length).toBe(announcements);

      // The twin: when somebody does move, it is said at once.
      deliver(parent, 'https://club.example.org', focus('moment', '2019-07-06T09:30:00Z'));
      expect(readies(sent).length).toBeGreaterThan(announcements);
      expect(readies(sent).at(-1)?.past).toMatchObject({ tripLogId: TRIP_2019 });
    } finally {
      vi.useRealTimers();
    }
  });

  it('says a team nobody has placed is nowhere, rather than claiming it was found', () => {
    live = {
      data: envelope({
        teams: [{ id: TEAM_A, title: 'Advance' }],
        participants: [
          {
            ordinal: 1,
            label: 'Ana',
            teamId: TEAM_A,
            stationName: null,
            depthM: null,
            lastRecordedAt: '2026-09-14T09:00:00Z',
            positionRecordedAt: null,
            positionOnOtherModel: false,
            in: true,
            out: false,
          },
        ],
      }),
      isPending: false,
      error: null,
    };
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);

    deliver(parent, 'https://club.example.org', focus('team', TEAM_A));

    expect(focused(sent).at(-1)).toMatchObject({ target: { kind: 'team' }, found: false });
  });
});
