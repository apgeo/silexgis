// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import type { FeatureLike } from 'ol/Feature';
import GeoJSON from 'ol/format/GeoJSON';
import VectorLayer from 'ol/layer/Vector';
import { transformExtent } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Circle as CircleStyle, Fill, Icon, Stroke, Style } from 'ol/style';
import { fetchSurfaceFeatureCollection, type FeatureType } from '../api/hooks.ts';
import { getMapTagFilter } from './mapFilters.ts';

export const SURFACE_FEATURE_LAYER_ID = 'surface-features';

const source = new VectorSource();
const format = new GeoJSON();

/** The live feature source — the edit controller draws into and snaps against it. */
export function getSurfaceFeatureSource(): VectorSource {
  return source;
}

// featureTypeId → symbol file / display name, fed from the /feature-types
// catalog by the map page. Names back the hover tooltip for unnamed features.
let symbolByTypeId = new globalThis.Map<number, string>();
let nameByTypeId = new globalThis.Map<number, string>();
const iconCache = new globalThis.Map<string, Icon>();

export function setFeatureTypeSymbols(types: FeatureType[]): void {
  symbolByTypeId = new globalThis.Map(
    types.filter((t) => t.symbolFile).map((t) => [Number(t.id), t.symbolFile!]),
  );
  nameByTypeId = new globalThis.Map(types.map((t) => [Number(t.id), t.name]));
  source.changed(); // restyle already-loaded features with the fresh catalog
}

export function getFeatureTypeName(featureTypeId: unknown): string | undefined {
  return nameByTypeId.get(Number(featureTypeId));
}

// Selection highlight: the styling reads this id so the highlight survives
// bbox reloads (features are recreated, the id is stable).
let selectedFeatureId: string | null = null;

export function setSelectedSurfaceFeature(id: string | null): void {
  if (selectedFeatureId !== id) {
    selectedFeatureId = id;
    source.changed();
  }
}

export function createSurfaceFeatureLayer(): VectorLayer {
  const layer = new VectorLayer({ source, zIndex: 9, style: featureStyle });
  layer.set('id', SURFACE_FEATURE_LAYER_ID);
  return layer;
}

let activeReload: (() => void) | undefined;

/** Forces a refetch of the current extent (e.g. after saving edits). */
export function reloadSurfaceFeatures(): void {
  activeReload?.();
}

/** Bbox loading on moveend (debounced), stale responses discarded; returns a detach fn. */
export function attachSurfaceFeatureLoader(map: Map): () => void {
  let requestSeq = 0;
  let timer: number | undefined;

  const load = async () => {
    const view = map.getView();
    const size = map.getSize();
    if (!size) {
      return;
    }
    const extent = transformExtent(view.calculateExtent(size), 'EPSG:3857', 'EPSG:4326');
    const bbox = extent.map((n) => n.toFixed(5)).join(',');
    const seq = ++requestSeq;
    try {
      const collection = await fetchSurfaceFeatureCollection(bbox, getMapTagFilter() ?? undefined);
      if (seq !== requestSeq) {
        return; // a newer request superseded this one
      }
      source.clear(true);
      source.addFeatures(format.readFeatures(collection, { featureProjection: 'EPSG:3857' }));
    } catch {
      // Keep previous features on transient errors; next moveend retries.
    }
  };

  const onMoveEnd = () => {
    window.clearTimeout(timer);
    timer = window.setTimeout(() => void load(), 250);
  };

  map.on('moveend', onMoveEnd);
  void load();
  activeReload = () => void load();
  return () => {
    map.un('moveend', onMoveEnd);
    window.clearTimeout(timer);
    activeReload = undefined;
  };
}

const palette = {
  point: '#7a5c1e',
  line: '#8c4a2f',
  fill: 'rgba(140, 74, 47, 0.15)',
  stroke: '#ffffff',
  highlight: '#1677ff',
  highlightHalo: 'rgba(22, 119, 255, 0.25)',
};

function featureStyle(feature: FeatureLike): Style | Style[] {
  const geometryType = feature.getGeometry()?.getType();
  const selected = selectedFeatureId !== null && feature.get('id') === selectedFeatureId;

  if (geometryType === 'Point') {
    // A translucent halo behind the symbol marks the selected feature.
    const halo = selected
      ? [new Style({
          image: new CircleStyle({
            radius: 14,
            fill: new Fill({ color: palette.highlightHalo }),
            stroke: new Stroke({ color: palette.highlight, width: 2 }),
          }),
        })]
      : [];

    const symbol = symbolByTypeId.get(Number(feature.get('featureTypeId')));
    if (symbol) {
      let icon = iconCache.get(symbol);
      if (!icon) {
        icon = new Icon({ src: `/feature_symbols/${symbol}`, scale: 0.5 });
        iconCache.set(symbol, icon);
      }
      return [...halo, new Style({ image: icon })];
    }
    return [...halo, new Style({
      image: new CircleStyle({
        radius: 6,
        fill: new Fill({ color: palette.point }),
        stroke: new Stroke({ color: palette.stroke, width: 2 }),
      }),
    })];
  }

  if (selected) {
    return [
      new Style({ stroke: new Stroke({ color: palette.highlight, width: 6 }) }),
      new Style({
        stroke: new Stroke({ color: palette.line, width: 2.5 }),
        fill: new Fill({ color: palette.highlightHalo }),
      }),
    ];
  }

  return new Style({
    stroke: new Stroke({ color: palette.line, width: 2.5 }),
    fill: new Fill({ color: palette.fill }),
  });
}
