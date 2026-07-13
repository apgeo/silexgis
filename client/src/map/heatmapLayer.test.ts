// SPDX-License-Identifier: AGPL-3.0-or-later
import Heatmap from 'ol/layer/Heatmap';
import { describe, expect, it } from 'vitest';
import { getEntranceSource } from './entranceLayer.ts';
import { ENTRANCE_HEATMAP_LAYER_ID, createEntranceHeatmapLayer } from './heatmapLayer.ts';

describe('entrance heatmap layer', () => {
  it('creates a Heatmap over the shared entrance source, off by default', () => {
    const layer = createEntranceHeatmapLayer();
    expect(layer).toBeInstanceOf(Heatmap);
    expect(layer.get('id')).toBe(ENTRANCE_HEATMAP_LAYER_ID);
    expect(layer.getVisible()).toBe(false);
    // Shares the point layer's protection-filtered source — no separate coordinate feed.
    expect(layer.getSource()).toBe(getEntranceSource());
  });
});
