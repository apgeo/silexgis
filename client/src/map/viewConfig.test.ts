// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { applyViewConfig, captureViewConfig, type WorkspaceUiState } from './viewConfig.ts';

const baseUi: Omit<WorkspaceUiState, 'overlayOrder'> = {
  baseLayerId: 3,
  entrancesVisible: true,
  surfaceFeaturesVisible: false,
  centerlinesVisible: true,
  heatmapVisible: true,
  geofileIds: [],
  rasters: [],
  tagFilter: null,
  overlayOpacity: { entrances: 0.5 },
  baseOpacity: { 3: 0.6 },
};

describe('viewConfig heatmap + base opacity', () => {
  it('captures heatmap visibility and per-base opacity into the config', () => {
    const config = captureViewConfig(baseUi);
    expect(config.heatmapVisible).toBe(true);
    expect(config.baseOpacity).toEqual({ 3: 0.6 });
  });

  it('round-trips the new fields back through applyViewConfig', () => {
    const config = captureViewConfig(baseUi);
    const restored = applyViewConfig(config);
    expect(restored?.heatmapVisible).toBe(true);
    expect(restored?.baseOpacity).toEqual({ 3: 0.6 });
  });

  it('defaults the new fields for older saved views that omit them', () => {
    // A pre-B2 config: valid v1, no heatmapVisible / baseOpacity keys.
    const legacy = {
      configVersion: 1,
      center: [25.3, 45.7],
      zoom: 10,
      entrancesVisible: true,
      surfaceFeaturesVisible: true,
      geofileIds: [],
      rasters: [],
      tagFilter: null,
    };
    const restored = applyViewConfig(legacy);
    expect(restored?.heatmapVisible).toBe(false);
    expect(restored?.baseOpacity).toEqual({});
  });
});
