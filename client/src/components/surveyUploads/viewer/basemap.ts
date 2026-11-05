// SPDX-License-Identifier: AGPL-3.0-or-later
import type { MapLayerInfo } from '../../../api/hooks.ts';
import { isDrawableOnMap } from '../../../map/baseLayers.ts';

/**
 * The background drawn when the installation names none of its own: OpenTopoMap, whose contours
 * and paths are what a reader placing a cave in the terrain wants to see.
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
