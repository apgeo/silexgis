// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import {
  setActiveViewCamera,
  viewFitGeometry,
  viewFlyTo,
  type ViewCameraTarget,
} from './viewCamera.ts';

function recorder() {
  return {
    flyTo: vi.fn(),
    fitGeometry: vi.fn(),
  } satisfies ViewCameraTarget;
}

describe('the camera of the view on screen', () => {
  it('sends a move to the view that is mounted', () => {
    const view = recorder();
    const detach = setActiveViewCamera(view);

    viewFlyTo(25.3, 45.7, 16);
    viewFitGeometry({ type: 'Point', coordinates: [25.3, 45.7] });

    expect(view.flyTo).toHaveBeenCalledWith(25.3, 45.7, 16);
    expect(view.fitGeometry).toHaveBeenCalledWith({ type: 'Point', coordinates: [25.3, 45.7] });
    detach();
  });

  it('sends it to the view mounted now, not to the one that was there before', () => {
    // The defect this exists to prevent: a panel shared by two views calling one of them
    // directly, so its buttons move a map the viewer is not looking at.
    const flat = recorder();
    const detachFlat = setActiveViewCamera(flat);
    const scene = recorder();
    const detachScene = setActiveViewCamera(scene);

    viewFlyTo(25.3, 45.7, 16);

    expect(scene.flyTo).toHaveBeenCalledTimes(1);
    expect(flat.flyTo).not.toHaveBeenCalled();
    detachScene();
    detachFlat();
  });

  it('does not let the leaving view unregister the arriving one', () => {
    // A route change mounts the new view before the old one tears down, and React re-runs an
    // effect's cleanup in development: an unconditional detach would leave the visible view with
    // no camera at all.
    const flat = recorder();
    const detachFlat = setActiveViewCamera(flat);
    const scene = recorder();
    const detachScene = setActiveViewCamera(scene);

    detachFlat();
    viewFlyTo(25.3, 45.7, 16);

    expect(scene.flyTo).toHaveBeenCalledTimes(1);
    detachScene();
  });

  it('stops delivering once the view goes away, rather than staging the move for the next one', () => {
    const view = recorder();
    setActiveViewCamera(view)();

    expect(() => viewFlyTo(25.3, 45.7, 16)).not.toThrow();
    expect(() => viewFitGeometry({ type: 'Point', coordinates: [0, 0] })).not.toThrow();
    expect(view.flyTo).not.toHaveBeenCalled();
  });

  it('has a default close-up for callers that only name a place', () => {
    const view = recorder();
    const detach = setActiveViewCamera(view);

    viewFlyTo(25.3, 45.7);

    expect(view.flyTo).toHaveBeenCalledWith(25.3, 45.7, 15);
    detach();
  });
});
