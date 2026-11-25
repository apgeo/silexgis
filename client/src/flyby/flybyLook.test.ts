// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { FLYBY_VERSION, flybyProblems, normalizeFlybyDocument, type FlybyChange, type FlybyDocument } from './flybyDocument.ts';
import { flybyLookAt, flybyLookPlan, mixFlybyColours, type FlybyLookBase } from './flybyLook.ts';

/** Two sections: an approach of 10 s from 0 to 10, a circle of 20 s from 10 to 30. */
function flyby(overrides: Partial<FlybyDocument> = {}): FlybyDocument {
  return {
    version: FLYBY_VERSION,
    name: '',
    speed: 1,
    loop: false,
    subjects: [{ kind: 'cave', caveId: 'c1' }],
    start: { subject: 0, pose: { heading: 0, pitch: -20, range: 6, lift: 0, clearance: 60 } },
    sections: [
      { id: 'approach-1', kind: 'approach', subject: 0, seconds: 10, to: { range: 3 } },
      { id: 'orbit-2', kind: 'orbit', subject: 0, seconds: 20, to: { headingBy: 360 } },
    ],
    ...overrides,
  };
}

const BASE: FlybyLookBase = {
  background: 1,
  backgroundOpacity: 1,
  surface: 'overlay',
  layers: {
    centerlines: { visible: true, opacity: 1 },
    'filter:f1': { visible: false, opacity: 0.8 },
  },
};

function lookAt(document: FlybyDocument, t: number) {
  return flybyLookAt(flybyLookPlan(document), BASE, t);
}

function change(partial: Partial<FlybyChange> & Pick<FlybyChange, 'kind'>): FlybyChange {
  return { id: 'c', section: 'orbit-2', at: 0, seconds: 4, ...partial } as FlybyChange;
}

describe('the look of a flyby at a moment', () => {
  it('mentions nothing the flyby does not', () => {
    expect(lookAt(flyby(), 5)).toEqual({ layers: {} });
    // Only the background's strength: the layers and the rest are the viewer's.
    const state = lookAt(flyby({ look: { backgroundOpacity: 0.5 } }), 5);
    expect(state).toEqual({ layers: {}, backgroundOpacity: 0.5 });
  });

  it('eases a number from the value in force to its target over the change, and holds it after', () => {
    const document = flyby({ changes: [change({ kind: 'backgroundOpacity', opacity: 0.2, at: 2, seconds: 4 })] });
    // Before the change: the viewer's own strength.
    expect(lookAt(document, 11)).toMatchObject({ backgroundOpacity: 1 });
    // Half-way, eased: exactly half-way between, because the easing is symmetric.
    expect(lookAt(document, 14).backgroundOpacity).toBeCloseTo(0.6, 6);
    // A quarter of the way it has moved less than a quarter: it starts gently.
    expect(1 - lookAt(document, 13).backgroundOpacity!).toBeLessThan(0.8 * 0.25);
    expect(lookAt(document, 16).backgroundOpacity).toBeCloseTo(0.2, 6);
    expect(lookAt(document, 29).backgroundOpacity).toBeCloseTo(0.2, 6);
  });

  it('follows the slider backwards exactly as forwards', () => {
    const document = flyby({
      changes: [
        change({ id: 'a', kind: 'backgroundOpacity', opacity: 0.2, at: 2, seconds: 4 }),
        change({ id: 'b', kind: 'backgroundOpacity', opacity: 0.9, at: 10, seconds: 2 }),
        change({ id: 'c', kind: 'sky', colour: '#336699', at: 0, seconds: 0 }),
      ],
    });
    const forwards = [0, 5, 12, 14, 19, 21, 25, 30].map((t) => lookAt(document, t));
    const backwards = [30, 25, 21, 19, 14, 12, 5, 0].map((t) => lookAt(document, t)).reverse();
    expect(backwards).toEqual(forwards);
    // And a moment after scrubbing back before every change is the opening look.
    expect(lookAt(document, 0)).toEqual({ layers: {}, backgroundOpacity: 1, sky: null });
  });

  it('moves a change with its section when the section is sped up, and stretches it as the section', () => {
    const changes = [change({ kind: 'backgroundOpacity', opacity: 0, at: 4, seconds: 4 })];
    const plain = flybyLookPlan(flyby({ changes }));
    expect(plain.changes[0]).toMatchObject({ start: 14, end: 18 });
    const document = flyby({ changes });
    document.sections[1] = { ...document.sections[1], speed: 2 };
    // The circle starts where it did; the change is twice as early in it and half as long.
    const fast = flybyLookPlan(document);
    expect(fast.changes[0]).toMatchObject({ start: 12, end: 14 });
    // The whole flyby's speed moves every section and every change.
    const quick = flybyLookPlan({ ...flyby({ changes }), speed: 2 });
    expect(quick.changes[0]).toMatchObject({ start: 7, end: 9 });
    expect(lookAt(document, 13).backgroundOpacity).toBeCloseTo(0.5, 6);
  });

  it('crosses from one background into another as two, then shows the new one alone', () => {
    const document = flyby({
      look: { background: 3 },
      changes: [change({ kind: 'background', background: 7, at: 0, seconds: 4 })],
    });
    expect(lookAt(document, 5).background).toEqual({ from: 3, mix: 0 });
    expect(lookAt(document, 12).background).toEqual({ from: 3, to: 7, mix: 0.5 });
    expect(lookAt(document, 14).background).toEqual({ from: 7, mix: 0 });
    // A cut has no in-between.
    const cut = flyby({ changes: [change({ kind: 'background', background: 7, at: 0, seconds: 0 })] });
    expect(lookAt(cut, 9.99).background).toEqual({ from: 1, mix: 0 });
    expect(lookAt(cut, 10).background).toEqual({ from: 7, mix: 0 });
  });

  it('turns a layer on from nothing and a layer off to nothing, hidden only once it has faded', () => {
    const document = flyby({
      changes: [
        change({ id: 'on', kind: 'layer', layer: 'filter:f1', visible: true, at: 0, seconds: 4 }),
        change({ id: 'off', kind: 'layer', layer: 'filter:f1', visible: false, at: 10, seconds: 4 }),
      ],
    });
    // The viewer had it off: off until the change.
    expect(lookAt(document, 5).layers['filter:f1']).toEqual({ visible: false, opacity: 0 });
    // On at the very start of the change, at nothing, rising to the strength the viewer gave it.
    expect(lookAt(document, 10).layers['filter:f1']).toEqual({ visible: true, opacity: 0 });
    expect(lookAt(document, 12).layers['filter:f1'].opacity).toBeCloseTo(0.4, 6);
    expect(lookAt(document, 15).layers['filter:f1']).toEqual({ visible: true, opacity: 0.8 });
    // Fading out it stays drawn until it has gone.
    expect(lookAt(document, 22).layers['filter:f1']).toMatchObject({ visible: true });
    expect(lookAt(document, 22).layers['filter:f1'].opacity).toBeCloseTo(0.4, 6);
    expect(lookAt(document, 24).layers['filter:f1']).toEqual({ visible: false, opacity: 0 });
    // A layer the flyby does not name is not in the look at all.
    expect(lookAt(document, 12).layers.centerlines).toBeUndefined();
  });

  it('opens with the layers the opening look names, and fades a layer\'s strength', () => {
    const document = flyby({
      look: { layers: { centerlines: { opacity: 0.5 } } },
      changes: [change({ kind: 'layerOpacity', layer: 'centerlines', opacity: 1, at: 0, seconds: 2 })],
    });
    expect(lookAt(document, 0).layers.centerlines).toEqual({ visible: true, opacity: 0.5 });
    expect(lookAt(document, 11).layers.centerlines.opacity).toBeCloseTo(0.75, 6);
    expect(lookAt(document, 12).layers.centerlines).toEqual({ visible: true, opacity: 1 });
  });

  it('tints the walls with a colour that rises, changes to another, and gives them back their own', () => {
    const document = flyby({
      changes: [
        change({ id: 'red', kind: 'walls', colour: '#ff0000', strength: 0.8, section: 'approach-1', at: 0, seconds: 2 }),
        change({ id: 'blue', kind: 'walls', colour: '#0000ff', strength: 0.8, at: 0, seconds: 2 }),
        change({ id: 'own', kind: 'walls', colour: '#0000ff', strength: 0, at: 10, seconds: 2 }),
      ],
    });
    // The colour is taken whole from untinted walls; only the strength rises.
    expect(lookAt(document, 1)).toMatchObject({ walls: { colour: '#ff0000' } });
    expect(lookAt(document, 1).walls!.strength).toBeCloseTo(0.4, 6);
    expect(lookAt(document, 11).walls).toEqual({ colour: '#800080', strength: 0.8 });
    expect(lookAt(document, 12).walls).toEqual({ colour: '#0000ff', strength: 0.8 });
    expect(lookAt(document, 21).walls!.colour).toBe('#0000ff');
    expect(lookAt(document, 21).walls!.strength).toBeCloseTo(0.4, 6);
    expect(lookAt(document, 22).walls).toEqual({ colour: '#0000ff', strength: 0 });
  });

  it('cuts the survey lines and the surface at the change\'s moment, whatever length it was given', () => {
    const document = flyby({
      changes: [
        change({ id: 'lines', kind: 'lines', colour: '#ffcc00', at: 2, seconds: 5 }),
        change({ id: 'back', kind: 'lines', colour: null, at: 10, seconds: 0 }),
        change({ id: 'cut', kind: 'surface', surface: 'cutaway', at: 2, seconds: 5 }),
      ],
    });
    expect(lookAt(document, 11.9)).toMatchObject({ lines: null, surface: 'overlay' });
    expect(lookAt(document, 12)).toMatchObject({ lines: '#ffcc00', surface: 'cutaway' });
    expect(lookAt(document, 20)).toMatchObject({ lines: null, surface: 'cutaway' });
  });

  it('fades a sky in from the black of no sky, and out to it', () => {
    const document = flyby({
      changes: [
        change({ id: 'in', kind: 'sky', colour: '#88aacc', at: 0, seconds: 2 }),
        change({ id: 'out', kind: 'sky', colour: null, at: 10, seconds: 2 }),
      ],
    });
    expect(lookAt(document, 5).sky).toBeNull();
    expect(lookAt(document, 11).sky).toBe('#445566');
    expect(lookAt(document, 15).sky).toBe('#88aacc');
    expect(lookAt(document, 21).sky).toBe('#445566');
    expect(lookAt(document, 22).sky).toBeNull();
  });

  it('mixes colours channel by channel', () => {
    expect(mixFlybyColours('#000000', '#ffffff', 0.5)).toBe('#808080');
    expect(mixFlybyColours('#102030', '#102030', 0.3)).toBe('#102030');
  });
});

describe('the look in a document', () => {
  it('is read back from a file as it was written, and a document without it still opens', () => {
    const document = flyby({
      look: {
        background: 4,
        backgroundOpacity: 0.7,
        layers: { 'raster:abc': { visible: true, opacity: 0.5 }, walls: { visible: false } },
        walls: { colour: '#aa3300', strength: 0.6 },
        lines: '#ffffff',
        surface: 'cutaway',
        sky: '#9db4cc',
      },
      changes: [
        change({ id: 'x', kind: 'background', background: 5 }),
        change({ id: 'y', kind: 'lines', colour: null, section: 'approach-1' }),
      ],
      texts: {
        title: { text: 'Peștera', subtitle: 'Bihor', seconds: 4 },
        captions: [{ section: 'orbit-2', text: 'Around it', at: 1, seconds: 4 }],
        end: { text: 'The end', seconds: 3 },
        fadeIn: 1,
        fadeOut: 2,
      },
    });
    expect(normalizeFlybyDocument(JSON.parse(JSON.stringify(document)))).toEqual(document);
    const plain = flyby();
    expect(normalizeFlybyDocument(JSON.parse(JSON.stringify(plain)))).toEqual(plain);
  });

  it('refuses a look, change or text that is not one', () => {
    for (const broken of [
      { look: { sky: 'blue' } },
      { look: { layers: { 'nowhere:1': {} } } },
      { look: { layers: { 'overlay:twelve': {} } } },
      { changes: [{ id: 'a', section: 'orbit-2', at: 0, seconds: 1, kind: 'explode' }] },
      { changes: [{ id: 'a', section: 'orbit-2', at: 0, seconds: 1, kind: 'layer', layer: 'walls' }] },
      { texts: { title: { text: '', seconds: 2 } } },
    ]) {
      expect(normalizeFlybyDocument({ ...flyby(), ...broken })).toBeNull();
    }
  });

  it('names every change that cannot be played, with its section', () => {
    const document = flyby({
      look: { backgroundOpacity: 2 },
      changes: [
        change({ id: 'gone', section: 'nowhere', kind: 'sky', colour: null }),
        change({ id: 'late', section: 'approach-1', at: 11, kind: 'sky', colour: null }),
        change({ id: 'strong', kind: 'backgroundOpacity', opacity: 3 }),
        change({ id: 'late', kind: 'surface', surface: 'cutaway', at: 15 }),
      ],
      texts: { captions: [{ section: 'nowhere', text: 'x', at: 0, seconds: 2 }] },
    });
    expect(flybyProblems(document)).toEqual([
      { code: 'badLook' },
      { code: 'unknownChangeSection', change: 'gone', section: 'nowhere' },
      { code: 'badChange', change: 'late', section: 'approach-1' },
      { code: 'badChange', change: 'strong', section: 'orbit-2' },
      { code: 'duplicateChange', change: 'late', section: 'orbit-2' },
      { code: 'badText', section: 'nowhere' },
    ]);
  });

  it('finds two changes that steer the same thing at once, even across sections', () => {
    const document = flyby({
      changes: [
        change({ id: 'early', section: 'approach-1', at: 8, seconds: 4, kind: 'backgroundOpacity', opacity: 0 }),
        change({ id: 'clash', section: 'orbit-2', at: 1, seconds: 2, kind: 'backgroundOpacity', opacity: 1 }),
        // Another member at the same time is no clash, and neither is the same layer's strength.
        change({ id: 'sky', section: 'orbit-2', at: 1, seconds: 2, kind: 'sky', colour: null }),
        change({ id: 'on', section: 'orbit-2', at: 1, seconds: 2, kind: 'layer', layer: 'walls', visible: true }),
        change({ id: 'fade', section: 'orbit-2', at: 1, seconds: 2, kind: 'layerOpacity', layer: 'walls', opacity: 0.5 }),
        // Two cuts at one moment are a clash; one right after another is not.
        change({ id: 'cut1', section: 'orbit-2', at: 5, kind: 'lines', colour: null }),
        change({ id: 'cut2', section: 'orbit-2', at: 5, kind: 'lines', colour: '#ffffff' }),
        change({ id: 'next', section: 'orbit-2', at: 3, seconds: 2, kind: 'backgroundOpacity', opacity: 0.5 }),
      ],
    });
    expect(flybyProblems(document)).toEqual([
      { code: 'overlappingChanges', change: 'clash', section: 'orbit-2', other: 'early' },
      { code: 'overlappingChanges', change: 'cut2', section: 'orbit-2', other: 'cut1' },
    ]);
    // Speeding the first section up pulls the first change out of the way of the second.
    const quicker = { ...document, changes: document.changes!.filter((c) => !c.id.startsWith('cut')) };
    quicker.sections = [{ ...quicker.sections[0], speed: 2 }, quicker.sections[1]];
    expect(flybyProblems(quicker)).toEqual([]);
  });
});

describe('the relief in a flyby', () => {
  const ramp = (factor: number, partial: Partial<FlybyChange> = {}) =>
    change({ kind: 'relief', factor, ...partial } as Partial<FlybyChange> & Pick<FlybyChange, 'kind'>);

  it('ramps from the viewer\'s factor to the target, eased, and holds it after', () => {
    const document = flyby({ changes: [ramp(3, { at: 2 })] });
    const plan = flybyLookPlan(document);
    const viewer = { ...BASE, relief: 1.5 };
    expect(flybyLookAt(plan, viewer, 0).relief).toBe(1.5);
    expect(flybyLookAt(plan, viewer, 12).relief).toBe(1.5);
    // Half-way through the change, eased half-way between.
    expect(flybyLookAt(plan, viewer, 14).relief).toBeCloseTo(2.25, 6);
    // A quarter of the way, eased: less than a quarter of the rise.
    expect(flybyLookAt(plan, viewer, 13).relief!).toBeLessThan(1.5 + 1.5 * 0.25);
    expect(flybyLookAt(plan, viewer, 16).relief).toBe(3);
    expect(flybyLookAt(plan, viewer, 30).relief).toBe(3);
    // A viewer at true height, said by leaving the factor out.
    expect(flybyLookAt(plan, BASE, 14).relief).toBeCloseTo(2, 6);
  });

  it('opens at a factor of its own, rises and falls back, and scrubbing back gives each moment again', () => {
    const document = flyby({
      look: { relief: 2 },
      changes: [ramp(4, { id: 'up', at: 0, seconds: 5 }), ramp(1, { id: 'down', at: 10, seconds: 5 })],
    });
    const plan = flybyLookPlan(document);
    const viewer = { ...BASE, relief: 1.5 };
    const forwards = [0, 5, 12, 15, 20, 22, 25, 30].map((t) => flybyLookAt(plan, viewer, t).relief);
    expect(forwards[0]).toBe(2);
    expect(forwards[3]).toBe(4);
    expect(forwards[7]).toBe(1);
    const backwards = [30, 25, 22, 20, 15, 12, 5, 0].map((t) => flybyLookAt(plan, viewer, t).relief).reverse();
    expect(backwards).toEqual(forwards);
  });

  it('says nothing of the relief in a flyby that does not mention it', () => {
    expect(flybyLookAt(flybyLookPlan(flyby()), { ...BASE, relief: 2 }, 5).relief).toBeUndefined();
  });

  it('is read back from a file and refused outside the range the scene\'s own setting has', () => {
    const document = flyby({ look: { relief: 1.5 }, changes: [ramp(4, { id: 'relief-1' })] });
    expect(normalizeFlybyDocument(JSON.parse(JSON.stringify(document)))).toEqual(document);
    expect(flybyProblems(document)).toEqual([]);
    // Back to how it opened is written as no factor at all, and read back so.
    const back = flyby({ changes: [ramp(4, { id: 'up' }), { ...ramp(1, { id: 'back', at: 10 }), factor: null } as FlybyChange] });
    expect(normalizeFlybyDocument(JSON.parse(JSON.stringify(back)))).toEqual(back);
    expect(flybyLookAt(flybyLookPlan(back), { ...BASE, relief: 1.5 }, 30).relief).toBe(1.5);
    expect(flybyProblems({ ...document, look: { relief: 6 } })).toContainEqual({ code: 'badLook' });
    expect(flybyProblems({ ...document, changes: [ramp(0.2, { id: 'relief-1' })] })).toContainEqual({
      code: 'badChange',
      change: 'relief-1',
      section: 'orbit-2',
    });
    // Two ramps at once steer one thing.
    expect(
      flybyProblems({ ...document, changes: [ramp(4, { id: 'a' }), ramp(2, { id: 'b', at: 1 })] }).map((problem) => problem.code),
    ).toContain('overlappingChanges');
  });
});
