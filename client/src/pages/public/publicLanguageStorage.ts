// SPDX-License-Identifier: AGPL-3.0-or-later
import type { PublicTripLanguage } from './publicTripEmbed.ts';

/**
 * Where a reader's press of the language button on a published trip is remembered.
 *
 * <b>A key of the published pages' own, and deliberately not the application's.</b> These pages
 * share their origin, and so their storage, with the signed-in application, where the key a
 * language choice is recorded under means more than "which language": it is read when the
 * application starts, to decide what it opens in, and at sign-in, to decide whether the account's
 * own stored language may still be adopted in this browser. The switch that writes it also saves
 * the language to the account, because the account's language is what its notifications are
 * written in. A page read without an account can save nothing to one — so a press recorded there
 * would turn the whole application to English on that machine while the member's messages went on
 * arriving in Romanian, and stop every account that signs in there from being given its own
 * language, with nothing on any screen showing the disagreement.
 */
export const PUBLIC_CHOICE_KEY = 'silexgis.publishedLanguageChosen';

export function chosenOnAPublishedPage(): PublicTripLanguage | null {
  try {
    const stored = window.localStorage.getItem(PUBLIC_CHOICE_KEY);
    return stored === 'ro' || stored === 'en' ? stored : null;
  } catch {
    // Private windows and blocked site data throw rather than return nothing.
    return null;
  }
}

export function rememberPublishedChoice(language: PublicTripLanguage): void {
  try {
    window.localStorage.setItem(PUBLIC_CHOICE_KEY, language);
  } catch {
    // Nothing to do: the language still changes and the address still carries it.
  }
}
