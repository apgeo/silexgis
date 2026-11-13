// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { FLYBY_VERSION, flybyProblems, type FlybyDocument } from './flybyDocument.ts';
import { flybyLookAt, flybyLookPlan, type FlybyLookBase } from './flybyLook.ts';
import { colourCaves, cycleBackgrounds, featuresInTurn, oldMapNewMap, seeThroughGround } from './flybyRecipes.ts';

/** An approach of 6 s and a circle of 24 s at a speed of 1: 30 s in all. */
function flyby(overrides: Partial<FlybyDocument> = {}): FlybyDocument {
  return {
    version: FLYBY_VERSION,
    name: '',
    speed: 1,
    loop: false,
    subjects: [{ kind: 'cave', caveId: 'c1' }],
    start: { subject: 0, pose: { heading: 0, pitch: -20, range: 6, lift: 0, clearance: 60 } },
    sections: [
      { id: 'approach-1', kind: 'approach', subject: 0, seconds: 6, to: { range: 3 } },
      { id: 'orbit-2', kind: 'orbit', subject: 0, seconds: 24, to: { headingBy: 360 } },
    ],
    ...overrides,
  };
}

const BASE: FlybyLookBase = { background: 9, backgroundOpacity: 1, surface: 'overlay', layers: {} };

function withRecipe(document: FlybyDocument, result: { look: FlybyDocument['look']; changes: FlybyDocument['changes'] }) {
  return { ...document, look: result.look, changes: result.changes };
}

describe('ready-made changes', () => {
  it('cycles the backgrounds, crossing into each in turn until the flyby ends', () => {
    const document = withRecipe(flyby(), cycleBackgrounds(flyby(), { backgrounds: [1, 2, 3], secondsEach: 8, fadeSeconds: 2 }));
    expect(flybyProblems(document)).toEqual([]);
    expect(document.look).toEqual({ background: 1 });
    // At 8, 16 and 24 s: the first in the approach would be at 8, which is 2 s into the circle.
    expect(document.changes!.map((change) => [change.section, change.at, change.seconds])).toEqual([
      ['orbit-2', 2, 2],
      ['orbit-2', 10, 2],
      ['orbit-2', 18, 2],
    ]);
    const plan = flybyLookPlan(document);
    expect(flybyLookAt(plan, BASE, 9).background).toEqual({ from: 1, to: 2, mix: 0.5 });
    expect(flybyLookAt(plan, BASE, 27).background).toEqual({ from: 1, mix: 0 });
    // Done again, it replaces the cycle rather than clashing with it.
    const again = withRecipe(document, cycleBackgrounds(document, { backgrounds: [4, 5], secondsEach: 10, fadeSeconds: 0 }));
    expect(flybyProblems(again)).toEqual([]);
    expect(again.changes!.map((change) => change.kind === 'background' && change.background)).toEqual([5, 4]);
  });

  it('writes its times in the section\'s own seconds, so the changes move with a faster section', () => {
    const fast = flyby();
    fast.sections[1] = { ...fast.sections[1], speed: 2 };
    // The circle lasts 12 s from 6 s; a change at 10 s is 4 s in, which is 8 of its own seconds.
    const document = withRecipe(fast, cycleBackgrounds(fast, { backgrounds: [1, 2], secondsEach: 10, fadeSeconds: 1 }));
    expect(document.changes).toEqual([
      { kind: 'background', background: 2, id: 'background-1', section: 'orbit-2', at: 8, seconds: 2 },
    ]);
    expect(flybyLookPlan(document).changes[0]).toMatchObject({ start: 10, end: 11 });
  });

  it('lets the ground be seen through during each circle, and keeps changes of other things', () => {
    const start = flyby({ changes: [{ id: 'sky-1', kind: 'sky', colour: null, section: 'approach-1', at: 0, seconds: 1 }] });
    const document = withRecipe(start, seeThroughGround(start, { share: 0.4 }));
    expect(flybyProblems(document)).toEqual([]);
    expect(document.changes!.map((change) => change.id)).toEqual(['sky-1', 'backgroundOpacity-1', 'backgroundOpacity-2']);
    const plan = flybyLookPlan(document);
    expect(flybyLookAt(plan, BASE, 18).backgroundOpacity).toBeCloseTo(0.4, 6);
    expect(flybyLookAt(plan, BASE, 30).backgroundOpacity).toBeCloseTo(1, 6);
  });

  it('colours every cave from the start of a section', () => {
    const document = withRecipe(flyby(), colourCaves(flyby(), { colour: '#ff8800', strength: 0.7, section: 'orbit-2' }));
    expect(flybyProblems(document)).toEqual([]);
    const plan = flybyLookPlan(document);
    expect(flybyLookAt(plan, BASE, 5)).toMatchObject({ lines: null });
    expect(flybyLookAt(plan, BASE, 9)).toMatchObject({ lines: '#ff8800', walls: { colour: '#ff8800', strength: 0.7 } });
  });

  it('fades an old map in over the background and out again, hidden when the flyby opens', () => {
    const document = withRecipe(flyby(), oldMapNewMap(flyby(), { raster: 'r1', section: 'orbit-2' }));
    expect(flybyProblems(document)).toEqual([]);
    const plan = flybyLookPlan(document);
    const base = { ...BASE, layers: { 'raster:r1': { visible: true, opacity: 0.8 } } };
    expect(flybyLookAt(plan, base, 0).layers['raster:r1']).toEqual({ visible: false, opacity: 0 });
    expect(flybyLookAt(plan, base, 18).layers['raster:r1']).toEqual({ visible: true, opacity: 0.8 });
    expect(flybyLookAt(plan, base, 30).layers['raster:r1']).toEqual({ visible: false, opacity: 0 });
  });

  it('shows filter layers one after another, the last to the end', () => {
    const document = withRecipe(flyby(), featuresInTurn(flyby(), { layers: ['filter:a', 'filter:b', 'filter:c'], secondsEach: 8 }));
    expect(flybyProblems(document)).toEqual([]);
    const base = { ...BASE, layers: { 'filter:a': { visible: true, opacity: 1 } } };
    const plan = flybyLookPlan(document);
    const shown = (t: number) =>
      Object.entries(flybyLookAt(plan, base, t).layers)
        .filter(([, layer]) => layer.visible && layer.opacity > 0.99)
        .map(([key]) => key);
    expect(shown(4)).toEqual(['filter:a']);
    expect(shown(12)).toEqual(['filter:b']);
    expect(shown(29)).toEqual(['filter:c']);
  });
});
