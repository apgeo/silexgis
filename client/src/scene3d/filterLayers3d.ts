// SPDX-License-Identifier: AGPL-3.0-or-later
import { fetchEntranceFeatures, fetchMapFeatures } from '../api/hooks.ts';
import { clusterCellOf } from '../geo/cluster.ts';
import { entranceLabel, surfaceFeatureLabel } from '../map/featureLabels.ts';
import { filterLayerId, type FilterLayerSpec } from '../map/filterLayerList.ts';
import { isMapFilterRefusal } from '../map/mapFilters.ts';
import { filterLayerPalette } from '../map/markerPalette.ts';
import { featuresOf, lineStrings, pointPositions, propertiesOf, stringProperty } from './geoJson3d.ts';
import { filterLayerIcon } from './markerIcons3d.ts';
import { mapZoomFor } from './pseudoZoom.ts';
import {
  pickPayload,
  samePickTarget,
  type ClusterPick,
  type EntrancePick,
  type FeaturePick,
  type Scene3DPickPayload,
} from './selection3d.ts';
import type {
  Scene3DAnchor,
  Scene3DCamera,
  Scene3DMarker,
  Scene3DPolyline,
  Scene3DPosition,
  Scene3DVectorSource,
  Scene3DVectorSources,
} from './scene3dEngine.ts';
import { boundsToBbox } from './viewBounds3d.ts';

// The layers drawn from saved filters, in the scene beside the map.
//
// Each layer asks the same two routes the flat map's copy asks — entrances (clusters below the
// cluster zoom, points above) and surface features — with its saved filter's id, so everything the
// server rules about a filter (who may run it, what it keeps, where a protected entrance is shown)
// applies unchanged, and the scene is handed nothing the flat map is not. It draws them in the
// layer's colour and symbol, and a click on one selects through the same payloads the main layers
// use: an entrance, a feature, or a cluster that lists the members this filter keeps.

/** The parts of the engine this loader touches. */
export interface FilterLayers3DEngine
  extends Pick<Scene3DCamera, 'getVisibleBounds' | 'getPseudoZoom' | 'onViewChanged'>,
    Pick<Scene3DVectorSources, 'createMarkerSource' | 'createPolylineSource'> {}

export interface FilterLayers3DHandle {
  /**
   * Draws exactly these layers: one is added, removed, restyled, switched on or off, faded or
   * emptied (its filter cannot be run for this reader) to match.
   */
  setLayers(
    specs: readonly FilterLayerSpec[],
    unreadable: ReadonlySet<string>,
    opacity: ReadonlyMap<string, number>,
  ): void;
  /** Loads every drawn layer for the current view now. */
  reload(): void;
  /** The payload now drawn for the same thing, if one of these layers still draws it. */
  currentPick(payload: Scene3DPickPayload): Scene3DPickPayload | undefined;
  detach(): void;
}

/** The same quarter of a second the flat map and the scene's own loader wait. */
const SETTLE_MILLISECONDS = 250;

const LINE_WIDTH_PIXELS = 3;

interface Drawn {
  spec: FilterLayerSpec;
  markers: Scene3DVectorSource<Scene3DMarker>;
  lines: Scene3DVectorSource<Scene3DPolyline>;
  drawnMarkers: readonly Scene3DMarker[];
  drawnLines: readonly Scene3DPolyline[];
  requestSeq: number;
  blocked: boolean;
}

export function attachFilterLayers3d(engine: FilterLayers3DEngine): FilterLayers3DHandle {
  const layers = new Map<string, Drawn>();
  let settleTimer: number | undefined;
  let detached = false;

  const key = (savedFilterId: string) => savedFilterId.toLowerCase();

  const empty = (layer: Drawn) => {
    layer.drawnMarkers = [];
    layer.drawnLines = [];
    layer.markers.replace(layer.drawnMarkers);
    layer.lines.replace(layer.drawnLines);
  };

  const loadOne = async (layer: Drawn, bbox: string, zoom: number) => {
    if (!layer.spec.visible || layer.blocked) {
      return;
    }
    const seq = ++layer.requestSeq;
    const params = { savedFilterId: layer.spec.savedFilterId };
    const [entrances, features] = await Promise.allSettled([
      fetchEntranceFeatures(bbox, zoom, params),
      fetchMapFeatures(bbox, params),
    ]);
    if (detached || seq !== layer.requestSeq || layers.get(key(layer.spec.savedFilterId)) !== layer) {
      return;
    }
    if ([entrances, features].some((answer) => answer.status === 'rejected' && isMapFilterRefusal(answer.reason))) {
      // Never the unfiltered layer under the filter's name, and never what it drew before. The
      // flat map's copy of the layer is what says so; this one only stops drawing.
      empty(layer);
      return;
    }
    if (entrances.status !== 'fulfilled' || features.status !== 'fulfilled') {
      return; // a dropped request keeps what was drawn; the next move asks again
    }
    const { color, symbol, savedFilterId } = layer.spec;
    layer.drawnMarkers = [
      ...filterLayerEntranceMarkers(entrances.value, zoom, color, symbol, savedFilterId),
      ...filterLayerFeatureMarkers(features.value, color, symbol),
    ];
    layer.drawnLines = filterLayerFeatureLines(features.value, color);
    layer.markers.replace(layer.drawnMarkers);
    layer.lines.replace(layer.drawnLines);
  };

  const load = () => {
    if (detached || layers.size === 0) {
      return;
    }
    const bounds = engine.getVisibleBounds();
    if (!bounds) {
      return;
    }
    const bbox = boundsToBbox(bounds);
    const zoom = mapZoomFor(engine.getPseudoZoom());
    for (const layer of layers.values()) {
      void loadOne(layer, bbox, zoom);
    }
  };

  const unsubscribeView = engine.onViewChanged(() => {
    window.clearTimeout(settleTimer);
    settleTimer = window.setTimeout(load, SETTLE_MILLISECONDS);
  });

  const remove = (id: string) => {
    const layer = layers.get(id);
    if (layer) {
      layer.requestSeq++;
      layer.markers.remove();
      layer.lines.remove();
      layers.delete(id);
    }
  };

  return {
    setLayers(specs, unreadable, opacity) {
      if (detached) {
        return;
      }
      const wanted = new Set(specs.map((spec) => key(spec.savedFilterId)));
      for (const id of [...layers.keys()]) {
        if (!wanted.has(id)) {
          remove(id);
        }
      }
      const toLoad: Drawn[] = [];
      for (const spec of specs) {
        const id = key(spec.savedFilterId);
        const blocked = unreadable.has(id);
        let layer = layers.get(id);
        if (!layer) {
          // Lines first so a layer's markers are added on top of its own lines.
          const sourceId = filterLayerId(id);
          const lines = engine.createPolylineSource(`${sourceId}:lines`);
          const markers = engine.createMarkerSource(sourceId);
          layer = { spec, markers, lines, drawnMarkers: [], drawnLines: [], requestSeq: 0, blocked };
          layers.set(id, layer);
          toLoad.push(layer);
        }
        const previous = layer.spec;
        layer.spec = spec;
        const restyled = previous.color !== spec.color || previous.symbol !== spec.symbol;
        const unblocked = layer.blocked && !blocked;
        layer.blocked = blocked;
        layer.markers.setVisible(spec.visible);
        layer.lines.setVisible(spec.visible);
        const fade = opacity.get(filterLayerId(spec.savedFilterId)) ?? 1;
        layer.markers.setOpacity(fade);
        layer.lines.setOpacity(fade);
        if (blocked) {
          layer.requestSeq++;
          empty(layer);
        } else if ((restyled || unblocked || (spec.visible && !previous.visible)) && !toLoad.includes(layer)) {
          toLoad.push(layer);
        }
      }
      if (toLoad.length > 0) {
        const bounds = engine.getVisibleBounds();
        if (bounds) {
          const bbox = boundsToBbox(bounds);
          const zoom = mapZoomFor(engine.getPseudoZoom());
          for (const layer of toLoad) {
            void loadOne(layer, bbox, zoom);
          }
        }
      }
    },
    reload: load,
    currentPick(payload) {
      for (const layer of layers.values()) {
        for (const item of layer.drawnMarkers) {
          const current = pickPayload({ id: item.id });
          if (current && samePickTarget(current, payload)) {
            return current;
          }
        }
      }
      return undefined;
    },
    detach() {
      detached = true;
      window.clearTimeout(settleTimer);
      unsubscribeView();
      for (const id of [...layers.keys()]) {
        remove(id);
      }
    },
  };
}

/** A marker dropped onto the ground, anchored where it is drawn — as the main layers' are. */
function onTheGround(position: Scene3DPosition): Scene3DAnchor {
  return { ...position, height: 0, onGround: true };
}

/**
 * Entrance markers of one filter layer: its clusters, each carrying the filter so that opening it
 * lists that filter's members, and its single entrances, which select like the main layer's.
 */
export function filterLayerEntranceMarkers(
  collection: unknown,
  zoom: number,
  color: FilterLayerSpec['color'],
  symbol: FilterLayerSpec['symbol'],
  savedFilterId: string,
): Scene3DMarker[] {
  const fill = filterLayerPalette[color];
  const markers: Scene3DMarker[] = [];
  for (const feature of featuresOf(collection)) {
    const properties = propertiesOf(feature);
    const positions = pointPositions(feature);
    if (properties.cluster === true) {
      const count = typeof properties.count === 'number' ? properties.count : 0;
      const cell = clusterCellOf(properties.cell);
      if (!cell) {
        continue;
      }
      const icon = filterLayerIcon(fill, symbol, { count });
      for (const position of positions) {
        const id: ClusterPick = {
          kind: 'cluster',
          lon: position.longitude,
          lat: position.latitude,
          count,
          zoom,
          cell,
          savedFilterId,
          anchor: onTheGround(position),
        };
        markers.push({ position, clampToGround: true, image: icon.image, scale: icon.scale, id });
      }
      continue;
    }
    const entranceId = stringProperty(properties, 'id');
    const caveId = stringProperty(properties, 'caveId');
    if (!entranceId || !caveId) {
      continue;
    }
    const icon = filterLayerIcon(fill, symbol, { approximate: properties.approximate === true });
    const label = entranceLabel(properties);
    for (const position of positions) {
      const id: EntrancePick = {
        kind: 'entrance',
        entranceId,
        caveId,
        anchor: onTheGround(position),
        ...(label ? { label } : {}),
      };
      markers.push({ position, clampToGround: true, image: icon.image, scale: icon.scale, id });
    }
  }
  return markers;
}

/** The point features of one filter layer, in its colour and symbol. */
export function filterLayerFeatureMarkers(
  collection: unknown,
  color: FilterLayerSpec['color'],
  symbol: FilterLayerSpec['symbol'],
): Scene3DMarker[] {
  const fill = filterLayerPalette[color];
  const markers: Scene3DMarker[] = [];
  for (const feature of featuresOf(collection)) {
    const properties = propertiesOf(feature);
    const featureId = stringProperty(properties, 'id');
    if (!featureId) {
      continue;
    }
    const label = surfaceFeatureLabel(properties);
    const icon = filterLayerIcon(fill, symbol, { approximate: properties.approximate === true });
    for (const position of pointPositions(feature)) {
      const id: FeaturePick = {
        kind: 'feature',
        featureId,
        anchor: onTheGround(position),
        ...(label ? { label } : {}),
      };
      markers.push({ position, clampToGround: true, image: icon.image, scale: icon.scale, id });
    }
  }
  return markers;
}

/** The lines and outlines of one filter layer, laid on the ground in its colour. */
export function filterLayerFeatureLines(collection: unknown, color: FilterLayerSpec['color']): Scene3DPolyline[] {
  const stroke = filterLayerPalette[color];
  const polylines: Scene3DPolyline[] = [];
  for (const feature of featuresOf(collection)) {
    const properties = propertiesOf(feature);
    const featureId = stringProperty(properties, 'id');
    if (!featureId) {
      continue;
    }
    const label = surfaceFeatureLabel(properties);
    for (const positions of lineStrings(feature)) {
      const flattened = positions.map((position) => ({ ...position, height: 0 }));
      const id: FeaturePick = {
        kind: 'feature',
        featureId,
        ...(flattened.length > 0 ? { anchor: onTheGround(flattened[0]) } : {}),
        ...(label ? { label } : {}),
      };
      polylines.push({ positions: flattened, widthPixels: LINE_WIDTH_PIXELS, color: stroke, clampToGround: true, id });
    }
  }
  return polylines;
}
