// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useEffect, useMemo, useState } from 'react';
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

  const query = usePublicPastTrack(token, tripLogId ?? undefined);
  const track = query.data;

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

  const open = useCallback(
    (chosen: string, options?: { at?: string | null; follow?: PastFollow | null }) => {
      setTripLogId((current) => {
        // Re-opening the trip already playing keeps its clock where the reader left it unless the
        // caller named a moment: a link that says "follow the survey team" should not rewind.
        if (current !== chosen) {
          setAt(null);
        }
        return chosen;
      });
      const asked = instantOf(options?.at);
      if (asked !== null) {
        setPendingAt(asked);
        setAt(null);
      }
      if (options?.follow !== undefined) {
        // Written straight rather than through the wrapper above: the track for the trip being
        // opened has not been read yet, so there is no moment to move to — the opening rule in the
        // effect above is what places the clock for a follow that arrives with its trip.
        setFollowState(options.follow);
      }
    },
    [],
  );

  const backToNow = useCallback(() => {
    setTripLogId(null);
    setFollowState(null);
    setAt(null);
    setPendingAt(null);
  }, []);

  const envelope = useMemo(
    () => (track === undefined || at === null ? null : pastEnvelopeAt(track, at)),
    [track, at],
  );

  return {
    tripLogId,
    engaged: tripLogId !== null,
    track,
    loading: tripLogId !== null && query.isPending,
    failed: tripLogId !== null && query.isError,
    span,
    moments,
    at,
    setAt,
    envelope,
    follow,
    setFollow,
    open,
    backToNow,
  };
}
