// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import type { ReplayWindow } from './trackingReplay.ts';

/**
 * The virtual clock a replay runs on — one engine, however many surfaces drive it.
 *
 * <b>Extracted because there are now two replays and there must not be two clocks.</b> A
 * coordinator winds a trip back over its own log; a visitor with no account winds a published trip
 * back over the archive. Those two strips look nothing alike and should not — one carries notes,
 * photographs and a way back to the live watch, the other carries a banner saying you are looking
 * at the past — but the arithmetic under them is identical, and it holds three corrections that
 * were each paid for once and must not be paid for twice.
 *
 * <b>Stopped at the end rather than wrapped.</b> Sliding from the last moment back to the entrance
 * reads as the party going in again.
 *
 * <b>Pressing play at the end starts the trip again</b>, rather than doing nothing visible.
 *
 * <b>The moment is advanced on the ref before it is announced, not after.</b> The ref is otherwise
 * only refreshed when a render delivers the new moment back, and a browser that batches two ticks
 * into one render would hand the second tick the moment the first one started from — a clock that
 * loses time exactly when the machine is busiest.
 */

/**
 * How fast the virtual clock runs against the real one. One is the trip as it happened, which is
 * the only honest anchor and useless for watching; the rest are what makes a day underground
 * something a reader can sit through — at three hundred, eight hours takes a minute and a half.
 */
export const REPLAY_SPEEDS = [1, 10, 60, 300] as const;
export const REPLAY_DEFAULT_SPEED = 60;

/** How often the clock advances while playing. Five steps a second; the markers slide between them. */
const TICK_MS = 200;

/**
 * How long a marker takes to slide to where the next tick puts it, while the replay plays.
 *
 * <b>One tick and a little over, rather than the viewer's own default.</b> The viewer's default
 * move is 600ms, three ticks: at three hundred times life speed each slide then ends three
 * trip-minutes behind the clock, and a move asked for mid-flight supersedes the one under way, so
 * the markers trail the moment printed beside them for the whole playback. Slightly longer than a
 * tick keeps the motion continuous — the next move starts before the last has quite stopped.
 */
export const REPLAY_MARKER_MOVE_MS = 220;

/**
 * How long each marker move of a replay takes: a slide while the clock plays, and none at all
 * otherwise.
 *
 * A drag of the handle, a step to the next report, or a follow that jumps the clock asks where the
 * party <em>was</em> at that moment, not for somewhere to set off towards — so the markers are
 * placed. Animated instead, a drag across the rail is the party racing through the cave behind the
 * reader's finger.
 */
export function replayMarkerMoveMs(playing: boolean): number {
  return playing ? REPLAY_MARKER_MOVE_MS : 0;
}

/** How many places the handle can stop at across the window, whatever the window's length. */
const SCRUB_STEPS = 1000;

/**
 * What a finger needs, chosen on the pointer and not on the width: a phone in landscape is wide
 * enough for the desk layout and still has nothing on it that can hit the ten-pixel handle antd
 * draws by default. Given as component tokens rather than as rules pushed into a stylesheet
 * because every other measurement of the control — where the handle sits on the rail, where a mark
 * sits in it, how tall the strip the rail is tapped in ends up — is derived from these by antd, and
 * a stylesheet that moved one of them by hand would move it out of that arithmetic. The *grab* area
 * around the handle is the one thing a stylesheet does add, since no token describes it.
 */
export const COARSE_SLIDER = {
  handleSize: 20,
  handleSizeHover: 22,
  railSize: 6,
  dotSize: 8,
};

export interface ReplayClockOptions {
  /** The stretch being played, or null when there is nothing to play. */
  span: ReplayWindow | null;
  /** The moment on screen, owned by the caller so the party it draws and this stay one value. */
  at: number | null;
  onAtChange(at: number): void;
  /**
   * Whether the replay is on screen at all. Leaving it stops the clock — a timer still running
   * against a panel that has gone back to the live watch is state nobody can see.
   */
  engaged: boolean;
}

export interface ReplayClock {
  playing: boolean;
  speed: number;
  setSpeed(speed: number): void;
  /** Play, pause, or — pressed at the very end — start the trip again. */
  toggle(): void;
  /**
   * Start the clock from where it stands; nothing at all when it is already running.
   *
   * <b>Not a press of the button, and it differs from one in the two places that matter.</b> It
   * is what something other than the reader asks for — a link that names a moment and says to
   * play from it — so asked twice it must still mean "playing", where a second press would pause;
   * and at the very end it does nothing, where a press starts the trip again: a link naming a
   * trip's last moment was written to show that moment, not to be answered with its first.
   */
  play(): void;
  /** Move the handle, which always stops the clock: a drag is somebody taking the wheel. */
  scrubTo(at: number): void;
  /** How far one step of the handle moves, so the rail has the same feel at every length. */
  step: number;
}

export function useReplayClock({ span, at, onAtChange, engaged }: ReplayClockOptions): ReplayClock {
  const [playing, setPlaying] = useState(false);
  const [speed, setSpeed] = useState<number>(REPLAY_DEFAULT_SPEED);

  // Read by the clock's own timer, which must not be rebuilt every time the moment moves — that
  // would restart the interval five times a second and make the playback rate the render rate.
  const atRef = useRef(at);
  atRef.current = at;

  useEffect(() => {
    if (!engaged) {
      setPlaying(false);
    }
  }, [engaged]);

  useEffect(() => {
    if (!playing || !engaged || span === null) {
      return;
    }
    const timer = setInterval(() => {
      const now = atRef.current ?? span.from;
      const next = Math.min(now + TICK_MS * speed, span.to);
      atRef.current = next;
      onAtChange(next);
      if (next >= span.to) {
        setPlaying(false);
      }
    }, TICK_MS);
    return () => clearInterval(timer);
  }, [playing, engaged, span, speed, onAtChange]);

  return {
    playing,
    speed,
    setSpeed,
    step: span === null ? 1 : Math.max(1, Math.round((span.to - span.from) / SCRUB_STEPS)),
    toggle: () => {
      if (playing) {
        setPlaying(false);
        return;
      }
      if (span !== null && (at ?? span.from) >= span.to) {
        onAtChange(span.from);
      }
      setPlaying(true);
    },
    play: () => {
      if (span !== null && (at ?? span.from) < span.to) {
        setPlaying(true);
      }
    },
    scrubTo: (value: number) => {
      setPlaying(false);
      onAtChange(value);
    },
  };
}
