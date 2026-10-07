// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen, within } from '@testing-library/react';
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
let trackAnswer: { data?: PublicPastTrack; isPending: boolean; isError: boolean; error?: unknown };
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
let address = new URLSearchParams();
const setAddress = vi.fn();
/** The link the frame is opened under, which a test can change under a frame already drawn. */
let linkToken = 'follow-token';
vi.mock('react-router-dom', () => ({
  useParams: () => ({ token: linkToken }),
  useSearchParams: () => [address, setAddress],
}));
// Whether a finger is driving the frame. False by default — the desk this suite is read on.
let coarse = false;
vi.mock('../../hooks/useCoarsePointer.ts', () => ({ useCoarsePointer: () => coarse }));
// Whether the frame has room for a past trip's whole strip. False by default: the frame this
// suite is mostly about is the 260px one in a phone's article, which keeps one line.
let roomy = false;
vi.mock('./publicEmbedRoom.ts', () => ({ useRoomyFrame: () => roomy }));

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
  roomy = false;
  address = new URLSearchParams();
  linkToken = 'follow-token';
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

    // On the frame's one line: that it is the past, and which trip.
    expect(screen.getByTestId('public-past-banner')).toHaveTextContent('Past trip');
    expect(screen.getByTestId('public-past-banner')).toHaveTextContent('the 2019 push');
    expect(((given?.trackedCavers ?? []) as { name: string }[]).map((caver) => caver.name)).toEqual(
      ['Mircea', 'Ileana'],
    );
  });

  it('keeps one line while a past trip plays, and the rest of the transport in the sheet behind it', () => {
    render(<PublicTripEmbedPage />);
    fireEvent.click(screen.getByTestId('public-past-open'));
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));

    // The line: the clock, play and the way back — one of each in the whole frame.
    const line = within(screen.getByTestId('public-past-bar'));
    for (const id of ['public-past-clock', 'public-past-play', 'public-past-back']) {
      expect(line.getByTestId(id)).toBeInTheDocument();
      expect(screen.getAllByTestId(id)).toHaveLength(1);
    }
    for (const id of [
      'public-past-speed',
      'public-past-report-next',
      'public-past-follow',
      'public-past-scrub',
    ]) {
      expect(line.queryByTestId(id)).toBeNull();
    }

    // The sheet, opened from the line's last button: named for what it now holds, with the rest
    // of the transport above the lists — so another trip can be picked without leaving this one.
    fireEvent.click(screen.getByTestId('public-past-controls-open'));
    const sheet = within(screen.getByTestId('public-past-drawer'));
    expect(
      screen.getByText('Replay controls and other trips', { selector: '.ant-drawer-title' }),
    ).toBeInTheDocument();
    for (const id of [
      'public-past-speed',
      'public-past-report-previous',
      'public-past-report-next',
      'public-past-follow',
      'public-past-scrub',
    ]) {
      expect(sheet.getByTestId(id)).toBeInTheDocument();
      expect(screen.getAllByTestId(id)).toHaveLength(1);
    }
    expect(sheet.getByTestId('public-past-statement-what')).toHaveTextContent('the 2019 push');
    // The cave's other trips are under the controls, behind a press of their own.
    expect(sheet.queryByTestId(`public-past-trip-${TRIP_2019}`)).toBeNull();
    fireEvent.click(sheet.getByTestId('public-past-lists-open'));
    expect(sheet.getByTestId(`public-past-trip-${TRIP_2019}`)).toBeInTheDocument();
    // The controls are still above them: the sheet grew, it did not change into something else.
    expect(sheet.getByTestId('public-past-scrub')).toBeInTheDocument();
    expect(sheet.queryByTestId('public-past-lists-open')).toBeNull();
  });

  it('reads neither of the cave’s lists for a reader who opens the sheet to move the replay', () => {
    // The rail, the speed, the steps and whom to follow are controls of the trip already in hand.
    // Reaching for one must not ask the server who else is underground, nor list them unasked.
    const { parent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);
    deliver(parent, 'https://club.example.org', focus('trip', TRIP_2019));
    expect(liveReads).toBe(0);
    expect(listReads).toBe(0);

    fireEvent.click(screen.getByTestId('public-past-controls-open'));
    const sheet = within(screen.getByTestId('public-past-drawer'));
    fireEvent.click(sheet.getByTestId('public-past-report-next'));
    fireEvent.click(sheet.getByTestId('public-past-report-previous'));

    expect(liveReads).toBe(0);
    expect(listReads).toBe(0);
    expect(sheet.queryByTestId('public-live')).toBeNull();
    expect(sheet.queryByTestId('public-past-list')).toBeNull();

    // Both are read on the press that asks for them, and not before.
    fireEvent.click(sheet.getByTestId('public-past-lists-open'));
    expect(liveReads).toBeGreaterThan(0);
    expect(listReads).toBeGreaterThan(0);
  });

  it('shuts a sheet opened for the controls when the replay it controlled is left, rather than turning it into the lists', () => {
    const { parent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);
    deliver(parent, 'https://club.example.org', focus('trip', TRIP_2019));
    fireEvent.click(screen.getByTestId('public-past-controls-open'));
    expect(screen.getByTestId('public-past-sheet')).toBeInTheDocument();

    // The article's own way back, pressed while the sheet stands open.
    deliver(parent, 'https://club.example.org', focus('trip', 'live'));

    expect(screen.queryByTestId('public-past-sheet')).toBeNull();
    expect(liveReads).toBe(0);
    expect(listReads).toBe(0);
  });

  it('offers no replay controls in the sheet while the party of now is on screen', () => {
    render(<PublicTripEmbedPage />);
    fireEvent.click(screen.getByTestId('public-past-open'));

    expect(
      screen.getByText('Past trips in this cave', { selector: '.ant-drawer-title' }),
    ).toBeInTheDocument();
    expect(screen.queryByTestId('public-past-sheet')).toBeNull();
    expect(screen.queryByTestId('public-past-scrub')).toBeNull();
    expect(screen.queryByTestId('public-past-controls-open')).toBeNull();
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

describe('a frame whose own address names a moment of a past trip', () => {
  // Read off the rail, which holds the moment to the millisecond — and stands in the frame's
  // sheet, so the sheet is opened for it the first time it is asked.
  const clockShows = (iso: string) => {
    if (screen.queryByTestId('public-past-scrub') === null) {
      fireEvent.click(screen.getByTestId('public-past-controls-open'));
    }
    expect(screen.getByTestId('public-past-scrub').querySelector('[role="slider"]')).toHaveAttribute(
      'aria-valuenow',
      String(Date.parse(iso)),
    );
  };

  it('opens that trip at that moment and sets it playing, as the full page does', () => {
    address = new URLSearchParams(`past=${TRIP_2019}&at=2019-07-06T09:30:00Z&play=1`);
    render(<PublicTripEmbedPage />);

    expect(trackReads).toContain(TRIP_2019);
    expect(screen.getByTestId('public-past-banner-what')).toHaveTextContent('the 2019 push');
    clockShows('2019-07-06T09:30:00Z');
    expect(screen.getByTestId('public-past-play')).toHaveAccessibleName('Pause');
  });

  it('opens it standing still when the address does not say play', () => {
    address = new URLSearchParams(`past=${TRIP_2019}&at=2019-07-06T09:30:00Z`);
    render(<PublicTripEmbedPage />);

    clockShows('2019-07-06T09:30:00Z');
    expect(screen.getByTestId('public-past-play')).toHaveAccessibleName('Play');
  });

  it('is not sent back to it because the frame’s language changed in the same address', () => {
    address = new URLSearchParams(`past=${TRIP_2019}&at=2019-07-06T09:30:00Z&play=1`);
    const { rerender } = render(<PublicTripEmbedPage />);
    fireEvent.click(screen.getByTestId('public-past-back'));
    expect(screen.queryByTestId('public-past-banner-what')).toBeNull();

    address = new URLSearchParams(`past=${TRIP_2019}&at=2019-07-06T09:30:00Z&play=1&lang=en`);
    rerender(<PublicTripEmbedPage />);

    expect(screen.queryByTestId('public-past-banner-what')).toBeNull();
  });

  it('reads no track for an address that names no past trip', () => {
    address = new URLSearchParams('lang=en&at=2019-07-06T09:30:00Z&play=1');
    render(<PublicTripEmbedPage />);

    expect(trackReads.filter((read) => read !== undefined)).toEqual([]);
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

    // Who is being followed is said in the sheet, behind the frame's one line.
    fireEvent.click(screen.getByTestId('public-past-controls-open'));
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

  it('opens a trip at a moment and sets it playing, when the link says play', () => {
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);

    deliver(
      parent,
      'https://club.example.org',
      focus('moment', '2019-07-06T09:30:00Z', { trip: TRIP_2019, play: true }),
    );

    expect(focused(sent).at(-1)).toMatchObject({ target: { kind: 'moment' }, found: true });
    // The button offers the opposite of what the clock is doing.
    expect(screen.getByTestId('public-past-play')).toHaveAccessibleName('Pause');
  });

  it('opens the same link standing still without the word, as every block already pasted sends it', () => {
    // The twin of the case above, and the promise to older blocks: a message with no `play`, and
    // one carrying something that only looks like it, open the trip exactly as before.
    const { parent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);

    deliver(
      parent,
      'https://club.example.org',
      focus('moment', '2019-07-06T09:30:00Z', { trip: TRIP_2019 }),
    );
    expect(screen.getByTestId('public-past-play')).toHaveAccessibleName('Play');

    deliver(
      parent,
      'https://club.example.org',
      focus('moment', '2019-07-06T09:40:00Z', { trip: TRIP_2019, play: 'yes' }),
    );
    expect(screen.getByTestId('public-past-play')).toHaveAccessibleName('Play');
  });

  it('starts the trip already playing from a moment named with play, and a second press leaves it playing', () => {
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);
    deliver(parent, 'https://club.example.org', focus('trip', TRIP_2019));
    expect(screen.getByTestId('public-past-play')).toHaveAccessibleName('Play');

    deliver(
      parent,
      'https://club.example.org',
      focus('moment', '2019-07-06T09:30:00Z', { play: true }),
    );

    expect(focused(sent).at(-1)).toMatchObject({ target: { kind: 'moment' }, found: true });
    expect(screen.getByTestId('public-past-play')).toHaveAccessibleName('Pause');

    // Pressed again, the link must not behave as the button does: that would pause it.
    deliver(
      parent,
      'https://club.example.org',
      focus('moment', '2019-07-06T09:30:00Z', { play: true }),
    );
    expect(screen.getByTestId('public-past-play')).toHaveAccessibleName('Pause');
  });

  it('holds a request to play made while the trip is still being read, and honours it when it lands', () => {
    trackAnswer = { data: undefined, isPending: true, isError: false };
    const { parent } = fakeParent();
    const { rerender } = render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);
    deliver(parent, 'https://club.example.org', focus('trip', TRIP_2019));

    deliver(
      parent,
      'https://club.example.org',
      focus('moment', '2019-07-06T09:30:00Z', { play: true }),
    );
    expect(screen.queryByTestId('public-past-play')).toBeNull();

    trackAnswer = { data: pastTrack(), isPending: false, isError: false };
    rerender(<PublicTripEmbedPage />);

    expect(screen.getByTestId('public-past-play')).toHaveAccessibleName('Pause');
  });

  it('leaves the word alone over the trip being followed now, where there is no clock to start', () => {
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);

    deliver(
      parent,
      'https://club.example.org',
      focus('moment', '2026-09-14T09:00:00Z', { play: true }),
    );

    // Refused for the moment, exactly as it is without the word — and no replay was opened by it.
    expect(focused(sent).at(-1)).toMatchObject({ target: { kind: 'moment' }, found: false });
    expect(screen.queryByTestId('public-past-bar')).toBeNull();
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

  it('listens to its article through one subscription, however far the replay’s clock moves', () => {
    const subscribed = vi.spyOn(window, 'addEventListener');
    try {
      const { parent, sent } = fakeParent();
      render(<PublicTripEmbedPage />);
      const listeners = () => subscribed.mock.calls.filter(([type]) => type === 'message').length;
      deliver(parent, 'https://club.example.org', hello);
      deliver(parent, 'https://club.example.org', focus('trip', TRIP_2019));
      expect(listeners()).toBe(1);
      const answersBefore = focused(sent).length;

      // Ten moments across the trip, the party at each being the party of that moment: nobody
      // placed before nine, Mircea from nine, Ileana from ten. Every one of them is a new view, and
      // a listener that closed over the view was taken down and stood up for each.
      for (let step = 0; step < 10; step++) {
        const moment = new Date(Date.parse('2019-07-06T08:15:00Z') + step * 20 * 60_000).toISOString();
        deliver(parent, 'https://club.example.org', focus('moment', moment));
      }

      // Every one of the ten was heard and answered, and the party did change under the listener:
      // the last thing the article was told is the seventh of them, where Ileana joins Mircea on
      // the drawing —
      expect(focused(sent).length).toBe(answersBefore + 10);
      expect(readies(sent).at(-1)?.past).toMatchObject({ at: '2019-07-06T10:15:00.000Z' });
      expect(
        ((readies(sent).at(-1)?.party ?? []) as { station: string | null }[]).map(
          (person) => person.station,
        ),
      ).toEqual(['p.g.7', 'far.end.2']);
      // — by the listener that was there at the start.
      expect(listeners()).toBe(1);
    } finally {
      subscribed.mockRestore();
    }
  });

  it('answers from the party as it stands now, though the listener is the one subscribed at the start', () => {
    // The other half of subscribing once: what the one listener decides against must not be the
    // party of the render it was subscribed in.
    const { parent } = fakeParent();
    const { rerender } = render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);
    deliver(parent, 'https://club.example.org', focus('caver', '1'));
    expect(given?.focusRequest).toMatchObject({ kind: 'station', ref: 'today.1' });

    // A minute's read moves Ana.
    const moved = envelope();
    moved.participants[0].stationName = 'today.2';
    live = { data: moved, isPending: false, error: null };
    rerender(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', focus('caver', '1'));

    expect(given?.focusRequest).toMatchObject({ kind: 'station', ref: 'today.2' });
  });

  it('tells the article that the server refused a past trip, once, by its id and beside a `past` left absent', () => {
    trackAnswer = {
      data: undefined,
      isPending: false,
      isError: true,
      error: new ApiError(404, 'trip_tracking.share.not_found'),
    };
    const { parent, sent } = fakeParent();
    const { rerender } = render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);
    // The party now, loaded, and no word of the past: what the article starts from.
    expect(readies(sent).at(-1)).toMatchObject({ loaded: true });
    expect(readies(sent).at(-1)?.past).toBeUndefined();
    expect(readies(sent).at(-1)?.pastUnreadable).toBeUndefined();

    deliver(parent, 'https://club.example.org', focus('trip', TRIP_2019));

    // Nothing is on screen, so nothing is loaded and nobody is announced — which alone is word for
    // word what a frame still waiting says. The member is what tells the two apart.
    const announced = readies(sent).at(-1);
    expect(announced?.pastUnreadable).toEqual({ tripLogId: TRIP_2019 });
    expect(announced?.loaded).toBe(false);
    expect(announced?.party).toEqual([]);
    // `past` means a replay with a name and a moment, and a listener pasted years ago reads both
    // off it without asking: for a trip there is nothing of, it stays absent as it always was.
    expect(announced?.past).toBeUndefined();

    // Said once, not on every render that follows.
    rerender(<PublicTripEmbedPage />);
    rerender(<PublicTripEmbedPage />);
    const refusals = () => readies(sent).filter((said) => said.pastUnreadable !== undefined);
    expect(refusals()).toHaveLength(1);

    // And gone from the next announcement once the frame is back on the party now.
    deliver(parent, 'https://club.example.org', focus('trip', 'live'));
    expect(readies(sent).at(-1)).toMatchObject({ loaded: true });
    expect(readies(sent).at(-1)?.past).toBeUndefined();
    expect(readies(sent).at(-1)?.pastUnreadable).toBeUndefined();
    expect(refusals()).toHaveLength(1);
  });

  it('does not tell the article a trip cannot be played because a read did not land', () => {
    // A phone that lost its signal as the link was pressed, a server fault, a request asked to
    // wait: the frame has learned nothing about the trip and reads again by itself when its
    // reader returns. Told "cannot be played" on that evidence, the article would print it beside
    // a frame that then plays the trip.
    for (const error of [
      new ApiError(503, 'unavailable'),
      new ApiError(429, 'rate_limited'),
      new TypeError('Failed to fetch'),
    ]) {
      trackAnswer = { data: undefined, isPending: false, isError: true, error };
      const { parent, sent } = fakeParent();
      const { rerender, unmount } = render(<PublicTripEmbedPage />);
      deliver(parent, 'https://club.example.org', hello);
      deliver(parent, 'https://club.example.org', focus('trip', TRIP_2019));

      // What a frame still waiting says, and nothing more — while the frame's own line tells the
      // reader in front of it that the trip could not be read.
      const announced = readies(sent).at(-1);
      expect(announced?.loaded).toBe(false);
      expect(announced?.past).toBeUndefined();
      expect(announced?.pastUnreadable).toBeUndefined();
      expect(screen.getByTestId('public-past-track-failed')).toBeInTheDocument();

      // The read made again on the reader's return lands, and the trip is announced as any other.
      trackAnswer = { data: pastTrack(), isPending: false, isError: false };
      rerender(<PublicTripEmbedPage />);
      expect(readies(sent).at(-1)).toMatchObject({ loaded: true, past: { tripLogId: TRIP_2019 } });
      expect(readies(sent).filter((said) => said.pastUnreadable !== undefined)).toHaveLength(0);
      unmount();
    }
  });

  it('says nothing of the kind about a past trip that was read', () => {
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);

    deliver(parent, 'https://club.example.org', focus('trip', TRIP_2019));

    const announced = readies(sent).at(-1);
    expect(announced?.loaded).toBe(true);
    expect(announced?.past).toMatchObject({ tripLogId: TRIP_2019 });
    expect(announced?.pastUnreadable).toBeUndefined();
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

describe('a second published link opened in the same frame', () => {
  it('does not show the first link’s replay, and stops announcing it', () => {
    const { parent, sent } = fakeParent();
    const { rerender } = render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);
    fireEvent.click(screen.getByTestId('public-past-open'));
    fireEvent.click(screen.getByTestId(`public-past-trip-${TRIP_2019}`));
    // The replay that must not be carried over really is on screen under the first link.
    expect(screen.getByTestId('public-trip-embed').className).toContain('embed-past');
    expect(readies(sent).at(-1)?.past).toMatchObject({ tripLogId: TRIP_2019 });

    linkToken = 'second-link';
    rerender(<PublicTripEmbedPage />);

    expect(screen.getByTestId('public-trip-embed').className).not.toContain('embed-past');
    expect(screen.queryByTestId('public-past-bar')).toBeNull();
    expect(screen.getByTestId('public-past-open')).toBeTruthy();
    expect(((given?.trackedCavers ?? []) as { name: string }[]).map((caver) => caver.name)).toEqual([
      'Ana',
    ]);
    expect(readies(sent).at(-1)?.past).toBeUndefined();
  });

  it('opens the past trip its own address names, at that address’s moment', () => {
    address = new URLSearchParams(`past=${TRIP_2019}&at=2019-07-06T09:30:00Z`);
    const { parent, sent } = fakeParent();
    const { rerender } = render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);
    // The first link's reader — or its article — winds the clock on.
    deliver(parent, 'https://club.example.org', focus('moment', '2019-07-06T10:30:00Z'));
    expect(readies(sent).at(-1)?.past).toMatchObject({ at: '2019-07-06T10:30:00.000Z' });

    linkToken = 'second-link';
    rerender(<PublicTripEmbedPage />);

    expect(screen.getByTestId('public-trip-embed').className).toContain('embed-past');
    expect(readies(sent).at(-1)?.past).toMatchObject({
      tripLogId: TRIP_2019,
      at: '2019-07-06T09:30:00.000Z',
    });
  });
});

describe('a past trip in a frame with room for its whole strip', () => {
  beforeEach(() => {
    roomy = true;
  });

  it('keeps the rail, the speed, the steps and whom to follow on screen, with no sheet to open for them', () => {
    const { parent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);
    deliver(parent, 'https://club.example.org', focus('trip', TRIP_2019));

    const strip = within(screen.getByTestId('public-past-bar'));
    for (const id of [
      'public-past-play',
      'public-past-clock',
      'public-past-back',
      'public-past-speed',
      'public-past-report-previous',
      'public-past-report-next',
      'public-past-follow',
      'public-past-scrub',
    ]) {
      expect(strip.getByTestId(id), id).toBeInTheDocument();
      expect(screen.getAllByTestId(id), id).toHaveLength(1);
    }
    expect(screen.queryByTestId('public-past-controls-open')).toBeNull();
    expect(screen.queryByTestId('public-past-sheet')).toBeNull();
    expect(screen.getByTestId('public-trip-embed').className).toContain('embed-past');
    // Stepping from the strip asks the server for nothing.
    fireEvent.click(strip.getByTestId('public-past-report-next'));
    expect(liveReads).toBe(0);
    expect(listReads).toBe(0);
  });

  it('opens the cave’s lists from the strip, in a sheet that holds no second set of controls', () => {
    const { parent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);
    deliver(parent, 'https://club.example.org', focus('trip', TRIP_2019));

    fireEvent.click(screen.getByTestId('public-past-lists-open'));

    const sheet = within(screen.getByTestId('public-past-drawer'));
    expect(
      screen.getByText('Past trips in this cave', { selector: '.ant-drawer-title' }),
    ).toBeInTheDocument();
    expect(sheet.getByTestId(`public-past-trip-${TRIP_2019}`)).toBeInTheDocument();
    expect(sheet.queryByTestId('public-past-sheet')).toBeNull();
    expect(screen.getAllByTestId('public-past-scrub')).toHaveLength(1);
    expect(liveReads).toBeGreaterThan(0);
    expect(listReads).toBeGreaterThan(0);
  });

  it('becomes the one line, and the line the whole strip, as the frame’s box changes', () => {
    const { parent } = fakeParent();
    const { rerender } = render(<PublicTripEmbedPage />);
    deliver(parent, 'https://club.example.org', hello);
    deliver(parent, 'https://club.example.org', focus('trip', TRIP_2019));
    expect(screen.queryByTestId('public-past-controls-open')).toBeNull();

    // A phone turned upright under the article.
    roomy = false;
    rerender(<PublicTripEmbedPage />);
    expect(screen.getByTestId('public-past-controls-open')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('public-past-controls-open'));
    expect(screen.getByTestId('public-past-sheet')).toBeInTheDocument();

    // And turned back: the controls are on the strip again, and the sheet that held them is shut
    // rather than left open as a list nobody asked for.
    roomy = true;
    rerender(<PublicTripEmbedPage />);
    expect(screen.queryByTestId('public-past-sheet')).toBeNull();
    expect(screen.getAllByTestId('public-past-scrub')).toHaveLength(1);
    expect(liveReads).toBe(0);
    expect(listReads).toBe(0);
  });
});
