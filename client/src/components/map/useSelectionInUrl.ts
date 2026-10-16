// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import { fetchFeature } from '../../api/hooks.ts';
import { APPROXIMATE_MAX_ZOOM, fitGeoJsonGeometry } from '../../map/mapContext.ts';
import {
  SELECT_PARAM,
  parseSelectionParam,
  selectionFromEnvelope,
  selectionRefOf,
  writeSelectionToUrl,
} from '../../map/selectionUrl.ts';
import { parseMapHash } from '../../map/urlHash.ts';
import { useWorkspaceStore } from '../../stores/workspaceStore.ts';

/**
 * Keeps what the map has selected in its address — `?select=cave:<id>` — so that a copied link
 * opens on the same selection, and selects what an address it was opened with names.
 *
 * What an address names is resolved only through the object's own endpoint, which decides what
 * this reader may see: an id the reader cannot see selects nothing, and nothing says whether it
 * does not exist or is not theirs to see. The camera is left alone when the address also says
 * where to look; otherwise it frames the object as that endpoint drew it — the position the
 * reader would be shown on the object's own page, snapped where it must be, loosely where it is
 * approximate, and nowhere at all where it is withheld.
 */
export function useSelectionInUrl() {
  const [requested] = useState(() =>
    parseSelectionParam(new URLSearchParams(window.location.search).get(SELECT_PARAM)),
  );
  const [cameraInAddress] = useState(() => parseMapHash() !== null);
  const resolving = useRef(requested !== null);
  const selection = useWorkspaceStore((s) => s.selection);

  useEffect(() => {
    if (!requested) {
      return undefined;
    }
    let cancelled = false;
    void fetchFeature(requested.id)
      .then((envelope) => {
        if (cancelled) {
          return;
        }
        const picked = selectionFromEnvelope(requested, envelope);
        if (!picked) {
          return;
        }
        useWorkspaceStore.getState().setSelection(picked);
        const geometry = envelope.feature.geometry;
        if (!cameraInAddress && geometry) {
          fitGeoJsonGeometry(geometry, envelope.feature.approximateLocation ? APPROXIMATE_MAX_ZOOM : undefined);
        }
      })
      .catch(() => {
        // Not there, or not this reader's to see — the two are not told apart.
      })
      .finally(() => {
        if (cancelled) {
          return;
        }
        resolving.current = false;
        // Whatever was resolved (or nothing) is what the address says from here on.
        writeSelectionToUrl(selectionRefOf(useWorkspaceStore.getState().selection));
      });
    return () => {
      cancelled = true;
    };
  }, [requested, cameraInAddress]);

  useEffect(() => {
    // While the address's own selection is being looked up, the empty selection the page starts
    // with must not erase it.
    if (resolving.current) {
      return;
    }
    writeSelectionToUrl(selectionRefOf(selection));
  }, [selection]);
}
