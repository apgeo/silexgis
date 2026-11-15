// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import type { MapLayerInfo } from '../../api/hooks.ts';
import { FLYBY_VERSION, type FlybyChange, type FlybyDocument } from '../../flyby/flybyDocument.ts';
import { flybyLookPlan } from '../../flyby/flybyLook.ts';
import { createFlybyLookStage } from '../../flyby/flybyLookApply.ts';
import type { CaveData3DHandle } from '../../scene3d/caveData3d.ts';
import type { FilterLayers3DHandle } from '../../scene3d/filterLayers3d.ts';
import type { GeofileTracks3DHandle } from '../../scene3d/geofileTracks3d.ts';
import type { ScansInView3DHandle } from '../../scene3d/scansInView3d.ts';
import type { Scene3DCore, Scene3DModelTint, Scene3DSurfaceMode } from '../../scene3d/scene3dEngine.ts';
import type { StationPhotos3DHandle } from '../../scene3d/stationPhotos3d.ts';
import type { SurveyMesh3DHandle } from '../../scene3d/surveyMesh3d.ts';
import type { SurveyMeshesInView3DHandle } from '../../scene3d/surveyMeshesInView3d.ts';
import { decodeFilter } from '../../filters/url.ts';
import {
  createScene3DLookTarget,
  drawBackground,
  drawCaveDataLayers,
  drawFilterLayers,
  drawHeldImagery,
  drawTracks,
  drawWalls,
  emptyLookHeld,
  switchedOn,
  type Scene3DLookHeld,
  type Scene3DLookSettings,
} from './scene3dFlybyLook.ts';

const base = (id: number, name: string) => ({ id, name, isBase: true }) as unknown as MapLayerInfo;
const overlay = (id: number) => ({ id, name: `overlay ${id}`, isBase: false }) as unknown as MapLayerInfo;

/** The viewer's settings: street map at 0.9, one overlay on, survey lines dimmed, one filter layer off. */
function settings(): Scene3DLookSettings {
  return {
    catalog: [base(1, 'Street'), base(2, 'Aerial'), overlay(5)],
    activeBaseId: 1,
    baseOpacity: { 1: 0.9 },
    overlayVisible: { entrances: false },
    overlayOpacity: { centerlines: 0.6, 'filter:f1': 0.8, g1: 0.5 },
    visibleTileOverlayIds: [5],
    tileOverlayOpacity: { 5: 0.7 },
    rasters: [],
    visibleRasterIds: [],
    rasterOpacity: {},
    geofiles: [{ id: 'g1', name: 'Track', style: null, importStatus: 'imported' }] as unknown as Scene3DLookSettings['geofiles'],
    visibleGeofileIds: [],
    filterLayers: [{ savedFilterId: 'f1', color: 'red', symbol: 'circle', visible: false }] as unknown as Scene3DLookSettings['filterLayers'],
    unreadableFilterLayers: [],
    surfaceMode: 'overlay',
    wallsMode: 'selected',
    exaggeration: 1,
  };
}

/** A scene and the view's handles, all plain objects that remember what they were last told. */
function world() {
  const imagery = new Map<string, { visible: boolean; opacity: number }>([
    ['base:1', { visible: false, opacity: 1 }],
    ['base:2', { visible: false, opacity: 1 }],
    ['tile-overlay:5', { visible: false, opacity: 1 }],
  ]);
  const state = {
    imagery,
    tint: undefined as Scene3DModelTint | undefined,
    sky: undefined as string | undefined,
    surface: 'overlay' as Scene3DSurfaceMode,
    caveLayers: {} as Record<string, { visible?: boolean; opacity?: number }>,
    lineColour: undefined as string | undefined,
    filterSpecs: undefined as unknown,
    filterOpacity: undefined as unknown,
    files: undefined as string[] | undefined,
    fileOpacity: {} as Record<string, number>,
    meshVisible: undefined as boolean | undefined,
    meshesInViewActive: undefined as boolean | undefined,
    scansActive: undefined as boolean | undefined,
    photosVisible: undefined as boolean | undefined,
    lens: undefined as number | undefined,
    relief: 1,
    featureSpecs: undefined as unknown,
    modelTints: {} as Record<string, Scene3DModelTint | undefined>,
    caveLines: {} as Record<string, string | undefined>,
  };
  const engine = {
    hasImageryLayer: (id: string) => imagery.has(id),
    getImageryLayerIds: () => [...imagery.keys()],
    setImageryLayerVisible: (id: string, visible: boolean) => {
      imagery.get(id)!.visible = visible;
    },
    setImageryLayerOpacity: (id: string, opacity: number) => {
      imagery.get(id)!.opacity = opacity;
    },
    setModelTint: (tint: Scene3DModelTint | undefined) => {
      state.tint = tint;
    },
    setModelTintFor: (ids: readonly string[], tint: Scene3DModelTint | undefined) => {
      for (const id of ids) {
        state.modelTints[id] = tint;
      }
    },
    setBackgroundColor: (css: string | undefined) => {
      state.sky = css;
    },
    setSurfaceMode: (mode: Scene3DSurfaceMode) => {
      state.surface = mode;
    },
    setFieldOfView: (degrees: number | undefined) => {
      state.lens = degrees;
    },
    getFieldOfView: () => state.lens ?? 60,
    setVerticalExaggeration: (factor: number) => {
      state.relief = factor;
    },
    getVerticalExaggeration: () => state.relief,
    requestRender: () => {},
  } as unknown as Scene3DCore;
  const data = {
    setLayerVisible: (layer: string, visible: boolean) => {
      state.caveLayers[layer] = { ...state.caveLayers[layer], visible };
    },
    setLayerOpacity: (layer: string, opacity: number) => {
      state.caveLayers[layer] = { ...state.caveLayers[layer], opacity };
    },
    setLineColour: (colour: string | undefined, caveId?: string) => {
      if (caveId === undefined) {
        state.lineColour = colour;
      } else {
        state.caveLines[caveId] = colour;
      }
    },
  } as unknown as CaveData3DHandle;
  const filterLayers = {
    setLayers: (specs: unknown, _unreadable: unknown, opacity: ReadonlyMap<string, number>) => {
      state.filterSpecs = structuredClone(specs);
      state.filterOpacity = Object.fromEntries(opacity);
    },
  } as unknown as FilterLayers3DHandle;
  const featureLayers = {
    setDrawnLayers: (specs: unknown) => {
      state.featureSpecs = structuredClone(specs);
    },
  } as unknown as FilterLayers3DHandle;
  const tracks = {
    setFiles: (_files: unknown, ids: ReadonlySet<string>) => {
      state.files = [...ids].sort();
    },
    setOpacity: (id: string, opacity: number) => {
      state.fileOpacity[id] = opacity;
    },
  } as unknown as GeofileTracks3DHandle;
  const mesh = {
    // The selected cave's walls are cave c1's.
    getState: () => ({ caveId: 'c1' }),
    setVisible: (visible: boolean) => {
      state.meshVisible = visible;
    },
  } as unknown as SurveyMesh3DHandle;
  const meshesInView = {
    setActive: (active: boolean) => {
      state.meshesInViewActive = active;
    },
  } as unknown as SurveyMeshesInView3DHandle;
  const scans = {
    setActive: (active: boolean) => {
      state.scansActive = active;
    },
  } as unknown as ScansInView3DHandle;
  const photos = {
    setVisible: (visible: boolean) => {
      state.photosVisible = visible;
    },
  } as unknown as StationPhotos3DHandle;
  const viewer = settings();
  const held = emptyLookHeld();
  /** What the view's own effects draw, with whatever a flyby holds at that moment. */
  const viewDraws = (heldNow: Scene3DLookHeld) => {
    drawBackground(engine, viewer, heldNow);
    drawHeldImagery(engine, viewer, heldNow);
    engine.setImageryLayerVisible('tile-overlay:5', viewer.visibleTileOverlayIds.includes(5));
    engine.setImageryLayerOpacity('tile-overlay:5', viewer.tileOverlayOpacity[5] ?? 1);
    drawHeldImagery(engine, viewer, heldNow);
    drawCaveDataLayers(data, viewer, heldNow);
    drawFilterLayers(filterLayers, viewer, heldNow);
    drawTracks(tracks, viewer, heldNow);
    drawWalls(mesh, meshesInView, switchedOn(viewer, heldNow, 'walls'), viewer.wallsMode);
    scans.setActive(switchedOn(viewer, heldNow, 'scans'));
    photos.setVisible(switchedOn(viewer, heldNow, 'photos'));
    engine.setSurfaceMode(heldNow.surface ?? viewer.surfaceMode);
    engine.setVerticalExaggeration(heldNow.relief ?? viewer.exaggeration);
  };
  const target = createScene3DLookTarget(
    {
      engine: () => engine,
      data: () => data,
      filterLayers: () => filterLayers,
      featureLayers: () => featureLayers,
      tracks: () => tracks,
      mesh: () => mesh,
      meshesInView: () => meshesInView,
      scans: () => scans,
      photos: () => photos,
      settings: () => viewer,
    },
    held,
  );
  const snapshot = () => structuredClone({ ...state, imagery: Object.fromEntries(state.imagery) });
  return { state, viewer, held, viewDraws, target, snapshot };
}

function flyby(changes: FlybyChange[]): FlybyDocument {
  return {
    version: FLYBY_VERSION,
    name: '',
    speed: 1,
    loop: false,
    subjects: [{ kind: 'cave', caveId: 'c1' }],
    start: { subject: 0, pose: { heading: 0, pitch: -20, range: 6, lift: 0, clearance: null } },
    sections: [{ id: 'orbit-1', kind: 'orbit', subject: 0, seconds: 20, to: { headingBy: 360 } }],
    look: { sky: '#9db4cc', layers: { entrances: { visible: true } } },
    changes,
  };
}

const at = (id: string, seconds = 4) => ({ id, section: 'orbit-1', at: 2, seconds });

describe('one cave in a colour of its own', () => {
  it('tints the walls loaded for that cave and colours its survey lines, and hands both back', () => {
    const { state, target } = world();
    target.showCave!('c1', { colour: '#ff0000', strength: 0.5 });
    target.showCave!('c2', { colour: '#00ff00', strength: 1 });
    // The walls of every cave in view are under ids of their own; the selected cave's under the one id of its walls.
    expect(state.modelTints).toEqual({
      'survey-mesh-in-view:c1': { red: 1, green: 0, blue: 0, amount: 0.5 },
      'survey-mesh': { red: 1, green: 0, blue: 0, amount: 0.5 },
      'survey-mesh-in-view:c2': { red: 0, green: 1, blue: 0, amount: 1 },
    });
    expect(state.caveLines).toEqual({ c1: '#ff0000', c2: '#00ff00' });
    target.showCave!('c1', undefined);
    expect(state.modelTints['survey-mesh-in-view:c1']).toBeUndefined();
    expect(state.modelTints['survey-mesh']).toBeUndefined();
    expect(state.caveLines.c1).toBeUndefined();
    expect(state.tint).toBeUndefined();
  });
});

describe('the layers of features a flyby draws itself', () => {
  it('asks for them as the viewer\'s layers from filters are asked, shows and hides them, and takes them all away', () => {
    const { state, target } = world();
    const stage = createFlybyLookStage(target);
    const document: FlybyDocument = {
      ...flyby([{ kind: 'layer', layer: 'features:f2', visible: true, ...at('on', 2) }]),
      look: { layers: { 'features:f2': { visible: false } } },
      features: [
        { id: 'f1', source: { kind: 'filter', savedFilterId: 'saved-1' }, colour: 'magenta', symbol: 'square' },
        { id: 'f2', source: { kind: 'types', typeIds: [9, 3] }, colour: 'green', symbol: 'diamond' },
      ],
    };
    const plan = flybyLookPlan(document);
    stage.show(plan, 0);
    const before = state.featureSpecs as { id: string; params: Record<string, string>; visible: boolean; opacity: number }[];
    expect(before.map((spec) => [spec.id, spec.visible, spec.opacity])).toEqual([
      ['flyby-features:f1', true, 1],
      ['flyby-features:f2', false, 0],
    ]);
    expect(before[0].params).toEqual({ savedFilterId: 'saved-1' });
    // The kinds go as a filter document, in the address form every map route reads.
    expect(decodeFilter(before[1].params.filter)?.scope[0]).toEqual({
      world: 'feature',
      where: { node: 'condition', field: 'typeId', op: 'in', values: [{ type: 'id', value: '3' }, { type: 'id', value: '9' }] },
    });
    stage.show(plan, 5);
    expect((state.featureSpecs as { visible: boolean }[]).map((spec) => spec.visible)).toEqual([true, true]);
    // Stopped: nothing of the flyby's own is left in the scene.
    stage.restore();
    expect(state.featureSpecs).toEqual([]);
  });
});

/** Every member a flyby can change, at once. */
const EVERYTHING: FlybyChange[] = [
  { kind: 'background', background: 2, ...at('bg') },
  { kind: 'backgroundOpacity', opacity: 0.5, ...at('op') },
  { kind: 'layer', layer: 'overlay:5', visible: false, ...at('ov') },
  { kind: 'layerOpacity', layer: 'centerlines', opacity: 1, ...at('cl') },
  { kind: 'layer', layer: 'filter:f1', visible: true, ...at('fi') },
  { kind: 'layer', layer: 'file:g1', visible: true, ...at('fl') },
  { kind: 'layer', layer: 'walls', visible: false, ...at('wa') },
  { kind: 'layer', layer: 'scans', visible: false, ...at('sc') },
  { kind: 'layer', layer: 'photos', visible: false, ...at('ph') },
  { kind: 'walls', colour: '#ff8000', strength: 0.8, ...at('wt') },
  { kind: 'lines', colour: '#00ff00', ...at('li', 0) },
  { kind: 'surface', surface: 'cutaway', ...at('su', 0) },
  { kind: 'lens', degrees: 30, ...at('le') },
  { kind: 'relief', factor: 3, ...at('re') },
];

describe('a flyby\'s look on the 3D view\'s scene', () => {
  it('changes everything it names during the flight, writes none of the viewer\'s settings, and leaves the scene as one that never played', () => {
    const untouched = world();
    untouched.viewDraws(untouched.held);
    const neverPlayed = untouched.snapshot();

    const played = world();
    played.viewDraws(played.held);
    const settingsBefore = structuredClone(played.viewer);
    const stage = createFlybyLookStage(played.target);
    const plan = flybyLookPlan(flyby(EVERYTHING));
    stage.show(plan, 0);
    stage.show(plan, 4);
    // Half-way through every change: two backgrounds crossing, the new one higher in the stack.
    expect(played.state.imagery.get('base:1')).toEqual({ visible: true, opacity: 0.7 });
    expect(played.state.imagery.get('base:2')).toEqual({ visible: true, opacity: 0.35 });
    stage.show(plan, 10);
    const flying = played.snapshot();
    expect(flying).toMatchObject({
      imagery: { 'base:1': { visible: false }, 'base:2': { visible: true, opacity: 0.5 }, 'tile-overlay:5': { visible: false } },
      tint: { red: 1, amount: 0.8 },
      sky: '#9db4cc',
      surface: 'cutaway',
      lineColour: '#00ff00',
      caveLayers: { centerlines: { visible: true, opacity: 1 }, entrances: { visible: true } },
      files: ['g1'],
      meshVisible: false,
      scansActive: false,
      photosVisible: false,
      lens: 30,
      relief: 3,
    });
    expect((played.state.filterSpecs as { visible: boolean }[])[0].visible).toBe(true);

    // The viewer's own effects run in the middle of the flight — something changed in the flat
    // map — and the flyby's look holds.
    played.viewDraws(played.held);
    expect(played.snapshot()).toEqual(flying);

    stage.restore();
    expect(played.snapshot()).toEqual(neverPlayed);
    expect(played.viewer).toEqual(settingsBefore);
  });

  it('crosses into a background lower in the stack by fading the one above it out', () => {
    const scene = world();
    scene.viewDraws(scene.held);
    const stage = createFlybyLookStage(scene.target);
    const plan = flybyLookPlan({
      ...flyby([{ kind: 'background', background: 1, ...at('bg') }]),
      look: { background: 2 },
    });
    stage.show(plan, 4);
    expect(scene.state.imagery.get('base:1')).toEqual({ visible: true, opacity: 0.9 });
    expect(scene.state.imagery.get('base:2')!.opacity).toBeCloseTo(0.5, 6);
  });
});

describe('the relief during a flyby', () => {
  it('ramps the scene\'s factor and gives back the viewer\'s own, or the one a saved view brought', () => {
    for (const own of [1, 1.5]) {
      const scene = world();
      // 1.5 stands for a saved view opened in this session: the view's factor in force either way.
      scene.viewer.exaggeration = own;
      scene.viewDraws(scene.held);
      const stage = createFlybyLookStage(scene.target);
      const plan = flybyLookPlan(flyby([{ kind: 'relief', factor: 4, ...at('re') }]));
      stage.show(plan, 0);
      expect(scene.state.relief).toBe(own);
      stage.show(plan, 4);
      expect(scene.state.relief).toBeCloseTo((own + 4) / 2, 6);
      // The view's own effects run meanwhile — a scene rebuilt, a setting changed — and the flyby's factor holds.
      scene.viewDraws(scene.held);
      expect(scene.state.relief).toBeCloseTo((own + 4) / 2, 6);
      stage.show(plan, 10);
      expect(scene.state.relief).toBe(4);
      stage.restore();
      expect(scene.state.relief).toBe(own);
      expect(scene.held.relief).toBeUndefined();
      expect(scene.viewer.exaggeration).toBe(own);
    }
  });
});
