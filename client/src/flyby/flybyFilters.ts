// SPDX-License-Identifier: AGPL-3.0-or-later
import { emptyDocument, withWhere } from '../filters/document.ts';
import type { FilterDocument, FilterNode } from '../filters/types.ts';

// The filters a flyby asks the server, in the one filter language the application speaks.
//
// A flyby never holds a list of caves or features of its own: what it is about and what it shows
// are questions, put to the same routes the rest of the application asks, under the rights of
// whoever plays the flyby. Here are the two questions it composes itself — "the caves this saved
// filter keeps" and "the features of these types" — written as ordinary filter documents with the
// conditions every other surface writes, so the server reads them exactly as it reads those.

/** The world caves and surface features are both in. */
export const FLYBY_FEATURE_WORLD = 'feature';

function idEquals(field: string, value: string): FilterNode {
  return { node: 'condition', field, op: 'equals', values: [{ type: 'id', value }] };
}

/** One more condition beside a world's own: added to its all-of group rather than nesting another. */
function alsoRequiring(where: FilterNode | null, condition: FilterNode): FilterNode {
  if (!where) {
    return condition;
  }
  return where.node === 'allOf' ? { node: 'allOf', of: [...where.of, condition] } : { node: 'allOf', of: [where, condition] };
}

/**
 * The caves a saved filter's document keeps, in the order it sorts them: its conditions on
 * features, and that the feature is a cave. Undefined for a filter about nothing a cave is — a
 * filter of documents or of trips selects no cave to fly to.
 */
export function cavesOfFilterDocument(document: FilterDocument): FilterDocument | undefined {
  const scope = document.scope.find((candidate) => candidate.world === FLYBY_FEATURE_WORLD);
  if (!scope) {
    return undefined;
  }
  return {
    version: document.version,
    scope: [{ world: FLYBY_FEATURE_WORLD, where: alsoRequiring(scope.where, idEquals('kind', 'cave')) }],
    sort: document.sort,
    descending: document.descending,
    ...(document.anchor ? { anchor: document.anchor } : {}),
  };
}

/** The features of these types from the catalogue, written as the feature list's own type condition would be. */
export function featuresOfTypesDocument(typeIds: readonly number[]): FilterDocument {
  const values = [...new Set(typeIds)].sort((a, b) => a - b).map((id) => ({ type: 'id' as const, value: String(id) }));
  const where: FilterNode =
    values.length === 1 ? idEquals('typeId', values[0].value) : { node: 'condition', field: 'typeId', op: 'in', values };
  return withWhere(emptyDocument([FLYBY_FEATURE_WORLD]), FLYBY_FEATURE_WORLD, where);
}
