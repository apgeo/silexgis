// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { Scene3DCameraState } from '../scene3d/scene3dEngine.ts';
import type { Scene3DPresenterBridge } from '../scene3d/presenterBridge3d.ts';
import { MOVE_SECONDS } from './pointer.ts';
import { createRunner } from './runner.ts';
import type { Scenario } from './scenario.ts';

/**
 * A scenario played by the runner against a stand-in 3D view: the camera kept above the ground it
 * would otherwise pass through, and a pointer step that waits for its target, starts when the
 * target appears, and gives up once it has waited too long. The place and heights are invented.
 */

const GROUND = 5000;
const SPHERE = { longitude: 25, latitude: 45.5, height: 1000, radius: 100 };

let cameras: Scene3DCameraState[];
let now: number;

function standInBridge(): Scene3DPresenterBridge {
  return {
    groundHeight: () => GROUND,
    modelSphere: () => SPHERE,
    setCamera: (state: Scene3DCameraState) => {
      cameras.push(state);
    },
    setModelTint: () => undefined,
  } as unknown as Scene3DPresenterBridge;
}

beforeEach(() => {
  cameras = [];
  now = 0;
  vi.spyOn(performance, 'now').mockImplementation(() => now);
  window.__silexgisPresenter = { scene3d: standInBridge() };
});

afterEach(() => {
  vi.restoreAllMocks();
  delete window.__silexgisPresenter;
  document.body.replaceChildren();
});

describe('the camera of a played scenario', () => {
  it('keeps the eye the asked height above the ground once the move asks for it, and not before', async () => {
    const scenario: Scenario = {
      name: 'invented',
      items: [{ kind: 'camera', at: 0, duration: 10, ease: 'linear', to: { pitch: -5, range: 3, aboveGround: 50 } }],
    };
    const runner = createRunner(scenario, { mode: 'live' });
    await runner.prepare();

    // The opening pose lifts no limit: the eye is where the orbit puts it, under this ground.
    runner.seek(0);
    expect(cameras.at(-1)!.height).toBeLessThan(GROUND);

    runner.seek(10);
    const end = cameras.at(-1)!;
    expect(end.height).toBeCloseTo(GROUND + 50);
    // Lifted above the pivot, it tilts down to keep looking at the same point.
    expect(end.pitch).toBeLessThan(-5);
    runner.destroy();
  });
});

describe('a pointer step whose target is not on the page yet', () => {
  const waitingForTheLayerButton: Scenario = {
    name: 'invented',
    items: [
      { kind: 'camera', at: 0, to: {} },
      { kind: 'layerPanel', at: 0, open: true },
    ],
  };

  it('holds until the target appears, and begins its move then rather than when it was due', async () => {
    const runner = createRunner(waitingForTheLayerButton, { mode: 'live' });
    await runner.prepare();

    runner.seek(0);
    now = 10_000;
    runner.seek(4);
    // The last frame drawn before the target appeared: the waited-for step starts from it.
    runner.seek(5);

    const trigger = document.createElement('button');
    trigger.dataset.testid = 'scene3d-layers-trigger';
    // The test document lays nothing out, so it has no scrolling to do.
    trigger.scrollIntoView = () => undefined;
    const clicked = vi.fn();
    trigger.addEventListener('click', clicked);
    document.body.append(trigger);

    now = 14_000;
    runner.seek(5 + 1 / 30);
    runner.seek(5 + MOVE_SECONDS - 0.05);
    expect(clicked).not.toHaveBeenCalled();
    runner.seek(5 + MOVE_SECONDS + 0.05);
    expect(clicked).toHaveBeenCalledTimes(1);
    runner.destroy();
  });

  it('fails the scenario, naming what it waited for, once it has waited longer than it may', async () => {
    const runner = createRunner(waitingForTheLayerButton, { mode: 'live' });
    await runner.prepare();

    runner.seek(0);
    now = 14_900;
    expect(() => runner.seek(1)).not.toThrow();
    now = 15_100;
    expect(() => runner.seek(2)).toThrow(/the 3D layer button is not on the page/);
    runner.destroy();
  });
});

describe('a scenario that cannot be played', () => {
  it('is refused before anything moves, with every problem named', () => {
    expect(() =>
      createRunner(
        { name: 'invented', items: [{ kind: 'camera', at: -1, to: { range: 0 } }] },
        { mode: 'live' },
      ),
    ).toThrow(/starts before the movie does[\s\S]*range that is not above zero/);
  });
});
