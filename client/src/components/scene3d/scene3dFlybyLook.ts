// SPDX-License-Identifier: AGPL-3.0-or-later
import type { MapLayerInfo, RasterMapInfo } from '../../api/hooks.ts';
import { flybyFeatureLayerKey, type FlybyFeatureLayer, type FlybyLayerKey } from '../../flyby/flybyDocument.ts';
import { featuresOfTypesDocument } from '../../flyby/flybyFilters.ts';
import { encodeFilter } from '../../filters/url.ts';
import type { FlybyLayerState, FlybyLookBase } from '../../flyby/flybyLook.ts';
import type { FlybyBackgroundShown, FlybyLookTarget } from '../../flyby/flybyLookApply.ts';
import { filterLayerId, type FilterLayerSpec } from '../../map/filterLayerList.ts';
import { baseImageryLayerId, tileOverlayImageryLayerId } from '../../scene3d/baseImagery3d.ts';
import { CAVE_DATA_3D_LAYERS, type CaveData3DHandle } from '../../scene3d/caveData3d.ts';
import type { FilterLayer3DSpec, FilterLayers3DHandle } from '../../scene3d/filterLayers3d.ts';
import type { GeofileTrack3DFile, GeofileTracks3DHandle } from '../../scene3d/geofileTracks3d.ts';
import { prepareRasterImagery, rasterImageryLayerId } from '../../scene3d/rasterOverlay3d.ts';
import { SCANS_IN_VIEW_LAYER_ID, type ScansInView3DHandle } from '../../scene3d/scansInView3d.ts';
import type { Scene3DCore, Scene3DSurfaceMode } from '../../scene3d/scene3dEngine.ts';
import { STATION_PHOTOS_SOURCE_ID, type StationPhotos3DHandle } from '../../scene3d/stationPhotos3d.ts';
import { SURVEY_MESH_LAYER_ID, type SurveyMesh3DHandle } from '../../scene3d/surveyMesh3d.ts';
import {
  surveyMeshInViewModelId,
  type SurveyMeshesInView3DHandle,
  type SurveyWalls3DMode,
} from '../../scene3d/surveyMeshesInView3d.ts';

// A flyby's look, put on the 3D view's own scene.
//
// The view draws the viewer's layer settings into the scene from effects that re-run whenever a
// setting changes — settings the flat map beside it shares. A flyby must not write those settings:
// a fade would flicker the flat map, and thirty writes a second would re-render the page. So it
// holds its own word on the members it names, here, beside the settings; the view's effects and
// the flyby both draw through the functions below, which take the flyby's word where there is one
// and the viewer's setting everywhere else. A setting the viewer changes in the flat map during a
// flyby therefore reaches every member the flyby does not name, and none that it does.
//
// Taking the flyby's word away is giving the viewer's setting back: the same function, run again
// with nothing held, draws exactly what the view would have drawn had no flyby played.

/** What a flyby holds of the scene for as long as it plays. */
export interface Scene3DLookHeld {
  background?: FlybyBackgroundShown;
  layers: Map<FlybyLayerKey, FlybyLayerState>;
  surface?: Scene3DSurfaceMode;
  /** The layers of features the flyby draws itself; none when it draws none, or has stopped. */
  features?: readonly FlybyFeatureLayer[];
  /** The factor the flyby draws the relief at; none when it leaves the relief as the viewer has it. */
  relief?: number;
}

export function emptyLookHeld(): Scene3DLookHeld {
  return { layers: new Map() };
}

/** The viewer's own settings, as the view has them now. */
export interface Scene3DLookSettings {
  catalog: readonly MapLayerInfo[];
  activeBaseId: number | undefined;
  baseOpacity: Readonly<Record<number, number>>;
  overlayVisible: Readonly<Record<string, boolean>>;
  overlayOpacity: Readonly<Record<string, number>>;
  visibleTileOverlayIds: readonly number[];
  tileOverlayOpacity: Readonly<Record<number, number>>;
  rasters: readonly RasterMapInfo[];
  visibleRasterIds: readonly string[];
  rasterOpacity: Readonly<Record<string, number>>;
  geofiles: readonly GeofileTrack3DFile[];
  visibleGeofileIds: readonly string[];
  filterLayers: readonly FilterLayerSpec[];
  unreadableFilterLayers: readonly string[];
  surfaceMode: Scene3DSurfaceMode;
  wallsMode: SurveyWalls3DMode;
  /** The factor the view draws the relief at: a saved view's for this session, else this browser's own. */
  exaggeration: number;
}

/** The factor the relief is to be drawn at: the flyby's while it holds one, else the viewer's. */
export function reliefInForce(settings: Pick<Scene3DLookSettings, 'exaggeration'>, held: Scene3DLookHeld): number {
  return held.relief ?? settings.exaggeration;
}

/** The overlay keys the view keeps the walls', the scans' and the photographs' switches under. */
const NAMED_SWITCHES: Record<string, string> = {
  walls: SURVEY_MESH_LAYER_ID,
  scans: SCANS_IN_VIEW_LAYER_ID,
  photos: STATION_PHOTOS_SOURCE_ID,
};

function rasterOwnOpacity(settings: Scene3DLookSettings, rasterId: string): number {
  const raster = settings.rasters.find((candidate) => candidate.id === rasterId);
  return settings.rasterOpacity[rasterId] ?? Number(raster?.defaultOpacity ?? 1);
}

/** How the viewer has one layer: on or off, and how strongly. */
export function viewerLayer(settings: Scene3DLookSettings, key: FlybyLayerKey): FlybyLayerState {
  const colon = key.indexOf(':');
  const kind = colon < 0 ? key : key.slice(0, colon);
  const id = key.slice(colon + 1);
  switch (kind) {
    case 'overlay':
      return {
        visible: settings.visibleTileOverlayIds.includes(Number(id)),
        opacity: settings.tileOverlayOpacity[Number(id)] ?? 1,
      };
    case 'raster':
      return { visible: settings.visibleRasterIds.includes(id), opacity: rasterOwnOpacity(settings, id) };
    case 'file':
      return { visible: settings.visibleGeofileIds.includes(id), opacity: settings.overlayOpacity[id] ?? 1 };
    case 'filter': {
      const spec = settings.filterLayers.find((candidate) => filterLayerId(candidate.savedFilterId) === key);
      return { visible: spec?.visible ?? false, opacity: settings.overlayOpacity[key] ?? 1 };
    }
    case 'features':
      // The flyby's own: the viewer has no setting for it, and it is the flyby's to show.
      return { visible: true, opacity: 1 };
    default: {
      const switchKey = NAMED_SWITCHES[key] ?? key;
      return { visible: settings.overlayVisible[switchKey] ?? true, opacity: settings.overlayOpacity[key] ?? 1 };
    }
  }
}

/** The layer as it is to be drawn: the flyby's word on it, else the viewer's. */
function layerInForce(settings: Scene3DLookSettings, held: Scene3DLookHeld, key: FlybyLayerKey): FlybyLayerState {
  return held.layers.get(key) ?? viewerLayer(settings, key);
}

/**
 * The backgrounds: the viewer's one at the strength they gave it, or the flyby's — one, or two
 * being crossed. Two are crossed by drawing both and fading whichever lies higher in the scene's
 * stack, so the picture goes from one to the other without passing through no ground at all.
 */
export function drawBackground(engine: Scene3DCore, settings: Scene3DLookSettings, held: Scene3DLookHeld): void {
  const shown = held.background;
  const exists = (id: number | undefined): id is number =>
    id !== undefined && engine.hasImageryLayer(baseImageryLayerId(id));
  const from = exists(shown?.from) ? shown.from : settings.activeBaseId;
  const to = shown && shown.mix > 0 && exists(shown.to) && shown.to !== from ? shown.to : undefined;
  const mix = shown?.mix ?? 0;
  const stack = engine.getImageryLayerIds();
  const toIsHigher =
    to !== undefined &&
    from !== undefined &&
    stack.indexOf(baseImageryLayerId(to)) > stack.indexOf(baseImageryLayerId(from));
  for (const entry of settings.catalog) {
    if (!entry.isBase) {
      continue;
    }
    const id = Number(entry.id);
    const layerId = baseImageryLayerId(id);
    if (!engine.hasImageryLayer(layerId)) {
      continue;
    }
    let opacity = shown?.opacity ?? settings.baseOpacity[id] ?? 1;
    if (id === to && toIsHigher) {
      opacity *= mix;
    } else if (id === from && to !== undefined && !toIsHigher) {
      opacity *= 1 - mix;
    }
    engine.setImageryLayerVisible(layerId, id === from || id === to);
    engine.setImageryLayerOpacity(layerId, opacity);
  }
}

/** One catalogue overlay or georeferenced map, drawn as the flyby or the viewer has it. */
function drawListedImagery(engine: Scene3DCore, settings: Scene3DLookSettings, held: Scene3DLookHeld, key: FlybyLayerKey) {
  const id = key.slice(key.indexOf(':') + 1);
  const layerId = key.startsWith('overlay:') ? tileOverlayImageryLayerId(Number(id)) : rasterImageryLayerId(id);
  if (!engine.hasImageryLayer(layerId)) {
    return;
  }
  const layer = layerInForce(settings, held, key);
  engine.setImageryLayerVisible(layerId, layer.visible);
  engine.setImageryLayerOpacity(layerId, layer.opacity);
}

/** Every overlay and georeferenced map the flyby holds: after the view has drawn the viewer's. */
export function drawHeldImagery(engine: Scene3DCore, settings: Scene3DLookSettings, held: Scene3DLookHeld): void {
  for (const key of held.layers.keys()) {
    if (key.startsWith('overlay:') || key.startsWith('raster:')) {
      drawListedImagery(engine, settings, held, key);
    }
  }
}

export function drawCaveDataLayers(data: CaveData3DHandle, settings: Scene3DLookSettings, held: Scene3DLookHeld): void {
  for (const layer of CAVE_DATA_3D_LAYERS) {
    const state = layerInForce(settings, held, layer);
    data.setLayerVisible(layer, state.visible);
    data.setLayerOpacity(layer, state.opacity);
  }
}

export function drawFilterLayers(
  handle: FilterLayers3DHandle,
  settings: Scene3DLookSettings,
  held: Scene3DLookHeld,
): void {
  const opacity = new Map(Object.entries(settings.overlayOpacity));
  const specs = settings.filterLayers.map((spec) => {
    const key = filterLayerId(spec.savedFilterId);
    const state = held.layers.get(key);
    if (!state) {
      return spec;
    }
    opacity.set(key, state.opacity);
    return { ...spec, visible: state.visible };
  });
  handle.setLayers(specs, new Set(settings.unreadableFilterLayers), opacity);
}

/** The scene source a layer of the flyby's own is drawn into; apart from every layer of the viewer's. */
export function flybyFeatureSourceId(id: string): string {
  return `flyby-features:${id}`;
}

/**
 * The flyby's own layers of features, as the flyby holds them now: each asked for with a saved
 * filter's id or with a document of feature types, exactly the parameters the viewer's layers from
 * saved filters send, and none at all once the flyby has let the scene go.
 */
export function drawFeatureLayers(handle: FilterLayers3DHandle, held: Scene3DLookHeld): void {
  const specs = (held.features ?? []).map((layer): FilterLayer3DSpec => {
    const state = held.layers.get(flybyFeatureLayerKey(layer.id));
    return {
      id: flybyFeatureSourceId(layer.id),
      params:
        layer.source.kind === 'filter'
          ? { savedFilterId: layer.source.savedFilterId }
          : { filter: encodeFilter(featuresOfTypesDocument(layer.source.typeIds)) },
      color: layer.colour,
      symbol: layer.symbol,
      visible: state?.visible ?? false,
      opacity: state?.opacity ?? 0,
      ...(layer.source.kind === 'filter' ? { savedFilterId: layer.source.savedFilterId } : {}),
    };
  });
  handle.setDrawnLayers(specs);
}

export function drawTracks(handle: GeofileTracks3DHandle, settings: Scene3DLookSettings, held: Scene3DLookHeld): void {
  const visible = new Set(settings.visibleGeofileIds);
  for (const file of settings.geofiles) {
    const state = layerInForce(settings, held, `file:${file.id}`);
    if (state.visible) {
      visible.add(file.id);
    } else {
      visible.delete(file.id);
    }
    handle.setOpacity(file.id, state.opacity);
  }
  handle.setFiles(settings.geofiles, visible);
}

/** Whether the walls, the placed scans and the photographs are drawn now. */
export function switchedOn(settings: Scene3DLookSettings, held: Scene3DLookHeld, key: 'walls' | 'scans' | 'photos'): boolean {
  return layerInForce(settings, held, key).visible;
}

/**
 * The walls are one layer asked for in one of two ways, and only one of the two loaders holds
 * anything at a time; the one being left is told first, so its memory is let go before the other
 * starts reading.
 */
export function drawWalls(
  mesh: SurveyMesh3DHandle,
  meshesInView: SurveyMeshesInView3DHandle,
  visible: boolean,
  mode: SurveyWalls3DMode,
): void {
  const everyCaveInView = visible && mode === 'inView';
  if (!everyCaveInView) {
    meshesInView.setActive(false);
  }
  mesh.setVisible(visible && mode === 'selected');
  meshesInView.setActive(everyCaveInView);
}

/** The handles the view holds, read at the moment they are needed: a scene can be rebuilt under a flyby. */
export interface Scene3DLookHandles {
  engine(): Scene3DCore | null;
  data(): CaveData3DHandle | null;
  filterLayers(): FilterLayers3DHandle | null;
  /** The loader a flyby's own layers of features are drawn by, apart from the viewer's layers. */
  featureLayers?(): FilterLayers3DHandle | null;
  tracks(): GeofileTracks3DHandle | null;
  mesh(): SurveyMesh3DHandle | null;
  meshesInView(): SurveyMeshesInView3DHandle | null;
  scans(): ScansInView3DHandle | null;
  photos(): StationPhotos3DHandle | null;
  settings(): Scene3DLookSettings;
}

function channel(hex: string, at: number): number {
  return Number.parseInt(hex.slice(at, at + 2), 16) / 255;
}

function modelTint(tint: { colour: string; strength: number }) {
  return { red: channel(tint.colour, 1), green: channel(tint.colour, 3), blue: channel(tint.colour, 5), amount: tint.strength };
}

/** What a flyby does to the view's scene, through the view's own handles and settings. */
export function createScene3DLookTarget(handles: Scene3DLookHandles, held: Scene3DLookHeld): FlybyLookTarget {
  const drawLayer = (key: FlybyLayerKey) => {
    const settings = handles.settings();
    const engine = handles.engine();
    if (key.startsWith('overlay:') || key.startsWith('raster:')) {
      // Given back, this is the viewer's own — which for a sheet only the flyby had drawn is hidden.
      if (engine) {
        drawListedImagery(engine, settings, held, key);
      }
      return;
    }
    if (key.startsWith('file:')) {
      const tracks = handles.tracks();
      if (tracks) {
        drawTracks(tracks, settings, held);
      }
      return;
    }
    if (key.startsWith('filter:')) {
      const filterLayers = handles.filterLayers();
      if (filterLayers) {
        drawFilterLayers(filterLayers, settings, held);
      }
      return;
    }
    if (key.startsWith('features:')) {
      const featureLayers = handles.featureLayers?.();
      if (featureLayers) {
        drawFeatureLayers(featureLayers, held);
      }
      return;
    }
    if (key === 'walls') {
      const mesh = handles.mesh();
      const meshesInView = handles.meshesInView();
      if (mesh && meshesInView) {
        drawWalls(mesh, meshesInView, switchedOn(settings, held, 'walls'), settings.wallsMode);
      }
      return;
    }
    if (key === 'scans') {
      handles.scans()?.setActive(switchedOn(settings, held, 'scans'));
      return;
    }
    if (key === 'photos') {
      handles.photos()?.setVisible(switchedOn(settings, held, 'photos'));
      return;
    }
    const data = handles.data();
    if (data) {
      drawCaveDataLayers(data, settings, held);
    }
  };

  return {
    viewerLook(layers): FlybyLookBase {
      const settings = handles.settings();
      const engine = handles.engine();
      // The sheets the flyby will show are drawn now, hidden: drawing one takes seconds, and a
      // fade that starts before its sheet exists would show nothing until half-way through.
      for (const key of layers) {
        const raster = key.startsWith('raster:')
          ? settings.rasters.find((candidate) => `raster:${candidate.id}` === key)
          : undefined;
        if (engine && raster) {
          void prepareRasterImagery(engine, raster).then((ready) => {
            const now = handles.engine();
            if (ready && now === engine && held.layers.has(key)) {
              drawListedImagery(now, handles.settings(), held, key);
              now.requestRender();
            }
          });
        }
      }
      return {
        background: settings.activeBaseId,
        backgroundOpacity: settings.activeBaseId === undefined ? 1 : (settings.baseOpacity[settings.activeBaseId] ?? 1),
        surface: settings.surfaceMode,
        ...(engine ? { lens: engine.getFieldOfView() } : {}),
        relief: settings.exaggeration,
        layers: Object.fromEntries(layers.map((key) => [key, viewerLayer(settings, key)])),
      };
    },
    showBackground(shown) {
      held.background = shown;
      const engine = handles.engine();
      if (engine) {
        drawBackground(engine, handles.settings(), held);
      }
    },
    showLayer(key, state) {
      if (state) {
        held.layers.set(key, state);
      } else {
        held.layers.delete(key);
      }
      drawLayer(key);
    },
    showFeatureLayers(layers) {
      held.features = layers;
      const featureLayers = handles.featureLayers?.();
      if (featureLayers) {
        drawFeatureLayers(featureLayers, held);
      }
    },
    showWalls(tint) {
      handles.engine()?.setModelTint(tint ? modelTint(tint) : undefined);
    },
    showCave(caveId, tint) {
      // A cave's walls are loaded under its own id when every cave in view has walls, and under the
      // one id of the selected cave's walls otherwise — which are this cave's only while it is the
      // one selected.
      const ids = [surveyMeshInViewModelId(caveId), ...(handles.mesh()?.getState().caveId === caveId ? [SURVEY_MESH_LAYER_ID] : [])];
      handles.engine()?.setModelTintFor(ids, tint ? modelTint(tint) : undefined);
      handles.data()?.setLineColour(tint?.colour, caveId);
    },
    showLines(colour) {
      handles.data()?.setLineColour(colour);
    },
    showSurface(surface) {
      held.surface = surface;
      handles.engine()?.setSurfaceMode(surface ?? handles.settings().surfaceMode);
    },
    showSky(colour) {
      handles.engine()?.setBackgroundColor(colour);
    },
    showLens(degrees) {
      handles.engine()?.setFieldOfView(degrees);
    },
    showRelief(factor) {
      // Held beside the settings, so a scene rebuilt under the flyby is drawn at the flyby's factor
      // and not at the viewer's; given back, the view's own factor — the very one it applies.
      held.relief = factor;
      handles.engine()?.setVerticalExaggeration(reliefInForce(handles.settings(), held));
    },
    requestRender() {
      handles.engine()?.requestRender();
    },
  };
}
