// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { PublicPastTrack } from '../../api/hooks.ts';
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

let answers: Record<string, { data?: PublicPastTrack; isPending: boolean; isError: boolean }>;

vi.mock('../../api/hooks.ts', () => ({
  usePublicPastTrack: (_token: string | undefined, tripLogId: string | undefined) =>
    tripLogId === undefined
      ? { data: undefined, isPending: false, isError: false }
      : (answers[tripLogId] ?? { data: undefined, isPending: true, isError: false }),
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
