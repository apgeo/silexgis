// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';
import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import {
  ACCOUNT_PREF_KEYS,
  accountPrefsIn,
  changedSince,
  mergeAccountPrefs,
  pickAccountPrefs,
  planAccountWrite,
  storeValuesFor,
  withoutLayouts,
  type AccountPrefs,
} from '../../stores/accountPrefs.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';
import { queryKeys } from './queryKeys.ts';
import { unwrap } from './shared.ts';

function asDocument(value: unknown): Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : {};
}

async function readStoredDocument(queryClient: QueryClient): Promise<Record<string, unknown>> {
  const answer = await queryClient.fetchQuery({
    queryKey: queryKeys.uiPreferences,
    queryFn: () => unwrap(api.GET('/api/v1/me/preferences')),
    staleTime: 0,
  });
  return asDocument(answer.preferences);
}

/**
 * True while this module itself is putting the account's values into the store, so that taking
 * them is not mistaken for a change made here and written straight back.
 */
let takingFromAccount = false;

/**
 * Takes the account's document as what this browser now knows the account to hold, and the
 * account's value of every preference not changed here since `base`.
 */
function takeAccount(document: Record<string, unknown>, base: AccountPrefs): void {
  const account = accountPrefsIn(document);
  const state = useUiPrefsStore.getState();
  const merged = mergeAccountPrefs(base, pickAccountPrefs(state), account);
  takingFromAccount = true;
  try {
    state.applyAccountPrefs(storeValuesFor(merged, state), account);
  } finally {
    takingFromAccount = false;
  }
}

/** The baseline this browser holds for its account, in the compared form. */
function baselineHere(): AccountPrefs {
  return (useUiPrefsStore.getState().accountPrefsBaseline ?? {}) as AccountPrefs;
}

/** Saves run one after another, each from where the last one left the baseline. */
let saving: Promise<unknown> = Promise.resolve();

/**
 * Writes what was changed in this browser to the account's stored document — the one place that
 * writes it.
 *
 * What is sent is this browser's own changes: what differs between the preferences held here and
 * the baseline, the account's document as this browser last read or wrote it. Not this browser's
 * whole state — that would send a preference another browser changed since, read here before the
 * change, back over it. The patch goes to the server's merging write (a JSON Merge Patch: members
 * not named are left as they are), with the named layouts merged layout by layout into the list
 * the account holds now. The answer becomes the new baseline, and the account's value of every
 * preference not changed here meanwhile is taken from it — so this browser also catches up with
 * what was changed elsewhere.
 *
 * Before sending, the patched document is held to the server's size limit by leaving named
 * layouts out of it, rather than having the whole write refused; the ones left out stay here.
 */
export function saveAccountPreferences(queryClient: QueryClient, accountId: string): Promise<void> {
  const run = saving.then(() => saveOnce(queryClient, accountId));
  saving = run.catch(() => undefined);
  return run;
}

async function saveOnce(queryClient: QueryClient, accountId: string): Promise<void> {
  // Only for the account the cache belongs to: a save that comes due after the person signed out
  // — or after another member signed in — would otherwise write the defaults a sign-out leaves, or
  // somebody else's choices, over this account's document.
  const ownedBy = () => useUiPrefsStore.getState().accountId === accountId;
  if (!ownedBy()) {
    return;
  }
  const base = baselineHere();
  const local = pickAccountPrefs(useUiPrefsStore.getState());
  if (!changedSince(base, local)) {
    return;
  }
  const stored = await readStoredDocument(queryClient);
  if (!ownedBy()) {
    return;
  }
  const write = planAccountWrite(stored, base, local);
  if (!write) {
    // Nothing that fits: the browser keeps it, and the account keeps what it had.
    return;
  }
  let answer = stored;
  if (Object.keys(write.patch).length > 0) {
    const written = await unwrap(api.PATCH('/api/v1/me/preferences', { body: { preferences: write.patch } }));
    queryClient.setQueryData(queryKeys.uiPreferences, written);
    answer = asDocument(written.preferences);
  }
  if (!ownedBy()) {
    return;
  }
  // Measured from what was sent: a layout left out to fit is not one the account removed.
  takeAccount(answer, withoutLayouts(local, write.leftOut));
}

/** `saveAccountPreferences` as a mutation, for a screen that says whether the save went through. */
export function useSaveAccountPreferences() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      const accountId = useUiPrefsStore.getState().accountId;
      if (accountId) {
        await saveAccountPreferences(queryClient, accountId);
      }
    },
  });
}

/** How long a burst of changes — a panel being dragged wider — is let settle before it is written. */
const SAVE_DELAY_MS = 1500;

/**
 * Keeps the account preferences in step between this browser and the account, for as long as the
 * signed-in shell is drawn.
 *
 * At sign-in the browser's cache is claimed for the account (reset first if another account held
 * it) and the account's stored values are taken for every preference not changed here; from then
 * on, a change to any account preference is written back, a moment after the changes stop.
 */
export function useAccountPrefsSync(accountId: string | null) {
  const queryClient = useQueryClient();

  useEffect(() => {
    if (!accountId) {
      return undefined;
    }
    let cancelled = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    let unsubscribe: (() => void) | undefined;

    const scheduleSave = () => {
      clearTimeout(timer);
      timer = setTimeout(() => {
        void saveAccountPreferences(queryClient, accountId).catch(() => {
          // The cache keeps the choice; the next change, or the next sign-in, writes it.
        });
      }, SAVE_DELAY_MS);
    };

    useUiPrefsStore.getState().adoptAccount(accountId);
    void (async () => {
      try {
        const stored = await readStoredDocument(queryClient);
        if (cancelled) {
          return;
        }
        // Measured from the baseline the cache kept: a change made here and not yet written — the
        // page reloaded within moments of it, or made while this read was on its way — is newer
        // than the account and stays, to be written; everything else is the account's.
        takeAccount(stored, baselineHere());
      } catch {
        // Unreachable or refused: what this browser holds for the account stands in until the
        // next sign-in, and nothing is written over an account document that could not be read.
        return;
      }
      if (cancelled) {
        return;
      }
      if (changedSince(baselineHere(), pickAccountPrefs(useUiPrefsStore.getState()))) {
        // What this browser holds that the account has not yet — a change the last page did not
        // get to write — is written up now rather than at the next change.
        scheduleSave();
      }
      unsubscribe = useUiPrefsStore.subscribe((state, previous) => {
        // A sign-out resetting the cache is not a change to this account's preferences, and nor
        // is the account's own value being taken.
        if (takingFromAccount || state.accountId !== accountId) {
          return;
        }
        // Compared by what would be stored, not by reference: a screen that writes back the same
        // arrangement it read (the map's search box does, each time it is drawn) changes nothing.
        if (
          ACCOUNT_PREF_KEYS.some((key) => state[key] !== previous[key]) &&
          JSON.stringify(pickAccountPrefs(state)) !== JSON.stringify(pickAccountPrefs(previous))
        ) {
          scheduleSave();
        }
      });
    })();

    return () => {
      cancelled = true;
      clearTimeout(timer);
      unsubscribe?.();
    };
  }, [accountId, queryClient]);
}
