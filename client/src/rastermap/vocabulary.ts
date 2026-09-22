// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The seeded relation codes the raster-map surfaces are built from.
 *
 * A cave's scanned maps live entirely inside the resource-link machinery: "this image is
 * the plan view of this survey model" is a link under one of three seeded map-of codes,
 * and "this point on the image is station S" is a link under the seeded pin code. The
 * codes are the whole of the model — there is no map table — so which codes count is the
 * one fact every map surface must agree on, and it lives here.
 *
 * <b>Explicit list, NOT prefix-derived.</b> A "map-" prefix rule would swallow the pin
 * code, which names a point on a map rather than a map of a model, and every pin link
 * would surface as a phantom map tab. Seeded codes only, as with the trip roles: an
 * installation's custom `map-*` code joins the generic link panel, not the map tabs — a
 * designed surface is built from designed vocabulary.
 */
export const MAP_VIEW_CODES = ['map-plan-of', 'map-profile-of', 'map-other-of'] as const;

export type MapViewCode = (typeof MAP_VIEW_CODES)[number];

/** The code claiming a point on a raster map IS a survey station — a calibration-grade claim. */
export const MAP_STATION_POINT_CODE = 'map-station-point';

/** The view a map declares itself to be, which is nothing but its relation code read out. */
export type MapViewKind = 'plan' | 'profile' | 'other';

const VIEW_KIND_BY_CODE: Record<MapViewCode, MapViewKind> = {
  'map-plan-of': 'plan',
  'map-profile-of': 'profile',
  'map-other-of': 'other',
};

export function isMapViewCode(code: string): code is MapViewCode {
  return (MAP_VIEW_CODES as readonly string[]).includes(code);
}

export function viewKindOf(code: MapViewCode): MapViewKind {
  return VIEW_KIND_BY_CODE[code];
}

const CODE_BY_VIEW_KIND: Record<MapViewKind, MapViewCode> = {
  plan: 'map-plan-of',
  profile: 'map-profile-of',
  other: 'map-other-of',
};

/** The relation code that states a view kind — what declaring or re-kinding a map writes. */
export function codeOfViewKind(kind: MapViewKind): MapViewCode {
  return CODE_BY_VIEW_KIND[kind];
}

/** The three view kinds in the order every chooser offers them — the tab-strip order. */
export const MAP_VIEW_KINDS: readonly MapViewKind[] = ['plan', 'profile', 'other'];
