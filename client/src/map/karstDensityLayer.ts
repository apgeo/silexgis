// SPDX-License-Identifier: AGPL-3.0-or-later
import Feature from 'ol/Feature';
import type { FeatureLike } from 'ol/Feature';
import { Polygon } from 'ol/geom';
import VectorLayer from 'ol/layer/Vector';
import type Map from 'ol/Map';
import { fromLonLat, transformExtent } from 'ol/proj';
import VectorSource from 'ol/source/Vector';
import { Fill, Stroke, Style } from 'ol/style';
import { fetchMapDensity, type DensityCell, type DensityGrid, type DensityQuery } from '../api/hooks.ts';
import { fetchSteppedDensity, type DensityGridLimits, type KarstDensityMode } from './densityCells.ts';
import { karstDensityPalette as palette } from './markerPalette.ts';

export const KARST_DENSITY_LAYER_ID = 'karst-density';

// How thickly caves sit, drawn from the server's grid and from nothing else.
//
// The cells are the server's own: counted over every readable entrance with a protected one
// snapped to the protection lattice first, binned on a lattice no finer than that, and smoothed
// only over the binned counts. A client could draw a prettier surface from the entrance points it
// already holds, and it would be wrong twice — those answers are capped per window, so a dense
// window would read as sparse, and a heat surface over exact positions would put the peaks where
// the protected caves are. So this overlay draws the cells it is sent, at the size it was sent,
// and nothing finer.

export { KARST_DENSITY_MODES, isKarstDensityMode, type KarstDensityMode } from './densityCells.ts';

/** How many value bands the two density readings are divided into. */
export const KARST_DENSITY_BANDS = palette.ramp.length;

/**
 * What the panel needs to explain the overlay honestly. A refusal is its own state: an empty
 * layer and a refused window look the same on a map, and only one of them means "no caves here".
 */
export interface KarstDensityLoadState {
  status: 'idle' | 'loading' | 'ok' | 'refused' | 'error';
  /** The stable code of the refusal, when the server (or the step search) refused. */
  refusal?: string;
  /** The cell actually drawn, in metres, once an answer arrived. */
  cellMetres: number | null;
  /** The finest cell this installation publishes, once known. */
  minimumCellMetres: number | null;
  /** Upper bound of each value band of the current reading, lowest first; empty when none. */
  bandMaxima: number[];
  /** The z beyond which a cell is a hot or a cold spot, as the server states it. */
  significanceZ: number | null;
  mode: KarstDensityMode;
}

const source = new VectorSource();
let mode: KarstDensityMode = 'density';
let lastGrid: DensityGrid | null = null;

const emptyState = (): KarstDensityLoadState => ({
  status: 'idle',
  cellMetres: null,
  minimumCellMetres: knownLimits?.minimumCellMetres ?? null,
  bandMaxima: [],
  significanceZ: null,
  mode,
});

// The floor and the cell limit are properties of the installation, not of a window, so once one
// answer (or refusal) has stated them every later window can choose its cell before asking.
let knownLimits: DensityGridLimits | null = null;
let loadState: KarstDensityLoadState = emptyState();
const listeners = new Set<(state: KarstDensityLoadState) => void>();

/** The features the layer is drawing, so what it draws can be asserted without a canvas. */
export function getKarstDensitySource(): VectorSource {
  return source;
}

export function getKarstDensityLoadState(): KarstDensityLoadState {
  return loadState;
}

/** Subscribes to load-state changes; returns an unsubscribe function. */
export function subscribeKarstDensityLoadState(
  listener: (state: KarstDensityLoadState) => void,
): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function publish(state: KarstDensityLoadState): void {
  loadState = state;
  for (const listener of listeners) {
    listener(state);
  }
}

export function createKarstDensityLayer(): VectorLayer {
  const layer = new VectorLayer({ source, style: cellStyle, visible: false });
  layer.set('id', KARST_DENSITY_LAYER_ID);
  return layer;
}

/** The value a cell is filled by, in the given reading; null when it has none. */
export function cellValue(cell: DensityCell, reading: KarstDensityMode): number | null {
  switch (reading) {
    case 'density':
      return cell.densityPerKm2;
    case 'kernel':
      return cell.kernelDensityPerKm2;
    case 'hotSpot':
      return cell.hotSpotZ ?? null;
  }
}

/**
 * Equal-interval bands from zero to the largest value in the answer. Relative to the window on
 * purpose: the same colour then means "among the densest here", which is what a reader scanning
 * a view asks, and the legend states the numbers each band stands for.
 */
export function densityBandMaxima(cells: readonly DensityCell[], reading: KarstDensityMode): number[] {
  if (reading === 'hotSpot') {
    return [];
  }
  let max = 0;
  for (const cell of cells) {
    const value = cellValue(cell, reading);
    if (value !== null && Number.isFinite(value) && value > max) {
      max = value;
    }
  }
  if (max <= 0) {
    return [];
  }
  return Array.from({ length: KARST_DENSITY_BANDS }, (_, i) => (max * (i + 1)) / KARST_DENSITY_BANDS);
}

/** Which band a value falls in, or null for a value nothing is drawn for. */
export function bandOf(value: number | null, bandMaxima: readonly number[]): number | null {
  if (value === null || !Number.isFinite(value) || value <= 0 || bandMaxima.length === 0) {
    return null;
  }
  const index = bandMaxima.findIndex((upper) => value <= upper);
  return index === -1 ? bandMaxima.length - 1 : index;
}

/**
 * One polygon per cell, from the cell's own bounds. The bounds are the server's — the lattice is
 * anchored at zero, not at the window — so drawing them as sent is what keeps two overlapping
 * windows showing the same cells.
 */
export function densityCellFeatures(
  cells: readonly DensityCell[],
  reading: KarstDensityMode,
  significanceZ: number | null,
): Feature[] {
  const bandMaxima = densityBandMaxima(cells, reading);
  const features: Feature[] = [];
  for (const cell of cells) {
    const value = cellValue(cell, reading);
    const fill =
      reading === 'hotSpot' ? hotSpotFill(value, significanceZ) : bandFill(bandOf(value, bandMaxima));
    if (fill === null) {
      // An empty or insignificant cell draws nothing: a wash over the whole window would hide
      // the ground for no information.
      continue;
    }
    const ring = [
      [cell.west, cell.south],
      [cell.east, cell.south],
      [cell.east, cell.north],
      [cell.west, cell.north],
      [cell.west, cell.south],
    ].map((corner) => fromLonLat(corner));
    const feature = new Feature({ geometry: new Polygon([ring]) });
    feature.set('fill', fill);
    feature.set('value', value);
    features.push(feature);
  }
  return features;
}

function bandFill(band: number | null): string | null {
  return band === null ? null : palette.ramp[band];
}

function hotSpotFill(z: number | null, significanceZ: number | null): string | null {
  if (z === null || significanceZ === null || !Number.isFinite(z)) {
    return null;
  }
  if (z >= significanceZ) return palette.hot;
  if (z <= -significanceZ) return palette.cold;
  return null;
}

const styleCache = new globalThis.Map<string, Style>();

function cellStyle(feature: FeatureLike): Style | undefined {
  const fill = feature.get('fill') as string | undefined;
  if (!fill) {
    return undefined;
  }
  let style = styleCache.get(fill);
  if (!style) {
    style = new Style({
      fill: new Fill({ color: fill }),
      stroke: new Stroke({ color: palette.cellStroke, width: 0.5 }),
    });
    styleCache.set(fill, style);
  }
  return style;
}

function draw(grid: DensityGrid): void {
  source.clear(true);
  source.addFeatures(densityCellFeatures(grid.cells, mode, grid.autocorrelation.significanceZ));
}

/** Changes the reading the cells are filled by, redrawing the last answer without asking again. */
export function setKarstDensityMode(value: KarstDensityMode): void {
  if (value === mode) {
    return;
  }
  mode = value;
  if (lastGrid && loadState.status === 'ok') {
    draw(lastGrid);
    publish(okState(lastGrid));
  } else {
    publish({ ...loadState, mode });
  }
}

function okState(grid: DensityGrid): KarstDensityLoadState {
  return {
    status: 'ok',
    cellMetres: grid.cellMetres,
    minimumCellMetres: grid.minimumCellMetres,
    bandMaxima: densityBandMaxima(grid.cells, mode),
    significanceZ: grid.autocorrelation.significanceZ,
    mode,
  };
}

// ---------------------------------------------------------------------------
// Asking — the cell is chosen by the shared step search
// ---------------------------------------------------------------------------

export type DensityFetch = (query: DensityQuery, signal?: AbortSignal) => Promise<DensityGrid>;

// ---------------------------------------------------------------------------
// Loading
// ---------------------------------------------------------------------------

let enabled = false;
let activeReload: (() => void) | undefined;

/** Enables/disables loading; enabling loads the current window at once. */
export function setKarstDensityEnabled(value: boolean): void {
  if (value === enabled) {
    return;
  }
  enabled = value;
  if (enabled) {
    activeReload?.();
  } else {
    // A hidden layer states nothing, so the panel must not go on reporting the last answer.
    source.clear(true);
    lastGrid = null;
    publish(emptyState());
  }
}

/**
 * Bbox loading on moveend, debounced; a newer window aborts the request it supersedes, and an
 * answer that arrives after its window was left is dropped rather than drawn.
 */
export function attachKarstDensityLoader(map: Map, fetchGrid: DensityFetch = fetchMapDensity): () => void {
  let timer: number | undefined;
  let inFlight: AbortController | undefined;

  const load = async () => {
    if (!enabled) {
      return;
    }
    const size = map.getSize();
    if (!size) {
      return;
    }
    const extent = transformExtent(map.getView().calculateExtent(size), 'EPSG:3857', 'EPSG:4326');
    inFlight?.abort();
    const controller = new AbortController();
    inFlight = controller;
    publish({ ...loadState, status: 'loading', refusal: undefined, mode });
    try {
      const answer = await fetchSteppedDensity(
        [extent[0], extent[1], extent[2], extent[3]],
        knownLimits,
        fetchGrid,
        controller.signal,
      );
      if (controller.signal.aborted || !enabled) {
        return;
      }
      knownLimits = answer.limits ?? knownLimits;
      if (answer.kind === 'grid') {
        lastGrid = answer.grid;
        draw(answer.grid);
        publish(okState(answer.grid));
      } else {
        // Nothing from an earlier window may stay drawn under a refusal of this one.
        source.clear(true);
        lastGrid = null;
        publish({ ...emptyState(), status: 'refused', refusal: answer.code });
      }
    } catch {
      if (controller.signal.aborted) {
        return;
      }
      source.clear(true);
      lastGrid = null;
      publish({ ...emptyState(), status: 'error' });
    } finally {
      if (inFlight === controller) {
        inFlight = undefined;
      }
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
    inFlight?.abort();
    activeReload = undefined;
  };
}
