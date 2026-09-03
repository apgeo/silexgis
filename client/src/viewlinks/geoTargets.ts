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
 * Roughly how wide the uncertainty is around a position that came back approximate, in degrees.
 *
 * The server snaps such a point to a grid whose size is a server setting (5 km by default) that no
 * endpoint publishes, so this is the client's honest approximation of it rather than the number
 * itself — about 5.5 km north–south, and more than that east–west at these latitudes, which errs
 * in the safe direction. Views translate it into their own terms: the flat map into a zoom
 * ceiling, the scene into a wider box. If the grid is ever published, use it and delete this.
 */
export const APPROXIMATE_SPAN_DEGREES = 0.05;

/** Where a reference is, and how well this reader is allowed to know it. */
export interface GeoTarget {
  geometry: object;
  /**
   * The position came back snapped to a grid rather than as surveyed. The geometry is real and
   * worth framing; what is not real is its precision, so a view that frames it must not close in
   * on it as though it were a surveyed point.
   */
  approximate: boolean;
}

/**
 * Where a reference is, or null when this caller may not be told at all.
 *
 * Null is an ordinary answer rather than an error, but it covers less ground than it looks like
 * it does. Protection has two outcomes, not one: a line or a polygon whose position is withheld
 * comes back with no geometry and there is nothing to frame — but a *point* comes back snapped to
 * a grid, which is present and approximate. So the flag is returned beside the geometry instead
 * of the geometry alone: a caller that reasoned only about the null case would take a 5 km-snapped
 * point for a surveyed one, which is the exact belief the snapping exists to prevent.
 */
export async function geometryFor(
  queryClient: QueryClient,
  ref: ResourceRef,
): Promise<GeoTarget | null> {
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
  const geometry = (feature.feature.geometry as object | null) ?? null;
  return geometry === null ? null : { geometry, approximate: feature.feature.approximateLocation };
}
