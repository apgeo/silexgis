// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it } from 'vitest';
import { useWorkspaceStore } from './workspaceStore.ts';

afterEach(() => {
  useWorkspaceStore.setState({ overlayOpacity: {}, rasterOpacity: {} });
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
});
