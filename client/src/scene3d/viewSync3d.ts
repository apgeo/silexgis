// SPDX-License-Identifier: AGPL-3.0-or-later
import { readCamera3D, toSceneCamera, type Camera3DReader } from './camera3d.ts';
import { mapZoomFor } from './pseudoZoom.ts';
import { cameraFramingBounds } from './viewBounds3d.ts';
import type { Scene3DCamera } from './scene3dEngine.ts';
import type { WorkspaceSelection } from '../stores/workspaceStore.ts';
import {
  attachViewSync,
  type ViewSelectionPort,
  type ViewSyncHandle,
} from '../workspace/viewSync.ts';

// The scene's end of the two-way view sync — the mirror of the flat map's, and deliberately the
// same shape: turn this camera into the four degrees the bus carries, and turn four degrees back
// into a camera move. The protocol itself, including everything about which messages to ignore,
// belongs to one module and neither view reimplements it.
//
// The one asymmetry worth stating: following an extent here moves the camera WITHOUT changing
// which way it points. The obvious alternative is the scene's own "frame this rectangle", which
// reorients to look straight down at it — so panning the flat map would repeatedly flatten a view
// the viewer had deliberately tilted to look into a cave, and the only way to keep a tilt would be
// to stop touching the map.
//
// The move a follower makes is a JUMP, not a flight, and that is what makes the exchange stop.
// The protocol keeps a following view quiet for a fixed settling period; a flight outlasts it, so
// an animated follow comes to rest after the quiet has already lapsed, and the camera's own
// arrival is then announced as though the viewer had made it. Measured, that is not a harmless
// extra message: the box this view reports for a given zoom is wider than the one the flat map
// reports for it, so each such echo sends the pair a step further out, and a few pans walk both
// views back to the whole globe. Landing immediately keeps the whole of a followed move inside the
// quiet period, which is the same reason a camera restored from the URL is not flown either.

/** How long the camera has to be still before the scene announces where it is looking. */
const SETTLE_MILLISECONDS = 300;

/** The parts of the engine this touches; a plain object satisfies it in a test. */
export type ViewSync3dEngine = Camera3DReader &
  Pick<
    Scene3DCamera,
    'setCamera' | 'getVisibleBounds' | 'getPseudoZoom' | 'onViewChanged' | 'cameraHeightForZoom'
  >;

export interface ViewSync3dHandle {
  /** Announces where the scene is looking right now — used when it opens beside an existing view. */
  announce(): void;
  /** Announces what the viewer just picked in the scene. */
  publishSelection(selection: WorkspaceSelection | null): void;
  /**
   * Keeps this scene quiet for a camera it is about to be given that nobody asked it to share —
   * a saved view being opened, which places both views itself.
   */
  muteUntilSettled(): void;
  /**
   * Brings the scene back to where the flat map is standing, after a spell of not following it.
   *
   * The scene gives way rather than the map being brought to the scene, and that is not a
   * preference: it is the rule the exchange already applies everywhere else — whoever was already
   * there answers, whoever has just arrived listens. A camera flown around underground while
   * uncoupled is the one arriving.
   */
  rejoin(): void;
  detach(): void;
}

export function attachViewSync3d(
  engine: ViewSync3dEngine,
  selection: ViewSelectionPort,
  options: { followsExtent?: () => boolean } = {},
): ViewSync3dHandle {
  let timer: number | undefined;

  const here = () => {
    const bounds = engine.getVisibleBounds();
    return bounds ? { bounds, zoom: mapZoomFor(engine.getPseudoZoom()) } : undefined;
  };

  const announce = () => {
    const view = here();
    if (view) {
      sync.publishExtent(view.bounds, view.zoom);
    }
  };

  const sync: ViewSyncHandle = attachViewSync('scene3d', {
    onSelection: (picked) => selection.set(picked),
    currentSelection: () => selection.current(),
    currentExtent: here,
    onExtent: (bounds, zoom) => {
      const next = cameraFramingBounds(bounds, zoom, readCamera3D(engine), (forZoom, latitude) =>
        engine.cameraHeightForZoom(forZoom, latitude),
      );
      if (next) {
        engine.setCamera(toSceneCamera(next), { animate: false });
      }
    },
  }, { followsExtent: options.followsExtent });

  const unsubscribeView = engine.onViewChanged(() => {
    window.clearTimeout(timer);
    timer = window.setTimeout(announce, SETTLE_MILLISECONDS);
  });

  return {
    announce,
    publishSelection: (selection) => sync.publishSelection(selection),
    muteUntilSettled: () => sync.muteUntilSettled(),
    rejoin: () => sync.rejoin(),
    detach() {
      window.clearTimeout(timer);
      unsubscribeView();
      sync.detach();
    },
  };
}
