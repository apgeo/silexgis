// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The filter as it travels — the same shape the server reads, written out here rather than taken
 * from the generated client.
 *
 * The generated types describe one request's body. This describes a document that is saved, put in
 * a link, referenced by a map view and edited by a control that never sends it anywhere. Those
 * outlive any single endpoint, so they are spelled out where the logic that manipulates them lives,
 * and the request type is assembled from them at the edge.
 */

export type FilterOp =
  | 'equals'
  | 'in'
  | 'contains'
  | 'startsWith'
  | 'lessThan'
  | 'greaterThan'
  | 'between'
  | 'isEmpty'
  | 'isNotEmpty'
  | 'within'
  | 'inside';

export type FieldKind = 'text' | 'number' | 'boolean' | 'instant' | 'id' | 'spatial';

export type SortKey = 'created' | 'updated' | 'title' | 'owner' | 'proximity';

/**
 * A value with its type still attached.
 *
 * The type travels the whole way rather than being inferred at either end, because the difference
 * between the number 4.5 and the text "4.5" is a difference the database can see: a depth recorded
 * as one is not a row a filter for the other should find.
 *
 * The discriminator is spelled `type`, which is what the server reads. The generated contract
 * cannot check this — the tool describes a polymorphic list as an unknown — so it is pinned by a
 * test on each side holding the same literal JSON.
 */
export type FilterValue =
  | { type: 'text'; value: string }
  | { type: 'number'; value: number }
  | { type: 'boolean'; value: boolean }
  | { type: 'instant'; value: string }
  | { type: 'id'; value: string };

export type FilterNode =
  | { node: 'allOf'; of: FilterNode[] }
  | { node: 'anyOf'; of: FilterNode[] }
  | { node: 'not'; of: FilterNode }
  | { node: 'condition'; field: string; op: FilterOp; values: FilterValue[] };

export interface WorldScope {
  world: string;
  where: FilterNode | null;
}

/**
 * A filter, as it is saved, linked and edited.
 *
 * It carries no viewport and no free text, and both absences are load-bearing. A stored extent
 * would be a shareable, negatable statement about where somebody was looking; a "search" beside
 * the conditions would be a second way to say one thing, and a second place to enforce what may be
 * matched on.
 */
export interface FilterDocument {
  version: number;
  scope: WorldScope[];
  sort: SortKey;
  descending: boolean;
}

export const CURRENT_FILTER_VERSION = 1;

/** One field a world admits, exactly as the server described it. */
export interface FilterField {
  key: string;
  labelKey: string;
  kind: FieldKind;
  /**
   * Which operators this field admits.
   *
   * Read from the server rather than derived from the kind here. A copy of that table on this side
   * would be right until the day it was not, and the disagreement would show as a control offering
   * something the server refuses.
   */
  ops: FilterOp[];
  options: string | null;
  sortable: boolean;
}

export interface WorldVocabulary {
  world: string;
  labelKey: string;
  fields: FilterField[];
  sorts: SortKey[];
}

export interface FilterLimits {
  maxNodes: number;
  maxDepth: number;
  maxValuesPerCondition: number;
  maxWorlds: number;
  maxTextValueLength: number;
  maxPageSize: number;
}

export interface FilterVocabulary {
  worlds: WorldVocabulary[];
  limits: FilterLimits;
}

/**
 * One row of an answer.
 *
 * There is nowhere here for a coordinate, and that is the shape rather than an omission: how much
 * of a protected position somebody is shown is decided in one place, and this is not it.
 * `placeable` is a permission, not a position — it says whether "show me where this is" is an offer
 * the control can make.
 */
export interface FilterHit {
  world: string;
  id: string;
  title: string;
  subtitle: string | null;
  symbol: string | null;
  placeable: boolean;
}
