// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import {
  applyViewCamera3d,
  setActiveViewCamera,
  viewCamera3dState,
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

  it('gives the camera back to the view underneath when the newer one closes', () => {
    // The arrangement this exists for: the 3D scene opens as a pane BESIDE the flat map, inside
    // the page the map is already mounted in. The map registers once when it mounts and there is
    // nothing that would ever re-register it, so a registry that merely cleared itself would leave
    // the map's own "zoom to" buttons dead for the rest of the visit the moment the pane closed.
    const flat = recorder();
    const detachFlat = setActiveViewCamera(flat);
    const scene = recorder();
    const detachScene = setActiveViewCamera(scene);

    detachScene();
    viewFlyTo(25.3, 45.7, 16);
    viewFitGeometry({ type: 'Point', coordinates: [25.3, 45.7] });

    expect(flat.flyTo).toHaveBeenCalledWith(25.3, 45.7, 16);
    expect(flat.fitGeometry).toHaveBeenCalledTimes(1);
    expect(scene.flyTo).not.toHaveBeenCalled();
    detachFlat();
  });

  it('carries the 3D camera members back to the view underneath too', () => {
    const camera = {
      eye: { lon: 25.3, lat: 45.7, height: 900 },
      heading: 0,
      pitch: -45,
      roll: 0,
      projection: 'perspective' as const,
    };
    const flat = { ...recorder(), getCamera3D: vi.fn(() => camera), setCamera3D: vi.fn() };
    const detachFlat = setActiveViewCamera(flat);
    const scene = { ...recorder(), getCamera3D: vi.fn(() => undefined), setCamera3D: vi.fn() };
    setActiveViewCamera(scene)();

    expect(viewCamera3dState()).toBe(camera);
    applyViewCamera3d(camera);

    expect(flat.setCamera3D).toHaveBeenCalledWith(camera);
    expect(scene.setCamera3D).not.toHaveBeenCalled();
    detachFlat();
  });

  it('ignores a second detach rather than unregistering somebody else', () => {
    // React runs an effect's cleanup twice in development; a second call must not reach past this
    // view's own registration and take the camera off whoever holds it now.
    const flat = recorder();
    const detachFlat = setActiveViewCamera(flat);
    const scene = recorder();
    const detachScene = setActiveViewCamera(scene);

    detachScene();
    detachScene();
    viewFlyTo(25.3, 45.7, 16);

    expect(flat.flyTo).toHaveBeenCalledTimes(1);
    detachFlat();
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
