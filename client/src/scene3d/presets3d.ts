// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  eyeLookingAt,
  fallbackTarget,
  metersBetween,
  wrapHeading,
  type Camera3DEye,
  type Camera3DState,
} from './camera3d.ts';

// The one-press camera positions.
//
// A survey is read from a handful of directions and always the same ones: from directly above,
// which is the plan, and from each of the four compass points, which are the elevations. Getting
// to any of them by dragging is a fiddle, and getting back to a *known* one by dragging is not
// possible at all — which is the real reason these exist. A viewer who has tumbled the camera into
// nonsense needs one press that means "north, from here", not a slider to fight.
//
// Two properties are deliberate and are what the tests are about:
//
//   * A preset turns the camera around what it is already looking at, rather than jumping to some
//     remembered place. It changes the direction the cave is seen from and nothing else, so the
//     viewer stays where they were and does not have to find the cave again.
//   * Nothing latches. Applying a preset writes a camera and stops; the next drag is an ordinary
//     free camera move that no preset undoes or fights. Which preset a camera "is at" is therefore
//     a question answered by looking at the camera, not by remembering the last button pressed —
//     and the moment the viewer drags, the answer is "none of them".

export type Camera3DPreset = 'top' | 'north' | 'south' | 'east' | 'west';

/** The order the presets are offered in: the plan first, then the elevations clockwise from north. */
export const CAMERA_3D_PRESETS: readonly Camera3DPreset[] = ['top', 'north', 'south', 'east', 'west'];

/**
 * How far below the horizon the four compass views look.
 *
 * Not zero, which is what a drawn elevation would be, and the reason is not aesthetic: a camera
 * looking exactly at the horizon has the sky in the middle of the screen, and the middle of the
 * screen is what decides which patch of ground the scene asks the server about and where the next
 * preset will pivot. A few degrees of tilt keeps the aim point on the cave instead of on the limb
 * of the planet a hundred kilometres away, and is far too small to read as a bird's-eye view.
 */
export const CARDINAL_PITCH_DEGREES = -10;

/**
 * Compass direction the camera *faces* in each preset. A view "from the north" puts the viewer to
 * the north of the cave looking south, which is what a cave surveyor means by a north elevation.
 */
const PRESET_HEADING: Record<Camera3DPreset, number> = {
  top: 0,
  north: 180,
  south: 0,
  east: 270,
  west: 90,
};

const PRESET_PITCH: Record<Camera3DPreset, number> = {
  top: -90,
  north: CARDINAL_PITCH_DEGREES,
  south: CARDINAL_PITCH_DEGREES,
  east: CARDINAL_PITCH_DEGREES,
  west: CARDINAL_PITCH_DEGREES,
};

/**
 * How close a camera has to be to a preset's angles to be reported as sitting at it, in degrees.
 *
 * Loose enough that the rounding a renderer does to the angles it hands back cannot make a camera
 * that was just placed read as free; tight enough that a deliberate nudge does.
 */
const PRESET_MATCH_DEGREES = 0.5;

/**
 * The closest the camera is allowed to be pulled to its pivot when a preset is applied, in metres.
 *
 * The distance is preserved from wherever the camera already was, and it can legitimately be zero
 * — a camera sitting exactly on the point it is looking at, which happens after a fit to a single
 * entrance. Turning that around a pivot would leave it in the same place facing a new direction,
 * so a preset would appear to do nothing.
 */
const MINIMUM_PRESET_DISTANCE_METERS = 50;

/** The camera this preset means, from where the camera already is. Angles change; the pivot does not. */
export function presetCamera(preset: Camera3DPreset, current: Camera3DState): Camera3DState {
  const pivot = presetPivot(current);
  const distance = Math.max(metersBetween(current.eye, pivot), MINIMUM_PRESET_DISTANCE_METERS);
  const heading = PRESET_HEADING[preset];
  const pitch = PRESET_PITCH[preset];
  return {
    ...current,
    eye: eyeLookingAt(pivot, heading, pitch, distance),
    heading,
    pitch,
    // Any tilt of the camera about its own axis is part of the mess a preset exists to undo.
    roll: 0,
    target: pivot,
  };
}

/** Which preset this camera is sitting at, or undefined when it is somewhere the viewer put it. */
export function activePreset(state: Camera3DState): Camera3DPreset | undefined {
  if (Math.abs(state.roll) > PRESET_MATCH_DEGREES) {
    return undefined;
  }
  for (const preset of CAMERA_3D_PRESETS) {
    if (Math.abs(state.pitch - PRESET_PITCH[preset]) > PRESET_MATCH_DEGREES) {
      continue;
    }
    // Looking straight down, every heading shows the same thing and the renderer is free to report
    // whichever one the camera happens to hold, so the plan view is matched on its pitch alone.
    if (preset === 'top' || headingsAgree(state.heading, PRESET_HEADING[preset])) {
      return preset;
    }
  }
  return undefined;
}

/** What a preset turns around: what the camera is looking at, or the ground beneath it. */
function presetPivot(state: Camera3DState): Camera3DEye {
  return state.target ?? fallbackTarget(state);
}

function headingsAgree(left: number, right: number): boolean {
  const difference = Math.abs(wrapHeading(left) - wrapHeading(right));
  return Math.min(difference, 360 - difference) <= PRESET_MATCH_DEGREES;
}
