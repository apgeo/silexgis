// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { PublicPastTrack } from '../../api/hooks.ts';
import { ApiError } from '../../api/client.ts';
import { REPLAY_MARKER_MOVE_MS } from '../../caveview/useReplayClock.ts';

/**
 * The one state machine both public surfaces read the past through, driven directly.
 *
 * <b>What is proved here is the order things arrive in.</b> A link opens a trip and an instant in
 * one press, and the instant arrives long before the track it belongs to — so the hook holds it.
 * These cases are about what happens to a held instant when the trip it was held for is not the
 * trip that lands: the reader chose another one while the first was in flight, or after the first
 * was refused.
 */

const TRIP_X = 'aaaaaaaa-0000-0000-0000-000000000001';
const TRIP_Y = 'aaaaaaaa-0000-0000-0000-000000000002';

let answers: Record<
  string,
  { data?: PublicPastTrack; isPending: boolean; isError: boolean; error?: unknown }
>;

/** Every track the playback asked for, with the link it asked under. */
let asked: { token: string | undefined; tripLogId: string }[];

vi.mock('../../api/hooks.ts', () => ({
  usePublicPastTrack: (token: string | undefined, tripLogId: string | undefined) => {
    if (tripLogId !== undefined) {
      asked.push({ token, tripLogId });
    }
    return tripLogId === undefined
      ? { data: undefined, isPending: false, isError: false }
      : (answers[tripLogId] ?? { data: undefined, isPending: true, isError: false });
  },
}));

const { usePastTripPlayback } = await import('./usePastTripPlayback.ts');

/** A day in 2019, one report at nine and one at ten, so the rail runs 08:00–18:00. */
function trip2019(tripLogId: string): PublicPastTrack {
  return {
    tripLogId,
    expedition: null,
    title: 'Peștera Demo Mare, the 2019 push',
    tripDate: '2019-07-06',
    tripDateEnd: null,
    armedAt: '2019-07-06T08:00:00Z',
    closedAt: '2019-07-06T18:00:00Z',
    positionsWithheld: false,
    trackTruncated: false,
    model: null,
    teams: [],
    participants: [
      {
        ordinal: 1,
        label: 'Mircea',
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
          {
            recordedAt: '2019-07-06T10:00:00Z',
            teamId: null,
            stationName: 'p.g.9',
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

const START_2019 = Date.parse('2019-07-06T08:00:00Z');
const TEN_2019 = Date.parse('2019-07-06T10:00:00Z');

beforeEach(() => {
  answers = {};
  asked = [];
});

describe('a moment asked for with a trip', () => {
  it('opens that trip at that moment, clamped into its own stretch', () => {
    answers[TRIP_X] = { data: trip2019(TRIP_X), isPending: false, isError: false };
    const { result } = renderHook(() => usePastTripPlayback('follow-token'));

    act(() => result.current.open(TRIP_X, { at: '2019-07-06T10:00:00Z' }));

    expect(result.current.at).toBe(TEN_2019);
  });

  it('is dropped when the reader opens another trip before the first has landed', () => {
    // The link named a 2026 trip and an afternoon in it; that trip is slow. The reader opens a
    // 2019 trip from the list instead. Clamped into 2019, the 2026 instant is the very end of the
    // rail — a replay opened on everybody already out, instead of at its start.
    const { result, rerender } = renderHook(() => usePastTripPlayback('follow-token'));
    act(() => result.current.open(TRIP_X, { at: '2026-09-14T13:40:00Z' }));
    expect(result.current.loading).toBe(true);

    act(() => result.current.open(TRIP_Y));
    answers[TRIP_Y] = { data: trip2019(TRIP_Y), isPending: false, isError: false };
    rerender();

    expect(result.current.tripLogId).toBe(TRIP_Y);
    expect(result.current.at).toBe(START_2019);
  });

  it('is dropped when the trip it was asked for could not be read', () => {
    // A snippet left with a placeholder id, or a trip past its retention: refused. The reader
    // then opens a trip that can be read, and the refused trip's instant must not be waiting.
    answers[TRIP_X] = { data: undefined, isPending: false, isError: true };
    const { result, rerender } = renderHook(() => usePastTripPlayback('follow-token'));
    act(() => result.current.open(TRIP_X, { at: '2026-09-14T13:40:00Z' }));
    expect(result.current.failed).toBe(true);

    act(() => result.current.open(TRIP_Y));
    answers[TRIP_Y] = { data: trip2019(TRIP_Y), isPending: false, isError: false };
    rerender();

    expect(result.current.at).toBe(START_2019);
  });

  it('still moves the clock when the same trip is opened again at a moment', () => {
    // A second link in the article about the trip already playing, naming a later moment: the
    // clock goes there. Only a different trip forgets the instant.
    answers[TRIP_X] = { data: trip2019(TRIP_X), isPending: false, isError: false };
    const { result } = renderHook(() => usePastTripPlayback('follow-token'));
    act(() => result.current.open(TRIP_X));
    expect(result.current.at).toBe(START_2019);

    act(() => result.current.open(TRIP_X, { at: '2019-07-06T10:00:00Z' }));

    expect(result.current.at).toBe(TEN_2019);
  });

  it('keeps the clock where it was when the same trip is opened again without one', () => {
    answers[TRIP_X] = { data: trip2019(TRIP_X), isPending: false, isError: false };
    const { result } = renderHook(() => usePastTripPlayback('follow-token'));
    act(() => result.current.open(TRIP_X));
    act(() => result.current.setAt(TEN_2019));

    act(() => result.current.open(TRIP_X, { follow: { kind: 'caver', id: '1' } }));

    expect(result.current.at).toBe(TEN_2019);
  });
});

/**
 * A link that says "from here, playing".
 *
 * <b>The request is held exactly as the moment is, and for the same reason</b>: it is made in the
 * press that names the trip, long before that trip's track has been read. So the cases are the
 * moment's cases over again — what lands, what was asked of a trip that is no longer the one on
 * screen — and two of its own: a reader who asked for less movement, and a clock asked to start
 * twice.
 */
describe('a trip that could not be read', () => {
  const opened = () => {
    const { result } = renderHook(() => usePastTripPlayback('follow-token'));
    act(() => result.current.open(TRIP_X));
    return result;
  };

  it('is refused where the server said so: not this cave’s, taken back, or kept no longer', () => {
    for (const status of [404, 410, 403]) {
      answers[TRIP_X] = {
        data: undefined,
        isPending: false,
        isError: true,
        error: new ApiError(status, 'refused'),
      };
      const result = opened();
      expect(result.current.failed, String(status)).toBe(true);
      expect(result.current.refused, String(status)).toBe(true);
    }
  });

  it('has failed and is not refused where the read merely did not land', () => {
    // A server fault, a wait the limiter asked for that outlasted the retries, and a connection
    // that dropped: none of them is a word about the trip, and each is read again by itself.
    for (const error of [
      new ApiError(503, 'unavailable'),
      new ApiError(429, 'slow down'),
      new TypeError('Failed to fetch'),
    ]) {
      answers[TRIP_X] = { data: undefined, isPending: false, isError: true, error };
      const result = opened();
      expect(result.current.failed).toBe(true);
      expect(result.current.refused).toBe(false);
    }
  });

  it('is neither while it is in flight, once it has landed, or with nothing asked for', () => {
    const idle = renderHook(() => usePastTripPlayback('follow-token'));
    expect(idle.result.current.refused).toBe(false);
    const flying = opened();
    expect(flying.current.refused).toBe(false);
    answers[TRIP_X] = { data: trip2019(TRIP_X), isPending: false, isError: false };
    expect(opened().current.refused).toBe(false);
  });
});

describe('a replay asked to play', () => {
  it('starts once the track has landed, from the moment the link named', () => {
    const { result, rerender } = renderHook(() => usePastTripPlayback('follow-token'));
    act(() => result.current.open(TRIP_X, { at: '2019-07-06T10:00:00Z', play: true }));
    // Nothing to play yet, so nothing is playing — and the request is not lost for that.
    expect(result.current.loading).toBe(true);
    expect(result.current.transport.playing).toBe(false);

    answers[TRIP_X] = { data: trip2019(TRIP_X), isPending: false, isError: false };
    act(() => rerender());

    expect(result.current.at).toBe(TEN_2019);
    expect(result.current.transport.playing).toBe(true);
  });

  it('leaves a trip opened without it standing still', () => {
    // The twin of the case above: the same trip, the same moment, and no word about playing.
    answers[TRIP_X] = { data: trip2019(TRIP_X), isPending: false, isError: false };
    const { result } = renderHook(() => usePastTripPlayback('follow-token'));

    act(() => result.current.open(TRIP_X, { at: '2019-07-06T10:00:00Z' }));

    expect(result.current.at).toBe(TEN_2019);
    expect(result.current.transport.playing).toBe(false);
  });

  it('plays from where the trip opens when the link names no moment', () => {
    answers[TRIP_X] = { data: trip2019(TRIP_X), isPending: false, isError: false };
    const { result } = renderHook(() => usePastTripPlayback('follow-token'));

    act(() => result.current.open(TRIP_X, { play: true }));

    expect(result.current.at).toBe(START_2019);
    expect(result.current.transport.playing).toBe(true);
  });

  it('plays from where the trip opens when the moment named cannot be read', () => {
    answers[TRIP_X] = { data: trip2019(TRIP_X), isPending: false, isError: false };
    const { result } = renderHook(() => usePastTripPlayback('follow-token'));

    act(() => result.current.open(TRIP_X, { at: 'half-past-one', play: true }));

    expect(result.current.at).toBe(START_2019);
    expect(result.current.transport.playing).toBe(true);
  });

  it('is spent once: a reader who pauses is not set playing again by the link that opened the trip', () => {
    answers[TRIP_X] = { data: trip2019(TRIP_X), isPending: false, isError: false };
    const { result, rerender } = renderHook(() => usePastTripPlayback('follow-token'));
    act(() => result.current.open(TRIP_X, { play: true }));
    expect(result.current.transport.playing).toBe(true);

    act(() => result.current.transport.toggle());
    act(() => result.current.setAt(TEN_2019));
    act(() => rerender());

    expect(result.current.transport.playing).toBe(false);
  });

  it('starts the trip already on screen where its clock stands, and asked twice is still playing', () => {
    answers[TRIP_X] = { data: trip2019(TRIP_X), isPending: false, isError: false };
    const { result } = renderHook(() => usePastTripPlayback('follow-token'));
    act(() => result.current.open(TRIP_X));
    act(() => result.current.setAt(TEN_2019));
    expect(result.current.transport.playing).toBe(false);

    act(() => result.current.open(TRIP_X, { play: true }));
    expect(result.current.at).toBe(TEN_2019);
    expect(result.current.transport.playing).toBe(true);

    // A second press of the same link in an article: a toggle would have paused it.
    act(() => result.current.open(TRIP_X, { play: true }));
    expect(result.current.transport.playing).toBe(true);
  });

  it('does not answer a link naming the trip’s last moment by starting the trip again', () => {
    answers[TRIP_X] = { data: trip2019(TRIP_X), isPending: false, isError: false };
    const { result } = renderHook(() => usePastTripPlayback('follow-token'));

    act(() => result.current.open(TRIP_X, { at: '2019-07-06T23:00:00Z', play: true }));

    const end = result.current.span?.to;
    expect(end).toBeDefined();
    expect(result.current.at).toBe(end);
    expect(result.current.transport.playing).toBe(false);
  });

  it('is dropped when the reader opens another trip before the first has landed', () => {
    const { result, rerender } = renderHook(() => usePastTripPlayback('follow-token'));
    act(() => result.current.open(TRIP_X, { play: true }));
    expect(result.current.loading).toBe(true);

    act(() => result.current.open(TRIP_Y));
    answers[TRIP_Y] = { data: trip2019(TRIP_Y), isPending: false, isError: false };
    act(() => rerender());

    expect(result.current.at).toBe(START_2019);
    expect(result.current.transport.playing).toBe(false);
  });

  it('is not waiting for the next trip a reader picks after the one it named was refused', () => {
    answers[TRIP_X] = { data: undefined, isPending: false, isError: true };
    const { result, rerender } = renderHook(() => usePastTripPlayback('follow-token'));
    act(() => result.current.open(TRIP_X, { play: true }));
    expect(result.current.failed).toBe(true);

    act(() => result.current.open(TRIP_Y));
    answers[TRIP_Y] = { data: trip2019(TRIP_Y), isPending: false, isError: false };
    act(() => rerender());

    expect(result.current.at).toBe(START_2019);
    expect(result.current.transport.playing).toBe(false);
  });

  it('is dropped when the trip it was asked for could not be read, even if that trip answers later', () => {
    // The read is tried again by itself when the reader comes back to the tab. A replay that
    // then began to move, minutes after the page said the trip could not be read and with nobody
    // pressing anything, would be movement nobody was asking for any more.
    answers[TRIP_X] = { data: undefined, isPending: false, isError: true };
    const { result, rerender } = renderHook(() => usePastTripPlayback('follow-token'));
    act(() => result.current.open(TRIP_X, { play: true }));
    expect(result.current.failed).toBe(true);

    answers[TRIP_X] = { data: trip2019(TRIP_X), isPending: false, isError: false };
    act(() => rerender());

    expect(result.current.failed).toBe(false);
    expect(result.current.at).toBe(START_2019);
    expect(result.current.transport.playing).toBe(false);
  });

  it('is dropped when the reader goes back to the live party before the track has landed', () => {
    const { result, rerender } = renderHook(() => usePastTripPlayback('follow-token'));
    act(() => result.current.open(TRIP_X, { play: true }));
    act(() => result.current.backToNow());

    answers[TRIP_X] = { data: trip2019(TRIP_X), isPending: false, isError: false };
    act(() => result.current.open(TRIP_X));
    act(() => rerender());

    expect(result.current.at).toBe(START_2019);
    expect(result.current.transport.playing).toBe(false);
  });

  describe('for a reader who asked for less movement', () => {
    const realMatchMedia = window.matchMedia;

    afterEach(() => {
      window.matchMedia = realMatchMedia;
      delete document.documentElement.dataset.reduceMotion;
    });

    it('opens at the moment named and stands still, when the system says so', () => {
      window.matchMedia = ((query: string) => ({
        ...realMatchMedia(query),
        matches: query.includes('prefers-reduced-motion'),
      })) as typeof window.matchMedia;
      answers[TRIP_X] = { data: trip2019(TRIP_X), isPending: false, isError: false };
      const { result } = renderHook(() => usePastTripPlayback('follow-token'));

      act(() => result.current.open(TRIP_X, { at: '2019-07-06T10:00:00Z', play: true }));

      expect(result.current.at).toBe(TEN_2019);
      expect(result.current.transport.playing).toBe(false);
      // Their own press still plays: what is declined is movement nobody on this page asked for.
      act(() => result.current.transport.toggle());
      expect(result.current.transport.playing).toBe(true);
    });

    it('opens standing still when this application’s own setting says so', () => {
      document.documentElement.dataset.reduceMotion = 'true';
      answers[TRIP_X] = { data: trip2019(TRIP_X), isPending: false, isError: false };
      const { result } = renderHook(() => usePastTripPlayback('follow-token'));

      act(() => result.current.open(TRIP_X, { play: true }));

      expect(result.current.at).toBe(START_2019);
      expect(result.current.transport.playing).toBe(false);
    });
  });
});

/**
 * How the markers move, decided with the moment rather than a render after it.
 *
 * <b>Placed unless the clock is playing.</b> A drag of the handle asks where the party was, and a
 * slide there is the party racing through the cave behind the reader's finger; a playing clock
 * ticks five times a second, and the viewer's own slide lasts three ticks, so the markers would
 * trail the moment printed beside them. The drawing reads this in the same render the moment
 * changes, which is why the clock is held here and not in the strip that draws its controls.
 */
describe('the reports of whoever is followed', () => {
  const NINE_2019 = Date.parse('2019-07-06T09:00:00Z');
  const NOON_2019 = Date.parse('2019-07-06T12:00:00Z');
  /** The same day with a second person, reported once at noon. */
  const twoPeople = (tripLogId: string): PublicPastTrack => {
    const trip = trip2019(tripLogId);
    return {
      ...trip,
      participants: [
        ...trip.participants,
        {
          ordinal: 2,
          label: 'Ileana',
          track: [{ ...trip.participants[0].track[0], recordedAt: '2019-07-06T12:00:00Z' }],
        },
      ],
    };
  };

  it('are theirs alone while they are followed, beside everybody’s for the rail', () => {
    answers[TRIP_X] = { data: twoPeople(TRIP_X), isPending: false, isError: false };
    const { result } = renderHook(() => usePastTripPlayback('follow-token'));
    act(() => result.current.open(TRIP_X));

    // Nobody followed: nothing to narrow the steps to.
    expect(result.current.moments).toEqual([NINE_2019, TEN_2019, NOON_2019]);
    expect(result.current.followedMoments).toEqual([]);

    act(() => result.current.setFollow({ kind: 'caver', id: '2' }));
    expect(result.current.followedMoments).toEqual([NOON_2019]);
    // The rail's marks are still the whole trip's.
    expect(result.current.moments).toEqual([NINE_2019, TEN_2019, NOON_2019]);

    act(() => result.current.setFollow({ kind: 'caver', id: '1' }));
    expect(result.current.followedMoments).toEqual([NINE_2019, TEN_2019]);

    act(() => result.current.setFollow(null));
    expect(result.current.followedMoments).toEqual([]);
  });

  it('are none for a follow this trip holds no report of, and are not rebuilt as the clock runs', () => {
    answers[TRIP_X] = { data: twoPeople(TRIP_X), isPending: false, isError: false };
    const { result } = renderHook(() => usePastTripPlayback('follow-token'));
    act(() => result.current.open(TRIP_X, { follow: { kind: 'caver', id: '99' } }));
    expect(result.current.followedMoments).toEqual([]);

    act(() => result.current.setFollow({ kind: 'caver', id: '2' }));
    const held = result.current.followedMoments;
    // The strip reads this on every render of a playing replay; a list rebuilt on each of them is
    // the whole track walked five times a second.
    act(() => result.current.setAt(TEN_2019));
    act(() => result.current.setAt(NOON_2019));
    expect(result.current.followedMoments).toBe(held);
  });
});

describe('a replay when the link itself changes', () => {
  it('is dropped, with whom it followed and its clock, and the new link is never asked for the old link’s trip', () => {
    // Two published links of one cave, opened one after the other in one tab. The trip id of the
    // first is one the second would answer for, so nothing but this rule keeps it off the screen.
    answers[TRIP_X] = { data: trip2019(TRIP_X), isPending: false, isError: false };
    const { result, rerender } = renderHook(({ token }) => usePastTripPlayback(token), {
      initialProps: { token: 'first-link' },
    });
    act(() =>
      result.current.open(TRIP_X, { at: '2019-07-06T10:00:00Z', follow: { kind: 'caver', id: '1' } }),
    );
    act(() => result.current.transport.toggle());
    // The state this test is about really is there to be dropped.
    expect(result.current.engaged).toBe(true);
    expect(result.current.at).toBe(TEN_2019);
    expect(result.current.follow).toEqual({ kind: 'caver', id: '1' });
    expect(result.current.transport.playing).toBe(true);
    expect(asked).toContainEqual({ token: 'first-link', tripLogId: TRIP_X });

    rerender({ token: 'second-link' });

    expect(result.current.engaged).toBe(false);
    expect(result.current.tripLogId).toBeNull();
    expect(result.current.track).toBeUndefined();
    expect(result.current.at).toBeNull();
    expect(result.current.follow).toBeNull();
    expect(result.current.transport.playing).toBe(false);
    expect(result.current.markerMoveMs).toBeUndefined();
    expect(asked.filter((read) => read.token === 'second-link')).toEqual([]);
  });

  it('opens the same trip under the new link from that trip’s own beginning, not from the moment left under the old one', () => {
    answers[TRIP_X] = { data: trip2019(TRIP_X), isPending: false, isError: false };
    const { result, rerender } = renderHook(({ token }) => usePastTripPlayback(token), {
      initialProps: { token: 'first-link' },
    });
    act(() => result.current.open(TRIP_X, { at: '2019-07-06T10:00:00Z' }));
    expect(result.current.at).toBe(TEN_2019);

    rerender({ token: 'second-link' });
    act(() => result.current.open(TRIP_X));

    expect(result.current.tripLogId).toBe(TRIP_X);
    expect(result.current.at).toBe(START_2019);
    expect(asked).toContainEqual({ token: 'second-link', tripLogId: TRIP_X });
  });

  it('is kept across a render under the same link', () => {
    answers[TRIP_X] = { data: trip2019(TRIP_X), isPending: false, isError: false };
    const { result, rerender } = renderHook(({ token }) => usePastTripPlayback(token), {
      initialProps: { token: 'first-link' },
    });
    act(() => result.current.open(TRIP_X, { at: '2019-07-06T10:00:00Z' }));

    rerender({ token: 'first-link' });

    expect(result.current.tripLogId).toBe(TRIP_X);
    expect(result.current.at).toBe(TEN_2019);
  });
});

describe('how the markers move', () => {
  it('leaves the live party to the viewer’s own slide', () => {
    const { result } = renderHook(() => usePastTripPlayback('follow-token'));

    expect(result.current.markerMoveMs).toBeUndefined();
  });

  it('places them while the replay stands still, slides them while it plays, and places them again under a drag', () => {
    answers[TRIP_X] = { data: trip2019(TRIP_X), isPending: false, isError: false };
    const { result } = renderHook(() => usePastTripPlayback('follow-token'));
    act(() => result.current.open(TRIP_X));
    expect(result.current.markerMoveMs).toBe(0);

    act(() => result.current.transport.toggle());
    expect(result.current.transport.playing).toBe(true);
    expect(result.current.markerMoveMs).toBe(REPLAY_MARKER_MOVE_MS);

    // The drag and the moment it asks for arrive together, and so does the answer: placed.
    act(() => result.current.transport.scrubTo(TEN_2019));
    expect(result.current.at).toBe(TEN_2019);
    expect(result.current.markerMoveMs).toBe(0);

    act(() => result.current.backToNow());
    expect(result.current.markerMoveMs).toBeUndefined();
  });
});
