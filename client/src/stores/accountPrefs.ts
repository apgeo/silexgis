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
 * The document trimmed to the server's limit, or null when even that cannot be done.
 *
 * Named layouts are the one preference that grows without bound, so they are what gives way —
 * the oldest first, since a list is appended to. The ones left out stay in this browser: it does
 * not take the account's word for a layout the account never had.
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
 * The account preferences a stored document holds, in the form this browser compares them in:
 * only what this client can draw, and only what differs from the defaults. A value of a shape this
 * client cannot read counts as not there — and so is never written back over by it unless the
 * person changes that preference here.
 */
export function accountPrefsIn(document: unknown): AccountPrefs {
  return pickAccountPrefs(sanitizeAccountPrefs(readAccountPrefs(document)) as AccountPrefs);
}

interface Layout {
  id: string;
}

function layoutsOf(prefs: AccountPrefs): Layout[] {
  return Array.isArray(prefs.layouts) ? (prefs.layouts as Layout[]) : [];
}

/**
 * Three sets of named layouts brought to one, layout by layout: what this browser last knew the
 * account to hold (`base`), what it holds now (`local`) and what the account holds now.
 *
 * A layout added, changed or removed here since `base` is kept as it is here; every other layout
 * is taken as the account has it — so a layout saved in another browser arrives, one removed there
 * goes, and neither undoes anything done here. The account's order is kept, with what was added
 * here after it.
 */
export function mergeLayouts<T extends Layout>(base: readonly T[], local: readonly T[], account: readonly T[]): T[] {
  const baseById = new Map(base.map((layout) => [layout.id, layout]));
  const localById = new Map(local.map((layout) => [layout.id, layout]));
  const accountIds = new Set(account.map((layout) => layout.id));
  const merged: T[] = [];
  for (const theirs of account) {
    const before = baseById.get(theirs.id);
    const here = localById.get(theirs.id);
    if (!here) {
      // Removed here since it was last seen, or never seen here: gone, or taken, respectively.
      if (!before) {
        merged.push(theirs);
      }
      continue;
    }
    merged.push(before && sameValue(before, here) ? theirs : here);
  }
  for (const here of local) {
    if (accountIds.has(here.id)) {
      continue;
    }
    const before = baseById.get(here.id);
    // Unchanged here and no longer in the account: removed elsewhere. Anything else — added here,
    // or changed here while removed elsewhere — stays.
    if (!before || !sameValue(before, here)) {
      merged.push(here);
    }
  }
  return merged;
}

function layoutsChanged(base: AccountPrefs, local: AccountPrefs): boolean {
  const before = layoutsOf(base);
  const now = layoutsOf(local);
  if (before.length !== now.length) {
    return true;
  }
  const beforeById = new Map(before.map((layout) => [layout.id, layout]));
  return now.some((layout) => {
    const previous = beforeById.get(layout.id);
    return !previous || !sameValue(previous, layout);
  });
}

/** Whether anything this browser holds for the account was changed here since `base`. */
export function changedSince(base: AccountPrefs, local: AccountPrefs): boolean {
  return ACCOUNT_PREF_KEYS.some((key) =>
    key === 'layouts' ? layoutsChanged(base, local) : !sameValue(base[key], local[key]),
  );
}

/**
 * Three sets of account preferences brought to what this browser should hold: what it last knew
 * the account to hold (`base`), what it holds now (`local`) and what the account holds now. A
 * preference changed here since `base` stays as it is here — it is newer than the account and is
 * still to be written; any other preference is the account's. Layouts are taken layout by layout.
 */
export function mergeAccountPrefs(base: AccountPrefs, local: AccountPrefs, account: AccountPrefs): AccountPrefs {
  const merged: AccountPrefs = {};
  for (const key of ACCOUNT_PREF_KEYS) {
    if (key === 'layouts') {
      const layouts = mergeLayouts(layoutsOf(base), layoutsOf(local), layoutsOf(account));
      if (layouts.length > 0) {
        merged.layouts = layouts;
      }
      continue;
    }
    const value = sameValue(base[key], local[key]) ? account[key] : local[key];
    if (value !== undefined) {
      merged[key] = value;
    }
  }
  return merged;
}

/**
 * The merge patch that carries what was changed HERE — between `base`, what this browser last knew
 * the account to hold, and `local`, what it holds now — into the stored document, and nothing else.
 *
 * Built from this browser's own changes rather than from the difference between the stored
 * document and this browser's whole state: another browser's change, read here before it was
 * made, is not this browser's to send back. An object preference is patched member by member; the
 * named layouts, a list, are merged layout by layout into the list the account holds now and sent
 * whole. A preference back at its default is removed.
 */
export function accountPrefsPatchSince(
  stored: Record<string, unknown>,
  base: AccountPrefs,
  local: AccountPrefs,
): Record<string, unknown> {
  const patch: Record<string, unknown> = {};
  for (const key of ACCOUNT_PREF_KEYS) {
    if (key === 'layouts') {
      if (layoutsChanged(base, local)) {
        const layouts = mergeLayouts(layoutsOf(base), layoutsOf(local), layoutsOf(accountPrefsIn(stored)));
        if (layouts.length > 0) {
          patch.layouts = layouts;
        } else if ('layouts' in stored) {
          patch.layouts = null;
        }
      }
      continue;
    }
    if (sameValue(base[key], local[key])) {
      continue;
    }
    if (local[key] === undefined) {
      if (key in stored) {
        patch[key] = null;
      }
      continue;
    }
    const member = diffPatch(base[key], local[key]);
    if (member !== undefined) {
      patch[key] = member;
    }
  }
  return patch;
}

/** A write to the account's document: the patch to send, and the layouts left out of it to fit. */
export interface AccountWrite {
  patch: Record<string, unknown>;
  /** The ids of named layouts the document could not hold; this browser keeps them. */
  leftOut: string[];
}

/**
 * What to send to bring this browser's changes into the stored document, held to the server's
 * limit — or null when even leaving layouts out does not make it fit.
 */
export function planAccountWrite(
  stored: Record<string, unknown>,
  base: AccountPrefs,
  local: AccountPrefs,
  maxLength: number = PREFERENCES_MAX_LENGTH,
): AccountWrite | null {
  const patch = accountPrefsPatchSince(stored, base, local);
  if (Object.keys(patch).length === 0) {
    return { patch, leftOut: [] };
  }
  const document = applyMergePatch(stored, patch) as Record<string, unknown>;
  const fitted = fitToBudget(document, maxLength);
  if (!fitted) {
    return null;
  }
  if (fitted.leftOutLayouts === 0) {
    return { patch, leftOut: [] };
  }
  const kept = Array.isArray(fitted.document.layouts) ? (fitted.document.layouts as Layout[]) : [];
  const keptIds = new Set(kept.map((layout) => layout.id));
  const leftOut = (document.layouts as Layout[]).filter((layout) => !keptIds.has(layout.id)).map((layout) => layout.id);
  return { patch: { ...patch, layouts: kept.length > 0 ? kept : null }, leftOut };
}

/** `prefs` without the named layouts whose ids are given. */
export function withoutLayouts(prefs: AccountPrefs, ids: readonly string[]): AccountPrefs {
  if (ids.length === 0) {
    return prefs;
  }
  const left = new Set(ids);
  const layouts = layoutsOf(prefs).filter((layout) => !left.has(layout.id));
  const rest: AccountPrefs = { ...prefs };
  delete rest.layouts;
  return layouts.length > 0 ? { ...rest, layouts } : rest;
}

/**
 * Account preferences in the compared form, spelled out as the store holds them — a default for
 * each one they leave out — and only where that differs from what `current` holds, so that taking
 * a value already held redraws nothing.
 */
export function storeValuesFor(prefs: AccountPrefs, current: AccountPrefs): Partial<AccountPrefsState> {
  const values: Record<string, unknown> = {};
  for (const key of ACCOUNT_PREF_KEYS) {
    const value = prefs[key] ?? ACCOUNT_PREF_DEFAULTS[key];
    if (!sameValue(value, current[key])) {
      values[key] = value;
    }
  }
  return values as Partial<AccountPrefsState>;
}
