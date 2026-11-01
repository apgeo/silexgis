// SPDX-License-Identifier: AGPL-3.0-or-later
import { noFilters } from './filters.ts';
import type { GraphFilters } from './types.ts';

/**
 * The filter choice outlives a page reload: someone who always hides tags should not have to hide
 * them again for every item. It is kept with the person's account preferences, so it follows the
 * account to another browser and is not inherited by the next member who signs in on this one.
 * This file holds only the stored shape and its conversions; the preference store keeps the value.
 */
export interface GraphFilterPref {
  categories: string[];
  relations: string[];
  types: string[];
}

/** Nothing hidden; one shared object so "unchanged" compares by identity. */
export const NO_GRAPH_FILTER_PREF: GraphFilterPref = { categories: [], relations: [], types: [] };

const MAX_ENTRIES = 200;
const MAX_LENGTH = 120;

const strings = (value: unknown): string[] =>
  Array.isArray(value)
    ? value.filter((v): v is string => typeof v === 'string' && v.length <= MAX_LENGTH).slice(0, MAX_ENTRIES)
    : [];

/** Reads whatever was stored, keeping only what is a short string; anything else means "nothing hidden". */
export function sanitizeGraphFilterPref(value: unknown): GraphFilterPref {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) {
    return NO_GRAPH_FILTER_PREF;
  }
  const o = value as Record<string, unknown>;
  return { categories: strings(o.categories), relations: strings(o.relations), types: strings(o.types) };
}

export function filtersToPref(f: GraphFilters): GraphFilterPref {
  return {
    categories: [...f.disabledCategories].slice(0, MAX_ENTRIES),
    relations: [...f.disabledRelations].slice(0, MAX_ENTRIES),
    types: [...f.disabledTypes].slice(0, MAX_ENTRIES),
  };
}

export function prefToFilters(pref: GraphFilterPref): GraphFilters {
  if (pref === NO_GRAPH_FILTER_PREF) {
    return noFilters();
  }
  return {
    disabledCategories: new Set(pref.categories),
    disabledRelations: new Set(pref.relations),
    disabledTypes: new Set(pref.types),
  };
}

export function samePref(a: GraphFilterPref, b: GraphFilterPref): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}
