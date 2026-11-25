// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { flybyProblems, normalizeFlybyDocument, type FlybyDocument } from './flybyDocument.ts';
import { expandFlyby } from './flybyExpand.ts';
import { flybyLookAt, flybyLookPlan, type FlybyLookBase } from './flybyLook.ts';
import { createFlybyLookStage, type FlybyLookTarget } from './flybyLookApply.ts';
import { buildPreset, DEFAULT_FLYBY_PRESET_OPTIONS } from './flybyPresets.ts';
import { lightTheVisited } from './flybyRecipes.ts';

const BASE: FlybyLookBase = { backgroundOpacity: 1, surface: 'overlay', layers: {} };
const LIGHT = { lit: '#ffb000', dim: '#5a6470', dimStrength: 0.75 };

/** A tour of two caves, each a hop of 8 s and a circle of 24 s, and the rise: 72 s. */
function tourOfTwo(): FlybyDocument {
  return buildPreset('tour', [
    { kind: 'cave', caveId: 'c1' },
    { kind: 'cave', caveId: 'c2' },
  ]);
}

describe('a colour for one cave', () => {
  it('is kept in a file by the subject it colours, and refused for a subject that is not a cave', () => {
    const document: FlybyDocument = {
      ...tourOfTwo(),
      changes: [{ id: 'c', kind: 'cave', subject: 1, colour: '#ff0000', strength: 0.5, section: 'orbit-2', at: 0, seconds: 1 }],
    };
    expect(normalizeFlybyDocument(JSON.parse(JSON.stringify(document)))?.changes).toEqual(document.changes);
    expect(flybyProblems(document)).toEqual([]);
    const ofAView: FlybyDocument = { ...document, subjects: [{ kind: 'cave', caveId: 'c1' }, { kind: 'view' }] };
    expect(flybyProblems(ofAView)).toContainEqual({ code: 'badChange', change: 'c', section: 'orbit-2' });
  });

  it('lights the cave visited and dims the others, a stop at a time', () => {
    const document = tourOfTwo();
    const result = lightTheVisited(document, LIGHT);
    expect(result.look).toMatchObject({ walls: { colour: '#5a6470', strength: 0.75 }, lines: '#5a6470' });
    const played: FlybyDocument = { ...document, ...result };
    expect(flybyProblems(played)).toEqual([]);
    const plan = flybyLookPlan(played);
    const caves = (t: number) =>
      Object.fromEntries(Object.entries(flybyLookAt(plan, BASE, t).caves ?? {}).map(([caveId, tint]) => [caveId, tint.strength]));
    // The first stop is the approach and the circle of the first cave; the second, the hop and the circle of the other.
    expect(caves(10)).toEqual({ c1: 0.85, c2: 0 });
    expect(caves(45)).toEqual({ c1: 0, c2: 0.85 });
    expect(caves(70)).toEqual({ c1: 0, c2: 0 });
    // Pressed again, it replaces what it wrote rather than adding to it.
    expect(lightTheVisited(played, LIGHT).changes).toHaveLength(result.changes.length);
  });

  it('is written once for a filter\'s caves, and lights each of them in turn once they are known', () => {
    const document = buildPreset('tour', [{ kind: 'filter', savedFilterId: 'saved-1', limit: 12 }], DEFAULT_FLYBY_PRESET_OPTIONS);
    const written = { ...document, ...lightTheVisited(document, LIGHT) };
    expect(written.changes?.map((change) => `${change.kind}@${change.section}`)).toEqual(['cave@hop-1', 'cave@orbit-2']);
    const expanded = expandFlyby(written, new Map([[0, ['c7', 'c9']]])).document;
    expect(expanded.changes?.map((change) => (change.kind === 'cave' ? `${change.subject}:${change.strength}` : ''))).toEqual([
      '1:0.85',
      '2:0.85',
      '1:0',
      '2:0',
    ]);
    expect(flybyProblems(expanded)).toEqual([]);
    const plan = flybyLookPlan(expanded);
    expect(plan.caveOf).toEqual({ 1: 'c7', 2: 'c9' });
    expect(flybyLookAt(plan, BASE, 10).caves).toMatchObject({ c7: { strength: 0.85 }, c9: { strength: 0 } });
  });

  it('colours every cave of a filter when the change is not in a section flown for each', () => {
    const document: FlybyDocument = {
      ...buildPreset('tour', [{ kind: 'filter', savedFilterId: 'saved-1', limit: 12 }]),
      changes: [{ id: 'all', kind: 'cave', subject: 0, colour: '#00ff00', strength: 1, section: 'rise-3', at: 0, seconds: 1 }],
    };
    const expanded = expandFlyby(document, new Map([[0, ['c7', 'c9']]])).document;
    expect(expanded.changes?.map((change) => `${change.id}>${change.kind === 'cave' ? change.subject : ''}`)).toEqual(['all~1>1', 'all~2>2']);
  });

  it('is put on the scene one cave at a time, and handed back to the colour of the rest', () => {
    const shown: string[] = [];
    const target: FlybyLookTarget = {
      viewerLook: () => BASE,
      showBackground: () => undefined,
      showLayer: () => undefined,
      showWalls: (tint) => shown.push(`walls ${tint?.colour ?? 'own'}`),
      showCave: (caveId, tint) => shown.push(`${caveId} ${tint ? tint.strength : 'back'}`),
      showLines: () => undefined,
      showSurface: () => undefined,
      showSky: () => undefined,
      requestRender: () => undefined,
    };
    const document = tourOfTwo();
    const plan = flybyLookPlan({ ...document, ...lightTheVisited(document, LIGHT) });
    const stage = createFlybyLookStage(target);
    stage.show(plan, 10);
    expect(shown).toEqual(['walls #5a6470', 'c1 0.85', 'c2 back']);
    shown.length = 0;
    stage.restore();
    expect(shown).toEqual(expect.arrayContaining(['walls own', 'c1 back', 'c2 back']));
  });
});
