// SPDX-License-Identifier: AGPL-3.0-or-later
import { decodeFilter, encodeFilter, FILTER_PARAM } from './url.ts';
import { emptyDocument, whereOf, withWhere } from './document.ts';
import type { FilterDocument, FilterNode } from './types.ts';

/**
 * The conditions of a list that has no controls of its own beyond the search box — the geodata
 * files and the georeferenced maps — as one document in the screen's address and back.
 *
 * Every condition is "extra" in the sense the gallery uses: there is no control to read it into, so
 * what the editor wrote is exactly what the list is asked with. The same two address members the
 * gallery and the map use carry it (the document, and the saved filter it came from), so a link, the
 * back button and an applied saved filter all arrive at the same state by the same road. A screen
 * that shows several lists in tabs keeps only the active tab's members in its address; a document
 * about another world is read as "nothing usable here", never as this list's conditions.
 */

/** The address member naming the saved filter the conditions came from. */
export const WORLD_SAVED_PARAM = 'saved';

/** A list's filter as its address holds it. */
export interface WorldFilter {
  /** The world's conditions, or null when there are none. */
  where: FilterNode | null;
  /** A filter was in the address and could not be read; the list opens without it and says so. */
  unreadableLink: boolean;
  /** The saved filter the conditions came from, if they came from one. */
  savedFilterId?: string;
}

/** The document a list's conditions compose, or null when they narrow nothing. */
export function worldDocument(world: string, where: FilterNode | null): FilterDocument | null {
  return where === null ? null : withWhere(emptyDocument([world]), world, where);
}

/** The conditions as the condition editor opens them; empty when there are none. */
export function worldEditorDocument(world: string, where: FilterNode | null): FilterDocument {
  return withWhere(emptyDocument([world]), world, where);
}

/**
 * What a document asks of one world, or undefined when it is not about that world at all — a filter
 * about caves is not taken as one about geodata files, and the server refuses the substitution too.
 */
export function readWorldDocument(document: FilterDocument, world: string): FilterNode | null | undefined {
  return document.scope.some((s) => s.world === world) ? whereOf(document, world) : undefined;
}

/** The list's filter, read from its address. */
export function readWorldFilter(params: URLSearchParams, world: string): WorldFilter {
  const token = params.get(FILTER_PARAM);
  const document = decodeFilter(token);
  const where = document === null ? undefined : readWorldDocument(document, world);
  return {
    where: where ?? null,
    unreadableLink: token !== null && token !== '' && where === undefined,
    savedFilterId: params.get(WORLD_SAVED_PARAM) ?? undefined,
  };
}

/**
 * The address with the list's filter replaced and every other member (the tab) kept as it was. A
 * filter that narrows nothing leaves no token behind.
 */
export function writeWorldFilter(
  params: URLSearchParams,
  world: string,
  filter: { where: FilterNode | null; savedFilterId?: string },
): URLSearchParams {
  const next = new URLSearchParams(params);
  const document = worldDocument(world, filter.where);
  if (document === null) {
    next.delete(FILTER_PARAM);
  } else {
    next.set(FILTER_PARAM, encodeFilter(document));
  }
  if (filter.savedFilterId) {
    next.set(WORLD_SAVED_PARAM, filter.savedFilterId);
  } else {
    next.delete(WORLD_SAVED_PARAM);
  }
  return next;
}

/** The token a list's request carries for these conditions, or undefined when there are none. */
export function worldToken(world: string, where: FilterNode | null): string | undefined {
  const document = worldDocument(world, where);
  return document === null ? undefined : encodeFilter(document);
}

/** Whether two sets of conditions are the same question, written the same way. */
export function sameWorldQuestion(a: FilterNode | null, b: FilterNode | null): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}
