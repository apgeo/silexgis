// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it } from 'vitest';
import { useWorkspaceStore } from './workspaceStore.ts';

afterEach(() => {
  useWorkspaceStore.setState({
    overlayOpacity: {},
    rasterOpacity: {},
    baseOpacity: {},
    overlayVisible: {},
    scene3dSurfaceMode: 'overlay',
  });
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

  it('resetBaseOpacity replaces the whole map so stale dims do not survive a view apply', () => {
    const store = useWorkspaceStore.getState();
    store.setBaseOpacity(1, 0.4);
    store.setBaseOpacity(2, 0.7);

    // Applying a view that only mentions base 2: base 1 must drop out entirely (→ opaque),
    // not linger at 0.4.
    store.resetBaseOpacity({ 2: 0.5 });

    expect(useWorkspaceStore.getState().baseOpacity).toEqual({ 2: 0.5 });
  });
});

describe('workspaceStore overlay visibility', () => {
  it('starts with nothing hidden, so a view opens showing everything it has', () => {
    // Absence means shown: nothing has to enumerate the overlays before a view can be drawn.
    expect(useWorkspaceStore.getState().overlayVisible).toEqual({});
  });

  it('records what was hidden, keyed the same way the opacities are', () => {
    const { setOverlayVisible, setOverlayOpacity } = useWorkspaceStore.getState();
    setOverlayVisible('centerlines', false);
    setOverlayVisible('entrances', true);
    setOverlayOpacity('centerlines', 0.5);

    // One key names one layer across both maps and both settings — a viewer who dims the survey
    // lines in one view does not meet a second, independent setting in the other.
    expect(useWorkspaceStore.getState().overlayVisible).toEqual({
      centerlines: false,
      entrances: true,
    });
    expect(useWorkspaceStore.getState().overlayOpacity).toEqual({ centerlines: 0.5 });
  });
});

describe('workspaceStore surface mode', () => {
  it('starts by drawing the cave over the ground', () => {
    expect(useWorkspaceStore.getState().scene3dSurfaceMode).toBe('overlay');
  });

  it('remembers the cutaway a viewer asked for', () => {
    useWorkspaceStore.getState().setScene3dSurfaceMode('cutaway');

    expect(useWorkspaceStore.getState().scene3dSurfaceMode).toBe('cutaway');
  });
});
