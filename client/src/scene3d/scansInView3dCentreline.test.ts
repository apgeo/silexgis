// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import type { CaveScanInView, CaveScansInView, SurveyModelCentreline } from '../api/hooks.ts';
import type { Scene3DPointCloudOptions } from './scene3dEngine.ts';

// The two server reads this loader makes are stubbed; everything it decides is about what came back.
let listing: CaveScansInView = { items: [], total: 0 };
const centrelinesAsked: string[] = [];
let centrelineAnswer: () => Promise<SurveyModelCentreline> = () => Promise.reject(new Error('none'));

vi.mock('../api/hooks.ts', () => ({
  fetchCaveScansInView: () => Promise.resolve(listing),
  fetchSurveyModelCentreline: (surveyModelId: string) => {
    centrelinesAsked.push(surveyModelId);
    return centrelineAnswer();
  },
}));

const { attachScansInView3d, scanInViewId } = await import('./scansInView3d.ts');
type ScansInView3DEngine = import('./scansInView3d.ts').ScansInView3DEngine;

function scan(caveId: string, hasCentreline: boolean): CaveScanInView {
  return {
    caveId,
    caveName: caveId,
    surveyModelId: `model-${caveId}`,
    modelName: 'scan.laz',
    tilesetUrl: `/tiles/${caveId}/tileset.json`,
    anchorLongitude: 25,
    anchorLatitude: 45,
    anchorHeightM: 1000,
    headingDeg: 30,
    pitchDeg: 0,
    rollDeg: 0,
    pointCount: 1,
    tiledPointCount: 1,
    sizeBytes: 10,
    hasCentreline,
  };
}

/** A scene that records every load it is asked for and lands each at once. */
function fakeScene() {
  const loads: { id: string; options: Scene3DPointCloudOptions }[] = [];
  const engine: ScansInView3DEngine = {
    getVisibleBounds: () => [24.9, 44.9, 25.1, 45.1],
    getPseudoZoom: () => 16,
    onViewChanged: () => () => {},
    loadPointCloud: (id, options) => {
      loads.push({ id, options });
      return Promise.resolve();
    },
    removePointCloud: () => {},
    setModelPlacement: () => {},
  };
  return { engine, loads };
}

const settle = () => new Promise<void>((resolve) => setTimeout(resolve, 0));

async function started(engine: ScansInView3DEngine) {
  const loader = attachScansInView3d(engine, () => undefined, () => ({ maximumScreenSpaceError: 16, cacheBytes: 1 }));
  loader.setLimits({ minZoom: 14, maxCaves: 5, maxBytes: 1000 });
  loader.setActive(true);
  await settle();
  await settle();
  return loader;
}

describe('a placed scan’s centreline on the map', () => {
  it('is read for a scan that has one and handed to the scene in the tiles’ metres, as a re-placing of the same cloud', async () => {
    centrelinesAsked.length = 0;
    listing = { items: [scan('a', true), scan('b', false)], total: 2 };
    centrelineAnswer = () =>
      Promise.resolve({
        surveyModelId: 'model-a',
        frame: 'tiles',
        coordinates: [
          [0, 0, 0],
          [5, 2, -1],
        ],
        lengthM: 5.5,
      });
    const { engine, loads } = fakeScene();
    const loader = await started(engine);

    // Only the scan the server said has a line is asked for one.
    expect(centrelinesAsked).toEqual(['model-a']);
    const ofA = loads.filter((load) => load.id === scanInViewId('a'));
    expect(ofA).toHaveLength(2);
    expect(ofA[0]!.options.centreline).toBeUndefined();
    // The second load is the same tileset under the same id — a move the engine makes without a read.
    expect(ofA[1]!.options.url).toBe(ofA[0]!.options.url);
    expect(ofA[1]!.options.centreline).toEqual([
      { x: 0, y: 0, z: 0 },
      { x: 5, y: 2, z: -1 },
    ]);
    expect(loads.filter((load) => load.id === scanInViewId('b')).every((load) => load.options.centreline === undefined)).toBe(true);
    expect(loader.getState().failed).toBe(0);
    loader.detach();
  });

  it('leaves the scan drawn and not counted as failed when its line cannot be read', async () => {
    centrelinesAsked.length = 0;
    listing = { items: [scan('a', true)], total: 1 };
    centrelineAnswer = () => Promise.reject(new Error('404'));
    const { engine, loads } = fakeScene();
    const loader = await started(engine);

    expect(centrelinesAsked).toEqual(['model-a']);
    expect(loads).toHaveLength(1);
    expect(loader.getState()).toMatchObject({ shown: 1, failed: 0 });
    loader.detach();
  });
});
