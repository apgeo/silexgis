// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import type { WorkspaceSelection } from '../stores/workspaceStore.ts';
import {
  attachViewSync,
  type ViewSelectionPort,
  type ViewSyncHandle,
} from '../workspace/viewSync.ts';
import { fitLonLatExtent, mapLonLatExtent } from './mapContext.ts';

// The flat map's end of the two-way view sync.
//
// Only two things belong here: turning this map's camera into the four degrees the bus carries,
// and turning four degrees back into a camera move. Everything about *when* either of those is
// allowed to happen — which echoes to ignore, how long a following view stays quiet — is the
// protocol's, next door, so that both views obey one copy of it.

/** How long the map has to be still before it announces where it is. Matches its URL-hash writer. */
const SETTLE_MILLISECONDS = 300;

export interface ViewSync2dHandle {
  /** Announces where the map is looking right now — used when a second view opens beside it. */
  announce(): void;
  /** Announces what the viewer just picked on the map. */
  publishSelection(selection: WorkspaceSelection | null): void;
  /**
   * Keeps this map quiet for a move it is about to be given that nobody asked it to share —
   * a saved view being opened, which places both views itself.
   */
  muteUntilSettled(): void;
  detach(): void;
}

/**
 * Joins the flat map to the other views: publishes the ground it is showing once it settles,
 * follows the ground another view reports, and carries picks both ways.
 *
 * The selection is handed over as something that can be read as well as written, because the
 * protocol has to compare an arriving pick against what this window is actually showing — which
 * panels beside the map change without announcing anything.
 *
 * The debounce is the same one the URL writer uses and for the same reason — a pan raises the
 * move-ended event repeatedly and announcing each one would put the other view into a chase.
 */
export function attachViewSync2d(map: Map, selection: ViewSelectionPort): ViewSync2dHandle {
  let sync: ViewSyncHandle | undefined;
  let timer: number | undefined;

  const here = () => {
    const bounds = mapLonLatExtent();
    const zoom = map.getView().getZoom();
    return bounds && zoom !== undefined ? { bounds, zoom: Math.round(zoom) } : undefined;
  };

  const announce = () => {
    const view = here();
    if (view) {
      sync?.publishExtent(view.bounds, view.zoom);
    }
  };

  sync = attachViewSync('map2d', {
    onSelection: (picked: WorkspaceSelection | null) => selection.set(picked),
    currentSelection: () => selection.current(),
    onExtent: (bounds) => fitLonLatExtent(bounds),
    currentExtent: here,
  });

  const onMoveEnd = () => {
    window.clearTimeout(timer);
    timer = window.setTimeout(announce, SETTLE_MILLISECONDS);
  };
  map.on('moveend', onMoveEnd);

  return {
    announce,
    publishSelection: (selection) => sync?.publishSelection(selection),
    muteUntilSettled: () => sync?.muteUntilSettled(),
    detach() {
      map.un('moveend', onMoveEnd);
      window.clearTimeout(timer);
      sync?.detach();
      sync = undefined;
    },
  };
}
