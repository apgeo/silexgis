// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useMemo } from 'react';
import type { usePhotoLibraries } from '../../api/hooks.ts';
import {
  libraryPhotoLayerId,
  libraryPhotoSourceOf,
  createLibraryPhotoLayer,
  setLibraryPhotosEnabled,
} from '../../map/libraryPhotoLayer.ts';
import { applyPendingOverlayOrder, findOverlayLayer, getOverlayGroup } from '../../map/mapContext.ts';

/**
 * The neighbouring photo libraries this account may see, split into the ones in use, the ones
 * switched off by the installation and the ones nobody supplied an address for — and the map
 * overlays for the first group, kept in step with which of them are switched on.
 */
export function usePhotoLibraryOverlays(
  libraryStatus: ReturnType<typeof usePhotoLibraries>['data'],
  libraryPhotoSources: string[],
) {
  // The libraries this account may see. A caller outside the audience is told it may read nothing
  // and given an empty list, which is the same answer to this question as an installation that has
  // been given no library: either way there is nothing to offer, and neither is told which
  // products the installation runs.
  //
  // A library this installation has stopped using is left out, so no overlay is made for it and
  // nothing asks it for a viewport. That is the whole of what the switch has to do here: an
  // overlay left in place with its requests refused would draw an empty layer somebody would read
  // as an empty library.
  const photoLibraries = useMemo(
    () =>
      libraryStatus?.mayRead
        ? (libraryStatus.providers ?? []).filter((library) => !library.suspended)
        : [],
    [libraryStatus],
  );

  // The ones this installation has and is not using. No overlay and no layer row — a layer that
  // can only ever draw nothing is not a layer — but they are named in the panel all the same,
  // because a library that was there yesterday and is gone today is a question, and the panel is
  // where somebody looks for the answer.
  const suspendedPhotoLibraries = useMemo(
    () =>
      libraryStatus?.mayRead
        ? (libraryStatus.providers ?? []).filter((library) => library.suspended)
        : [],
    [libraryStatus],
  );

  // The products this build can read that nobody supplied an address for. The server answers this
  // for a full administrator and with an empty list for everybody else, so nothing here decides
  // who is told; what it earns is the one thing an empty layer panel cannot say for itself —
  // whether there is nothing to look at because nothing was connected. No overlay is made for
  // these: a layer that can only ever draw nothing is not a layer.
  const unconfiguredPhotoLibraries = useMemo(
    () => (libraryStatus?.mayRead ? (libraryStatus.unconfigured ?? []) : []),
    [libraryStatus],
  );

  // Overlays for those libraries. Not registered with the built-ins on mount, because their
  // existence is a server answer that arrives after it — the same way imported files and
  // georeferenced rasters are registered — and followed by the pending-order pass, because a saved
  // view can name a layer that did not exist when the view was applied.
  useEffect(() => {
    const wanted = new Set(photoLibraries.map((library) => libraryPhotoLayerId(library.source)));
    for (const library of photoLibraries) {
      if (!findOverlayLayer(libraryPhotoLayerId(library.source))) {
        getOverlayGroup().getLayers().push(createLibraryPhotoLayer(library.source));
      }
    }
    // A library disconnected while somebody was looking at the map: the row goes away rather than
    // staying as a layer that can only ever fail.
    for (const layer of getOverlayGroup().getLayers().getArray().slice()) {
      const id = layer.get('id') as string | undefined;
      if (id && libraryPhotoSourceOf(id) && !wanted.has(id)) {
        getOverlayGroup().getLayers().remove(layer);
      }
    }
    applyPendingOverlayOrder();
  }, [photoLibraries]);

  useEffect(() => {
    for (const library of photoLibraries) {
      const on = libraryPhotoSources.includes(library.source);
      findOverlayLayer(libraryPhotoLayerId(library.source))?.setVisible(on);
      setLibraryPhotosEnabled(library.source, on); // gate the bbox loader so hidden = no fetches
    }
  }, [photoLibraries, libraryPhotoSources]);

  return { photoLibraries, suspendedPhotoLibraries, unconfiguredPhotoLibraries };
}
