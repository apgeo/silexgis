// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { FLYBY_VERSION, flybyProblems, normalizeFlybyDocument, type FlybyDocument, type FlybyFeatureLayer } from './flybyDocument.ts';
import { newFeatureLayer, withoutFeatureLayer } from './flybyEdit.ts';
import { flybyLookAt, flybyLookIsEmpty, flybyLookPlan, type FlybyLookBase } from './flybyLook.ts';
import { createFlybyLookStage, type FlybyLookTarget } from './flybyLookApply.ts';
import { featuresInTurn } from './flybyRecipes.ts';

const SPRINGS: FlybyFeatureLayer = { id: 'f1', source: { kind: 'types', typeIds: [4, 7] }, colour: 'indigo', symbol: 'circle' };
const SAVED: FlybyFeatureLayer = { id: 'f2', source: { kind: 'filter', savedFilterId: 'saved-1' }, colour: 'gold', symbol: 'triangle' };

/** An approach of 6 s and a circle of 24 s: 30 s in all. */
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
    features: [SPRINGS, SAVED],
    ...overrides,
  };
}

const BASE: FlybyLookBase = { backgroundOpacity: 1, surface: 'overlay', layers: {} };

describe('the layers of features a flyby draws itself', () => {
  it('are kept in a file by what they show — kinds or a saved filter — and never by the features', () => {
    const read = normalizeFlybyDocument(JSON.parse(JSON.stringify(flyby())));
    expect(read?.features).toEqual([SPRINGS, SAVED]);
  });

  it('are refused when they show nothing, have a colour the palette does not, or are named and not there', () => {
    expect(flybyProblems(flyby())).toEqual([]);
    expect(flybyProblems(flyby({ features: [{ ...SPRINGS, source: { kind: 'types', typeIds: [] } }] }))).toContainEqual({
      code: 'badFeatures',
      layer: 'f1',
    });
    expect(flybyProblems(flyby({ features: [{ ...SPRINGS, colour: 'red' as never }] }))).toContainEqual({ code: 'badFeatures', layer: 'f1' });
    expect(flybyProblems(flyby({ features: [SPRINGS, { ...SAVED, id: 'f1' }] }))).toContainEqual({ code: 'badFeatures', layer: 'f1' });
    expect(flybyProblems(flyby({ features: [], look: { layers: { 'features:f9': { visible: false } } } }))).toContainEqual({
      code: 'badFeatures',
      layer: 'f9',
    });
    expect(normalizeFlybyDocument({ ...flyby(), features: [{ ...SPRINGS, symbol: 'star' }] })).toBeNull();
  });

  it('are shown from the start unless the look says otherwise, and shown, hidden and faded like any layer', () => {
    const document = flyby({
      look: { layers: { 'features:f2': { visible: false } } },
      changes: [{ id: 'on', kind: 'layer', layer: 'features:f2', visible: true, section: 'orbit-2', at: 0, seconds: 2 }],
    });
    const plan = flybyLookPlan(document);
    expect(flybyLookIsEmpty(flybyLookPlan(flyby({ features: [] })))).toBe(true);
    expect(flybyLookIsEmpty(flybyLookPlan(flyby()))).toBe(false);
    expect(plan.layers).toEqual(['features:f1', 'features:f2']);
    expect(flybyLookAt(plan, BASE, 0).layers).toEqual({
      'features:f1': { visible: true, opacity: 1 },
      'features:f2': { visible: false, opacity: 0 },
    });
    expect(flybyLookAt(plan, BASE, 9).layers['features:f2']).toEqual({ visible: true, opacity: 1 });
  });

  it('are handed to the scene before any of them is shown, and taken away when the flyby lets go', () => {
    const told: string[] = [];
    const target: FlybyLookTarget = {
      viewerLook: () => BASE,
      showBackground: () => undefined,
      showLayer: (key, state) => told.push(`${key} ${state ? state.visible : 'back'}`),
      showFeatureLayers: (layers) => told.push(`layers ${layers ? layers.map((layer) => layer.id).join(',') : 'none'}`),
      showWalls: () => undefined,
      showLines: () => undefined,
      showSurface: () => undefined,
      showSky: () => undefined,
      requestRender: () => undefined,
    };
    const stage = createFlybyLookStage(target);
    stage.show(flybyLookPlan(flyby()), 0);
    expect(told).toEqual(['layers f1,f2', 'features:f1 true', 'features:f2 true']);
    told.length = 0;
    stage.restore();
    expect(told).toEqual(['layers none', 'features:f1 back', 'features:f2 back']);
  });

  it('take the first colour and symbol no other has, and go with every word about them when removed', () => {
    const third = newFeatureLayer([SPRINGS, SAVED], { kind: 'types', typeIds: [2] });
    expect(third).toEqual({ id: 'f3', source: { kind: 'types', typeIds: [2] }, colour: 'violet', symbol: 'circle' });
    const parts = withoutFeatureLayer(
      {
        look: { sky: '#000000', layers: { 'features:f1': { visible: false }, centerlines: { opacity: 0.5 } } },
        changes: [
          { id: 'a', kind: 'layer', layer: 'features:f1', visible: true, section: 'orbit-2', at: 0, seconds: 1 },
          { id: 'b', kind: 'layerOpacity', layer: 'centerlines', opacity: 1, section: 'orbit-2', at: 0, seconds: 1 },
        ],
        features: [SPRINGS, SAVED],
      },
      'f1',
    );
    expect(parts.features).toEqual([SAVED]);
    expect(parts.look).toEqual({ sky: '#000000', layers: { centerlines: { opacity: 0.5 } } });
    expect(parts.changes.map((change) => change.id)).toEqual(['b']);
  });

  it('are offered by the "features in turn" recipe beside the viewer\'s layers from filters', () => {
    const document = flyby();
    const result = featuresInTurn(document, { layers: ['features:f1', 'filter:x', 'features:f2'], secondsEach: 10 });
    const played = flybyLookPlan({ ...document, ...result });
    const shown = (t: number) =>
      Object.entries(flybyLookAt(played, { ...BASE, layers: { 'filter:x': { visible: true, opacity: 1 } } }, t).layers)
        .filter(([, layer]) => layer.visible && layer.opacity > 0.99)
        .map(([key]) => key);
    expect(shown(5)).toEqual(['features:f1']);
    expect(shown(15)).toEqual(['filter:x']);
    expect(shown(25)).toEqual(['features:f2']);
    expect(flybyProblems({ ...document, ...result })).toEqual([]);
  });
});
