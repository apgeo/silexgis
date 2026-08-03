// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import { onSurfaceFeaturesChanged, surfaceFeaturesChanged } from './surfaceFeatureRefresh.ts';

describe('announcing a write to the surface features', () => {
  it('reaches every view that draws them, not just the one the writer knows about', () => {
    // The defect this exists to prevent: a write refreshing one view's overlay while a second
    // view goes on drawing — and letting the viewer click — a feature that has been deleted.
    const flatOverlay = vi.fn();
    const scene = vi.fn();
    const detachFlat = onSurfaceFeaturesChanged(flatOverlay);
    const detachScene = onSurfaceFeaturesChanged(scene);

    surfaceFeaturesChanged();

    expect(flatOverlay).toHaveBeenCalledTimes(1);
    expect(scene).toHaveBeenCalledTimes(1);
    detachFlat();
    detachScene();
  });

  it('stops calling a view that has gone away', () => {
    const gone = vi.fn();
    onSurfaceFeaturesChanged(gone)();

    surfaceFeaturesChanged();

    expect(gone).not.toHaveBeenCalled();
  });

  it('is a no-op with nothing mounted, so a write from a page with no map costs nothing', () => {
    expect(() => surfaceFeaturesChanged()).not.toThrow();
  });

  it('survives a listener that unsubscribes while the news is being delivered', () => {
    // A view can tear down in response to the write it is hearing about; iterating the live set
    // would then skip whichever listener happened to come after it.
    const leaving: { detach?: () => void } = {};
    leaving.detach = onSurfaceFeaturesChanged(() => leaving.detach?.());
    const staying = vi.fn();
    const detachStaying = onSurfaceFeaturesChanged(staying);

    surfaceFeaturesChanged();

    expect(staying).toHaveBeenCalledTimes(1);
    detachStaying();
  });
});
