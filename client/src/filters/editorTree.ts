// SPDX-License-Identifier: AGPL-3.0-or-later
import { isComplete } from './preflight.ts';
import { isSpatialAnchor, namesAnchor } from './spatial.ts';
import type { FieldKind, FilterField, FilterNode, FilterOp, FilterValue } from './types.ts';

/**
 * The condition editor's model of one world's conditions, with nothing drawn.
 *
 * The editor works on a tree that may hold half-written rows — a field with no value yet, a range
 * with one end — because that is what a filter looks like while somebody is writing it. What is
 * counted and what is applied is that tree with the half-written parts left out ({@link prune}),
 * never the draft itself, so a row somebody has not finished can neither be refused by the server
 * nor quietly match everything.
 *
 * A node is addressed by its path from the root: the index of each child taken on the way down,
 * with a negation's single child at index 0. Paths are positions, not identities, which is enough
 * here: every edit replaces the whole tree, and the editor re-reads the paths from the new one.
 */

export type NodePath = readonly number[];

/** A path as a map key. */
export function pathKey(path: NodePath): string {
  return path.join('.');
}

export function childrenOf(node: FilterNode): FilterNode[] {
  if (node.node === 'condition') {
    return [];
  }
  return node.node === 'not' ? [node.of] : node.of;
}

export function nodeAt(root: FilterNode, path: NodePath): FilterNode | null {
  let current: FilterNode | undefined = root;
  for (const index of path) {
    if (current === undefined) {
      return null;
    }
    current = childrenOf(current)[index];
  }
  return current ?? null;
}

/** The tree with the node at `path` replaced. The root itself is replaced by the empty path. */
export function replaceAt(root: FilterNode, path: NodePath, replacement: FilterNode): FilterNode {
  if (path.length === 0) {
    return replacement;
  }
  const [head, ...rest] = path;
  if (root.node === 'condition') {
    return root;
  }
  if (root.node === 'not') {
    return head === 0 ? { node: 'not', of: replaceAt(root.of, rest, replacement) } : root;
  }
  return {
    node: root.node,
    of: root.of.map((child, i) => (i === head ? replaceAt(child, rest, replacement) : child)),
  };
}

/**
 * The tree with the node at `path` taken out.
 *
 * Removing a negation's only child removes the negation with it: a NOT of nothing is not a question.
 * The root cannot be removed, only emptied, which the caller does by replacing it.
 */
export function removeAt(root: FilterNode, path: NodePath): FilterNode {
  if (path.length === 0) {
    return root;
  }
  const parentPath = path.slice(0, -1);
  const parent = nodeAt(root, parentPath);
  if (parent === null || parent.node === 'condition') {
    return root;
  }
  if (parent.node === 'not') {
    return parentPath.length === 0 ? { node: 'allOf', of: [] } : removeAt(root, parentPath);
  }
  const index = path[path.length - 1];
  return replaceAt(root, parentPath, { node: parent.node, of: parent.of.filter((_, i) => i !== index) });
}

/** The tree with `child` added at the end of the group at `groupPath`. */
export function appendChild(root: FilterNode, groupPath: NodePath, child: FilterNode): FilterNode {
  const group = nodeAt(root, groupPath);
  if (group === null || (group.node !== 'allOf' && group.node !== 'anyOf')) {
    return root;
  }
  return replaceAt(root, groupPath, { node: group.node, of: [...group.of, child] });
}

/** Wraps the node at `path` in a negation, or takes the negation away if it already is one. */
export function toggleNot(root: FilterNode, path: NodePath): FilterNode {
  const node = nodeAt(root, path);
  if (node === null) {
    return root;
  }
  return replaceAt(root, path, node.node === 'not' ? node.of : { node: 'not', of: node });
}

/** The deepest nesting under a node, counted the way the server counts it: a condition is one. */
export function depthOf(node: FilterNode | null): number {
  if (node === null) {
    return 0;
  }
  if (node.node === 'condition') {
    return 1;
  }
  return 1 + childrenOf(node).reduce((deepest, child) => Math.max(deepest, depthOf(child)), 0);
}

/**
 * Whether an edit keeps the tree inside the server's nesting limit.
 *
 * Asked of the tree the edit would produce rather than computed from the position, so a nested
 * group, a negation and a negated group all answer by the one rule the server applies. A row that
 * is still empty counts like a written one: the limit is about the shape, and a group the editor
 * offers and the server would then refuse is worse than one it never offers.
 */
export function withinDepth(candidate: FilterNode, maxDepth: number): boolean {
  return depthOf(candidate) <= maxDepth;
}

/**
 * The tree as it can be sent: half-written conditions, and the groups and negations they leave
 * empty, taken out. Null when nothing is left.
 */
export function prune(node: FilterNode | null): FilterNode | null {
  if (node === null) {
    return null;
  }
  if (node.node === 'condition') {
    return isComplete(node) ? node : null;
  }
  if (node.node === 'not') {
    const inner = prune(node.of);
    return inner === null ? null : { node: 'not', of: inner };
  }
  const kept = node.of.map(prune).filter((child): child is FilterNode => child !== null);
  return kept.length === 0 ? null : { node: node.node, of: kept };
}

/** Whether the draft holds anything the pruned tree leaves out. */
export function hasUnfinished(node: FilterNode | null): boolean {
  return JSON.stringify(prune(node)) !== JSON.stringify(emptyToNull(node));
}

function emptyToNull(node: FilterNode | null): FilterNode | null {
  if (node === null) {
    return null;
  }
  if (node.node === 'condition') {
    return node;
  }
  if (node.node === 'not') {
    const inner = emptyToNull(node.of);
    return inner === null ? null : { node: 'not', of: inner };
  }
  const kept = node.of.map(emptyToNull).filter((child): child is FilterNode => child !== null);
  return kept.length === 0 ? null : { node: node.node, of: kept };
}

/**
 * Whether a field may be asked at the row at `path`.
 *
 * A field that names a requirement — a feature type's own property, which only means something on
 * rows of that type — is offered only where the server will accept it: below an all-of group that
 * holds, directly, the condition the field requires (`field` equal to `value`, or a one-valued
 * in-list), anywhere above the row. That condition under an any-of, under a negation, or in a sibling
 * group does not fix the type for this row, and the server refuses the field there exactly as it
 * refuses a field it has never heard of.
 */
export function requirementMet(root: FilterNode, path: NodePath, field: FilterField): boolean {
  const requires = field.requires;
  if (requires === null) {
    return true;
  }
  for (let length = 0; length < path.length; length++) {
    const ancestor = nodeAt(root, path.slice(0, length));
    if (ancestor === null || ancestor.node !== 'allOf') {
      continue;
    }
    const own = path[length];
    const fixes = ancestor.of.some((child, i) => i !== own && fixesValue(child, requires.field, requires.value));
    if (fixes) {
      return true;
    }
  }
  return false;
}

function fixesValue(node: FilterNode, field: string, value: string): boolean {
  if (node.node !== 'condition' || node.field !== field) {
    return false;
  }
  if (node.op !== 'equals' && node.op !== 'in') {
    return false;
  }
  return node.values.length > 0 && node.values.every((v) => v.type === 'id' && v.value === value);
}

/** The condition a row starts with when somebody picks a field: its first operator, no value. */
export function startCondition(field: FilterField): FilterNode {
  const op = editableOps(field)[0] ?? 'equals';
  return { node: 'condition', field: field.key, op, values: [] };
}

/**
 * The operators the editor offers for a field: exactly those the server listed. A spatial field's
 * `within` and `inside` are written as a radius and an anchor, or as an outline, like any other
 * row's values.
 */
export function editableOps(field: FilterField): FilterOp[] {
  return field.ops;
}

/** Whether the editor can write a value for this kind of field at all. Every kind it knows. */
export function isEditableKind(kind: FieldKind): boolean {
  return kind === 'text' || kind === 'number' || kind === 'boolean' || kind === 'instant'
    || kind === 'id' || kind === 'spatial';
}

/**
 * The values a condition keeps when its operator changes: as many as the new operator takes, so
 * moving from "more than 30" to "at least 30" keeps the 30, and moving to "is empty" drops it.
 */
export function valuesForOp(op: FilterOp, values: FilterValue[]): FilterValue[] {
  if (op === 'isEmpty' || op === 'isNotEmpty') {
    return [];
  }
  if (op === 'between') {
    return values.slice(0, 2);
  }
  if (op === 'in') {
    return values;
  }
  // A radius and an anchor mean nothing to another operator and nothing else means anything to
  // these, so a spatial row starts again when its operator changes either way.
  if (op === 'within') {
    return values[0]?.type === 'number' ? values.slice(0, 2) : [];
  }
  if (op === 'inside') {
    return values[0]?.type === 'polygon' || values[0]?.type === 'object' ? values.slice(0, 1) : [];
  }
  return isSpatialAnchor(values[0]) || isSpatialAnchor(values[1]) ? [] : values.slice(0, 1);
}

/**
 * Where a refusal of an anchor lands: on every sent row that names something the server had to
 * look up — an object, an area, another saved filter. The server answers all of them with one
 * sentence that names no row, on purpose: saying which anchor was withheld would say that it
 * exists. With no such row the refusal was about the place nearest-first is measured from, and
 * stays with the filter as a whole.
 */
export function placeAnchorRefusal(
  detail: string,
  root: FilterNode | null,
): { byPath: Map<string, string[]>; unplaced: string[] } {
  const byPath = new Map<string, string[]>();
  const rows = conditionsWithPaths(root)
    .filter((c) => isComplete(c.condition) && namesAnchor(c.condition));
  for (const row of rows) {
    byPath.set(pathKey(row.path), [detail]);
  }
  return { byPath, unplaced: rows.length === 0 ? [detail] : [] };
}

/** Every condition in the tree, with its path. */
export function conditionsWithPaths(
  node: FilterNode | null,
  path: NodePath = [],
): { path: NodePath; condition: Extract<FilterNode, { node: 'condition' }> }[] {
  if (node === null) {
    return [];
  }
  if (node.node === 'condition') {
    return [{ path, condition: node }];
  }
  return childrenOf(node).flatMap((child, i) => conditionsWithPaths(child, [...path, i]));
}

/**
 * Where on the screen each sentence of a refusal belongs.
 *
 * The server refuses a filter with one problem whose detail joins a sentence per fault, and every
 * sentence about a condition quotes that condition's field. A sentence is put on each row whose
 * field it quotes; a sentence that quotes no field on screen — a limit on the whole filter, a world
 * — stays with the filter as a whole. Matching prose is the weaker form of this: the stronger is a
 * refusal that names the node it is about, which the server does not send yet. Until it does, a
 * reworded sentence degrades to "shown above the rows", never to a sentence on the wrong row.
 */
export function placeRefusal(
  detail: string,
  root: FilterNode | null,
): { byPath: Map<string, string[]>; unplaced: string[] } {
  const byPath = new Map<string, string[]>();
  const unplaced: string[] = [];
  // Only the rows that were sent: a half-written row is left out of the request, so a sentence
  // cannot be about it even when it holds the same field.
  const conditions = conditionsWithPaths(root).filter((c) => isComplete(c.condition));
  for (const sentence of splitSentences(detail)) {
    const rows = conditions.filter((c) => sentence.includes(`'${c.condition.field}'`));
    if (rows.length === 0) {
      unplaced.push(sentence);
      continue;
    }
    for (const row of rows) {
      const key = pathKey(row.path);
      byPath.set(key, [...(byPath.get(key) ?? []), sentence]);
    }
  }
  return { byPath, unplaced };
}

function splitSentences(detail: string): string[] {
  return detail
    .split(/(?<=[.!?])\s+(?=\S)/)
    .map((s) => s.trim())
    .filter((s) => s.length > 0);
}
