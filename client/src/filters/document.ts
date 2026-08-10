// SPDX-License-Identifier: AGPL-3.0-or-later
import {
  CURRENT_FILTER_VERSION,
  type FilterDocument,
  type FilterNode,
  type FilterOp,
  type FilterValue,
  type SortKey,
  type WorldScope,
} from './types.ts';

/**
 * Reading and rewriting a filter, without a control anywhere near it.
 *
 * Every function returns a new document. A control that edited in place would work perfectly until
 * two of them were open on the same filter — one in the panel, one in a dialog — and then the
 * second would be showing changes nobody made in it. Copying is cheap at this size and removes the
 * whole class of question.
 */

/** The empty filter: about nothing, so it asks nothing. */
export function emptyDocument(worlds: string[] = []): FilterDocument {
  return {
    version: CURRENT_FILTER_VERSION,
    scope: worlds.map((world) => ({ world, where: null })),
    sort: 'updated',
    descending: true,
  };
}

/** Whether this filter narrows anything, as opposed to naming worlds and asking them for everything. */
export function isEmpty(document: FilterDocument): boolean {
  return document.scope.length === 0 || document.scope.every((s) => s.where === null);
}

/**
 * The worlds a filter is about, replaced wholesale.
 *
 * Conditions already written for a world that survives are kept, which is what makes the world
 * buttons usable: turning documents on and off while narrowing caves must not throw the cave
 * conditions away, because somebody who lost them once will not trust the buttons again.
 */
export function withWorlds(document: FilterDocument, worlds: string[]): FilterDocument {
  const existing = new Map(document.scope.map((s) => [s.world, s]));
  return {
    ...document,
    scope: worlds.map<WorldScope>((world) => existing.get(world) ?? { world, where: null }),
  };
}

export function withSort(document: FilterDocument, sort: SortKey, descending: boolean): FilterDocument {
  return { ...document, sort, descending };
}

/** One world's conditions, replaced. */
export function withWhere(
  document: FilterDocument,
  world: string,
  where: FilterNode | null,
): FilterDocument {
  return {
    ...document,
    scope: document.scope.map((s) => (s.world === world ? { ...s, where } : s)),
  };
}

export function whereOf(document: FilterDocument, world: string): FilterNode | null {
  return document.scope.find((s) => s.world === world)?.where ?? null;
}

/**
 * The condition groups of a world, as the editor shows them: an OR of ANDs.
 *
 * Any tree the server accepts can be read, but only this shape is offered for editing. Arbitrary
 * nesting is expressible and almost nobody can read it back — a person who wrote one three months
 * ago cannot say what it does, which makes a saved filter something to be re-derived rather than
 * trusted. Two levels covers what people actually ask for, and reads aloud.
 */
export function groupsOf(where: FilterNode | null): FilterNode[][] {
  if (where === null) {
    return [];
  }

  if (where.node === 'anyOf') {
    return where.of.map(conjunctsOf);
  }

  return [conjunctsOf(where)];
}

function conjunctsOf(node: FilterNode): FilterNode[] {
  return node.node === 'allOf' ? [...node.of] : [node];
}

/**
 * Groups back into a tree, normalised so that one shape means one thing.
 *
 * A single group is not wrapped in an OR and a single condition is not wrapped in an AND, so two
 * editors that produced the same question produce the same document — which is what lets a saved
 * filter be compared to the one on screen without a tree walk that knows about wrappers.
 */
export function fromGroups(groups: FilterNode[][]): FilterNode | null {
  const filled = groups.map((g) => g.filter(Boolean)).filter((g) => g.length > 0);
  if (filled.length === 0) {
    return null;
  }

  const conjunctions = filled.map((g) => (g.length === 1 ? g[0] : { node: 'allOf' as const, of: g }));
  return conjunctions.length === 1 ? conjunctions[0] : { node: 'anyOf', of: conjunctions };
}

export function condition(field: string, op: FilterOp, values: FilterValue[] = []): FilterNode {
  return { node: 'condition', field, op, values };
}

/** How many conditions a filter holds, across every world. */
export function conditionCount(document: FilterDocument): number {
  return document.scope.reduce((total, s) => total + countNodes(s.where, true), 0);
}

/** How many nodes a filter holds, which is what the server's limit is about. */
export function nodeCount(document: FilterDocument): number {
  return document.scope.reduce((total, s) => total + countNodes(s.where, false), 0);
}

function countNodes(node: FilterNode | null, conditionsOnly: boolean): number {
  if (node === null) {
    return 0;
  }

  if (node.node === 'condition') {
    return 1;
  }

  const children = node.node === 'not' ? [node.of] : node.of;
  const own = conditionsOnly ? 0 : 1;
  return own + children.reduce((total, child) => total + countNodes(child, conditionsOnly), 0);
}

/** The deepest nesting in a filter, counted the way the server counts it. */
export function depth(document: FilterDocument): number {
  return document.scope.reduce((deepest, s) => Math.max(deepest, depthOf(s.where)), 0);
}

function depthOf(node: FilterNode | null): number {
  if (node === null) {
    return 0;
  }

  if (node.node === 'condition') {
    return 1;
  }

  const children = node.node === 'not' ? [node.of] : node.of;
  return 1 + children.reduce((deepest, child) => Math.max(deepest, depthOf(child)), 0);
}
