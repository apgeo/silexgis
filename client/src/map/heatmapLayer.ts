// SPDX-License-Identifier: AGPL-3.0-or-later
import type { FeatureLike } from 'ol/Feature';
import Heatmap from 'ol/layer/Heatmap';
import { getEntranceSource } from './entranceLayer.ts';

export const ENTRANCE_HEATMAP_LAYER_ID = 'entrance-heatmap';

/**
 * Heat weight per rendered entrance feature. Weights clamp to [0,1] in OL, so a
 * lone entrance (count 1) registers faintly and a dense low-zoom cluster saturates.
 * The source is the visibility-filtered entrance feed shared with the point layer —
 * clusters carry `count`, individual entrances do not — so no coordinate path is
 * added and location protection stays entirely server-side.
 */
function heatmapWeight(feature: FeatureLike): number {
  const count = feature.get('cluster') === true ? Number(feature.get('count') ?? 1) : 1;
  return Math.min(1, count / 10);
}

/**
 * Entrance-density heatmap over the live entrance source. Off by default; it enters
 * the layer composer like any overlay (toggle, per-row opacity, drag z-order) and
 * needs no loader of its own — the entrance bbox loader already keeps the source fresh
 * whether or not the point layer is shown.
 */
export function createEntranceHeatmapLayer(): Heatmap {
  const layer = new Heatmap({
    source: getEntranceSource(),
    weight: heatmapWeight,
    radius: 12,
    blur: 18,
    visible: false,
  });
  layer.set('id', ENTRANCE_HEATMAP_LAYER_ID);
  return layer;
}
