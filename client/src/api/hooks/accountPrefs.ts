// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';
import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import {
  ACCOUNT_PREF_KEYS,
  accountPrefsPatch,
  applyMergePatch,
  fitToBudget,
  pickAccountPrefs,
  prefsToApply,
  readAccountPrefs,
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
 * Writes the account preferences this browser holds into the account's stored document — the one
 * place that writes it.
 *
 * The stored document is read fresh and the patch that brings its account preferences to what
 * this browser holds is sent to the server's merging write (a JSON Merge Patch: members this
 * client does not own are left as they are, a preference back at its default is removed). The read
 * is what makes removal possible: an arrangement taken away here is a member the patch has to name
 * as null. Before sending, the patched document is held to the server's size limit by leaving the
 * oldest named layouts out of it, rather than having the whole write refused.
 */
export async function saveAccountPreferences(queryClient: QueryClient, accountId: string): Promise<void> {
  // Only for the account the cache belongs to: a save that comes due after the person signed out
  // — or after another member signed in — would otherwise write the defaults a sign-out leaves, or
  // somebody else's choices, over this account's document.
  const ownedBy = () => useUiPrefsStore.getState().accountId === accountId;
  if (!ownedBy()) {
    return;
  }
  const stored = await readStoredDocument(queryClient);
  if (!ownedBy()) {
    return;
  }
  const patch = accountPrefsPatch(stored, pickAccountPrefs(useUiPrefsStore.getState()));
  if (Object.keys(patch).length === 0) {
    useUiPrefsStore.getState().setAccountPrefsUnsaved(false);
    return;
  }
  const fitted = fitToBudget(applyMergePatch(stored, patch) as Record<string, unknown>);
  if (!fitted) {
    // Nothing that fits: the browser keeps it, and the account keeps what it had.
    return;
  }
  const toSend =
    fitted.leftOutLayouts > 0 ? accountPrefsPatch(stored, readAccountPrefs(fitted.document)) : patch;
  const sent = JSON.stringify(pickAccountPrefs(useUiPrefsStore.getState()));
  const written = await unwrap(api.PATCH('/api/v1/me/preferences', { body: { preferences: toSend } }));
  queryClient.setQueryData(queryKeys.uiPreferences, written);
  // Written — unless something changed again while the write was on its way, which the next save
  // carries.
  if (ownedBy() && JSON.stringify(pickAccountPrefs(useUiPrefsStore.getState())) === sent) {
    useUiPrefsStore.getState().setAccountPrefsUnsaved(false);
  }
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
 * it) and the account's stored values are taken over it; from then on, a change to any account
 * preference is written back, a moment after the changes stop.
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

    const claim = useUiPrefsStore.getState().adoptAccount(accountId);
    const unsavedHere = useUiPrefsStore.getState().accountPrefsUnsaved;
    void (async () => {
      try {
        const stored = await readStoredDocument(queryClient);
        if (cancelled) {
          return;
        }
        useUiPrefsStore.getState().applyAccountPrefs(prefsToApply(stored, claim, unsavedHere));
      } catch {
        // Unreachable or refused: what this browser holds for the account stands in until the
        // next sign-in, and nothing is written over an account document that could not be read.
        return;
      }
      if (cancelled) {
        return;
      }
      if (claim === 'claimed' || unsavedHere) {
        // What this browser holds that the account has not yet — the first claim of an older
        // cache, or a change the last page did not get to write — is written up now rather than
        // at the next change.
        scheduleSave();
      }
      unsubscribe = useUiPrefsStore.subscribe((state, previous) => {
        // A sign-out resetting the cache is not a change to this account's preferences.
        if (state.accountId !== accountId) {
          return;
        }
        // Compared by what would be stored, not by reference: a screen that writes back the same
        // arrangement it read (the map's search box does, each time it is drawn) changes nothing.
        if (
          ACCOUNT_PREF_KEYS.some((key) => state[key] !== previous[key]) &&
          JSON.stringify(pickAccountPrefs(state)) !== JSON.stringify(pickAccountPrefs(previous))
        ) {
          // Marked at once, in the cache that survives a reload; the write follows when the
          // changes stop.
          if (!state.accountPrefsUnsaved) {
            state.setAccountPrefsUnsaved(true);
          }
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
