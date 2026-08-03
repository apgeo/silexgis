// SPDX-License-Identifier: AGPL-3.0-or-later
import type { Scene3DCutawayPause } from '../../scene3d/scene3dEngine.ts';

/**
 * The sentence that explains a cutaway the camera has taken away.
 *
 * Shared by the notice on the scene itself and by the hint under the control that asked for the
 * mode, so a viewer is never told two different things about one state. The two reasons get two
 * sentences because they need two different actions: "tilt down towards the cave" is sound advice
 * from above the ground and useless from below it, where there is no ground left between the
 * camera and the cave to remove and no angle at all brings the opening back.
 */
export function cutawayPauseMessage(pausedBy: Scene3DCutawayPause): string {
  return pausedBy === 'belowSurface' ? 'scene3d.cutawayUnderground' : 'scene3d.cutawayPaused';
}
