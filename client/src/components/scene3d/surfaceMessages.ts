// SPDX-License-Identifier: AGPL-3.0-or-later
import type { Scene3DCutawayPause } from '../../scene3d/scene3dEngine.ts';
import type { TerrainSourceProblem } from '../../scene3d/terrainSource3d.ts';

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

/**
 * The sentence that says an installation's elevation model was configured and could not be used.
 *
 * Shown rather than swallowed, and the three reasons kept apart, because this is the one thing in
 * the scene whose failure has no appearance of its own: a globe drawing tiles that cannot be
 * parsed is a black void with every request answering 200 and nothing in the console, which is
 * indistinguishable from any other empty globe. The viewer is not the audience — they get the
 * ordinary smooth globe, which works — but somebody who can fix it will eventually look, and the
 * difference between "it is not where you said" and "your web server is describing the files
 * wrongly" is the whole of the fix.
 *
 * <p><code>fellBack</code> says a second source was drawn after this one was refused, which changes
 * what the viewer is looking at without changing what is broken. The notice stays up either way:
 * a configured source that silently stopped being used is exactly how a stale configuration line
 * outlives the setup it described.</p>
 */
export function terrainProblemMessage(problem: TerrainSourceProblem, fellBack = false): string {
  const key =
    problem === 'unreachable'
      ? 'terrainUnreachable'
      : problem === 'encodingMismatch'
        ? 'terrainEncodingMismatch'
        : 'terrainMalformed';
  // The diagnosis is the same either way; the consequence is not, and saying "the ground is shown
  // as a smooth globe" over ground that is visibly there is the kind of wrongness that teaches a
  // reader to stop believing the notices. Six keys rather than a composed sentence because the two
  // halves decline together in Romanian.
  return `scene3d.${key}${fellBack ? 'FellBack' : ''}`;
}
