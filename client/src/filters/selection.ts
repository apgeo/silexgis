// SPDX-License-Identifier: AGPL-3.0-or-later
import { condition, emptyDocument, fromGroups, withSort, withWhere } from './document.ts';
import type { FilterDocument, FilterHit, SortKey, WorldVocabulary } from './types.ts';

/**
 * Everything the object selector does, with nothing mounted.
 *
 * The control is a shell over this. Which worlds are switched on, what somebody typed, which sort
 * is active, and how all of that becomes a filter document are decisions a table, an export dialog
 * or a test can make without a component existing — and, more to the point, decisions that can be
 * tested by asserting on values instead of by driving a rendered tree.
 */

/**
 * One button on the control: a name somebody recognises, and the question it stands for.
 *
 * The buttons are not worlds. "Caves" and "Entrances" are two buttons over one world, separated by
 * a condition; "Documents" is a world with no condition. Modelling them as worlds would have made
 * the two cave buttons impossible, and modelling them as conditions would have made the document
 * button impossible — so a scope is a world *and* an optional condition, and both cases fit.
 */
export interface SelectorScope {
  /** Stable; stored in remembered configuration, so it is chosen once. */
  id: string;
  labelKey: string;
  world: string;
  /** Narrows within the world. Null means the whole world. */
  where: { field: string; value: string } | null;
  /**
   * A single character that selects this scope when typed at the start of the query — `#` for
   * caves, `@` for people, and so on. Optional: a scope without one is reachable by its button.
   */
  prefix?: string;
}

/**
 * What the control is showing, and what it has been asked.
 */
export interface SelectorState {
  /** Which scope buttons are on. Empty means every scope the caller allowed. */
  activeScopeIds: string[];
  query: string;
  sort: SortKey;
  descending: boolean;
  /** Ids the caller already chose, in the order they were chosen. */
  chosen: string[];
}

export function initialState(sort: SortKey = 'updated'): SelectorState {
  return { activeScopeIds: [], query: '', sort, descending: true, chosen: [] };
}

/**
 * The scopes actually in play: the ones switched on, or all of them when none is.
 *
 * "None switched on" means "everything", not "nothing". A control that answered nothing when a
 * person turned off the last button would look broken at exactly the moment they were trying to
 * widen the search.
 */
export function scopesInPlay(
  available: readonly SelectorScope[],
  state: SelectorState,
): SelectorScope[] {
  const active = available.filter((s) => state.activeScopeIds.includes(s.id));
  return active.length > 0 ? active : [...available];
}

/**
 * A typed prefix, and the query with it removed.
 *
 * Only at the very start, and only when a scope claims that character. Anywhere else it is text
 * somebody meant to search for — cave names contain punctuation, and a control that swallowed a
 * character mid-word would be unusable for the people whose caves are named that way.
 */
export function readPrefix(
  available: readonly SelectorScope[],
  raw: string,
): { scope: SelectorScope | null; query: string } {
  const first = raw.slice(0, 1);
  const scope = first === '' ? undefined : available.find((s) => s.prefix === first);

  return scope ? { scope, query: raw.slice(1).trimStart() } : { scope: null, query: raw };
}

/**
 * The filter document the current state asks for.
 *
 * @param textFieldOf which field a world matches free text against — the world's own business,
 *   since one calls it a name and another a title, and there is no field every world shares.
 */
export function documentFor(
  available: readonly SelectorScope[],
  state: SelectorState,
  textFieldOf: (world: string) => string | null,
): FilterDocument {
  const { scope: prefixed, query } = readPrefix(available, state.query);
  const scopes = prefixed ? [prefixed] : scopesInPlay(available, state);

  // One entry per world even when two buttons share it, or the document names a world twice and
  // the server refuses the whole request rather than the button that caused it.
  const byWorld = new Map<string, SelectorScope[]>();
  for (const scope of scopes) {
    byWorld.set(scope.world, [...(byWorld.get(scope.world) ?? []), scope]);
  }

  const text = query.trim();
  let document: FilterDocument = { ...emptyDocument([...byWorld.keys()]) };

  for (const [world, worldScopes] of byWorld) {
    const textField = textFieldOf(world);
    const textCondition = text.length > 0 && textField
      ? condition(textField, 'contains', [{ type: 'text', value: text }])
      : null;

    // Buttons within a world are alternatives: caves OR entrances. What was typed applies to both,
    // so it is repeated into each group rather than sitting outside them — there is no shape in the
    // document for "this and (that or the other)" without writing it out.
    const groups = worldScopes.map((scope) => {
      const parts = [];
      if (scope.where) {
        parts.push(condition(scope.where.field, 'equals', [
          { type: 'id', value: scope.where.value },
        ]));
      }

      if (textCondition) {
        parts.push(textCondition);
      }

      return parts;
    });

    document = withWhere(document, world, fromGroups(groups));
  }

  return withSort(document, state.sort, state.descending);
}

/**
 * Whether there is enough to ask the server.
 *
 * A control that queried on an empty box would dump whatever the first page of a domain happens to
 * be, which is how a picker becomes a way to enumerate. Callers that genuinely want to browse a
 * small bounded set say so.
 */
export function shouldAsk(
  available: readonly SelectorScope[],
  state: SelectorState,
  { minChars, browseOnEmpty }: { minChars: number; browseOnEmpty: boolean },
): boolean {
  // The scopes have to be handed in, or the prefix is never recognised and therefore never
  // stripped: `#u` would count as two characters of search when one was typed, and a bare `#`
  // would count as one. What is measured is what will actually be matched on.
  const { query } = readPrefix(available, state.query);
  const text = query.trim();
  return text.length === 0 ? browseOnEmpty : text.length >= minChars;
}

/**
 * The rows to show: what the search found, with anything already chosen that the search did not
 * find shown first.
 *
 * Without this a stored choice vanishes from the list the moment somebody types something it does
 * not match, and the control looks like it has forgotten what they picked.
 */
export function rowsToShow(
  found: readonly FilterHit[],
  resolved: readonly FilterHit[],
  state: SelectorState,
): FilterHit[] {
  const seen = new Set(found.map((h) => h.id));
  const pinned = state.chosen
    .map((id) => resolved.find((h) => h.id === id))
    .filter((h): h is FilterHit => h !== undefined && !seen.has(h.id));

  return [...pinned, ...found];
}

/**
 * Which of the chosen ids could not be described.
 *
 * An id that resolves to nothing is one this caller may not see, or one that is gone — and the two
 * are indistinguishable on purpose, because telling them apart would confirm the row exists. The
 * control shows "restricted" for both and never the identifier itself, which would put a raw uuid
 * on screen where a name belongs.
 */
export function unresolvedIds(
  state: SelectorState,
  resolved: readonly FilterHit[],
): string[] {
  const known = new Set(resolved.map((h) => h.id));
  return state.chosen.filter((id) => !known.has(id));
}

/** Choosing a row, honouring whether the control takes one or several. */
export function choose(state: SelectorState, id: string, multiple: boolean): SelectorState {
  if (!multiple) {
    return { ...state, chosen: [id] };
  }

  return state.chosen.includes(id)
    ? { ...state, chosen: state.chosen.filter((x) => x !== id) }
    : { ...state, chosen: [...state.chosen, id] };
}

/**
 * The state as it can actually be honoured right now.
 *
 * A remembered arrangement outlives the thing it referred to: a caller stops offering a scope, a
 * world stops declaring a sort, and what was stored months ago now selects nothing. Pruned on the
 * way out rather than written back, so the person's choice survives if whatever it named returns —
 * somebody who narrowed to documents, used a screen that has none, and came back should find their
 * choice where they left it.
 */
export function effectiveState(
  state: SelectorState,
  offeredScopeIds: readonly string[],
  availableSorts: readonly SortKey[],
): SelectorState {
  const scopes = state.activeScopeIds.filter((id) => offeredScopeIds.includes(id));
  const sortSurvives = availableSorts.length === 0 || availableSorts.includes(state.sort);

  return {
    ...state,
    activeScopeIds: scopes,
    sort: sortSurvives ? state.sort : (availableSorts[0] ?? state.sort),
  };
}

/** Turning a scope button on or off. */
export function toggleScope(state: SelectorState, scopeId: string): SelectorState {
  return {
    ...state,
    activeScopeIds: state.activeScopeIds.includes(scopeId)
      ? state.activeScopeIds.filter((x) => x !== scopeId)
      : [...state.activeScopeIds, scopeId],
  };
}

/**
 * Pressing a sort button: the same one again reverses it, a different one starts descending.
 *
 * Descending first because every sort here is one people read newest-first or nearest-first. The
 * exception is the alphabetical one, which nobody reads backwards.
 */
export function pressSort(state: SelectorState, sort: SortKey): SelectorState {
  if (state.sort === sort) {
    return { ...state, descending: !state.descending };
  }

  return { ...state, sort, descending: sort !== 'title' };
}

/**
 * The sorts worth offering: those every world in play can actually answer.
 *
 * An intersection rather than a union. A sort only some worlds could honour would leave the others
 * in an order nobody chose, with nothing on screen to say so.
 */
export function sortsOffered(
  vocabularies: readonly WorldVocabulary[],
  worlds: readonly string[],
): SortKey[] {
  const inPlay = vocabularies.filter((v) => worlds.includes(v.world));
  if (inPlay.length === 0) {
    return [];
  }

  return inPlay
    .map((v) => v.sorts)
    .reduce((all, sorts) => all.filter((s) => sorts.includes(s)));
}
