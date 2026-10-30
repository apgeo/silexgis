// SPDX-License-Identifier: AGPL-3.0-or-later
import type { MapFilterParams } from '../api/hooks/map.ts';
import { emptyDocument, whereOf, withWhere } from '../filters/document.ts';
import { decodeFilter, encodeFilter, FILTER_PARAM } from '../filters/url.ts';
import type { FilterDocument, FilterNode } from '../filters/types.ts';

/**
 * The filter the map's own layers draw through — the entrance layer, the surface features, the
 * survey lines, the 3D scene's copies of those and its walls, and a clicked cluster's member list.
 *
 * It is one filter in the language every other surface speaks: either a saved filter, named by its
 * id, or a document. A saved filter is sent by id rather than copied out, so the map follows the
 * filter when its author refines it, and a filter that has since become unreadable for this person
 * is refused by the server instead of being run from a stale copy.
 *
 * Loaders read the current value on every fetch, so changing it needs only a reload of the
 * affected layers. It lives at module level rather than in React state because the loaders are
 * OpenLayers callbacks, not components.
 */
export type MapFilter =
  | { kind: 'saved'; savedFilterId: string }
  | { kind: 'document'; document: FilterDocument };

/** The world the map's layers belong to. */
export const MAP_FILTER_WORLD = 'feature';

/** The address parameter naming a saved filter; the document itself travels as `filter`. */
export const MAP_SAVED_FILTER_PARAM = 'saved';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Whether a value can name a saved filter at all; anything else is read as an unreadable link. */
export function isSavedFilterId(value: unknown): value is string {
  return typeof value === 'string' && GUID.test(value);
}

let current: MapFilter | null = null;
const refusalListeners = new Set<(code: string) => void>();
const changeListeners = new Set<() => void>();

export function getMapFilter(): MapFilter | null {
  return current;
}

export function setMapFilter(filter: MapFilter | null): void {
  const changed = !sameMapFilter(filter, current);
  current = filter;
  if (changed) {
    for (const listener of changeListeners) {
      listener();
    }
  }
}

/**
 * For loaders that are not reloaded by name when the filter changes — the survey lines on the flat
 * map and the walls in the 3D scene — so they follow it too.
 */
export function onMapFilterChange(listener: () => void): () => void {
  changeListeners.add(listener);
  return () => {
    changeListeners.delete(listener);
  };
}

/** The query parameters a map route takes for this filter: one of the two, never both. */
export function mapFilterParams(filter: MapFilter | null = current): MapFilterParams {
  if (filter === null) {
    return {};
  }
  return filter.kind === 'saved' ? { savedFilterId: filter.savedFilterId } : { filter: encodeFilter(filter.document) };
}

/**
 * Whether two filters are the same statement. Compared by what is sent, which is also what the
 * address holds, so a filter read back out of the address equals the one written into it.
 */
export function sameMapFilter(a: MapFilter | null, b: MapFilter | null): boolean {
  const left = mapFilterParams(a);
  const right = mapFilterParams(b);
  return left.filter === right.filter && left.savedFilterId === right.savedFilterId;
}

function tagEquals(slug: string): FilterNode {
  return { node: 'condition', field: 'tag', op: 'equals', values: [{ type: 'id', value: slug }] };
}

/**
 * The layer panel's tag chooser, as the document it stands for.
 *
 * Deliberately says only "tagged", not "caves" or "entrances": the map keeps an entrance that
 * matches and every entrance of a cave that matches, so one tag picks out both the entrances
 * tagged themselves and those of tagged caves.
 */
export function tagChipFilter(slug: string | null | undefined): MapFilter | null {
  const tag = slug?.trim();
  if (!tag) {
    return null;
  }
  return {
    kind: 'document',
    document: withWhere(emptyDocument([MAP_FILTER_WORLD]), MAP_FILTER_WORLD, tagEquals(tag)),
  };
}

/** The tag a filter consists of, when it is exactly what the tag chooser writes; null otherwise. */
export function tagOfMapFilter(filter: MapFilter | null): string | null {
  if (filter?.kind !== 'document' || filter.document.scope.length !== 1) {
    return null;
  }
  const where = whereOf(filter.document, MAP_FILTER_WORLD);
  if (
    where?.node === 'condition'
    && where.field === 'tag'
    && where.op === 'equals'
    && where.values.length === 1
    && where.values[0].type === 'id'
  ) {
    return where.values[0].value;
  }
  return null;
}

/**
 * The document the map's condition editor opens on: the map's one world and its conditions, and
 * nothing else of the document it was read from. Another world's conditions, a sort and an anchor
 * mean nothing to the map's layers, so they are not carried into a filter the map then sends.
 *
 * `saved` is the stored document of the saved filter the map draws through, when it does; the
 * editor starts from a copy of it, and applying turns the filter into a document of its own.
 */
export function mapEditorDocument(filter: MapFilter | null, saved?: FilterDocument | null): FilterDocument {
  const source = filter?.kind === 'document' ? filter.document : filter?.kind === 'saved' ? saved ?? null : null;
  return withWhere(emptyDocument([MAP_FILTER_WORLD]), MAP_FILTER_WORLD, source === null ? null : whereOf(source, MAP_FILTER_WORLD));
}

/** What the editor handed back, as the map's filter: null when it has no condition left. */
export function mapFilterOfEditor(edited: FilterDocument): MapFilter | null {
  const where = whereOf(edited, MAP_FILTER_WORLD);
  return where === null
    ? null
    : { kind: 'document', document: withWhere(emptyDocument([MAP_FILTER_WORLD]), MAP_FILTER_WORLD, where) };
}

/** The map's filter as its address holds it. */
export interface MapFilterAddress {
  filter: MapFilter | null;
  /** A filter was in the address and could not be read; the map opens unfiltered and says so. */
  unreadableLink: boolean;
}

/**
 * Reads the filter out of the map's address.
 *
 * A document wins over a saved filter's id when a link carries both: the document is what was on
 * the sender's screen. A filter that cannot be read opens the map with nothing filtered and a
 * notice — never an error screen, and never a guess at what the sender meant.
 */
export function readMapFilterAddress(params: URLSearchParams): MapFilterAddress {
  const token = params.get(FILTER_PARAM);
  const saved = params.get(MAP_SAVED_FILTER_PARAM);
  if (token) {
    const document = decodeFilter(token);
    return document === null
      ? { filter: null, unreadableLink: true }
      : { filter: { kind: 'document', document }, unreadableLink: false };
  }
  if (saved) {
    return isSavedFilterId(saved)
      ? { filter: { kind: 'saved', savedFilterId: saved }, unreadableLink: false }
      : { filter: null, unreadableLink: true };
  }
  return { filter: null, unreadableLink: false };
}

/** The same parameters with the map's filter written in (and the other form removed). */
export function writeMapFilterAddress(params: URLSearchParams, filter: MapFilter | null): URLSearchParams {
  const next = new URLSearchParams(params);
  const query = mapFilterParams(filter);
  if (query.filter === undefined) {
    next.delete(FILTER_PARAM);
  } else {
    next.set(FILTER_PARAM, query.filter);
  }
  if (query.savedFilterId === undefined) {
    next.delete(MAP_SAVED_FILTER_PARAM);
  } else {
    next.set(MAP_SAVED_FILTER_PARAM, query.savedFilterId);
  }
  return next;
}

/** The two address values the filter occupies, as one comparable string. */
export function mapFilterAddressKey(params: URLSearchParams): string {
  return `${params.get(FILTER_PARAM) ?? ''}|${params.get(MAP_SAVED_FILTER_PARAM) ?? ''}`;
}

/**
 * Whether an error is a map route refusing the filter it was sent. Matched by prefix because the
 * server's vocabulary of reasons grows; every one of them means "this filter was not run".
 */
export function isMapFilterRefusal(error: unknown): error is { code: string } {
  const code = (error as { code?: unknown } | null)?.code;
  return typeof code === 'string' && (code.startsWith('filter.') || code.startsWith('saved_filter.'));
}

export function onMapFilterRefusal(listener: (code: string) => void): () => void {
  refusalListeners.add(listener);
  return () => {
    refusalListeners.delete(listener);
  };
}

/**
 * For loaders: when the error was the filter's refusal, empties the layer and tells whoever
 * listens. Keeping what was drawn before would show the unfiltered layer under a filter's name;
 * any other error keeps it, because a dropped request is usually a network hiccup.
 */
export function handleMapFilterError(error: unknown, clear: () => void): void {
  if (isMapFilterRefusal(error)) {
    clear();
    for (const listener of refusalListeners) {
      listener(error.code);
    }
  }
}
