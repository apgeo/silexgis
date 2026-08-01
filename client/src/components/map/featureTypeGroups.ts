// SPDX-License-Identifier: AGPL-3.0-or-later
import type { FeatureType } from '../../api/hooks.ts';
import type { DrawShape } from '../../map/mapEdit.ts';

/** Geometry groups, rendered in this order. `any` collects the flexible types. */
export const FEATURE_TYPE_GROUP_ORDER = ['point', 'line', 'polygon', 'any'] as const;

export type FeatureTypeGroupKind = (typeof FEATURE_TYPE_GROUP_ORDER)[number];

export interface FeatureTypeGroup {
  kind: FeatureTypeGroupKind;
  items: FeatureType[];
}

// Each accepted geometry class collapses to the drawable family it belongs to
// (a MultiPoint type is still drawn point by point).
const familyOfClass: Record<string, Exclude<FeatureTypeGroupKind, 'any'>> = {
  point: 'point',
  multiPoint: 'point',
  lineString: 'line',
  multiLineString: 'line',
  polygon: 'polygon',
  multiPolygon: 'polygon',
};

/**
 * The palette group a feature type belongs to, derived from its accepted geometry
 * classes: a single family keeps its group, a mix (or an unconstrained type) is `any`.
 */
export function geometryGroupOf(type: Pick<FeatureType, 'acceptedGeometryClasses'>): FeatureTypeGroupKind {
  const families = new Set(
    type.acceptedGeometryClasses.map((c) => familyOfClass[c]).filter((f) => f !== undefined),
  );
  if (families.size === 1) {
    return [...families][0];
  }
  return families.size === 0 ? 'point' : 'any';
}

/**
 * The shape the draw tool should be armed with for a feature type; null means the
 * type accepts several families and the user (or a default) picks the shape.
 */
export function drawShapeForType(type: Pick<FeatureType, 'acceptedGeometryClasses'> | undefined): DrawShape | null {
  switch (type ? geometryGroupOf(type) : 'point') {
    case 'point':
      return 'Point';
    case 'line':
      return 'LineString';
    case 'polygon':
      return 'Polygon';
    default:
      return null;
  }
}

/**
 * Groups feature types by drawable geometry family in palette order, dropping empty
 * groups. Shared by the symbol palette and the map context menu so both offer the
 * same catalog in the same structure.
 */
export function groupFeatureTypes(featureTypes: FeatureType[]): FeatureTypeGroup[] {
  return FEATURE_TYPE_GROUP_ORDER.map((kind) => ({
    kind,
    items: featureTypes.filter((ft) => geometryGroupOf(ft) === kind),
  })).filter((group) => group.items.length > 0);
}
