// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import type { Scene3DCameraState } from '../scene3d/scene3dEngine.ts';
import { FLYBY_VERSION, type FlybyDocument } from './flybyDocument.ts';
import { createFlybyPlayer, type FlybyPlayerState, type FlybyStage } from './flybyPlayer.ts';
import {
  isFlybyAbort,
  prepareFlyby,
  routeStops,
  settleScene,
  type FlybyPrepareStage,
} from './flybyPrepare.ts';
import type { FlybyResolvedSubject, FlybyWorld } from './flybySubjects.ts';
import { compileFlyby, flybyCameraAt, FlybySubjectMissing } from './flybyTimeline.ts';

const cave: FlybyResolvedSubject = {
  centre: { lon: 25, lat: 45.5, height: 900 },
  radius: 200,
  halfHeight: 100,
  coarse: false,
};

const document: FlybyDocument = {
  version: FLYBY_VERSION,
  name: '',
  speed: 1,
  loop: false,
  subjects: [{ kind: 'cave', caveId: 'a' }],
  start: { subject: 0, pose: { heading: 0, pitch: -30, range: 5, lift: 0, clearance: null } },
  sections: [
    { id: 'a', kind: 'approach', subject: 0, seconds: 4, to: { range: 2 } },
    { id: 'o', kind: 'orbit', subject: 0, seconds: 6, to: { headingBy: 180 } },
  ],
};

const HOME: Scene3DCameraState = { longitude: 20, latitude: 40, height: 5000, heading: 10, pitch: -45, roll: 0 };

/** A scene with a clock and a display that only move when the test says so. */
function stage() {
  let now = 0;
  let camera = HOME;
  let handle = 0;
  const frames = new Map<number, () => void>();
  const fake: FlybyStage & {
    advance(milliseconds: number): void;
    camera(): Scene3DCameraState;
    refreshes: number;
    pendingFrames(): number;
  } = {
    getCamera: () => camera,
    setCamera: (next) => {
      camera = next;
    },
    groundHeight: () => 0,
    refreshData: () => {
      fake.refreshes += 1;
    },
    now: () => now,
    requestFrame: (callback) => {
      handle += 1;
      frames.set(handle, callback);
      return handle;
    },
    cancelFrame: (id) => {
      frames.delete(id);
    },
    /** Time passes, then the display draws once. */
    advance(milliseconds) {
      now += milliseconds;
      const due = [...frames.values()];
      frames.clear();
      for (const callback of due) {
        callback();
      }
    },
    camera: () => camera,
    refreshes: 0,
    pendingFrames: () => frames.size,
  };
  return fake;
}

const timeline = compileFlyby(document, [cave]);

describe('playing a flyby', () => {
  it('moves the camera with real time and ends on the last pose', () => {
    const scene = stage();
    const player = createFlybyPlayer(timeline, scene);
    const states: FlybyPlayerState[] = [];
    player.subscribe((state) => states.push(state));
    expect(player.length).toBe(10);
    expect(player.getState()).toEqual({ status: 'idle', t: 0, section: 0 });

    player.play();
    expect(scene.camera()).toEqual(flybyCameraAt(timeline, 0, () => 0));
    scene.advance(2500);
    expect(player.getState()).toEqual({ status: 'playing', t: 2.5, section: 0 });
    expect(scene.camera()).toEqual(flybyCameraAt(timeline, 2.5, () => 0));
    scene.advance(3000);
    expect(player.getState().section).toBe(1);
    scene.advance(60_000);
    expect(player.getState()).toEqual({ status: 'ended', t: 10, section: 1 });
    expect(scene.camera()).toEqual(flybyCameraAt(timeline, 10, () => 0));
    // Nothing is left running once it has ended.
    expect(scene.pendingFrames()).toBe(0);
    expect(states.at(-1)?.status).toBe('ended');

    // Played again, it starts from the beginning.
    player.play();
    expect(player.getState()).toMatchObject({ status: 'playing', t: 0 });
  });

  it('holds still while paused and goes on from the same moment', () => {
    const scene = stage();
    const player = createFlybyPlayer(timeline, scene);
    player.play();
    scene.advance(3000);
    player.pause();
    const held = scene.camera();
    scene.advance(5000);
    expect(player.getState()).toMatchObject({ status: 'paused', t: 3 });
    expect(scene.camera()).toEqual(held);
    player.play();
    scene.advance(1000);
    expect(player.getState().t).toBeCloseTo(4);
  });

  it('can be put at any moment, earlier or later, playing or not', () => {
    const scene = stage();
    const player = createFlybyPlayer(timeline, scene);
    player.seek(7);
    expect(player.getState()).toEqual({ status: 'paused', t: 7, section: 1 });
    expect(scene.camera()).toEqual(flybyCameraAt(timeline, 7, () => 0));
    player.seek(1);
    expect(scene.camera()).toEqual(flybyCameraAt(timeline, 1, () => 0));
    player.seek(99);
    expect(player.getState().t).toBe(10);

    player.seek(2);
    player.play();
    scene.advance(1000);
    player.seek(8);
    expect(player.getState()).toMatchObject({ status: 'playing', t: 8 });
    scene.advance(1000);
    expect(player.getState().t).toBeCloseTo(9);
  });

  it('starts again from the beginning when it loops, without losing time', () => {
    const scene = stage();
    const player = createFlybyPlayer(timeline, scene, { loop: true });
    player.play();
    scene.advance(9500);
    scene.advance(1000);
    expect(player.getState().status).toBe('playing');
    expect(player.getState().t).toBeCloseTo(0.5);
    player.setLoop(false);
    scene.advance(20_000);
    expect(player.getState().status).toBe('ended');
  });

  it('hands the camera back as it found it when stopped', () => {
    const scene = stage();
    const player = createFlybyPlayer(timeline, scene);
    player.play();
    scene.advance(4000);
    expect(scene.camera()).not.toEqual(HOME);
    player.stop();
    expect(scene.camera()).toEqual(HOME);
    expect(player.getState()).toEqual({ status: 'idle', t: 0, section: 0 });
    expect(scene.pendingFrames()).toBe(0);
    // Stopped before it ever moved the camera, it moves nothing.
    const untouched = stage();
    createFlybyPlayer(timeline, untouched).stop();
    expect(untouched.camera()).toEqual(HOME);
  });

  it('has the scene load what is in view now and then, not on every frame', () => {
    const scene = stage();
    const player = createFlybyPlayer(timeline, scene);
    player.play();
    const afterStart = scene.refreshes;
    for (let frame = 0; frame < 200; frame += 1) {
      scene.advance(1000 / 60);
    }
    // A little over three seconds of flight at sixty frames a second: a refresh every second and a half.
    expect(scene.refreshes - afterStart).toBe(2);
    player.pause();
    expect(scene.refreshes - afterStart).toBe(3);
  });

  it('does not have the scene load at every step of a clock being moved by hand, only when it comes to rest', () => {
    const scene = stage();
    const player = createFlybyPlayer(timeline, scene);
    player.seek(1);
    const afterFirst = scene.refreshes;
    // A slider dragged, or an arrow key held: sixty moments in six tenths of a second.
    for (let step = 0; step < 60; step += 1) {
      scene.advance(10);
      player.seek(1 + step * 0.1);
    }
    // Every one of them moved the camera; none of them asked the scene to read itself again.
    expect(scene.camera()).toEqual(flybyCameraAt(timeline, 6.9, () => 0));
    expect(scene.refreshes).toBe(afterFirst);
    // Left alone for a quarter of a second, where it rests is loaded — once.
    for (let frame = 0; frame < 30; frame += 1) {
      scene.advance(16);
    }
    expect(scene.refreshes).toBe(afterFirst + 1);
    for (let frame = 0; frame < 30; frame += 1) {
      scene.advance(16);
    }
    expect(scene.refreshes).toBe(afterFirst + 1);
    expect(scene.pendingFrames()).toBe(0);

    // A clock moved for longer than a flight goes between refreshes still refreshes as it goes.
    for (let step = 0; step < 200; step += 1) {
      scene.advance(10);
      player.seek(step * 0.05);
    }
    expect(scene.refreshes).toBeGreaterThan(afterFirst + 1);
    // And nothing is left waiting once the flyby is stopped.
    player.stop();
    expect(scene.pendingFrames()).toBe(0);
  });

  it('leaves the camera alone when its scene goes away', () => {
    const scene = stage();
    const player = createFlybyPlayer(timeline, scene);
    player.play();
    scene.advance(2000);
    const left = scene.camera();
    player.destroy();
    scene.advance(2000);
    player.seek(5);
    player.play();
    expect(scene.camera()).toEqual(left);
    expect(scene.pendingFrames()).toBe(0);
  });
});

describe('getting a flyby ready', () => {
  function world(overrides: Partial<FlybyWorld> = {}): FlybyWorld {
    return {
      caveInView: () => undefined,
      caveFacts: async () => ({ longitude: 25, latitude: 45.5, depthM: 200, lengthM: 800 }),
      featureBounds: async () => undefined,
      groundHeight: () => 1000,
      viewTarget: () => undefined,
      ...overrides,
    };
  }

  function prepareStage(onSettle?: () => void): FlybyPrepareStage & { cameras: Scene3DCameraState[]; refreshes: number } {
    const fake: FlybyPrepareStage & { cameras: Scene3DCameraState[]; refreshes: number } = {
      cameras: [],
      refreshes: 0,
      setCamera: (camera) => {
        fake.cameras.push(camera);
      },
      refreshData: () => {
        fake.refreshes += 1;
      },
      groundHeight: () => 1000,
      settle: async () => {
        onSettle?.();
        return true;
      },
    };
    return fake;
  }

  it('goes to look at a cave nothing is drawn of, and finds it again from what is then drawn', async () => {
    let drawn = false;
    const scene = prepareStage(() => {
      drawn = true;
    });
    const prepared = await prepareFlyby(
      document,
      world({
        caveInView: () => (drawn ? { caveId: 'a', bounds: [25, 45.5, 25.004, 45.504], top: 990, bottom: 800 } : undefined),
      }),
      scene,
      { loadRoute: false },
    );
    // One visit: the camera put above where the cave is said to be, looking down at it.
    expect(scene.cameras).toHaveLength(1);
    expect(scene.cameras[0].pitch).toBe(-50);
    expect(scene.cameras[0].height).toBeGreaterThan(1000);
    expect(prepared.resolved[0]).toMatchObject({ coarse: false, halfHeight: 95 });
    expect(prepared.timeline.length).toBe(10);
  });

  it('keeps the rough guess for a cave that still draws nothing', async () => {
    const prepared = await prepareFlyby(document, world(), prepareStage(), { loadRoute: false });
    expect(prepared.resolved[0]?.coarse).toBe(true);
  });

  it('visits nothing for a cave already drawn', async () => {
    const scene = prepareStage();
    await prepareFlyby(
      document,
      world({ caveInView: () => ({ caveId: 'a', bounds: [25, 45.5, 25.004, 45.504], top: 990, bottom: 800 }) }),
      scene,
      { loadRoute: false },
    );
    expect(scene.cameras).toHaveLength(0);
  });

  it('visits the route beforehand when asked, and counts what did not finish loading', async () => {
    const progress: string[] = [];
    let waits = 0;
    const scene = prepareStage();
    scene.settle = async () => {
      waits += 1;
      return waits % 2 === 0;
    };
    const prepared = await prepareFlyby(
      document,
      world({ caveInView: () => ({ caveId: 'a', bounds: [25, 45.5, 25.004, 45.504], top: 990, bottom: 800 }) }),
      scene,
      { loadRoute: true, onProgress: (p) => progress.push(`${p.step} ${p.done}/${p.total}`) },
    );
    const stops = routeStops(prepared.timeline);
    // The start, the end of the four-second approach, and the six-second orbit in two steps.
    expect(stops).toEqual([0, 4, 7, 10]);
    expect(scene.cameras).toHaveLength(4);
    expect(scene.refreshes).toBe(4);
    expect(prepared.unsettled).toBe(2);
    expect(progress.at(-1)).toBe('route 4/4');
  });

  it('never visits more than a bounded number of moments, however long the flyby', () => {
    const long = compileFlyby(
      { ...document, sections: [{ id: 'o', kind: 'orbit', subject: 0, seconds: 600, to: { headingBy: 3600 } }] },
      [cave],
    );
    const stops = routeStops(long);
    expect(stops).toHaveLength(40);
    expect(stops[0]).toBe(0);
    expect(stops.at(-1)).toBe(600);
  });

  it('stops when cancelled, and says so differently from failing', async () => {
    const controller = new AbortController();
    const scene = prepareStage(() => controller.abort());
    const failure = await prepareFlyby(document, world(), scene, { loadRoute: true, signal: controller.signal }).catch(
      (error: unknown) => error,
    );
    expect(isFlybyAbort(failure)).toBe(true);
    expect(scene.cameras).toHaveLength(1);
  });

  it('refuses a flyby around a cave this reader cannot read', async () => {
    const failure = await prepareFlyby(document, world({ caveFacts: async () => undefined }), prepareStage(), {
      loadRoute: false,
    }).catch((error: unknown) => error);
    expect(failure).toBeInstanceOf(FlybySubjectMissing);
    expect(isFlybyAbort(failure)).toBe(false);
  });
});

describe('waiting for a scene to finish', () => {
  function scene(settledFrom: number, loadingUntil = 0) {
    let now = 0;
    const fake = {
      frames: 0,
      renders: 0,
      destroyed: false,
      isSettled: () => fake.frames >= settledFrom,
      dataLoading: () => fake.frames < loadingUntil,
      isDestroyed: () => fake.destroyed,
      requestRender: vi.fn(() => {
        fake.renders += 1;
      }),
      nextFrame: async () => {
        fake.frames += 1;
        now += 100;
      },
      now: () => now,
    };
    return fake;
  }

  it('answers once the scene has stayed finished for a moment, asking for a frame each time', async () => {
    const fake = scene(3);
    expect(await settleScene(fake, 10_000)).toBe(true);
    // Unfinished for two frames, then finished and watched for 350 ms more at 100 ms a frame.
    expect(fake.frames).toBe(7);
    expect(fake.renders).toBe(fake.frames);
  });

  it('starts the wait again when data begins to load after the tiles were done', async () => {
    let now = 0;
    let frames = 0;
    const fake = {
      isSettled: () => true,
      // Quiet at first, then a load that starts late and runs for three frames.
      dataLoading: () => frames >= 3 && frames < 6,
      isDestroyed: () => false,
      requestRender: () => {},
      nextFrame: async () => {
        frames += 1;
        now += 100;
      },
      now: () => now,
    };
    expect(await settleScene(fake, 10_000)).toBe(true);
    expect(frames).toBe(10);
  });

  it('gives up when the time runs out, and at once when the scene goes away', async () => {
    expect(await settleScene(scene(1_000_000), 1000)).toBe(false);
    const gone = scene(0);
    gone.destroyed = true;
    expect(await settleScene(gone, 10_000)).toBe(false);
    expect(gone.frames).toBe(0);
  });
});
