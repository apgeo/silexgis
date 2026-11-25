// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { FLYBY_VERSION, type FlybyChange, type FlybyDocument } from './flybyDocument.ts';
import { flybyLookPlan, type FlybyLookBase } from './flybyLook.ts';
import { createFlybyLookStage, type FlybyLookTarget } from './flybyLookApply.ts';
import { createFlybyPlayer, type FlybyStage } from './flybyPlayer.ts';
import { compileFlyby } from './flybyTimeline.ts';

function flyby(overrides: Partial<FlybyDocument> = {}): FlybyDocument {
  return {
    version: FLYBY_VERSION,
    name: '',
    speed: 1,
    loop: false,
    subjects: [{ kind: 'cave', caveId: 'c1' }],
    start: { subject: 0, pose: { heading: 0, pitch: -20, range: 6, lift: 0, clearance: null } },
    sections: [
      { id: 'approach-1', kind: 'approach', subject: 0, seconds: 10, to: { range: 3 } },
      { id: 'orbit-2', kind: 'orbit', subject: 0, seconds: 20, to: { headingBy: 360 } },
    ],
    ...overrides,
  };
}

const VIEWER: FlybyLookBase = {
  background: 1,
  backgroundOpacity: 0.9,
  surface: 'overlay',
  layers: { centerlines: { visible: true, opacity: 1 }, 'raster:old': { visible: false, opacity: 0.7 } },
};

/** A scene that remembers what it is showing, and every call that changed it. */
function scene() {
  const calls: string[] = [];
  const shown: Record<string, unknown> = {};
  const record = (member: string, value: unknown) => {
    calls.push(member);
    if (value === undefined) {
      delete shown[member];
    } else {
      shown[member] = value;
    }
  };
  let frames = 0;
  const target: FlybyLookTarget = {
    viewerLook: () => VIEWER,
    showBackground: (value) => record('background', value),
    showLayer: (key, value) => record(`layer:${key}`, value),
    showWalls: (value) => record('walls', value),
    showLines: (value) => record('lines', value),
    showSurface: (value) => record('surface', value),
    showSky: (value) => record('sky', value),
    requestRender: () => {
      frames += 1;
    },
  };
  return { target, calls, shown, frames: () => frames };
}

/** Every member the flyby can change, at once. */
const EVERYTHING: FlybyChange[] = [
  { id: 'bg', kind: 'background', background: 2, section: 'orbit-2', at: 0, seconds: 4 },
  { id: 'op', kind: 'backgroundOpacity', opacity: 0.4, section: 'orbit-2', at: 0, seconds: 4 },
  { id: 'ra', kind: 'layer', layer: 'raster:old', visible: true, section: 'orbit-2', at: 0, seconds: 4 },
  { id: 'cl', kind: 'layerOpacity', layer: 'centerlines', opacity: 0.3, section: 'orbit-2', at: 0, seconds: 4 },
  { id: 'wa', kind: 'walls', colour: '#ff0000', strength: 0.7, section: 'orbit-2', at: 0, seconds: 4 },
  { id: 'li', kind: 'lines', colour: '#00ff00', section: 'orbit-2', at: 0, seconds: 0 },
  { id: 'su', kind: 'surface', surface: 'cutaway', section: 'orbit-2', at: 0, seconds: 0 },
  { id: 'sk', kind: 'sky', colour: '#9db4cc', section: 'orbit-2', at: 0, seconds: 4 },
];

describe('putting a flyby\'s look on a scene', () => {
  it('touches nothing for a flyby that changes nothing of the look', () => {
    const { target, calls, frames } = scene();
    const stage = createFlybyLookStage(target);
    stage.show(flybyLookPlan(flyby()), 5);
    stage.restore();
    expect(calls).toEqual([]);
    expect(frames()).toBe(0);
  });

  it('puts only the members the flyby names, and puts again only what changed', () => {
    const { target, calls, shown } = scene();
    const stage = createFlybyLookStage(target);
    const plan = flybyLookPlan(flyby({ changes: [EVERYTHING[1]] }));
    stage.show(plan, 0);
    expect(calls).toEqual(['background']);
    expect(shown).toEqual({ background: { mix: 0, opacity: 0.9 } });
    // Nothing moves before the change: nothing is put again.
    stage.show(plan, 5);
    stage.show(plan, 9);
    expect(calls).toEqual(['background']);
    stage.show(plan, 12);
    stage.show(plan, 13);
    expect(calls).toEqual(['background', 'background', 'background']);
  });

  it('follows the slider backwards to the opening look', () => {
    const { target, shown } = scene();
    const stage = createFlybyLookStage(target);
    const plan = flybyLookPlan(flyby({ changes: EVERYTHING }));
    stage.show(plan, 0);
    const opening = structuredClone(shown);
    stage.show(plan, 20);
    expect(shown).not.toEqual(opening);
    stage.show(plan, 0);
    expect(shown).toEqual(opening);
  });

  it('gives back everything it changed, and only that, when it is restored', () => {
    const { target, calls, shown, frames } = scene();
    const stage = createFlybyLookStage(target);
    const plan = flybyLookPlan(flyby({ changes: EVERYTHING }));
    stage.show(plan, 12);
    stage.show(plan, 20);
    const before = frames();
    calls.length = 0;
    stage.restore();
    // Every member handed back once, and one frame asked for them all.
    expect([...calls].sort()).toEqual(
      ['background', 'layer:centerlines', 'layer:raster:old', 'lines', 'sky', 'surface', 'walls'].sort(),
    );
    expect(shown).toEqual({});
    expect(frames()).toBe(before + 1);
  });

  it('asks the viewer what they have once, when the flyby first puts a moment on the scene', () => {
    let asked = 0;
    const { target } = scene();
    const stage = createFlybyLookStage({
      ...target,
      viewerLook: (layers) => {
        asked += 1;
        return { ...VIEWER, layers: Object.fromEntries(layers.map((key) => [key, { visible: true, opacity: 1 }])) };
      },
    });
    const plan = flybyLookPlan(flyby({ changes: EVERYTHING }));
    stage.show(plan, 0);
    stage.show(plan, 15);
    stage.show(plan, 3);
    expect(asked).toBe(1);
    stage.restore();
    stage.show(plan, 0);
    expect(asked).toBe(2);
  });
});

describe('a player with a look', () => {
  const cave = { centre: { lon: 25, lat: 45.5, height: 900 }, radius: 200, halfHeight: 100, coarse: false };

  function playerStage() {
    const look = scene();
    let now = 0;
    const frames = new Map<number, () => void>();
    let handle = 0;
    const stage: FlybyStage = {
      getCamera: () => ({ longitude: 1, latitude: 2, height: 3, heading: 0, pitch: -90, roll: 0 }),
      setCamera: () => {},
      groundHeight: () => 0,
      refreshData: () => {},
      now: () => now,
      requestFrame: (callback) => {
        handle += 1;
        frames.set(handle, callback);
        return handle;
      },
      cancelFrame: (id) => {
        frames.delete(id);
      },
      look: createFlybyLookStage(look.target),
    };
    const advance = (milliseconds: number) => {
      now += milliseconds;
      const due = [...frames.values()];
      frames.clear();
      due.forEach((callback) => callback());
    };
    return { stage, look, advance };
  }

  it('changes the look as it plays and scrubs, and puts the scene back as it found it when stopped', () => {
    const { stage, look, advance } = playerStage();
    const player = createFlybyPlayer(compileFlyby(flyby({ changes: EVERYTHING }), [cave]), stage);
    player.play();
    expect(look.shown.sky).toBeUndefined();
    expect(look.shown.lines).toBeUndefined();
    advance(15_000);
    expect(look.shown).toMatchObject({ sky: '#9db4cc', lines: '#00ff00', surface: 'cutaway' });
    player.seek(2);
    expect(look.shown.lines).toBeUndefined();
    expect(look.shown.surface).toBe('overlay');
    player.stop();
    expect(look.shown).toEqual({});
  });
});

describe('the lens of a flyby', () => {
  it('narrows over the time a change takes, from the opening lens, and is given back when stopped', () => {
    const { target, shown } = scene();
    const lenses: (number | undefined)[] = [];
    target.showLens = (degrees) => {
      lenses.push(degrees);
      shown.lens = degrees;
    };
    const document = flyby({
      look: { lens: 50 },
      changes: [{ id: 'le', kind: 'lens', degrees: 20, section: 'orbit-2', at: 0, seconds: 4 }],
    });
    const plan = flybyLookPlan(document);
    const stage = createFlybyLookStage(target);
    stage.show(plan, 5);
    expect(shown.lens).toBe(50);
    stage.show(plan, 12);
    expect(shown.lens).toBeGreaterThan(20);
    expect(shown.lens).toBeLessThan(50);
    stage.show(plan, 20);
    expect(shown.lens).toBe(20);
    // Back before the change: the opening lens again, worked out from the moment alone.
    stage.show(plan, 1);
    expect(shown.lens).toBe(50);
    stage.restore();
    expect(lenses.at(-1)).toBeUndefined();
  });

  it('leaves the lens alone when the flyby says nothing of it, or the view cannot change it', () => {
    const { target, calls } = scene();
    const stage = createFlybyLookStage(target);
    stage.show(flybyLookPlan(flyby({ look: { lens: 30 } })), 3);
    expect(calls).not.toContain('lens');
    const own = scene();
    own.target.showLens = () => own.calls.push('lens');
    createFlybyLookStage(own.target).show(flybyLookPlan(flyby({ look: { sky: '#000000' } })), 3);
    expect(own.calls).not.toContain('lens');
  });
});

describe('the relief of a flyby', () => {
  it('ramps from the viewer\'s factor, puts a factor on the scene only when it changed, and gives the scene back', () => {
    const { target } = scene();
    target.viewerLook = () => ({ ...VIEWER, relief: 1.5 });
    const factors: (number | undefined)[] = [];
    target.showRelief = (factor) => factors.push(factor);
    const document = flyby({
      changes: [{ id: 're', kind: 'relief', factor: 3, section: 'orbit-2', at: 0, seconds: 4 }],
    });
    const plan = flybyLookPlan(document);
    const stage = createFlybyLookStage(target);
    stage.show(plan, 2);
    expect(factors).toEqual([1.5]);
    // Still before the change: nothing new is put on the scene, which a change of the relief redraws whole.
    stage.show(plan, 5);
    expect(factors).toEqual([1.5]);
    stage.show(plan, 12);
    expect(factors.at(-1)).toBeCloseTo(2.25, 6);
    stage.show(plan, 20);
    expect(factors.at(-1)).toBe(3);
    stage.show(plan, 25);
    expect(factors).toHaveLength(3);
    // Dragged back before the change: the viewer's factor again, worked out from the moment alone.
    stage.show(plan, 1);
    expect(factors.at(-1)).toBe(1.5);
    stage.restore();
    expect(factors.at(-1)).toBeUndefined();
  });

  it('leaves the relief alone when the flyby says nothing of it, or the view cannot stretch it', () => {
    const own = scene();
    own.target.showRelief = () => own.calls.push('relief');
    createFlybyLookStage(own.target).show(flybyLookPlan(flyby({ look: { sky: '#000000' } })), 3);
    expect(own.calls).not.toContain('relief');
    const { target, calls } = scene();
    createFlybyLookStage(target).show(flybyLookPlan(flyby({ look: { relief: 2 } })), 3);
    expect(calls).not.toContain('relief');
  });
});
