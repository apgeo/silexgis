// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { usePublicPastTrack, type PublicPastTrack, type PublicTripEnvelope } from '../../api/hooks.ts';
import { instantOf } from './publicTripParty.ts';
import {
  firstPlacedMoment,
  pastEnvelopeAt,
  pastReplayWindow,
  pastReportMoments,
  type PastFollow,
} from './pastTrackReplay.ts';
import type { ReplayWindow } from '../../caveview/trackingReplay.ts';
import {
  replayMarkerMoveMs,
  useReplayClock,
  type ReplayClock,
} from '../../caveview/useReplayClock.ts';

/**
 * What a visitor asked to be shown of a cave's past, held in one place for the two surfaces that
 * show it.
 *
 * <b>One state machine, because the followed page and the embedded viewer must behave
 * identically.</b> They are drawn differently on purpose — a page has room for a list, a frame in
 * somebody's article has none — but which trip is playing, which moment it is at, whom it is
 * following and how it is left are the same answers on both, and two copies of that would be two
 * pages of one installation disagreeing about what a link in an article means.
 *
 * <b>Nothing is fetched until a trip is chosen.</b> `tripLogId` null is the whole of the gate: the
 * hook that reads a track is enabled by it, so a page nobody asked for the past on makes exactly
 * the requests it made before this existed. That is not a tuning — the live page is opened by
 * families on phones in numbers nobody can see, and its cost has to stay what it was.
 */
export interface PastTripPlayback {
  /** Which past trip is playing, or null while the live party is what is on screen. */
  tripLogId: string | null;
  /** True once a trip has been asked for, whether or not its track has landed yet. */
  engaged: boolean;
  /** The track as read, or undefined while it is in flight or could not be read. */
  track: PublicPastTrack | undefined;
  loading: boolean;
  /** Set when the chosen trip could not be read: gone, withdrawn, or past its retention. */
  failed: boolean;
  /** The stretch being played, or null when this trip has nothing to play. */
  span: ReplayWindow | null;
  /** Every instant a report was made, for stepping between them. */
  moments: readonly number[];
  /** The moment on the clock, or null before one has been settled on. */
  at: number | null;
  setAt(at: number): void;
  /**
   * The trip at that moment, in the shape the live page already draws — or null when there is
   * nothing to draw yet. See {@link pastEnvelopeAt}.
   */
  envelope: PublicTripEnvelope | null;
  follow: PastFollow | null;
  setFollow(follow: PastFollow | null): void;
  /** Opens a past trip, optionally at a moment and following somebody. */
  open(tripLogId: string, options?: { at?: string | null; follow?: PastFollow | null }): void;
  /** Puts the live party back, and forgets everything about the past that was on screen. */
  backToNow(): void;
  /**
   * The clock the replay runs on — play, pause, speed and the handle.
   *
   * <b>Held here rather than in the strip that draws its controls</b>, because the drawing beside
   * the strip needs one of its answers in the same render the moment changes: whether the clock is
   * playing is what decides whether the markers slide to the new moment or are placed at it, and a
   * value reported upwards from the strip after it rendered arrives one render late — which is
   * exactly the render a drag of the handle happens in.
   */
  transport: ReplayClock;
  /**
   * How long each marker move takes on the drawing, in milliseconds: undefined while the live
   * party is on screen (the viewer's own slide, for a party re-read every half minute), a short
   * slide while the replay plays, and 0 — placed, not animated — for every other move.
   */
  markerMoveMs: number | undefined;
}

export function usePastTripPlayback(token: string | undefined): PastTripPlayback {
  const [tripLogId, setTripLogId] = useState<string | null>(null);
  const [follow, setFollowState] = useState<PastFollow | null>(null);
  const [at, setAt] = useState<number | null>(null);
  /**
   * A moment asked for before there was a track to place it on.
   *
   * A link in an article opens a trip and an instant in one press, and the instant arrives long
   * before the response it belongs to. Held rather than written into `at` immediately, because a
   * moment outside the trip's own stretch has to be clamped into it and nothing knows that stretch
   * until the track lands.
   */
  const [pendingAt, setPendingAt] = useState<number | null>(null);
  // The trip on screen as of the last call, for `open` to decide against without reading state
  // from inside an updater: an updater runs twice under strict rendering, and a decision made
  // there is made twice with side effects each time.
  const tripRef = useRef<string | null>(null);

  const query = usePublicPastTrack(token, tripLogId ?? undefined);
  const track = query.data;
  const failed = tripLogId !== null && query.isError;

  /**
   * A refused trip donates its moment to nobody.
   *
   * A link names a trip and an instant; the trip cannot be read — past its retention, taken back,
   * a placeholder id left in a snippet — and the reader picks another trip from the list. The
   * instant held for the first trip must not be waiting for the second: clamped into a trip years
   * away it lands at the far end of the rail, with everybody out, which reads as a replay that
   * has nothing to show. `open` already forgets it on a change of trip; this forgets it the moment
   * the trip it was asked for has answered that it cannot be played.
   */
  useEffect(() => {
    if (failed) {
      setPendingAt(null);
    }
  }, [failed]);

  const span = useMemo(() => (track === undefined ? null : pastReplayWindow(track)), [track]);
  const moments = useMemo(() => (track === undefined ? [] : pastReportMoments(track)), [track]);

  /**
   * Where a replay opens.
   *
   * The start of the trip, which is the one moment that needs no explanation — unless something
   * asked for somewhere else, in which case there are two askers and they are honoured in order:
   *
   * <b>A moment named by a link wins</b>, clamped into the stretch that actually exists rather than
   * left off the end of the rail where no handle can reach it. A club writing "around midday"
   * should not have to know when the watch was armed.
   *
   * <b>Otherwise, a follow opens where the followed party first appears.</b> At the armed instant
   * nobody has been reported into any team, so "show me the survey team" opened at the start is an
   * empty rail the reader has to think to drag. The start is still one drag away.
   */
  useEffect(() => {
    if (span === null || at !== null || track === undefined) {
      return;
    }
    const asked = pendingAt ?? firstPlacedMoment(track, follow) ?? span.from;
    setAt(Math.min(Math.max(asked, span.from), span.to));
    setPendingAt(null);
  }, [span, at, pendingAt, track, follow]);

  /**
   * Choosing whom to keep up with, which can also move the clock — once, forwards, and only when
   * the alternative is a control that answers with nothing visible.
   *
   * <b>A party asked for before they appear is the ordinary case, not the edge.</b> A replay opens
   * at the trip's beginning, where nobody has been reported anywhere; press "the survey team" there
   * and the honest answer is that they have nowhere to be drawn yet, which on screen is
   * indistinguishable from a control that did not work. So the clock moves to where they first
   * appear.
   *
   * <b>Never backwards.</b> Somebody who has scrubbed to the afternoon and then asks to follow a
   * team is not asking to be taken back to the morning; and a party who appeared earlier and cannot
   * be drawn <em>now</em> — a withheld report, a place measured on another survey — is a true
   * absence the surface already has words for.
   */
  const setFollow = useCallback(
    (next: PastFollow | null) => {
      setFollowState(next);
      if (next === null || track === undefined) {
        return;
      }
      const first = firstPlacedMoment(track, next);
      setAt((current) => (first !== null && current !== null && current < first ? first : current));
    },
    [track],
  );

  /**
   * The trip on screen and the follow rule that goes with its track, as of the last render — for
   * `open` to reach without depending on them.
   *
   * `open` has to stay one function for the life of the page: the followed page re-applies its
   * address whenever `open` changes, and one that changed with every track would send a reader who
   * pressed "back to now" straight back into the past by their own URL. The trip is held beside the
   * rule so that a rule built on one trip's track is never applied to a follow asked of another.
   */
  const playingRef = useRef<{ tripLogId: string | null; setFollow: typeof setFollow }>({
    tripLogId: null,
    setFollow,
  });
  // Written as the render commits rather than after it paints, so that a message arriving in
  // between is never decided against the render before.
  useLayoutEffect(() => {
    playingRef.current = { tripLogId, setFollow };
  }, [tripLogId, setFollow]);

  const open = useCallback(
    (chosen: string, options?: { at?: string | null; follow?: PastFollow | null }) => {
      const changed = tripRef.current !== chosen;
      tripRef.current = chosen;
      setTripLogId(chosen);
      const asked = instantOf(options?.at);
      if (changed) {
        // A different trip starts from its own opening rule, and any moment still held for the
        // trip before it is dropped with that trip: an instant asked for one trip is not an
        // instant in another, and clamped into one it is the end of the rail.
        setAt(null);
        setPendingAt(asked);
      } else if (asked !== null) {
        // Re-opening the trip already playing keeps its clock where the reader left it unless the
        // caller named a moment: a link that says "follow the survey team" should not rewind.
        setPendingAt(asked);
        setAt(null);
      }
      if (options?.follow !== undefined) {
        const playing = playingRef.current;
        if (!changed && asked === null && playing.tripLogId === chosen) {
          // The trip already on screen, with its clock left where the reader put it: a follow
          // named here is the same act as choosing whom to follow on the strip, and moves the
          // clock by the same rule — forwards to where that person first appears, when the replay
          // stands before it. Written straight, it would answer a link naming the trip and a
          // caver with nothing visible, while the same link without the trip moved the clock.
          playing.setFollow(options.follow);
        } else {
          // A trip being opened, or a clock being re-placed by a moment the caller named: there is
          // no track to move against yet, or the clock is about to be placed anyway — the opening
          // rule in the effect above places it for a follow that arrives with its trip.
          setFollowState(options.follow);
        }
      }
    },
    [],
  );

  const backToNow = useCallback(() => {
    tripRef.current = null;
    setTripLogId(null);
    setFollowState(null);
    setAt(null);
    setPendingAt(null);
  }, []);

  const envelope = useMemo(
    () => (track === undefined || at === null ? null : pastEnvelopeAt(track, at)),
    [track, at],
  );

  const engaged = tripLogId !== null;
  const transport = useReplayClock({ span, at, onAtChange: setAt, engaged });

  return {
    tripLogId,
    engaged,
    track,
    loading: tripLogId !== null && query.isPending,
    failed,
    span,
    moments,
    at,
    setAt,
    envelope,
    follow,
    setFollow,
    open,
    backToNow,
    transport,
    markerMoveMs: engaged ? replayMarkerMoveMs(transport.playing) : undefined,
  };
}
