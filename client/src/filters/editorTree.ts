// SPDX-License-Identifier: AGPL-3.0-or-later
import { dayBound, pickedDay } from './dayBounds.ts';
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
 *
 * A picked day is kept as the day, not as the moment written for the old comparison. "At least the
 * 5th" is bounded by the 5th's first moment and "more than the 5th" by its last, so a bound carried
 * across unchanged would turn "more than the 5th" into "from the 5th on" — the day picked shown,
 * and a different day asked.
 */
export function valuesForOp(op: FilterOp, values: FilterValue[]): FilterValue[] {
  if (op === 'isEmpty' || op === 'isNotEmpty') {
    return [];
  }
  // A radius and an anchor mean nothing to another operator and nothing else means anything to
  // these, so a spatial row starts again when its operator changes either way.
  if (op === 'within') {
    return values[0]?.type === 'number' ? values.slice(0, 2) : [];
  }
  if (op === 'inside') {
    return values[0]?.type === 'polygon' || values[0]?.type === 'object' ? values.slice(0, 1) : [];
  }
  if (op !== 'between' && op !== 'in' && (isSpatialAnchor(values[0]) || isSpatialAnchor(values[1]))) {
    return [];
  }
  const kept = op === 'between' ? values.slice(0, 2) : op === 'in' ? values : values.slice(0, 1);
  return kept.map((value, index) => rebound(op, value, op === 'between' && index === 1 ? 'high' : 'low'));
}

function rebound(op: FilterOp, value: FilterValue, end: 'low' | 'high'): FilterValue {
  if (value.type !== 'instant' || typeof value.value !== 'string') {
    return value;
  }
  const day = pickedDay(value.value);
  return day === null ? value : { type: 'instant', value: dayBound(op, day, end) };
}

/**
 * Where a refusal of an anchor lands: on every sent row that names something the server had to
 * look up — an object, an area, another saved filter. The server answers all of them with one
 * sentence that names no row, on purpose: saying which anchor was withheld would say that it
 * exists. With no such row the refusal was about the place nearest-first is measured from, and
 * stays with the filter as a whole.
 */
export function placeAnchorRefusal(
  problem: FilterProblem,
  root: FilterNode | null,
): { byPath: Map<string, FilterProblem[]>; unplaced: FilterProblem[] } {
  const byPath = new Map<string, FilterProblem[]>();
  const rows = conditionsWithPaths(root)
    .filter((c) => isComplete(c.condition) && namesAnchor(c.condition));
  for (const row of rows) {
    byPath.set(pathKey(row.path), [problem]);
  }
  return { byPath, unplaced: rows.length === 0 ? [problem] : [] };
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

/** One problem of a refused filter, as the server lists it beside its refusal. */
export interface FilterProblem {
  /** What is wrong, from a closed list; the editor words it from this and from nothing else. */
  code: string;
  /** A JSON Pointer into the document that was sent; empty for the document as a whole. */
  path: string;
  field?: string | null;
  op?: string | null;
  limit?: number | null;
}

/** The problems a refusal lists, read defensively: whatever is not a well-formed entry is skipped. */
export function problemsOf(problem: Readonly<Record<string, unknown>> | undefined): FilterProblem[] {
  const listed = problem?.problems;
  if (!Array.isArray(listed)) {
    return [];
  }
  return listed.filter(
    (p): p is FilterProblem =>
      typeof p === 'object' && p !== null
      && typeof (p as FilterProblem).code === 'string'
      && typeof (p as FilterProblem).path === 'string',
  );
}

/**
 * The scope entry and the node a problem's pointer names, in this module's path form — or null for
 * a pointer that names no node (the document, a whole scope entry).
 *
 * A group's child is `/of/<index>` and a negation's single child is `/of` with no index, which is
 * index 0 here.
 */
export function nodeOfPointer(pointer: string): { scope: number; path: NodePath } | null {
  const tokens = pointer.split('/').slice(1);
  if (tokens[0] !== 'scope' || !/^\d+$/.test(tokens[1] ?? '') || tokens[2] !== 'where') {
    return null;
  }
  const path: number[] = [];
  for (let i = 3; i < tokens.length; i++) {
    if (tokens[i] !== 'of') {
      return null;
    }
    const next = tokens[i + 1];
    if (next !== undefined && /^\d+$/.test(next)) {
      path.push(Number(next));
      i++;
    } else {
      path.push(0);
    }
  }
  return { scope: Number(tokens[1]), path };
}

/**
 * Where a node of the pruned tree is in the draft it was pruned from, or null when the path leads
 * nowhere in it. Each group's kept children are its children that survive pruning, in order.
 */
export function draftPathOf(root: FilterNode, prunedPath: NodePath): NodePath | null {
  let node = root;
  const out: number[] = [];
  for (const index of prunedPath) {
    if (node.node === 'condition') {
      return null;
    }
    if (node.node === 'not') {
      if (index !== 0) {
        return null;
      }
      out.push(0);
      node = node.of;
      continue;
    }
    const kept = node.of.flatMap((child, i) => (prune(child) === null ? [] : [i]));
    const at = kept[index];
    if (at === undefined) {
      return null;
    }
    out.push(at);
    node = node.of[at];
  }
  return out;
}

/**
 * Where a node of what the editor sent is in the draft on screen.
 *
 * What is sent is not the draft: half-written rows are pruned out, a draft that holds one node is
 * sent as that node rather than a group of one (`unwrapped`), and a page's fixed condition is put
 * first in an all-of beside the edited ones (`fixed`). Each of those shifts a node's position, so a
 * path from the server is walked back through them in the opposite order. The fixed condition
 * itself is on no row, and answers null.
 */
export function draftPathOfSent(
  root: FilterNode,
  sentPath: NodePath,
  options: { unwrapped: boolean; fixed: boolean },
): NodePath | null {
  const pruned = prune(root);
  if (pruned === null) {
    return null;
  }
  const where = options.unwrapped && pruned.node === 'allOf' && pruned.of.length === 1 ? pruned.of[0] : pruned;
  let path: NodePath = sentPath;
  if (options.fixed) {
    const [head, ...rest] = sentPath;
    if (head === undefined || head === 0) {
      return null;
    }
    if (where.node === 'allOf') {
      path = [head - 1, ...rest];
    } else if (head === 1) {
      path = rest;
    } else {
      return null;
    }
  }
  if (where !== pruned) {
    path = [0, ...path];
  }
  return draftPathOf(root, path);
}

/**
 * Where on the screen each problem of a refusal belongs: on the row of the condition it points at,
 * or, for one about no row — a limit on the whole filter, another world, the page's own fixed
 * condition — with the filter as a whole.
 */
export function placeProblems(
  problems: readonly FilterProblem[],
  scope: number,
  root: FilterNode,
  locate: (sentPath: NodePath) => NodePath | null,
): { byPath: Map<string, FilterProblem[]>; unplaced: FilterProblem[] } {
  const byPath = new Map<string, FilterProblem[]>();
  const unplaced: FilterProblem[] = [];
  for (const problem of problems) {
    const at = nodeOfPointer(problem.path);
    const draft = at !== null && at.scope === scope ? locate(at.path) : null;
    if (draft === null || nodeAt(root, draft)?.node !== 'condition') {
      unplaced.push(problem);
      continue;
    }
    const key = pathKey(draft);
    byPath.set(key, [...(byPath.get(key) ?? []), problem]);
  }
  return { byPath, unplaced };
}
