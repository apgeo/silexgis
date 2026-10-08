// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
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
import { EMBED_CHANNEL, EMBED_PROTOCOL } from './publicTripEmbed.ts';

/**
 * Another party of the cave, watched inside somebody else's article.
 *
 * <b>The frame has two audiences and both can be told something false.</b> Its reader, who pressed
 * a row and is now looking at markers that are not the party the article is about — so the frame's
 * one strip has to say whose they are, with the way back beside it. And the article itself, which
 * prints names and stations from what the frame announces under its own trip's heading — so every
 * announcement made while another party is on screen has to say that it is another party's, and
 * say so again when the reader moves to a third whose people happen to stand in the same places.
 */

const OWN = '0195f4a2-6c3e-7b10-9f21-ab44de77c001';
const OTHER = '0195f4a2-6c3e-7b10-9f21-ab44de77c002';
const THIRD = '0195f4a2-6c3e-7b10-9f21-ab44de77c003';
const TRIP_2019 = 'aaaaaaaa-0000-0000-0000-000000000001';
const HOST = 'https://club.example';

type ListAnswer = {
  data?: PublicLiveTripList;
  isPending: boolean;
  isError: boolean;
  error?: unknown;
  dataUpdatedAt?: number;
  /** When the last read that failed did so. */
  errorUpdatedAt?: number;
};

let live: {
  data?: PublicTripEnvelope;
  isPending: boolean;
  error: unknown;
  dataUpdatedAt?: number;
};
let liveList: ListAnswer;
let archive: {
  data?: PublicPastTripList;
  isPending: boolean;
  isError: boolean;
  error?: unknown;
  /** When the list of past trips in hand arrived. */
  dataUpdatedAt?: number;
};
/** Whether the list of past trips is being asked for at this moment. */
let pastAsked = false;
/** Every link the list of past trips was ever read under, which is what keeps it in hand. */
let pastReadUnder = new Set<string | undefined>();
/** Longer ago than a past list may have been read and still speak for the link as it is now. */
const LONG_AGO_MS = 10 * 60_000;
let trackAnswer: { data?: PublicPastTrack; isPending: boolean; isError: boolean };
/** Whether the frame is asking for the list of parties at this moment, and whether it ever did. */
let liveAsked = false;
let liveReads = 0;

vi.mock('../../api/hooks.ts', () => ({
  usePublicTrip: () => live,
  useResLinksForTarget: () => ({ data: undefined, isPending: false, error: null }),
  useSurveyModel: () => ({ data: undefined, isPending: false, error: null }),
  // As the real query answers: a list that was read stays in hand after it stops being asked
  // for, under the link it was read with. A fake that forgot it on the spot would let a test
  // stand in a state the application cannot reach.
  usePublicPastTrips: (token: string | undefined, enabled: boolean) => {
    pastAsked = enabled;
    if (enabled) {
      pastReadUnder.add(token);
    }
    return pastReadUnder.has(token) ? archive : { data: undefined, isPending: false, isError: false };
  },
  usePublicLiveTrips: (token: string | undefined, enabled: boolean) => {
    liveAsked = enabled;
    if (enabled) {
      liveReads++;
      liveAskedUnder.push(token);
    }
    // A read that failed always carries when it failed, as the real query's does.
    return enabled
      ? { ...liveList, errorUpdatedAt: liveList.errorUpdatedAt ?? (liveList.isError ? Date.now() : 0) }
      : { data: undefined, isPending: false, isError: false };
  },
  usePublicPastTrack: (_token: string | undefined, tripLogId: string | undefined) =>
    tripLogId === undefined ? { data: undefined, isPending: false, isError: false } : trackAnswer,
}));
// The frame reads its own address — for its language, and for a past trip named in it.
/** The link the frame is opened under, which a test can change under a frame already drawn. */
let linkToken = 'follow-token';
/** Every link the list of parties was asked for under, while it was being asked for. */
let liveAskedUnder: (string | undefined)[] = [];
vi.mock('react-router-dom', () => ({
  useParams: () => ({ token: linkToken }),
  useSearchParams: () => [new URLSearchParams(), vi.fn()],
}));
vi.mock('../../hooks/useCoarsePointer.ts', () => ({ useCoarsePointer: () => false }));
// The 260px frame of a phone's article, which keeps a past trip's strip to one line.
vi.mock('./publicEmbedRoom.ts', () => ({ useRoomyFrame: () => false }));

/** The viewer, faked at its contract: what it is handed is the whole of what the frame owes it. */
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
vi.mock('./PublicTripSheetPane.tsx', () => ({
  default: () => <div data-testid="sheet-pane" />,
}));

const { default: PublicTripEmbedPage } = await import('./PublicTripEmbedPage.tsx');

const MODEL = {
  format: 'survex3d' as const,
  modelUrl: '/api/v1/files/own/content?token=first',
  meshUrl: null,
  anchorLongitude: null,
  anchorLatitude: null,
  anchorHeightM: null,
  sourceEpsg: null,
  proj4: null,
  pictures: [],
  rasterMaps: [],
  places: [],
};

function person(
  ordinal: number,
  label: string,
  stationName: string | null,
  flags: { in: boolean; out: boolean } = { in: true, out: false },
) {
  return {
    ordinal,
    label,
    teamId: null,
    stationName,
    depthM: null,
    lastRecordedAt: '2026-09-14T09:00:00Z',
    positionRecordedAt: stationName === null ? null : '2026-09-14T09:00:00Z',
    positionOnOtherModel: false,
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
    expectedReturnAt: null,
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
    expectedReturnAt: null,
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

const ownRow = () =>
  row({ tripLogId: OWN, title: 'E1, the deep end', participants: [person(1, 'Ana', 'own.1')] });

const answered = (...trips: PublicLiveTrip[]): ListAnswer => ({
  data: { trips, more: false },
  isPending: false,
  isError: false,
});

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
const focus = (kind: string, ref: string) => ({
  silexgis: EMBED_CHANNEL,
  v: EMBED_PROTOCOL,
  type: 'focus',
  target: { kind, ref },
});

type Ready = {
  loaded: boolean;
  party: { ordinal: number; name: string; station: string | null }[];
  past?: { tripLogId: string };
  watching?: { tripLogId: string; title: string };
};
const readies = (sent: { message: Record<string, unknown> }[]) =>
  sent
    .filter((posted) => posted.message.type === 'ready')
    .map((posted) => posted.message as unknown as Ready);
const lastReady = (sent: { message: Record<string, unknown> }[]) => readies(sent).at(-1)!;
const focusedAnswers = (sent: { message: Record<string, unknown> }[]) =>
  sent.filter((posted) => posted.message.type === 'focused').map((posted) => posted.message);

const openSheet = () => fireEvent.click(screen.getByTestId('public-past-open'));
const watch = (tripLogId: string) => {
  openSheet();
  fireEvent.click(screen.getByTestId(`public-live-watch-${tripLogId}`));
};
const sheetIsOpen = () =>
  document.querySelector('.public-past-drawer')?.classList.contains('ant-drawer-open') ?? false;
const drawn = () =>
  ((given?.trackedCavers ?? []) as { name: string }[]).map((caver) => caver.name);
/** The station the drawing was last asked to fly to, if it was asked at all. */
const flownTo = () => (given?.focusRequest as { ref: string } | undefined)?.ref;

const realParent = window.parent;

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
    // Read a good while ago, unless a test says when: a list merely still in hand.
    dataUpdatedAt: Date.now() - LONG_AGO_MS,
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
      pictures: [],
      model: MODEL,
      teams: [],
      participants: [
        {
          ordinal: 1,
          label: 'Vlad',
          track: [
            {
              recordedAt: '2019-07-06T09:00:00Z',
              teamId: null,
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
  pastAsked = false;
  pastReadUnder = new Set();
  liveReads = 0;
  liveAskedUnder = [];
  linkToken = 'follow-token';
  given = undefined;
  viewersBuilt = 0;
});

afterEach(() => {
  cleanup();
  Object.defineProperty(window, 'parent', { value: realParent, configurable: true });
});

describe('watching another party of the cave inside a framed viewer', () => {
  it('reads nobody else until the sheet is opened, and offers this link’s own trip no Watch', () => {
    render(<PublicTripEmbedPage />);

    expect(liveReads).toBe(0);
    expect(screen.queryByTestId('public-watch-line')).toBeNull();

    openSheet();

    expect(liveReads).toBeGreaterThan(0);
    expect(screen.getByTestId(`public-live-watch-${OTHER}`)).toHaveTextContent('Watch');
    expect(screen.queryByTestId(`public-live-watch-${OWN}`)).toBeNull();
    // Opening the sheet changes nothing under it: the drawing is still this link's own party.
    expect(drawn()).toEqual(['Ana']);
    expect(screen.getByTestId('public-trip-embed')).not.toHaveClass('public-trip-embed-watch');
  });

  it('draws the party whose row was pressed on this link’s own survey, shuts the sheet and says whose party it is', () => {
    render(<PublicTripEmbedPage />);
    const survey = given?.fileUrl;
    openSheet();
    expect(sheetIsOpen()).toBe(true);

    fireEvent.click(screen.getByTestId(`public-live-watch-${OTHER}`));

    // The sheet covered the drawing the reader has just asked to see.
    expect(sheetIsOpen()).toBe(false);
    expect(drawn()).toEqual(['Mircea', 'Ileana', 'Radu']);
    expect(screen.getByTestId('public-watch-line')).toHaveTextContent(
      'Another party of this cave: E2, the survey',
    );
    expect(screen.getByTestId('public-watch-back')).toHaveTextContent("Back to this link's trip");
    expect(screen.getByTestId('public-trip-embed')).toHaveClass('public-trip-embed-watch');
    // The way into the list is still there, shrunk to its icon and named for a screen reader.
    expect(screen.getByTestId('public-past-open')).toHaveAttribute(
      'aria-label',
      'Past trips in this cave',
    );

    // The same drawing, not another and not the same one loaded again.
    expect(given?.fileUrl).toBe(survey);
    expect(viewersBuilt).toBe(1);
    // And the list goes on being read with the sheet shut: it is where this party's places
    // come from.
    expect(liveAsked).toBe(true);
  });

  it('goes back to this link’s own party on the strip’s button, and stops reading the list', () => {
    render(<PublicTripEmbedPage />);
    watch(OTHER);
    expect(drawn()).toEqual(['Mircea', 'Ileana', 'Radu']);

    fireEvent.click(screen.getByTestId('public-watch-back'));

    expect(drawn()).toEqual(['Ana']);
    expect(screen.queryByTestId('public-watch-line')).toBeNull();
    expect(screen.getByTestId('public-trip-embed')).not.toHaveClass('public-trip-embed-watch');
    expect(screen.getByTestId('public-past-open')).toHaveTextContent('Past trips in this cave');
    // Nobody is watched and the sheet is shut: the frame costs what it cost before the press.
    expect(liveAsked).toBe(false);
    // Going back by choice is not a watch that ended under the reader.
    expect(screen.queryByTestId('public-watch-ended')).toBeNull();
  });

  it('marks the watched row in the sheet and makes this link’s own row the way back', () => {
    render(<PublicTripEmbedPage />);
    watch(OTHER);

    // The list is still one press away while a party is watched, through the shrunk button.
    openSheet();
    expect(sheetIsOpen()).toBe(true);
    expect(screen.getByTestId(`public-live-watching-${OTHER}`)).toHaveTextContent('Watching');
    expect(screen.queryByTestId(`public-live-watch-${OTHER}`)).toBeNull();

    fireEvent.click(screen.getByTestId('public-live-back-own'));

    expect(sheetIsOpen()).toBe(false);
    expect(drawn()).toEqual(['Ana']);
    expect(screen.queryByTestId('public-watch-line')).toBeNull();
  });

  it('ends the watch with a line naming the party when it leaves the list, never by a silent swap', () => {
    const { rerender } = render(<PublicTripEmbedPage />);
    watch(OTHER);

    // A read that did not land is not evidence of anything: the last list stands, with the party
    // still in it, under the line saying the frame is not being refreshed.
    liveList = { ...answered(ownRow(), row()), isError: true, error: new TypeError('Failed to fetch') };
    rerender(<PublicTripEmbedPage />);
    expect(drawn()).toEqual(['Mircea', 'Ileana', 'Radu']);
    expect(screen.getByTestId('public-watch-line')).toBeTruthy();
    expect(screen.getByTestId('public-trip-stale')).toBeTruthy();
    expect(screen.queryByTestId('public-watch-ended')).toBeNull();

    // A list that was read and does not carry the party is.
    liveList = answered(ownRow());
    rerender(<PublicTripEmbedPage />);

    expect(drawn()).toEqual(['Ana']);
    expect(screen.queryByTestId('public-watch-line')).toBeNull();
    expect(screen.getByTestId('public-watch-ended')).toHaveTextContent(
      "E2, the survey is no longer in the list of parties being followed. This is this link's trip again.",
    );
    expect(screen.queryByTestId('public-trip-stale')).toBeNull();

    // The line stays until it is closed.
    fireEvent.click(screen.getByRole('button', { name: 'Close this notice' }));
    expect(screen.queryByTestId('public-watch-ended')).toBeNull();
    expect(drawn()).toEqual(['Ana']);
  });

  it('measures its age and its staleness by the list while another party is on screen', () => {
    // The link's own read fails and the list lands: what is drawn is the list's, so the frame is
    // not stale — and the reverse of the case above, where the list failing made it so.
    live = { data: envelope(), isPending: false, error: new TypeError('Failed to fetch') };
    render(<PublicTripEmbedPage />);
    expect(screen.getByTestId('public-trip-stale')).toBeTruthy();

    liveList = { ...answered(ownRow(), row()), dataUpdatedAt: Date.now() };
    watch(OTHER);

    expect(screen.queryByTestId('public-trip-stale')).toBeNull();
    expect(screen.getByTestId('public-trip-updated')).toHaveTextContent('Page updated');
  });

  it('gives the hour and not a running age for a watched party whose watch has closed, while this link’s trip is still followed', () => {
    liveList = {
      ...answered(ownRow(), row({ state: 'closed', closedAt: '2026-09-14T12:00:00Z' })),
      dataUpdatedAt: Date.now(),
    };
    live = { data: envelope(), isPending: false, error: null, dataUpdatedAt: Date.now() };
    render(<PublicTripEmbedPage />);
    expect(screen.getByTestId('public-trip-updated')).toHaveTextContent('Page updated');

    watch(OTHER);

    // The link's own trip is still armed; the party on screen is not, and it is that party the
    // strip speaks of.
    expect(screen.getByTestId('public-trip-updated')).toHaveTextContent('Last read at');
  });

  it('still says the link has ended while another party is on screen', () => {
    // The press that opened the sheet read the past trips too, and they are in hand from then on.
    const pressedAt = Date.now() - 120_000;
    archive = { ...archive, dataUpdatedAt: pressedAt };
    liveList = { ...liveList, dataUpdatedAt: pressedAt };
    const { rerender } = render(<PublicTripEmbedPage />);
    watch(OTHER);
    expect(archive.data).toBeDefined();

    // The list went on answering for a while; then it is refused with the link, and the last
    // one read stands. A past list from before that says nothing of the link as it is now.
    live = { data: envelope(), isPending: false, error: new ApiError(404, 'tracking.share_not_found') };
    liveList = {
      ...answered(ownRow(), row()),
      dataUpdatedAt: pressedAt + 60_000,
      isError: true,
      error: new ApiError(404, 'tracking.share_not_found'),
      errorUpdatedAt: pressedAt + 120_000,
    };
    rerender(<PublicTripEmbedPage />);

    expect(screen.getByTestId('public-trip-ended')).toBeTruthy();
    expect(screen.queryByTestId('public-trip-past-only')).toBeNull();
    // One notice, the final one — not also "stopped refreshing", which promises a refresh.
    expect(screen.queryByTestId('public-trip-stale')).toBeNull();
    expect(screen.getByTestId('public-watch-line')).toBeTruthy();
  });

  it('does not say the link has ended over a watched party because this link’s own read was refused, while the list still lands', () => {
    live = { data: envelope(), isPending: false, error: null, dataUpdatedAt: Date.now() };
    liveList = { ...answered(ownRow(), row()), dataUpdatedAt: Date.now() };
    const { rerender } = render(<PublicTripEmbedPage />);
    watch(OTHER);

    // The link's own trip stopped being published; the list goes on answering.
    live = { ...live, error: new ApiError(404, 'tracking.share_not_found') };
    rerender(<PublicTripEmbedPage />);

    expect(screen.getByTestId('public-watch-line')).toBeTruthy();
    expect(screen.queryByTestId('public-trip-ended')).toBeNull();
    expect(screen.getByTestId('public-trip-updated')).toHaveTextContent('Page updated');
    expect(drawn()).toEqual(['Mircea', 'Ileana', 'Radu']);

    // A later failure of the list is still said as one — it was not silenced for good.
    liveList = { ...liveList, isError: true, error: new ApiError(503) };
    rerender(<PublicTripEmbedPage />);
    expect(screen.getByTestId('public-trip-stale')).toBeTruthy();
    expect(screen.queryByTestId('public-trip-ended')).toBeNull();

    // The final word about this link's own trip is said where it is true: over that trip.
    fireEvent.click(screen.getByTestId('public-watch-back'));
    expect(screen.getByTestId('public-trip-ended')).toBeTruthy();
  });

  it('takes a final refusal of the list as final while a party is watched, though this link’s own closed trip was never read again', () => {
    live = {
      data: envelope({ state: 'closed', closedAt: '2026-09-14T12:00:00Z' }),
      isPending: false,
      error: null,
    };
    // The past trips were read on the press that opened the sheet; the list of parties has
    // answered since, so that earlier answer does not speak for the link when it is refused.
    const listReadAt = Date.now();
    archive = { ...archive, dataUpdatedAt: listReadAt - 60_000 };
    liveList = { ...answered(ownRow(), row()), dataUpdatedAt: listReadAt };
    const { rerender } = render(<PublicTripEmbedPage />);
    watch(OTHER);

    liveList = { ...liveList, isError: true, error: new ApiError(404, 'tracking.share_not_found') };
    rerender(<PublicTripEmbedPage />);

    // Not a fault that may clear: the line that promises nothing.
    expect(screen.queryByTestId('public-trip-stale')).toBeNull();
    expect(screen.getByTestId('public-trip-ended')).toBeTruthy();
    expect(screen.queryByTestId('public-trip-past-only')).toBeNull();
    expect(screen.getByTestId('public-trip-updated')).toHaveTextContent('Last read at');

    // The twin: the same list merely failing to land is a fault that may clear.
    liveList = { ...liveList, error: new ApiError(503) };
    rerender(<PublicTripEmbedPage />);
    expect(screen.getByTestId('public-trip-stale')).toBeTruthy();
    expect(screen.queryByTestId('public-trip-ended')).toBeNull();
  });

  it('keeps the rows of the list in hand when a later read of it merely fails, and gives them up for a final refusal', () => {
    const { rerender } = render(<PublicTripEmbedPage />);
    openSheet();
    expect(screen.getByTestId('public-live-list')).toBeTruthy();

    liveList = { ...liveList, isError: true, error: new TypeError('Failed to fetch') };
    rerender(<PublicTripEmbedPage />);
    expect(screen.getByTestId('public-live-list')).toBeTruthy();
    expect(screen.getByTestId(`public-live-watch-${OTHER}`)).toBeTruthy();
    expect(screen.queryByTestId('public-live-failed')).toBeNull();

    // A refusal the server settled, with the past trips refused beside it: nothing is known of
    // the link but that this list is refused, and the wording is the failure's.
    liveList = { ...liveList, error: new ApiError(404, 'tracking.share_not_found') };
    archive = {
      data: undefined,
      isPending: false,
      isError: true,
      error: new ApiError(404, 'tracking.share_not_found'),
    };
    rerender(<PublicTripEmbedPage />);
    expect(screen.queryByTestId('public-live-list')).toBeNull();
    expect(screen.getByTestId('public-live-failed')).toBeTruthy();
    expect(screen.queryByTestId('public-live-past-only')).toBeNull();

    // And with no list in hand at all, a failure of any kind is the notice.
    liveList = { isPending: false, isError: true, error: new TypeError('Failed to fetch') };
    rerender(<PublicTripEmbedPage />);
    expect(screen.getByTestId('public-live-failed')).toBeTruthy();
  });

  /**
   * A link outlives its trip, and an installation may stop an old one from listing today's
   * parties while it goes on opening the cave's past trips. In a frame the two lists are asked
   * for on one press, so which of them answered is known the moment the sheet is open.
   */
  it('says quietly that the link no longer lists today’s parties when the past trips answered and the list is refused for good', () => {
    liveList = {
      isPending: false,
      isError: true,
      error: new ApiError(404, 'tracking.share_not_found'),
    };
    // Both lists are read on the one press, so the past trips' answer is of that moment.
    archive = { ...archive, dataUpdatedAt: Date.now() };
    const { rerender } = render(<PublicTripEmbedPage />);
    expect(pastAsked).toBe(false);
    openSheet();

    const notice = screen.getByTestId('public-live-past-only');
    expect(notice).toHaveTextContent('This link no longer lists who is in the cave today');
    expect(notice).toHaveTextContent('past trips are still here');
    expect(screen.queryByTestId('public-live-failed')).toBeNull();
    expect(screen.queryByTestId('public-live-link-ended')).toBeNull();
    expect(screen.queryByText(/Try opening this list again/)).toBeNull();
    // The past trips stand beside it, as the sentence says.
    expect(screen.getByTestId('public-past-list')).toBeTruthy();

    // The twins: a list that did not land, a busy server and a wait it asked for may all clear.
    for (const fault of [
      new TypeError('Failed to fetch'),
      new ApiError(503),
      new ApiError(429, undefined, undefined, undefined, 30_000),
    ]) {
      liveList = { isPending: false, isError: true, error: fault };
      rerender(<PublicTripEmbedPage />);
      expect(screen.getByTestId('public-live-failed')).toBeTruthy();
      expect(screen.queryByTestId('public-live-past-only')).toBeNull();
    }
  });

  it('says the same on its one line over a watched party, once the past trips answered after the list was refused', () => {
    // Watching a party means the sheet was opened, and that press read the past trips: a framed
    // watcher always holds a past list. It is the order of the two answers that matters.
    const listReadAt = Date.now();
    archive = { ...archive, dataUpdatedAt: listReadAt - 60_000 };
    liveList = { ...answered(ownRow(), row()), dataUpdatedAt: listReadAt };
    const { rerender } = render(<PublicTripEmbedPage />);
    watch(OTHER);
    expect(pastAsked).toBe(false);

    // The list answered after the past trips were read, and is now refused for good: a link
    // that changed since, of which the past list in hand says nothing. The frame says what it
    // said before.
    liveList = {
      ...liveList,
      isError: true,
      error: new ApiError(404, 'tracking.share_not_found'),
      errorUpdatedAt: listReadAt + 60_000,
    };
    rerender(<PublicTripEmbedPage />);
    expect(pastAsked).toBe(false);
    expect(screen.getByTestId('public-trip-ended')).toBeTruthy();
    expect(screen.queryByTestId('public-trip-past-only')).toBeNull();

    // The sheet opened again reads the past trips again, and they answer: the link still opens
    // them, so only its list of parties has ended.
    archive = { ...archive, dataUpdatedAt: listReadAt + 120_000 };
    openSheet();
    expect(pastAsked).toBe(true);
    expect(screen.getByTestId('public-trip-past-only')).toHaveTextContent(
      'This link no longer lists who is in the cave today',
    );
    expect(screen.queryByTestId('public-trip-ended')).toBeNull();
    expect(screen.queryByTestId('public-trip-stale')).toBeNull();
    expect(screen.getByTestId('public-live-past-only')).toBeTruthy();

    // The twin: read again and refused as well, the link has simply stopped answering.
    archive = {
      data: undefined,
      isPending: false,
      isError: true,
      error: new ApiError(404, 'tracking.share_not_found'),
    };
    rerender(<PublicTripEmbedPage />);
    expect(screen.getByTestId('public-trip-ended')).toBeTruthy();
    expect(screen.queryByTestId('public-trip-past-only')).toBeNull();
  });
});

describe('what a framed viewer tells its article while another party is watched', () => {
  it('names the watched party on the announcement, and only while one is watched', () => {
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, HOST, hello);

    // The trip this link was published for: nothing says otherwise, as before this existed.
    expect(lastReady(sent).party.map((member) => member.name)).toEqual(['Ana']);
    expect(lastReady(sent).watching).toBeUndefined();

    watch(OTHER);

    const told = lastReady(sent);
    expect(told.loaded).toBe(true);
    expect(told.party.map((member) => member.name)).toEqual(['Mircea', 'Ileana', 'Radu']);
    expect(told.watching).toEqual({ tripLogId: OTHER, title: 'E2, the survey' });
    // Another party being followed now is not a replay, and is never announced as one.
    expect(told.past).toBeUndefined();
    // Addressed to the framer and nobody else, like every announcement.
    expect(sent.at(-1)?.origin).toBe(HOST);

    fireEvent.click(screen.getByTestId('public-watch-back'));

    expect(lastReady(sent).party.map((member) => member.name)).toEqual(['Ana']);
    expect(lastReady(sent).watching).toBeUndefined();
  });

  it('says so again when the reader moves to a third party whose people stand exactly where the second’s did', () => {
    // Two parties of one cave, numbered from one, under the same names at the same stations: the
    // party announced is identical to the letter, and only whose it is has changed.
    liveList = answered(ownRow(), row(), row({ tripLogId: THIRD, title: 'E3, the dive' }));
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, HOST, hello);
    watch(OTHER);
    const before = readies(sent).length;
    const second = lastReady(sent);

    watch(THIRD);

    expect(readies(sent).length).toBe(before + 1);
    expect(lastReady(sent).party).toEqual(second.party);
    expect(lastReady(sent).watching).toEqual({ tripLogId: THIRD, title: 'E3, the dive' });
  });

  it('does not say it again while the watched party has not changed', () => {
    const { parent, sent } = fakeParent();
    const { rerender } = render(<PublicTripEmbedPage />);
    deliver(parent, HOST, hello);
    watch(OTHER);
    const before = readies(sent).length;

    // A minute's read that found nobody moved: a fresh list, the same party.
    liveList = answered(ownRow(), row());
    rerender(<PublicTripEmbedPage />);
    expect(readies(sent).length).toBe(before);

    // And one that found somebody moved is said, still under the watched party's name.
    liveList = answered(
      ownRow(),
      row({ participants: [person(1, 'Mircea', 'other.5'), person(2, 'Ileana', 'other.9')] }),
    );
    rerender(<PublicTripEmbedPage />);
    expect(readies(sent).length).toBe(before + 1);
    expect(lastReady(sent).party[0]).toMatchObject({ name: 'Mircea', station: 'other.5' });
    expect(lastReady(sent).watching).toEqual({ tripLogId: OTHER, title: 'E2, the survey' });
  });

  it('stops naming a watched party the moment it leaves the list', () => {
    const { parent, sent } = fakeParent();
    const { rerender } = render(<PublicTripEmbedPage />);
    deliver(parent, HOST, hello);
    watch(OTHER);
    expect(lastReady(sent).watching?.tripLogId).toBe(OTHER);

    liveList = answered(ownRow());
    rerender(<PublicTripEmbedPage />);

    expect(lastReady(sent).party.map((member) => member.name)).toEqual(['Ana']);
    expect(lastReady(sent).watching).toBeUndefined();
    // No announcement in between handed the article this link's own party as the other one's.
    const mislabelled = readies(sent).filter(
      (told) => told.watching !== undefined && told.party.some((member) => member.name === 'Ana'),
    );
    expect(mislabelled).toEqual([]);
  });

  it('answers a link naming a place in the party from the party on screen', () => {
    const { parent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, HOST, hello);

    deliver(parent, HOST, focus('caver', '1'));
    expect(flownTo()).toBe('own.1');

    watch(OTHER);
    deliver(parent, HOST, focus('caver', '1'));

    // "Caver 1" is whoever the announcement last said caver 1 is — which is why it says whose.
    expect(flownTo()).toBe('other.4');
  });

  it('leaves the watch when the article asks for the trip called live', () => {
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, HOST, hello);
    watch(OTHER);

    deliver(parent, HOST, focus('trip', 'live'));

    expect(drawn()).toEqual(['Ana']);
    expect(screen.queryByTestId('public-watch-line')).toBeNull();
    expect(focusedAnswers(sent).at(-1)).toMatchObject({ found: true });
    expect(lastReady(sent).watching).toBeUndefined();
    expect(liveAsked).toBe(false);
  });

  it('drops the watch for a past trip the article asks for, and comes back from it to this link’s own trip', () => {
    const { parent, sent } = fakeParent();
    render(<PublicTripEmbedPage />);
    deliver(parent, HOST, hello);
    watch(OTHER);

    deliver(parent, HOST, focus('trip', TRIP_2019));

    expect(drawn()).toEqual(['Vlad']);
    expect(screen.queryByTestId('public-watch-line')).toBeNull();
    expect(screen.getByTestId('public-trip-embed')).toHaveClass('public-trip-embed-past');
    expect(screen.getByTestId('public-trip-embed')).not.toHaveClass('public-trip-embed-watch');
    expect(lastReady(sent).past?.tripLogId).toBe(TRIP_2019);
    expect(lastReady(sent).watching).toBeUndefined();

    deliver(parent, HOST, focus('trip', 'live'));

    // Home is the trip the link was published for, not the party watched before the replay.
    expect(drawn()).toEqual(['Ana']);
    expect(lastReady(sent).watching).toBeUndefined();
    expect(lastReady(sent).past).toBeUndefined();
  });
});

describe('a second published link opened in the same frame, while another party was being watched', () => {
  it('starts on its own party, tells the article so, and reads nobody else’s list under it', () => {
    const { parent, sent } = fakeParent();
    const { rerender } = render(<PublicTripEmbedPage />);
    deliver(parent, HOST, hello);
    watch(OTHER);
    expect(drawn()).toEqual(['Mircea', 'Ileana', 'Radu']);
    expect(lastReady(sent).watching).toMatchObject({ tripLogId: OTHER });

    linkToken = 'second-link';
    rerender(<PublicTripEmbedPage />);

    expect(drawn()).toEqual(['Ana']);
    expect(screen.queryByTestId('public-watch-line')).toBeNull();
    expect(screen.queryByTestId('public-watch-ended')).toBeNull();
    expect(screen.getByTestId('public-trip-embed')).not.toHaveClass('public-trip-embed-watch');
    expect(lastReady(sent).watching).toBeUndefined();
    expect(liveAsked).toBe(false);
    expect(liveAskedUnder).not.toContain('second-link');
  });

  it('shuts a sheet left open under the first link, so its lists are not read under the second', () => {
    const { rerender } = render(<PublicTripEmbedPage />);
    openSheet();
    expect(sheetIsOpen()).toBe(true);

    linkToken = 'second-link';
    rerender(<PublicTripEmbedPage />);

    expect(sheetIsOpen()).toBe(false);
    expect(liveAskedUnder).not.toContain('second-link');
  });
});
