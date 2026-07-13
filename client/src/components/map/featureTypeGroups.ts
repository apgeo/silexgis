// SPDX-License-Identifier: AGPL-3.0-or-later
import type { FeatureType } from '../../api/hooks.ts';

/** Geometry-kind groups, rendered in this order. `any` collects the flexible types. */
export const FEATURE_TYPE_GROUP_ORDER = ['point', 'line', 'polygon', 'any'] as const;

export type FeatureTypeGroupKind = (typeof FEATURE_TYPE_GROUP_ORDER)[number];

export interface FeatureTypeGroup {
  kind: FeatureTypeGroupKind;
  items: FeatureType[];
}

/**
 * Groups feature types by geometry kind in palette order, dropping empty groups.
 * Shared by the symbol palette and the map context menu so both offer the same
 * catalog in the same structure.
 */
export function groupFeatureTypes(featureTypes: FeatureType[]): FeatureTypeGroup[] {
  return FEATURE_TYPE_GROUP_ORDER.map((kind) => ({
    kind,
    items: featureTypes.filter((ft) => (ft.geometryKind ?? 'point') === kind),
  })).filter((group) => group.items.length > 0);
}
