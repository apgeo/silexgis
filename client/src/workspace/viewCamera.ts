// SPDX-License-Identifier: AGPL-3.0-or-later

// Where "show me this" goes when the application has more than one view to show it in.
//
// The detail panel is deliberately shared: the same component is mounted beside the flat map and
// beside the 3D scene, so a cave clicked in either one is described identically. Its camera
// buttons cannot be, because there is no single camera to move. Calling the flat map's camera
// directly — which is all a one-view application ever needs — leaves those buttons dead wherever
// the flat map is not on screen, and worse than dead: that map is a module-level object that
// exists whether or not it is mounted, so the command is accepted in silence and then takes
// visible effect the next time the viewer opens the other view.
//
// A view therefore registers its camera for as long as it is mounted, and the panel asks for
// "the view's camera" instead of naming one. Nothing here mirrors one camera onto another and no
// command reaches more than one view; this is about which view is on screen, not about keeping
// two of them in step.

import type { Camera3DState } from '../scene3d/camera3d.ts';

export interface ViewCameraTarget {
  /** Centres the view on a point, framed as a map at `zoom` would be. Animated. */
  flyTo(longitude: number, latitude: number, zoom: number): void;
  /** Frames a GeoJSON geometry given in degrees; a point gets a sane close-up instead. */
  fitGeometry(geometry: object): void;
  /**
   * The view's camera written down, when it has one worth writing down. Optional because a flat
   * map has no direction or tilt to report, and inventing one for it would put a made-up camera
   * into a saved view that the next thing to read it would faithfully restore.
   */
  getCamera3D?(): Camera3DState | undefined;
  /** Restores a written-down camera. Optional for the same reason. */
  setCamera3D?(state: Camera3DState): void;
}

/** One view's registration, wrapped so that two views offering the same camera stay distinct. */
interface ViewCameraClaim {
  target: ViewCameraTarget;
}

/**
 * The views that have registered a camera, oldest first. The last one is the one commands go to.
 *
 * A stack rather than a single slot, because the views nest as well as follow one another. The 3D
 * scene opens as a pane BESIDE the flat map, inside the page the map is already mounted in: with
 * one slot, opening the pane would replace the map's registration and closing it would clear the
 * slot outright, leaving a mounted map whose camera buttons had gone quietly dead for the rest of
 * the visit — there is nothing that re-registers a view already on screen. Stacking makes the pane
 * borrow the camera while it is open and hand it back when it closes, and it keeps the behaviour a
 * single slot was chosen for: during a route change the arriving view is on top, and the leaving
 * one is removed from underneath it without disturbing anything.
 */
const claims: ViewCameraClaim[] = [];

function active(): ViewCameraTarget | undefined {
  return claims.at(-1)?.target;
}

/**
 * Registers the camera of a view for as long as it is mounted, and returns a detach function.
 *
 * The newest registration is the one commands reach: it is the view the viewer just opened. When
 * it detaches the camera goes back to whichever view registered under it, if any — a view that
 * detaches from the middle of the stack (the leaving half of a route change) changes nothing.
 *
 * Detaching twice is harmless. React re-runs an effect's cleanup and setup in development to
 * surface exactly this class of bug, and a latch is what keeps a cleanup that runs twice from
 * removing somebody else's registration.
 */
export function setActiveViewCamera(target: ViewCameraTarget): () => void {
  const claim: ViewCameraClaim = { target };
  claims.push(claim);

  let released = false;
  return () => {
    if (released) {
      return;
    }
    released = true;
    const index = claims.indexOf(claim);
    if (index >= 0) {
      claims.splice(index, 1);
    }
  };
}

/** What a "zoom to this thing" button means when nothing more specific is asked for. */
export const DEFAULT_FLY_TO_ZOOM = 15;

/**
 * Moves the view on screen to a point. Does nothing when no view is mounted — there is nothing to
 * move, and quietly staging a move for whichever view opens next is how a button ends up firing
 * minutes later in a place the viewer was not looking.
 */
export function viewFlyTo(longitude: number, latitude: number, zoom = DEFAULT_FLY_TO_ZOOM): void {
  active()?.flyTo(longitude, latitude, zoom);
}

/** Frames a geometry in the view on screen; does nothing when no view is mounted. */
export function viewFitGeometry(geometry: object): void {
  active()?.fitGeometry(geometry);
}

/**
 * The 3D camera of the view on screen, for writing into a saved view. Undefined when the view on
 * screen has no such camera, or when there is no view on screen — a saved view then simply carries
 * no 3D camera, which is exactly what it should carry when none was being looked at.
 */
export function viewCamera3dState(): Camera3DState | undefined {
  return active()?.getCamera3D?.();
}

/**
 * Restores a saved 3D camera into the view on screen. Does nothing when the view on screen has no
 * 3D camera: the block stays in the saved view for whenever one is opened, rather than being
 * staged for a scene the viewer has not asked for.
 */
export function applyViewCamera3d(state: Camera3DState | undefined): void {
  if (state) {
    active()?.setCamera3D?.(state);
  }
}
