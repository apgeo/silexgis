// SPDX-License-Identifier: AGPL-3.0-or-later
import type { Scene3DCameraState } from '../scene3d/scene3dEngine.ts';
import type { FlybyLookStage } from './flybyLookApply.ts';
import { flybyCameraAt, flybySectionAt, type FlybyTimeline } from './flybyTimeline.ts';

// Playing a flyby in a scene.
//
// One clock, two ways to move it. `seek(t)` puts the camera where moment `t` has it and returns:
// a position slider calls it, and so does anything that takes one picture per frame, however long
// a frame takes to draw. `play()` moves the same clock with real time. Because a moment depends on
// nothing but its time, the clock can be moved either way and to any place.
//
// The scene is somebody's view, and it is handed back as it was found: the camera it had when the
// flyby first moved it is put back when the flyby is stopped.

/** What a player needs of the scene and of the browser; a test supplies a plain object. */
export interface FlybyStage {
  getCamera(): Scene3DCameraState;
  /** Puts the camera somewhere at once, without a flight of the scene's own, and draws. */
  setCamera(camera: Scene3DCameraState): void;
  groundHeight(longitude: number, latitude: number): number;
  /**
   * Has the scene load what its present view needs. The scene's own loaders wait for the camera to
   * stop, and a camera in flight never does.
   */
  refreshData(): void;
  /** Milliseconds on a clock that only goes forward. */
  now(): number;
  requestFrame(callback: () => void): number;
  cancelFrame(handle: number): void;
  /**
   * Puts a moment's look on the scene and gives the viewer's own back. Left out, a flyby moves the
   * camera and nothing else.
   */
  look?: FlybyLookStage;
}

/**
 * Puts a scene at moment `t` of a flyby.
 *
 * The one place a moment is applied. Whatever plays a flyby and whatever takes pictures of one both
 * come through here, so a frame of a movie shows exactly what the screen showed at that moment —
 * and anything a flyby learns to change besides the camera is added here once, for both.
 */
export function placeFlybyMoment(
  timeline: FlybyTimeline,
  stage: Pick<FlybyStage, 'setCamera' | 'groundHeight' | 'look'>,
  t: number,
): void {
  // The look first, so the frame the camera move asks for is drawn with it.
  stage.look?.show(timeline.look, Math.min(Math.max(t, 0), timeline.length));
  stage.setCamera(flybyCameraAt(timeline, t, stage.groundHeight));
}

export type FlybyPlayerStatus = 'idle' | 'playing' | 'paused' | 'ended';

export interface FlybyPlayerState {
  status: FlybyPlayerStatus;
  /** Seconds from the start. */
  t: number;
  /** The section being flown, by its place in the document. */
  section: number;
}

export interface FlybyPlayer {
  readonly length: number;
  getState(): FlybyPlayerState;
  subscribe(listener: (state: FlybyPlayerState) => void): () => void;
  /** Puts the scene at moment `t` and stays there; playing goes on from it if it was playing. */
  seek(t: number): void;
  /** Plays from where the clock is, or from the start when it had reached the end. */
  play(): void;
  pause(): void;
  /** Ends the flyby and puts the camera, and everything the flyby changed of the look, back as it found them. */
  stop(): void;
  setLoop(loop: boolean): void;
  /** Stops without touching the camera: for a scene that is going away. */
  destroy(): void;
}

/** How often, in real time, a flying camera has the scene load what is now in view. */
const REFRESH_MILLISECONDS = 1500;
/** How long the clock has to be left alone after being moved by hand before its moment counts as where it rests. */
const SEEK_REST_MILLISECONDS = 250;

export interface FlybyPlayerOptions {
  loop?: boolean;
  /**
   * The camera to put back when the flyby is stopped, when the view's own was already moved before
   * the player existed — by the visits that prepared the flyby. Left out, it is whatever camera the
   * scene has when the flyby first moves it.
   */
  home?: Scene3DCameraState;
}

export function createFlybyPlayer(
  timeline: FlybyTimeline,
  stage: FlybyStage,
  options: FlybyPlayerOptions = {},
): FlybyPlayer {
  const listeners = new Set<(state: FlybyPlayerState) => void>();
  let state: FlybyPlayerState = { status: 'idle', t: 0, section: 0 };
  let loop = options.loop ?? false;
  /** The camera the view had before this flyby first moved it. */
  let found: Scene3DCameraState | undefined = options.home;
  let frame: number | undefined;
  /** The real time at which the flyby's clock read zero, for the play in progress. */
  let zeroAt = 0;
  let lastRefresh = Number.NEGATIVE_INFINITY;
  let destroyed = false;

  const publish = (next: Partial<FlybyPlayerState>) => {
    state = { ...state, ...next };
    for (const listener of [...listeners]) {
      listener(state);
    }
  };

  const place = (t: number) => {
    found ??= stage.getCamera();
    placeFlybyMoment(timeline, stage, t);
  };

  const refresh = (force: boolean) => {
    const now = stage.now();
    if (force || now - lastRefresh >= REFRESH_MILLISECONDS) {
      lastRefresh = now;
      stage.refreshData();
    }
  };

  const cancel = () => {
    if (frame !== undefined) {
      stage.cancelFrame(frame);
      frame = undefined;
    }
  };

  // A clock moved by hand is moved many times in a row: a slider being dragged, an arrow key held
  // down. Each step is a new moment, but not a new place to load the scene for — asking every
  // loader to read the view again at each of them is a storm of requests answered too late to
  // matter, and of redraws of the page that asked. So while the clock is being moved the scene is
  // refreshed no more often than in flight, and once more when the clock comes to rest, which is
  // the moment somebody will actually look at.
  let restFrame: number | undefined;
  let lastSeekAt = 0;
  const cancelRest = () => {
    if (restFrame !== undefined) {
      stage.cancelFrame(restFrame);
      restFrame = undefined;
    }
  };
  const refreshAtRest = () => {
    lastSeekAt = stage.now();
    if (restFrame !== undefined) {
      return;
    }
    const wait = () => {
      restFrame = undefined;
      // Playing again, the flight refreshes as it goes; stopped, there is no moment to load for.
      if (destroyed || state.status === 'playing' || state.status === 'idle') {
        return;
      }
      if (stage.now() - lastSeekAt >= SEEK_REST_MILLISECONDS) {
        refresh(true);
        return;
      }
      restFrame = stage.requestFrame(wait);
    };
    restFrame = stage.requestFrame(wait);
  };

  const tick = () => {
    frame = undefined;
    if (destroyed || state.status !== 'playing') {
      return;
    }
    let t = (stage.now() - zeroAt) / 1000;
    if (t >= timeline.length) {
      if (!loop || !(timeline.length > 0)) {
        place(timeline.length);
        refresh(true);
        publish({ status: 'ended', t: timeline.length, section: flybySectionAt(timeline, timeline.length) });
        return;
      }
      // Kept in step with real time rather than restarted at zero, so a loop does not gain a
      // frame's worth of time on every lap.
      t %= timeline.length;
      zeroAt = stage.now() - t * 1000;
    }
    place(t);
    refresh(false);
    publish({ t, section: flybySectionAt(timeline, t) });
    frame = stage.requestFrame(tick);
  };

  return {
    length: timeline.length,
    getState: () => state,
    subscribe(listener) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    seek(t) {
      if (destroyed) {
        return;
      }
      const clamped = Math.min(Math.max(t, 0), timeline.length);
      place(clamped);
      refresh(false);
      if (state.status === 'playing') {
        zeroAt = stage.now() - clamped * 1000;
        publish({ t: clamped, section: flybySectionAt(timeline, clamped) });
      } else {
        // A clock moved by hand is a flyby held at a moment, wherever it stood before.
        publish({ status: 'paused', t: clamped, section: flybySectionAt(timeline, clamped) });
        refreshAtRest();
      }
    },
    play() {
      if (destroyed || state.status === 'playing') {
        return;
      }
      const from = state.status === 'ended' || state.t >= timeline.length ? 0 : state.t;
      zeroAt = stage.now() - from * 1000;
      publish({ status: 'playing', t: from, section: flybySectionAt(timeline, from) });
      place(from);
      refresh(true);
      frame = stage.requestFrame(tick);
    },
    pause() {
      if (state.status !== 'playing') {
        return;
      }
      cancel();
      refresh(true);
      publish({ status: 'paused' });
    },
    stop() {
      cancel();
      cancelRest();
      if (!destroyed) {
        stage.look?.restore();
      }
      if (found && !destroyed) {
        stage.setCamera(found);
        stage.refreshData();
      }
      found = undefined;
      publish({ status: 'idle', t: 0, section: 0 });
    },
    setLoop(next) {
      loop = next;
    },
    destroy() {
      destroyed = true;
      cancel();
      cancelRest();
      listeners.clear();
    },
  };
}
