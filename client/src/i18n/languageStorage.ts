// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Where a deliberate choice of language made in this browser is recorded.
 *
 * Deliberately not i18next's own `i18nextLng`. The detector writes that key during `init()` with
 * whatever it detected, before anything has been chosen and before React has mounted, so reading
 * it answers "which language is showing", never "has anyone here picked one" — and gating on it
 * meant the account's stored language was never adopted at all.
 *
 * This is also the key the detector is pointed at, so the language the switch writes is the one
 * the next visit finds. The two disagreeing would be silent: every visit would re-detect and the
 * choice made last time would never be read back.
 */
export const CHOICE_KEY = 'silexgis.languageChosen';

/**
 * Kept in a module of its own, holding nothing but this key and the two calls around it, because
 * i18n bootstrap has to name it and must not reach the API layer to do so. Read from the module
 * that also defines the switch, `i18n/index.ts` would pull the query hooks — and every module
 * mocking them — into the file that runs before anything else.
 */
export function chosenHere(): boolean {
  try {
    return window.localStorage.getItem(CHOICE_KEY) !== null;
  } catch {
    // Private windows and blocked site data throw rather than return nothing.
    return false;
  }
}

export function rememberChoice(language: string): void {
  try {
    window.localStorage.setItem(CHOICE_KEY, language);
  } catch {
    // Nothing to do: the language still changes, it is only the memory of it that is lost.
  }
}
