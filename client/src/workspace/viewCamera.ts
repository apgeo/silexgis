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

export interface ViewCameraTarget {
  /** Centres the view on a point, framed as a map at `zoom` would be. Animated. */
  flyTo(longitude: number, latitude: number, zoom: number): void;
  /** Frames a GeoJSON geometry given in degrees; a point gets a sane close-up instead. */
  fitGeometry(geometry: object): void;
}

let active: ViewCameraTarget | undefined;

/**
 * Registers the camera of the view now on screen, and returns a detach function.
 *
 * Detaching clears the registration only if it is still the one it made. A route change mounts the
 * arriving view before the leaving one tears down, and React re-runs an effect's cleanup and setup
 * in development to surface exactly this: without the check, the older view's cleanup would
 * unregister the camera of the view the viewer is now looking at.
 */
export function setActiveViewCamera(target: ViewCameraTarget): () => void {
  active = target;
  return () => {
    if (active === target) {
      active = undefined;
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
  active?.flyTo(longitude, latitude, zoom);
}

/** Frames a geometry in the view on screen; does nothing when no view is mounted. */
export function viewFitGeometry(geometry: object): void {
  active?.fitGeometry(geometry);
}
