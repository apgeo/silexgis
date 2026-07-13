// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it } from 'vitest';
import { useWorkspaceStore } from './workspaceStore.ts';

afterEach(() => {
  useWorkspaceStore.setState({ overlayOpacity: {}, rasterOpacity: {}, baseOpacity: {} });
});

describe('workspaceStore overlay opacity', () => {
  it('sets and merges per-overlay opacity, last write wins per key', () => {
    const { setOverlayOpacity } = useWorkspaceStore.getState();
    setOverlayOpacity('entrances', 0.5);
    setOverlayOpacity('surface-features', 0.2);
    setOverlayOpacity('entrances', 0.8);

    expect(useWorkspaceStore.getState().overlayOpacity).toEqual({
      entrances: 0.8,
      'surface-features': 0.2,
    });
  });

  it('keeps raster opacity independent of overlay opacity', () => {
    const store = useWorkspaceStore.getState();
    store.setRasterOpacity('r1', 0.3);
    store.setOverlayOpacity('centerlines', 0.6);

    expect(useWorkspaceStore.getState().rasterOpacity).toEqual({ r1: 0.3 });
    expect(useWorkspaceStore.getState().overlayOpacity).toEqual({ centerlines: 0.6 });
  });

  it('sets and merges per-base-layer opacity keyed by id, independent of overlays', () => {
    const store = useWorkspaceStore.getState();
    store.setBaseOpacity(1, 0.4);
    store.setBaseOpacity(2, 0.7);
    store.setBaseOpacity(1, 0.9);
    store.setOverlayOpacity('entrances', 0.5);

    expect(useWorkspaceStore.getState().baseOpacity).toEqual({ 1: 0.9, 2: 0.7 });
    expect(useWorkspaceStore.getState().overlayOpacity).toEqual({ entrances: 0.5 });
  });
});
