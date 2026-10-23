// SPDX-License-Identifier: AGPL-3.0-or-later
import type { SavedFilter } from '../api/hooks/savedFilters.ts';
import { filterLayerPalette, type FilterLayerColor } from './markerPalette.ts';
import { isSavedFilterId } from './mapFilters.ts';

/**
 * The layers a person has added to the map from their saved filters: each one draws the entrances
 * and surface features its filter keeps, in a colour and a symbol of its own, over (or under) the
 * map's main layers.
 *
 * A layer names its filter by id and never copies the filter out, so it follows the filter's
 * author when they refine it, and a filter the viewer can no longer read is refused by the server
 * rather than run from a stale copy. Nothing else about the layer is a statement about the data:
 * colour, symbol and whether it is switched on are how this person likes to look at it.
 *
 * This file is the list and its rules, free of any drawing library, so a saved view can be read
 * and written without OpenLayers; the layers themselves are made in `filterLayers.ts`.
 */
export interface FilterLayerSpec {
  savedFilterId: string;
  color: FilterLayerColor;
  symbol: FilterLayerSymbol;
  visible: boolean;
}

export const FILTER_LAYER_SYMBOLS = ['circle', 'square', 'triangle', 'diamond'] as const;
export type FilterLayerSymbol = (typeof FILTER_LAYER_SYMBOLS)[number];

export const FILTER_LAYER_COLORS = Object.keys(filterLayerPalette) as FilterLayerColor[];

/**
 * How many filter layers one map holds at once. Twice the colours: past six a colour comes round
 * again, always with a different symbol, so no two layers on screen share both — every layer stays
 * one marker of its own (`sameStyleTaken`). Each layer asks the server twice on every pan, which is
 * what keeps the list short.
 */
export const MAX_FILTER_LAYERS = 12;

/** Whether another layer than `savedFilterId` already draws in this colour and this symbol. */
export function sameStyleTaken(
  list: readonly FilterLayerSpec[],
  color: FilterLayerColor,
  symbol: FilterLayerSymbol,
  savedFilterId?: string,
): boolean {
  return list.some(
    (layer) =>
      layer.color === color
      && layer.symbol === symbol
      && (savedFilterId === undefined || !sameId(layer.savedFilterId, savedFilterId)),
  );
}

/**
 * The style a new layer takes: while a colour is unused, the first unused colour with the symbols
 * in turn; once every colour is in use, the least-used colour with the first symbol no layer of
 * that colour has.
 */
function freeStyle(list: readonly FilterLayerSpec[]): { color: FilterLayerColor; symbol: FilterLayerSymbol } {
  const used = new Set(list.map((layer) => layer.color));
  const unused = FILTER_LAYER_COLORS.find((candidate) => !used.has(candidate));
  if (unused) {
    const turn = FILTER_LAYER_SYMBOLS[list.length % FILTER_LAYER_SYMBOLS.length];
    return { color: unused, symbol: turn };
  }
  const byUse = [...FILTER_LAYER_COLORS].sort(
    (a, b) =>
      list.filter((layer) => layer.color === a).length - list.filter((layer) => layer.color === b).length
      || FILTER_LAYER_COLORS.indexOf(a) - FILTER_LAYER_COLORS.indexOf(b),
  );
  for (const color of byUse) {
    const symbol = FILTER_LAYER_SYMBOLS.find((candidate) => !sameStyleTaken(list, color, candidate));
    if (symbol) {
      return { color, symbol };
    }
  }
  // Unreachable while the cap is below colours × symbols; kept so the type needs no assertion.
  return { color: FILTER_LAYER_COLORS[0], symbol: FILTER_LAYER_SYMBOLS[0] };
}

/** Every filter layer's OpenLayers id starts with this; the rest is the saved filter's id. */
export const FILTER_LAYER_PREFIX = 'filter:';

export function filterLayerId(savedFilterId: string): string {
  return `${FILTER_LAYER_PREFIX}${savedFilterId}`;
}

/** The saved filter a layer id names, or null when the id is not a filter layer's. */
export function filterLayerSavedIdOf(layerId: string | undefined | null): string | null {
  if (!layerId?.startsWith(FILTER_LAYER_PREFIX)) {
    return null;
  }
  const id = layerId.slice(FILTER_LAYER_PREFIX.length);
  return isSavedFilterId(id) ? id : null;
}

/**
 * The list with a layer for this saved filter appended: the first colour no layer is using, and
 * the symbols taken in turn. Unchanged when the filter already has a layer, when the list is full,
 * or when the id cannot name a saved filter at all.
 */
export function addFilterLayer(list: readonly FilterLayerSpec[], savedFilterId: string): FilterLayerSpec[] {
  if (
    !isSavedFilterId(savedFilterId)
    || list.length >= MAX_FILTER_LAYERS
    || list.some((layer) => sameId(layer.savedFilterId, savedFilterId))
  ) {
    return [...list];
  }
  return [...list, { savedFilterId, ...freeStyle(list), visible: true }];
}

export function removeFilterLayer(list: readonly FilterLayerSpec[], savedFilterId: string): FilterLayerSpec[] {
  return list.filter((layer) => !sameId(layer.savedFilterId, savedFilterId));
}

export function updateFilterLayer(
  list: readonly FilterLayerSpec[],
  savedFilterId: string,
  patch: Partial<Pick<FilterLayerSpec, 'color' | 'symbol' | 'visible'>>,
): FilterLayerSpec[] {
  return list.map((layer) => (sameId(layer.savedFilterId, savedFilterId) ? { ...layer, ...patch } : layer));
}

/** The colour after this one, wrapping round — what the row's colour button steps to. */
export function nextFilterLayerColor(color: FilterLayerColor): FilterLayerColor {
  const index = FILTER_LAYER_COLORS.indexOf(color);
  return FILTER_LAYER_COLORS[(index + 1) % FILTER_LAYER_COLORS.length];
}

/** The symbol after this one, wrapping round — what the row's symbol button steps to. */
export function nextFilterLayerSymbol(symbol: FilterLayerSymbol): FilterLayerSymbol {
  const index = FILTER_LAYER_SYMBOLS.indexOf(symbol);
  return FILTER_LAYER_SYMBOLS[(index + 1) % FILTER_LAYER_SYMBOLS.length];
}

/**
 * The colour a layer's colour button steps to: the next one round that no other layer draws with
 * this layer's symbol. The layer's own colour when every other is taken with it.
 */
export function nextFreeFilterLayerColor(list: readonly FilterLayerSpec[], spec: FilterLayerSpec): FilterLayerColor {
  let color = spec.color;
  for (let step = 1; step < FILTER_LAYER_COLORS.length; step++) {
    color = nextFilterLayerColor(color);
    if (!sameStyleTaken(list, color, spec.symbol, spec.savedFilterId)) {
      return color;
    }
  }
  return spec.color;
}

/** The same for the symbol button, keeping the layer's colour. */
export function nextFreeFilterLayerSymbol(list: readonly FilterLayerSpec[], spec: FilterLayerSpec): FilterLayerSymbol {
  let symbol = spec.symbol;
  for (let step = 1; step < FILTER_LAYER_SYMBOLS.length; step++) {
    symbol = nextFilterLayerSymbol(symbol);
    if (!sameStyleTaken(list, spec.color, symbol, spec.savedFilterId)) {
      return symbol;
    }
  }
  return spec.symbol;
}

/**
 * The filter layers a stored view names, checked entry by entry rather than trusted: the server
 * stores a view's document without looking inside it. An entry that cannot name a saved filter is
 * dropped; a colour or symbol this version does not know falls back to one it does rather than
 * losing the layer; duplicates and anything past the cap are dropped, first one wins.
 */
export function readFilterLayers(value: unknown): FilterLayerSpec[] {
  if (!Array.isArray(value)) {
    return [];
  }
  const result: FilterLayerSpec[] = [];
  for (const entry of value as unknown[]) {
    if (result.length >= MAX_FILTER_LAYERS) {
      break;
    }
    const candidate = entry as Partial<Record<keyof FilterLayerSpec, unknown>> | null;
    const id = candidate?.savedFilterId;
    if (!isSavedFilterId(id) || result.some((layer) => sameId(layer.savedFilterId, id))) {
      continue;
    }
    const known =
      FILTER_LAYER_COLORS.includes(candidate!.color as FilterLayerColor)
      && FILTER_LAYER_SYMBOLS.includes(candidate!.symbol as FilterLayerSymbol);
    // A style this version does not know, or one an earlier entry already draws with, is repaired
    // to a free one, so two layers are never one marker.
    const style =
      known && !sameStyleTaken(result, candidate!.color as FilterLayerColor, candidate!.symbol as FilterLayerSymbol)
        ? { color: candidate!.color as FilterLayerColor, symbol: candidate!.symbol as FilterLayerSymbol }
        : freeStyle(result);
    result.push({ savedFilterId: id, ...style, visible: candidate!.visible !== false });
  }
  return result;
}

/**
 * What a filter layer's row says about its filter, decided from the saved-filter list this person
 * reads and from what the server answered the layer's own requests.
 *
 * `unreadable` covers every way the filter cannot be run for this person: gone from their list
 * (deleted, or no longer shared with them — the two are deliberately not told apart), held but
 * unreadable to them, or refused by the map routes. `loading` is only the moment before the list
 * has arrived, and draws as an ordinary layer: the server, not the list, is what decides.
 */
export type FilterLayerStatus = 'ready' | 'unreadable' | 'loading';

export function filterLayerStatus(
  spec: FilterLayerSpec,
  savedFilters: readonly SavedFilter[] | undefined,
  refused: ReadonlySet<string>,
): FilterLayerStatus {
  if (refused.has(spec.savedFilterId.toLowerCase())) {
    return 'unreadable';
  }
  if (savedFilters === undefined) {
    return 'loading';
  }
  const filter = savedFilters.find((candidate) => sameId(candidate.id, spec.savedFilterId));
  if (!filter || filter.state === 'unreadable' || filter.document === null) {
    return 'unreadable';
  }
  return 'ready';
}

/** The saved filters a new layer can be made from: readable ones that have no layer yet. */
export function filterLayerCandidates(
  savedFilters: readonly SavedFilter[] | undefined,
  list: readonly FilterLayerSpec[],
): SavedFilter[] {
  return (savedFilters ?? []).filter(
    (filter) =>
      filter.state !== 'unreadable'
      && filter.document !== null
      && !list.some((layer) => sameId(layer.savedFilterId, filter.id)),
  );
}

function sameId(a: string, b: string): boolean {
  return a.toLowerCase() === b.toLowerCase();
}
