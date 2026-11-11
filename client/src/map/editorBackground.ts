// SPDX-License-Identifier: AGPL-3.0-or-later
import TileLayer from 'ol/layer/Tile';
import XYZ from 'ol/source/XYZ';
import type { MapLayerInfo } from '../api/hooks.ts';
import { backgroundLayerFor, getBaseLayerId, getBaseLayers, isDrawableOnMap, type BackgroundLayer } from './baseLayers.ts';
import { getWorkspaceMap } from './mapContext.ts';

/**
 * The background drawn when the installation names none of its own: OpenTopoMap, whose contours
 * and paths are what a reader placing a cave — or a cave map — in the terrain wants to see.
 */
export const OPEN_TOPO_MAP = {
  url: 'https://{a-c}.tile.opentopomap.org/{z}/{x}/{y}.png',
  attributions:
    'Map data © <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors, SRTM | Map style © <a href="https://opentopomap.org">OpenTopoMap</a> (CC-BY-SA)',
  maxZoom: 17,
} as const;

/**
 * The installation's default background, as the workspace map chooses it — the base layer marked as
 * the default, else the first base layer — or null when it publishes none the flat map can draw.
 */
export function defaultBasemap(layers: readonly MapLayerInfo[] | undefined): MapLayerInfo | null {
  const bases = (layers ?? []).filter((l) => l.isBase && isDrawableOnMap(l));
  return bases.find((l) => l.isDefault) ?? bases[0] ?? null;
}

/**
 * The catalogue entry a small map of its own should draw under its work: the background the reader
 * has on the workspace map right now, else the installation's default, else none.
 */
export function chooseBackground(
  layers: readonly MapLayerInfo[] | undefined,
  activeId: number | undefined,
): MapLayerInfo | null {
  const active =
    activeId === undefined ? undefined : (layers ?? []).find((l) => l.id === activeId && l.isBase && isDrawableOnMap(l));
  return active ?? defaultBasemap(layers);
}

/** The id of the background showing on the workspace map, or undefined before it has drawn any. */
export function workspaceBackgroundId(): number | undefined {
  const visible = getBaseLayers(getWorkspaceMap()).find((layer) => layer.getVisible());
  return visible ? getBaseLayerId(visible) : undefined;
}

/** A layer of its own for a catalogue entry, or OpenTopoMap when there is none to draw. */
export function editorBackgroundLayer(entry: MapLayerInfo | null, language: string): BackgroundLayer {
  const layer = entry ? backgroundLayerFor(entry, 0, language) : new TileLayer({ source: new XYZ(OPEN_TOPO_MAP) });
  // A catalogue layer is built hidden, for the workspace map to switch on; here it is the only one.
  layer.setVisible(true);
  return layer;
}
