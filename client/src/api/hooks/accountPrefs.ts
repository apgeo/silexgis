// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';
import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query';
import { ApiError, api } from '../client.ts';
import {
  ACCOUNT_PREF_KEYS,
  PREFERENCES_MAX_LENGTH,
  PREFERENCES_RETRY_LENGTH,
  accountPrefsIn,
  baselineAfterLeaving,
  baselineAfterRefusal,
  changedSince,
  layoutsNotIn,
  mergeAccountPrefs,
  pickAccountPrefs,
  planAccountWrite,
  planLeavingWrite,
  storeValuesFor,
  withoutLayouts,
  type AccountPrefs,
} from '../../stores/accountPrefs.ts';
import { useUiPrefsStore } from '../../stores/uiPrefsStore.ts';
import type { paths } from '../schema';
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
 *
 * `leftOut` names the layouts the write just made had to leave out because the document was full:
 * they join those this browser already keeps on its own, and any of them the account now holds —
 * written up by a later save, or from another browser — stops being marked as kept here.
 */
function takeAccount(document: Record<string, unknown>, base: AccountPrefs, leftOut: readonly string[] = []): void {
  const account = accountPrefsIn(document);
  const state = useUiPrefsStore.getState();
  const merged = mergeAccountPrefs(base, pickAccountPrefs(state), account);
  const keptBefore = new Set([...state.layoutsKeptHere, ...leftOut]);
  takingFromAccount = true;
  try {
    state.applyAccountPrefs(storeValuesFor(merged, state), account);
    const kept = layoutsNotIn(account, pickAccountPrefs(useUiPrefsStore.getState())).filter((id) => keptBefore.has(id));
    keepLayoutsHere(kept);
  } finally {
    takingFromAccount = false;
  }
}

function keepLayoutsHere(ids: string[]): void {
  const state = useUiPrefsStore.getState();
  if (ids.length !== state.layoutsKeptHere.length || ids.some((id, index) => state.layoutsKeptHere[index] !== id)) {
    state.setLayoutsKeptHere(ids);
  }
}

/** The baseline this browser holds for its account, in the compared form. */
function baselineHere(): AccountPrefs {
  return (useUiPrefsStore.getState().accountPrefsBaseline ?? {}) as AccountPrefs;
}

/**
 * Reads and writes of the account's document run one after another, each from where the last
 * one left the baseline: a save made while the sign-in read is on its way waits for it, and a read
 * never lands over a later save's answer.
 */
let queue: Promise<unknown> = Promise.resolve();

function enqueue<T>(work: () => Promise<T>): Promise<T> {
  const run = queue.then(work);
  queue = run.catch(() => undefined);
  return run;
}

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
 * layouts out of it, rather than having the whole write refused; the ones left out stay here. A
 * write the server still finds too large is planned once more with room to spare; one refused
 * after that is given up — and never left standing as a change still to be written.
 */
export function saveAccountPreferences(queryClient: QueryClient, accountId: string): Promise<void> {
  return enqueue(() => saveOnce(queryClient, accountId));
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
  for (const budget of [PREFERENCES_MAX_LENGTH, PREFERENCES_RETRY_LENGTH]) {
    const stored = await readStoredDocument(queryClient);
    if (!ownedBy()) {
      return;
    }
    const write = planAccountWrite(stored, base, local, budget);
    if (!write) {
      // Nothing that fits, even with layouts left out: the account keeps what it had.
      giveUp(base, local);
      throw new AccountPrefsRefused();
    }
    let answer = stored;
    if (Object.keys(write.patch).length > 0) {
      try {
        const written = await unwrap(api.PATCH('/api/v1/me/preferences', { body: { preferences: write.patch } }));
        queryClient.setQueryData(queryKeys.uiPreferences, written);
        answer = asDocument(written.preferences);
      } catch (error) {
        if (!(error instanceof ApiError) || !isRefusal(error)) {
          // Not reached, or not now: what is held here is still unsent, and goes with the next
          // change or the next sign-in.
          throw error;
        }
        if (error.code === TOO_LARGE && budget === PREFERENCES_MAX_LENGTH) {
          // Counted otherwise on the server, or added to elsewhere since the read: planned once
          // more, from a fresh read, with room to spare.
          continue;
        }
        if (ownedBy()) {
          giveUp(base, local);
        }
        throw error;
      }
    }
    if (!ownedBy()) {
      return;
    }
    // Measured from what was sent: a layout left out to fit is not one the account removed.
    takeAccount(answer, withoutLayouts(local, write.leftOut), write.leftOut);
    return;
  }
}

/** The server's code for a document over its limit. */
const TOO_LARGE = 'preferences.too_large';

/** A refusal of what was sent, as opposed to a failure to reach the server or a session ended. */
function isRefusal(error: ApiError): boolean {
  return error.status === 400 || error.status === 413 || error.status === 422;
}

/** A write given up because the account's document cannot hold it. */
export class AccountPrefsRefused extends Error {
  constructor() {
    super('The account cannot hold these preferences.');
    this.name = 'AccountPrefsRefused';
  }
}

/**
 * Gives a refused write up for good. The values stay on screen here for the rest of the visit, but
 * are no longer counted as changed here — a refusal that stuck would be offered again at every
 * save and, kept as this browser's change, would stop every later sign-in from taking the account's
 * own values.
 */
function giveUp(base: AccountPrefs, local: AccountPrefs): void {
  const state = useUiPrefsStore.getState();
  const settled = baselineAfterRefusal(base, local);
  takingFromAccount = true;
  try {
    state.applyAccountPrefs({}, settled);
    // The layouts added here stay out of the settled baseline, and so in this browser only.
    keepLayoutsHere([...new Set([...state.layoutsKeptHere, ...layoutsNotIn(settled, local)])]);
  } finally {
    takingFromAccount = false;
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

/** The longest a sign-out waits for the last change to be written before going ahead without it. */
const FLUSH_WAIT_MS = 3000;

/** Sends the change still waiting to be written, if any, of the sync now running. */
let flushRunning: (() => Promise<void>) | undefined;

/**
 * Writes now what is waiting to be written — called on the way out of a session, before the
 * cache is released, so a change made in the last moment before signing out reaches the account
 * instead of being thrown away with the cache. Bounded: an unreachable server delays a sign-out
 * by moments, not for good.
 */
export async function flushAccountPreferences(): Promise<void> {
  const flushing = flushRunning?.() ?? queue;
  await Promise.race([flushing.catch(() => undefined), new Promise((resolve) => setTimeout(resolve, FLUSH_WAIT_MS))]);
}

/** The account's own write, as the page names it when it has no time to go through the client. */
const PREFERENCES_PATH = '/api/v1/me/preferences';

type PreferencesPatchBody =
  paths['/api/v1/me/preferences']['patch']['requestBody']['content']['application/json'];

/**
 * The signed-in account's access token, as last handed to the sync — held here because a page
 * being closed has no time to ask the sign-in library for it.
 */
let bearer: { accountId: string; token: string } | undefined;

/**
 * Sends a change still unsent as the page goes away — with a request the browser carries on with
 * after the page has gone, and that cannot wait for a read first (see `planLeavingWrite` for what
 * that leaves out). Only for the account that holds the cache, and only with that account's own
 * token: the server writes to whichever account the token is of, so the change can land on
 * nobody else's preferences, however late it arrives.
 *
 * What is sent is counted as written from the moment it goes (see `baselineAfterLeaving`): a page
 * that is closing never sees the answer, and a change still counted as unsent would be written
 * again at the next sign-in here, over a choice made in another browser since. Only a page that is
 * still here to see the request fail or be refused — one kept in the back-forward cache — counts
 * the change as unsent again. So a closing request that never arrives loses that change: the
 * account keeps what it had, as it would have without the request. A change still in the cache
 * is dropped, never applied, if another member signs in on this browser, because claiming the
 * cache for them resets it.
 */
function sendBeforeLeaving(accountId: string): void {
  const state = useUiPrefsStore.getState();
  if (state.accountId !== accountId || bearer?.accountId !== accountId) {
    return;
  }
  const base = baselineHere();
  const patch = planLeavingWrite(base, pickAccountPrefs(state));
  if (!patch) {
    return;
  }
  const body: PreferencesPatchBody = { preferences: patch };
  const request = fetch(PREFERENCES_PATH, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${bearer.token}` },
    body: JSON.stringify(body),
    keepalive: true,
    credentials: 'omit',
  });
  const sentBaseline = baselineAfterLeaving(base, patch);
  setBaseline(sentBaseline);
  const unsent = () => {
    // Only while nothing has moved the baseline on since: a later read or write knows better.
    const now = useUiPrefsStore.getState();
    if (now.accountId === accountId && now.accountPrefsBaseline === sentBaseline) {
      setBaseline(base);
    }
  };
  void request.then((response) => {
    if (!response.ok) {
      unsent();
    }
  }, unsent);
}

/** Moves the baseline alone — no preference value changes, so nothing is scheduled to be written. */
function setBaseline(baseline: AccountPrefs): void {
  takingFromAccount = true;
  try {
    useUiPrefsStore.getState().applyAccountPrefs({}, baseline);
  } finally {
    takingFromAccount = false;
  }
}

/**
 * Keeps the account preferences in step between this browser and the account, for as long as the
 * signed-in shell is drawn.
 *
 * At sign-in the browser's cache is claimed for the account (reset first if another account held
 * it) and the account's stored values are taken for every preference not changed here; from the
 * start, a change to any account preference is written back, a moment after the changes stop —
 * whether or not the account's values could be read.
 */
export function useAccountPrefsSync(accountId: string | null, accessToken?: string) {
  const queryClient = useQueryClient();

  useEffect(() => {
    bearer = accountId && accessToken ? { accountId, token: accessToken } : undefined;
  }, [accountId, accessToken]);

  useEffect(() => {
    if (!accountId) {
      return undefined;
    }
    let cancelled = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    let due = false;

    const save = () =>
      saveAccountPreferences(queryClient, accountId).catch(() => {
        // The cache keeps the choice; the next change, or the next sign-in, writes it.
      });
    const scheduleSave = () => {
      clearTimeout(timer);
      due = true;
      timer = setTimeout(() => {
        due = false;
        void save();
      }, SAVE_DELAY_MS);
    };
    const flush = () => {
      if (!due) {
        return queue.then(() => undefined);
      }
      clearTimeout(timer);
      due = false;
      return save();
    };
    flushRunning = flush;

    useUiPrefsStore.getState().adoptAccount(accountId);

    // Listening from the start, before the account's values are read: a change made while the
    // read is on its way, or after it failed, is still written.
    const unsubscribe = useUiPrefsStore.subscribe((state, previous) => {
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

    void enqueue(async () => {
      const stored = await readStoredDocument(queryClient);
      if (cancelled || useUiPrefsStore.getState().accountId !== accountId) {
        return;
      }
      // Measured from the baseline the cache kept: a change made here and not yet written — the
      // page reloaded within moments of it, or made while this read was on its way — is newer
      // than the account and stays, to be written; everything else is the account's.
      takeAccount(stored, baselineHere());
      if (changedSince(baselineHere(), pickAccountPrefs(useUiPrefsStore.getState()))) {
        // What this browser holds that the account has not yet — a change the last page did
        // not get to write — is written up now rather than at the next change.
        scheduleSave();
      }
    }).catch(() => {
      // Unreachable or refused: what this browser holds for the account stands in, and changes
      // are still written as they are made.
    });

    // A change made moments before the page is put away or closed would otherwise wait out the
    // settling delay on a page that is no longer there. Hidden — the tab switched away from, the
    // phone's app switcher, often the first step of a close — it is written the ordinary way,
    // reading first. Going away for good, there is no time to read: what can be sent unread goes
    // with a request the browser finishes after the page is gone, and is counted as written. A
    // change caught by both is only written twice, with the same values.
    const onHidden = () => {
      if (document.visibilityState === 'hidden') {
        void flush();
      }
    };
    // The settling timer is left running: a page kept in the browser's back-forward cache comes
    // back with it, and a page that does not come back takes it along.
    const onPageHide = () => sendBeforeLeaving(accountId);
    document.addEventListener('visibilitychange', onHidden);
    window.addEventListener('pagehide', onPageHide);

    return () => {
      cancelled = true;
      clearTimeout(timer);
      unsubscribe();
      document.removeEventListener('visibilitychange', onHidden);
      window.removeEventListener('pagehide', onPageHide);
      if (flushRunning === flush) {
        flushRunning = undefined;
      }
    };
  }, [accountId, queryClient]);
}
