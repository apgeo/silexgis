// SPDX-License-Identifier: AGPL-3.0-or-later
import type { QueryClient } from '@tanstack/react-query';
import { featureQuery } from '../api/hooks.ts';
import type { ResourceRef } from './resourceRef.ts';

/**
 * Turning a reference into ground, for the two views that draw ground.
 *
 * The flat map and the 3D scene answer the same references in the same way — a feature is
 * somewhere, and being shown it means framing that somewhere — so the "where is it" half is
 * written once here and each view keeps only its own camera.
 *
 * <b>The geometry is fetched, not carried.</b> The bus carries references and never payloads,
 * which is the workspace rule and is what lets a pop-out in another window answer a reveal at
 * all: it has no access to the sending window's memory, only to its own cache and the server.
 * Going through the query client rather than a bare request means a reveal for a feature the
 * window is already showing costs nothing.
 */

/** Reference kinds a map or scene can frame. */
export function isGeographic(ref: ResourceRef): boolean {
  return ref.targetType === 'feature';
}

/**
 * The GeoJSON geometry a reference names, or null when it has none this caller may see.
 *
 * Null is an ordinary answer and not an error: a feature whose exact position is protected from
 * this reader comes back without one, or snapped, and the view's honest response is to not move
 * rather than to move somewhere approximate and let a reader believe it is the place.
 */
export async function geometryFor(
  queryClient: QueryClient,
  ref: ResourceRef,
): Promise<object | null> {
  if (ref.targetType !== 'feature') {
    return null;
  }

  const feature = await queryClient.fetchQuery({
    ...featureQuery(ref.targetId),
    // A position is the kind of thing worth re-reading; it is also the kind of thing a reader
    // follows several links to in a row, so a short life is the balance rather than none.
    staleTime: 30_000,
  });

  // The response is a discriminated envelope — a cave, an entrance, a centerline or a plain
  // feature — and only the inner row carries geometry. Read off `feature` rather than off the
  // envelope, so a kind added later keeps working instead of silently returning nothing.
  return (feature.feature.geometry as object | null) ?? null;
}
