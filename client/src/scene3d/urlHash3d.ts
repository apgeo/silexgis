// SPDX-License-Identifier: AGPL-3.0-or-later
import { toSceneCamera, wrapHeading, type Camera3DState } from './camera3d.ts';
import type { Scene3DCamera } from './scene3dEngine.ts';

// The 3D camera in the URL, so a place underground is as shareable as a place on the map.
//
// Format: `#3d/<lat>/<lon>/<height>/<heading>/<pitch>`. The flat map already owns
// `#<zoom>/<lat>/<lon>`, and the two cannot be confused: that parser is anchored at both ends and
// its first group accepts digits only, so the literal `3d` fails it on the first character and
// again on the segment count, while this one is anchored on that same literal prefix and so cannot
// match a bare number. Neither of those is a comment's job to guarantee, so both directions are
// tested.
//
// What is deliberately NOT in the hash: the projection, the layer switches, the surface mode and
// the basemap. A hash is a position, and a position is the thing worth pasting into a message;
// everything else about how a view is set up is what a saved view is for. Keeping the two apart is
// also what keeps the hash short enough to survive being pasted at all.
//
// Only one thing in a window may write the hash — there is exactly one of it — so this is attached
// by the 3D route and by nothing else. A scene mounted as a panel beside the flat map lets the map
// keep the hash it already owns, rather than the two overwriting each other on every pan.

export interface Scene3DHash {
  lat: number;
  lon: number;
  /** Metres above the ellipsoid; negative underground, which is a position worth sharing. */
  height: number;
  heading: number;
  pitch: number;
}

const SCENE_3D_HASH = /^#3d\/(-?\d+(?:\.\d+)?)\/(-?\d+(?:\.\d+)?)\/(-?\d+(?:\.\d+)?)\/(-?\d+(?:\.\d+)?)\/(-?\d+(?:\.\d+)?)$/;

/** How long the camera has to be still before the URL is rewritten. Matches the flat map's. */
const HASH_SETTLE_MILLISECONDS = 300;

/** Parses `#3d/<lat>/<lon>/<height>/<heading>/<pitch>`; null when absent or out of range. */
export function parseScene3dHash(hash: string = window.location.hash): Scene3DHash | null {
  const match = SCENE_3D_HASH.exec(hash);
  if (!match) {
    return null;
  }
  const [lat, lon, height, heading, pitch] = match.slice(1, 6).map(Number);
  if (![lat, lon, height, heading, pitch].every(Number.isFinite)) {
    return null;
  }
  if (lat < -90 || lat > 90 || lon < -180 || lon > 180) {
    return null;
  }
  if (pitch < -90 || pitch > 90) {
    return null;
  }
  return { lat, lon, height, heading: wrapHeading(heading), pitch };
}

/**
 * Writes the hash.
 *
 * The precisions are chosen so that reloading puts the camera back where it was to about a metre
 * and a tenth of a degree: five decimals of latitude is ~1 m, whole metres of altitude is finer
 * than any survey this shows, and a tenth of a degree of heading is a fifth of the width of the
 * moon. Truncating rather than writing every digit is also what makes the equality guard on the
 * write-back bite, so an idle camera stops rewriting the URL.
 */
export function formatScene3dHash({ lat, lon, height, heading, pitch }: Scene3DHash): string {
  return `#3d/${lat.toFixed(5)}/${lon.toFixed(5)}/${height.toFixed(0)}/${wrapHeading(heading).toFixed(1)}/${pitch.toFixed(1)}`;
}

/** True when the URL carries a shareable 3D position (used to skip an opening view). */
export function hasScene3dHash(): boolean {
  return parseScene3dHash() !== null;
}

/** The camera a hash describes. The projection is not in the hash, so it is the ordinary one. */
export function cameraFromHash(hash: Scene3DHash): Camera3DState {
  return {
    eye: { lon: hash.lon, lat: hash.lat, height: hash.height },
    heading: hash.heading,
    pitch: hash.pitch,
    roll: 0,
    projection: 'perspective',
  };
}

/** The part of the engine this needs; a plain object satisfies it in a test. */
export type Scene3DHashCamera = Pick<Scene3DCamera, 'getCamera' | 'setCamera' | 'onViewChanged'>;

/**
 * Restores the camera from the URL once, then keeps the URL in step with it. Returns a detach
 * function.
 *
 * Four things stop this from fighting the viewer, and all four are copied from the flat map's
 * because they were learnt there:
 *
 *   * The restore happens once, on attach. Nothing listens for later changes to the hash, so the
 *     write-back can never be read back as an instruction.
 *   * The restore jumps rather than flies. An animated restore raises the camera's own
 *     settled event part-way through, which would write a position the camera was merely
 *     passing through.
 *   * The write-back waits for the camera to be still, and the timer is cleared on detach.
 *   * It replaces the history entry instead of adding one, and only when the text would change,
 *     so panning does not fill the back button with a hundred near-identical positions.
 */
export function attachScene3dHash(engine: Scene3DHashCamera): () => void {
  const initial = parseScene3dHash();
  if (initial) {
    engine.setCamera(toSceneCamera(cameraFromHash(initial)), { animate: false });
  }

  let timer: number | undefined;
  const write = () => {
    const camera = engine.getCamera();
    const next = formatScene3dHash({
      lat: camera.latitude,
      lon: camera.longitude,
      height: camera.height,
      heading: camera.heading,
      pitch: camera.pitch,
    });
    if (next !== window.location.hash) {
      window.history.replaceState(
        null,
        '',
        `${window.location.pathname}${window.location.search}${next}`,
      );
    }
  };

  const unsubscribe = engine.onViewChanged(() => {
    window.clearTimeout(timer);
    timer = window.setTimeout(write, HASH_SETTLE_MILLISECONDS);
  });

  return () => {
    unsubscribe();
    window.clearTimeout(timer);
  };
}
