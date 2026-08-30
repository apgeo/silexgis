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

/** The drawable families a feature type accepts, with anything unrecognised dropped. */
function familiesOf(type: Pick<FeatureType, 'acceptedGeometryClasses'>): Set<FeatureTypeGroupKind> {
  return new Set(type.acceptedGeometryClasses.map((c) => familyOfClass[c]).filter((f) => f !== undefined));
}

/**
 * Which single geometry family a feature type is, for the purpose of arming a draw tool:
 * a single family answers itself, a mix answers `any` — meaning "more than one, so ask".
 */
export function geometryGroupOf(type: Pick<FeatureType, 'acceptedGeometryClasses'>): FeatureTypeGroupKind {
  const families = familiesOf(type);
  if (families.size === 1) {
    return [...families][0];
  }
  return families.size === 0 ? 'point' : 'any';
}

/**
 * The palette group a feature type is filed under.
 *
 * Deliberately not the same question as the one above. A kind that accepts two families has no
 * single family to arm a draw tool with, but it does have a place readers look for it in, and
 * those are different answers: filing every widened kind under "Other" would move the commonest
 * surface symbol there — a doline is a marker on a small depression and an outline on a large one
 * — out of the group it has always been in, as a side effect of gaining a second geometry. So a
 * kind is filed under the first family it accepts, in the order the palette lists its groups, and
 * `any` is left for a kind that names no drawable geometry at all.
 */
export function paletteGroupOf(type: Pick<FeatureType, 'acceptedGeometryClasses'>): FeatureTypeGroupKind {
  const families = familiesOf(type);
  return FEATURE_TYPE_GROUP_ORDER.find((kind) => families.has(kind)) ?? 'point';
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

/** Draw shapes in palette order, so a chooser lists them the same way the palette groups them. */
const shapeOfFamily: Record<Exclude<FeatureTypeGroupKind, 'any'>, DrawShape> = {
  point: 'Point',
  line: 'LineString',
  polygon: 'Polygon',
};

/**
 * Every shape a feature type may be drawn as, in palette order.
 *
 * A type accepting exactly one family has one entry and is armed with it without asking. A type
 * accepting several — a doline, which is a marker on a small one and an outline on a large one —
 * has more than one, and something has to offer the choice: arming such a type with a default and
 * offering no way past it would mean the shape the type was widened for could never be drawn.
 */
export function drawShapesForType(
  type: Pick<FeatureType, 'acceptedGeometryClasses'> | undefined,
): DrawShape[] {
  if (!type) {
    return ['Point'];
  }
  const families = familiesOf(type);
  const shapes = FEATURE_TYPE_GROUP_ORDER.filter(
    (kind): kind is Exclude<FeatureTypeGroupKind, 'any'> => kind !== 'any' && families.has(kind),
  ).map((kind) => shapeOfFamily[kind]);
  return shapes.length > 0 ? shapes : ['Point'];
}

/**
 * Groups feature types by drawable geometry family in palette order, dropping empty
 * groups. Shared by the symbol palette and the map context menu so both offer the
 * same catalog in the same structure.
 */
export function groupFeatureTypes(featureTypes: FeatureType[]): FeatureTypeGroup[] {
  return FEATURE_TYPE_GROUP_ORDER.map((kind) => ({
    kind,
    items: featureTypes.filter((ft) => paletteGroupOf(ft) === kind),
  })).filter((group) => group.items.length > 0);
}
