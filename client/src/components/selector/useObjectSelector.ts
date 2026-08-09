// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMemo, useState } from 'react';
import { useFilterQuery, useFilterResolve, useFilterVocabulary, type FilterHitDto } from '../../api/hooks.ts';
import {
  documentFor,
  initialState,
  rowsToShow,
  scopesInPlay,
  shouldAsk,
  sortsOffered,
  unresolvedIds,
  type SelectorScope,
  type SelectorState,
} from '../../filters/selection.ts';
import type { FilterHit, SortKey, WorldVocabulary } from '../../filters/types.ts';
import { useDebouncedValue } from '../../hooks/useDebouncedValue.ts';

/**
 * The selector's state machine, wired to the server.
 *
 * Everything that decides anything lives in `src/filters/selection.ts` and is tested without a
 * component. This is the joinery: it holds the state, debounces the typing, and asks the two
 * routes. Nothing here should ever grow a rule — if a question can be answered from values, it
 * belongs on the other side of that line where a test can reach it.
 */

export interface UseObjectSelectorOptions {
  scopes: readonly SelectorScope[];
  multiple: boolean;
  minChars: number;
  pageSize: number;
  browseOnEmpty: boolean;
  /** Ids never offered — the row being edited, rows already added elsewhere. */
  exclude: readonly string[];
  /** Restricts the sort buttons further; omitted offers everything the worlds in play share. */
  sorts?: readonly SortKey[];
  value: readonly string[];
  onChange: (ids: string[]) => void;
  onPick?: (hit: FilterHit | null) => void;
}

/** Which field a world matches free text against; a world calls it a name or a title, not both. */
const TEXT_FIELD: Record<string, string> = {
  feature: 'name',
  tripLog: 'title',
};

function textFieldOf(world: string): string | null {
  return TEXT_FIELD[world] ?? null;
}

export function useObjectSelector(options: UseObjectSelectorOptions) {
  const [state, setState] = useState<SelectorState>(() => initialState());
  const vocabulary = useFilterVocabulary();

  // The chosen ids are the caller's, not this hook's: a form owns its value, and a second copy
  // here would drift the moment the form reset without unmounting the control.
  const current: SelectorState = useMemo(
    () => ({ ...state, chosen: [...options.value] }),
    [state, options.value],
  );

  const debouncedQuery = useDebouncedValue(current.query, 300);
  const asked = useMemo(
    () => ({ ...current, query: debouncedQuery }),
    [current, debouncedQuery],
  );

  const worlds = useMemo(
    () => [...new Set(scopesInPlay(options.scopes, asked).map((s) => s.world))],
    [options.scopes, asked],
  );

  const document = useMemo(
    () => documentFor(options.scopes, asked, textFieldOf),
    [options.scopes, asked],
  );

  const enabled = shouldAsk(asked, {
    minChars: options.minChars,
    browseOnEmpty: options.browseOnEmpty,
  });

  const results = useFilterQuery(
    {
      document: document as never,
      page: 1,
      pageSize: options.pageSize,
      // Never counted. A total beside a search box is a population statistic for whatever
      // somebody just typed, and it costs a full pass over the composed set to produce.
      count: false,
    },
    enabled,
  );

  // Asked of the first world only: an id belongs to the world it was chosen from, and the control
  // is single-world at every call site that stores one.
  const resolveWorld = worlds[0] ?? '';
  const resolved = useFilterResolve(resolveWorld, options.value);

  const found: FilterHit[] = useMemo(() => {
    const hits = (results.data?.worlds ?? []).flatMap((w) => w.hits as FilterHitDto[]);
    return hits
      .filter((h) => !options.exclude.includes(h.id))
      .map((h) => h as FilterHit);
  }, [results.data, options.exclude]);

  const describedChoices = useMemo(
    () => ((resolved.data ?? []) as FilterHitDto[]).map((h) => h as FilterHit),
    [resolved.data],
  );

  const rows = useMemo(
    () => rowsToShow(found, describedChoices, current),
    [found, describedChoices, current],
  );

  const vocabularies = (vocabulary.data?.worlds ?? []) as unknown as WorldVocabulary[];
  const availableSorts = useMemo(() => {
    const shared = sortsOffered(vocabularies, worlds);
    return options.sorts ? shared.filter((s) => options.sorts!.includes(s)) : shared;
  }, [vocabularies, worlds, options.sorts]);

  return {
    state: current,
    setState,
    rows,
    describedChoices,
    /** Chosen ids nothing could describe: gone, or not this caller's to see. */
    restricted: unresolvedIds(current, describedChoices),
    availableSorts,
    worlds,
    // Only the typing is shown as loading. The resolve request runs whenever a form opens with a
    // stored value, and spinning the whole list for it would flicker on every mount.
    loading: enabled && results.isFetching,
    asked: enabled,
  };
}
