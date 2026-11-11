// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The refusals the map-sheet routes give — drawing, placing, previewing, importing — by the part of the
 * code after `georeferenced_map.`, each with its sentence under `geodata.placement.errors`.
 * The list is closed so an unknown code falls back to the generic sentence rather than putting
 * a key, or an identifier, on screen.
 */
export const PLACEMENT_ERROR_CODES = [
  'page_out_of_range',
  'layer_unknown',
  'dpi_out_of_range',
  'busy',
  'not_a_sheet',
  'not_rendered',
  'no_manual_placement',
  'placement_transformation_unknown',
  'placement_too_few_points',
  'placement_too_many_points',
  'placement_points_degenerate',
  'placement_point_outside_picture',
  'placement_point_invalid',
  'placement_mask_invalid',
  'placement_crs_unknown',
  'placement_version_unknown',
  'preview_busy',
  'preview_failed',
  'annotation_invalid',
  'no_placement',
] as const;

export type PlacementErrorCode = (typeof PLACEMENT_ERROR_CODES)[number];

const CODE_PREFIX = 'georeferenced_map.';

/** `placement_too_few_points` → `placementTooFewPoints`, the shape every translation key has. */
export function errorKeySuffix(code: PlacementErrorCode): string {
  return code.replace(/_([a-z])/g, (_, letter: string) => letter.toUpperCase());
}

/** The translation key for a refusal's code, or the generic one for anything not in the list. */
export function placementErrorKey(code: string | null | undefined): string {
  if (typeof code === 'string' && code.startsWith(CODE_PREFIX)) {
    const suffix = code.slice(CODE_PREFIX.length);
    if ((PLACEMENT_ERROR_CODES as readonly string[]).includes(suffix)) {
      return `geodata.placement.errors.${errorKeySuffix(suffix as PlacementErrorCode)}`;
    }
  }
  return 'geodata.placement.errors.generic';
}
