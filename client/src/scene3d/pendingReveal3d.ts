// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ResourceRef } from '../viewlinks/resourceRef.ts';

/**
 * A reveal asked of the 3D scene before there is a scene to ask.
 *
 * The scene answers reveals through the view-control roster, like the flat map and the survey
 * viewer do — but a control is listed only while its scene is mounted and its engine has arrived,
 * and a page that wants to *open* the scene on a cave has, by definition, no scene to send to yet.
 * So the request is parked here, outside React and outside the engine, and the scene takes it
 * once its engine is ready and shows it the way it shows any other reveal: selection first, then
 * the camera.
 *
 * One slot, not a queue. The request is an instruction about where the viewer is going next, and
 * a second one made before the scene opened replaces the first rather than queueing behind it —
 * the viewer changed their mind, and flying the camera to the first place and then the second
 * would show them a place they are no longer asking for. Taking the slot empties it, so the same
 * request is never answered twice: a scene remounted later must not fly back to a cave the
 * viewer left minutes ago.
 */
let pending: ResourceRef | undefined;

/** Asks the next 3D scene to show this, replacing whatever was asked before. */
export function requestReveal3d(ref: ResourceRef): void {
  pending = ref;
}

/** The latest request, or undefined when there is none. Clears it: a request is answered once. */
export function takePendingReveal3d(): ResourceRef | undefined {
  const taken = pending;
  pending = undefined;
  return taken;
}
