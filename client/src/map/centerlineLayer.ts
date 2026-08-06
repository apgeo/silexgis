// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import GeoJSON from 'ol/format/GeoJSON';
import VectorImageLayer from 'ol/layer/VectorImage';
import { transformExtent } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Stroke, Style } from 'ol/style';
import { fetchCenterlineFeatures, type MapConfig } from '../api/hooks.ts';
import { centerlinePalette } from './markerPalette.ts';

export const CENTERLINE_LAYER_ID = 'centerlines';

const source = new VectorSource();
const format = new GeoJSON();

/**
 * At overview zooms the overlay is a plain line: a survey holds tens of thousands of line
 * components, and every extra stroke pass is paid once per component. The doubled stroke (light
 * casing under a dark dashed core, which keeps the line readable over both aerial and topo
 * bases) is worth its cost only close in, where there are far fewer components on screen.
 */
const detailStyle = [
  new Style({ stroke: new Stroke({ color: centerlinePalette.casing, width: 4 }) }),
  new Style({ stroke: new Stroke({ color: centerlinePalette.line, width: 2, lineDash: [6, 4] }) }),
];
const overviewStyle = new Style({ stroke: new Stroke({ color: centerlinePalette.line, width: 2 }) });

/** Limits used until the server's own are loaded; overridden by /map/config. */
const fallbackLimits = { detailZoom: 18, maxPaths: 25000 };

let limits = { ...fallbackLimits };
let overrides: { detailZoom?: number; maxPaths?: number } = {};
let enabled = false;
let activeReload: (() => void) | undefined;
let lastZoom = 0;

const effectiveDetailZoom = () => overrides.detailZoom ?? limits.detailZoom;
const effectiveMaxPaths = () => overrides.maxPaths ?? limits.maxPaths;

/**
 * How many centerlines the last response held back, and why. Read by the layer panel so a user
 * who is looking at an empty-ish overlay is told to zoom in rather than left guessing.
 */
export interface CenterlineLoadState {
  withheldCount: number;
  /** True when the server sent full detail (splays included) rather than the skeleton. */
  detail: boolean;
}

let loadState: CenterlineLoadState = { withheldCount: 0, detail: false };
const listeners = new Set<(state: CenterlineLoadState) => void>();

export function getCenterlineLoadState(): CenterlineLoadState {
  return loadState;
}

/** Subscribes to load-state changes; returns an unsubscribe function. */
export function subscribeCenterlineLoadState(listener: (state: CenterlineLoadState) => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function publish(state: CenterlineLoadState): void {
  loadState = state;
  for (const listener of listeners) {
    listener(state);
  }
}

/**
 * A rasterising vector layer: while panning and zooming it reuses the rendered image instead of
 * re-stroking every component, which is what the geometry here actually costs. Safe because
 * centerlines are not interactive — both the click and hover handlers exclude this layer.
 */
export function createCenterlineLayer(): VectorImageLayer {
  // Stacking comes from the overlay group's collection order, not a fixed zIndex.
  const layer = new VectorImageLayer({
    source,
    style: () => (lastZoom >= effectiveDetailZoom() ? detailStyle : overviewStyle),
  });
  layer.set('id', CENTERLINE_LAYER_ID);
  layer.setVisible(false); // opt-in overlay: the heaviest one there is
  return layer;
}

/** Applies the installation's limits (from /map/config) and reloads if the overlay is on. */
export function setCenterlineLimits(config: Pick<MapConfig, 'centerlineDetailZoom' | 'centerlineMaxPaths'>): void {
  limits = {
    detailZoom: config.centerlineDetailZoom,
    maxPaths: config.centerlineMaxPaths,
  };
  if (enabled) {
    activeReload?.();
  }
}

/** Applies this viewer's personal overrides; either field may be left out to follow the server. */
export function setCenterlineOverrides(next: { detailZoom?: number; maxPaths?: number }): void {
  const changed = next.detailZoom !== overrides.detailZoom || next.maxPaths !== overrides.maxPaths;
  overrides = next;
  if (changed && enabled) {
    activeReload?.();
  }
}

/**
 * Enables/disables loading; enabling triggers an immediate load of the current extent. Without
 * this the overlay kept fetching, parsing and rendering while switched off, so turning it off
 * bought nothing.
 */
export function setCenterlinesEnabled(value: boolean): void {
  enabled = value;
  if (enabled) {
    activeReload?.();
  } else {
    source.clear(true);
    publish({ withheldCount: 0, detail: false });
  }
}

/** Bbox loading on moveend (debounced), stale responses discarded — mirrors the entrance loader. */
export function attachCenterlineLoader(map: Map): () => void {
  let requestSeq = 0;
  let timer: number | undefined;

  const load = async () => {
    if (!enabled) {
      return;
    }
    const view = map.getView();
    const size = map.getSize();
    if (!size) {
      return;
    }
    const zoom = Math.round(view.getZoom() ?? 0);
    lastZoom = zoom;
    const extent = transformExtent(view.calculateExtent(size), 'EPSG:3857', 'EPSG:4326');
    const bbox = extent.map((n) => n.toFixed(5)).join(',');
    const seq = ++requestSeq;
    try {
      const collection = await fetchCenterlineFeatures(
        bbox,
        zoom,
        overrides.detailZoom,
        overrides.maxPaths ?? effectiveMaxPaths(),
      );
      if (seq !== requestSeq) {
        return; // a newer request superseded this one
      }
      source.clear(true);
      source.addFeatures(format.readFeatures(collection, { featureProjection: 'EPSG:3857' }));
      publish({ withheldCount: collection.withheldCount, detail: collection.detail });
    } catch {
      // Keep previous features on transient errors; next moveend retries.
    }
  };

  const onMoveEnd = () => {
    window.clearTimeout(timer);
    timer = window.setTimeout(() => void load(), 250);
  };

  map.on('moveend', onMoveEnd);
  activeReload = () => void load();
  return () => {
    map.un('moveend', onMoveEnd);
    window.clearTimeout(timer);
    activeReload = undefined;
  };
}
