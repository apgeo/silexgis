// SPDX-License-Identifier: AGPL-3.0-or-later
import { DEFAULT_COORDINATE_FORMAT, isCoordinateFormat } from '../geo/coordinateFormat.ts';
import { DEFAULT_APPEARANCE, DEFAULT_DOCKS, type AccountPrefsState } from './uiPrefsStore.ts';

/**
 * The preferences that belong to the ACCOUNT rather than to the browser: they are saved in the
 * account's stored preferences, follow a member to another browser, and are not inherited by the
 * next member who signs in on this one.
 *
 * Everything else the preference store holds stays per browser — a centreline budget or a mesh
 * limit is a fact about the machine, not about the person.
 */
export const ACCOUNT_PREF_KEYS = [
  'appearance',
  'panels',
  'layouts',
  'selectors',
  'docks',
  'pinnedTypeIds',
  'coordinateFormat',
  'overviewMap',
] as const;

export type AccountPrefKey = (typeof ACCOUNT_PREF_KEYS)[number];

export type AccountPrefs = Partial<Record<AccountPrefKey, unknown>>;

/** What each account preference is when nobody has chosen anything. */
export const ACCOUNT_PREF_DEFAULTS: Record<AccountPrefKey, unknown> = {
  appearance: DEFAULT_APPEARANCE,
  panels: {},
  layouts: [],
  selectors: {},
  docks: DEFAULT_DOCKS,
  pinnedTypeIds: [],
  coordinateFormat: DEFAULT_COORDINATE_FORMAT,
  overviewMap: false,
};

/** The server's limit on the stored document, in characters of its JSON. */
export const PREFERENCES_MAX_LENGTH = 8000;

function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/**
 * A value with its empty and absent parts removed — `undefined` members, empty objects — so that
 * "nothing chosen" has one spelling and compares equal to the default.
 */
function compact(value: unknown): unknown {
  if (Array.isArray(value)) {
    return value.map(compact);
  }
  if (!isPlainObject(value)) {
    return value;
  }
  const result: Record<string, unknown> = {};
  for (const [key, member] of Object.entries(value)) {
    const compacted = compact(member);
    if (compacted === undefined || (isPlainObject(compacted) && Object.keys(compacted).length === 0)) {
      continue;
    }
    result[key] = compacted;
  }
  return result;
}

function sameValue(a: unknown, b: unknown): boolean {
  return JSON.stringify(compact(a)) === JSON.stringify(compact(b));
}

/**
 * The account preferences in a state that differ from the defaults, and only those — the stored
 * document is capped, and a default spelled out is a character spent on nothing.
 */
export function pickAccountPrefs(state: AccountPrefs): AccountPrefs {
  const picked: AccountPrefs = {};
  for (const key of ACCOUNT_PREF_KEYS) {
    const value = compact(state[key]);
    if (value !== undefined && !sameValue(value, ACCOUNT_PREF_DEFAULTS[key])) {
      picked[key] = value;
    }
  }
  return picked;
}

/**
 * Applies a JSON Merge Patch (RFC 7396) — the rule the server's merging write follows: a named
 * member replaces, an object merges member by member, a null removes, anything unnamed stays.
 */
export function applyMergePatch(target: unknown, patch: unknown): unknown {
  if (!isPlainObject(patch)) {
    return patch;
  }
  const result: Record<string, unknown> = isPlainObject(target) ? { ...target } : {};
  for (const [key, value] of Object.entries(patch)) {
    if (value === null) {
      delete result[key];
    } else {
      result[key] = applyMergePatch(result[key], value);
    }
  }
  return result;
}

/**
 * The merge patch that turns `from` into `to`: members `to` lacks become null, objects are
 * patched member by member, everything else is replaced. Undefined when there is nothing to do.
 */
function diffPatch(from: unknown, to: unknown): unknown {
  if (isPlainObject(from) && isPlainObject(to)) {
    const patch: Record<string, unknown> = {};
    for (const key of Object.keys(from)) {
      if (!(key in to)) {
        patch[key] = null;
      }
    }
    for (const [key, value] of Object.entries(to)) {
      const member = diffPatch(from[key], value);
      if (member !== undefined) {
        patch[key] = member;
      }
    }
    return Object.keys(patch).length > 0 ? patch : undefined;
  }
  return JSON.stringify(from) === JSON.stringify(to) ? undefined : to;
}

/**
 * The patch that brings the stored document's account preferences to what this browser holds:
 * a preference back at its default is removed (null), a changed one is patched, and every member
 * of the document this client does not own is left alone.
 */
export function accountPrefsPatch(stored: Record<string, unknown>, desired: AccountPrefs): Record<string, unknown> {
  const patch: Record<string, unknown> = {};
  for (const key of ACCOUNT_PREF_KEYS) {
    if (desired[key] === undefined) {
      if (key in stored) {
        patch[key] = null;
      }
      continue;
    }
    const member = diffPatch(stored[key], desired[key]);
    if (member !== undefined) {
      patch[key] = member;
    }
  }
  return patch;
}

/**
 * The document trimmed to the server's limit, or null when even that cannot be done.
 *
 * Named layouts are the one preference that grows without bound, so they are what gives way —
 * the oldest first, since a list is appended to — and they stay in this browser all the same.
 */
export function fitToBudget(
  document: Record<string, unknown>,
  maxLength: number = PREFERENCES_MAX_LENGTH,
): { document: Record<string, unknown>; leftOutLayouts: number } | null {
  let candidate = document;
  let leftOut = 0;
  while (JSON.stringify(candidate).length > maxLength) {
    const layouts = candidate.layouts;
    if (!Array.isArray(layouts) || layouts.length === 0) {
      return null;
    }
    const rest = layouts.slice(1);
    candidate = { ...candidate };
    if (rest.length > 0) {
      candidate.layouts = rest;
    } else {
      delete candidate.layouts;
    }
    leftOut++;
  }
  return { document: candidate, leftOutLayouts: leftOut };
}

/**
 * The stored document's account preferences, read for this browser: only the known keys, each
 * taken as stored. Absent keys are absent — the caller decides what an absent key means.
 */
export function readAccountPrefs(stored: unknown): AccountPrefs {
  if (!isPlainObject(stored)) {
    return {};
  }
  const read: AccountPrefs = {};
  for (const key of ACCOUNT_PREF_KEYS) {
    if (key in stored) {
      read[key] = stored[key];
    }
  }
  return read;
}

/**
 * The account preferences a stored document holds, as this client can use them: each key taken
 * only in a shape it can draw, anything else dropped. The document is written by whichever client
 * version the person last used, so a value of an older or newer shape must not reach the screen.
 */
export function sanitizeAccountPrefs(read: AccountPrefs): Partial<AccountPrefsState> {
  const out: Partial<AccountPrefsState> = {};
  const { appearance, panels, layouts, selectors, docks, pinnedTypeIds, coordinateFormat, overviewMap } = read;
  if (
    isPlainObject(appearance) &&
    ['system', 'light', 'dark'].includes(appearance.theme as string) &&
    ['comfortable', 'compact'].includes(appearance.density as string) &&
    typeof appearance.reduceMotion === 'boolean'
  ) {
    out.appearance = appearance as unknown as AccountPrefsState['appearance'];
  }
  if (isPlainObject(panels)) {
    out.panels = panels as AccountPrefsState['panels'];
  }
  if (Array.isArray(layouts) && layouts.every((l) => isPlainObject(l) && typeof l.id === 'string' && typeof l.name === 'string')) {
    out.layouts = layouts as AccountPrefsState['layouts'];
  }
  if (isPlainObject(selectors)) {
    out.selectors = selectors as AccountPrefsState['selectors'];
  }
  if (isPlainObject(docks)) {
    out.docks = { leftHidden: docks.leftHidden === true, rightHidden: docks.rightHidden === true };
  }
  if (Array.isArray(pinnedTypeIds) && pinnedTypeIds.every((id) => Number.isInteger(id))) {
    out.pinnedTypeIds = pinnedTypeIds as number[];
  }
  if (isCoordinateFormat(coordinateFormat)) {
    out.coordinateFormat = coordinateFormat;
  }
  if (typeof overviewMap === 'boolean') {
    out.overviewMap = overviewMap;
  }
  return out;
}

/**
 * What the browser takes from the account's stored document at sign-in.
 *
 * The document is the account's word, and a preference absent from it is one at its default —
 * the document never spells defaults out — so the default is what is taken. Two exceptions. A
 * change made here that had not been written yet (the page was reloaded within moments of it) is
 * newer than the document, so nothing is taken and the change is written instead. And the first
 * sign-in that claims a cache nobody had claimed: that cache is this person's history from before
 * preferences travelled, so what the account has not stored yet is kept, to be written up.
 */
export function prefsToApply(
  stored: unknown,
  claim: 'same' | 'claimed' | 'reset',
  unsavedHere = false,
): Partial<AccountPrefsState> {
  if (claim === 'same' && unsavedHere) {
    return {};
  }
  const fromAccount = sanitizeAccountPrefs(readAccountPrefs(stored));
  if (claim === 'claimed') {
    return fromAccount;
  }
  return { ...(ACCOUNT_PREF_DEFAULTS as unknown as AccountPrefsState), ...fromAccount };
}
