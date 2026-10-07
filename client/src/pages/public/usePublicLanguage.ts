// SPDX-License-Identifier: AGPL-3.0-or-later
import { useLayoutEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useSearchParams } from 'react-router-dom';
import {
  PUBLIC_TRIP_LANGUAGE_PARAM,
  type PublicTripLanguage,
  readLanguageLink,
} from './publicTripEmbed.ts';
import { chosenOnAPublishedPage, rememberPublishedChoice } from './publicLanguageStorage.ts';

export interface PublicLanguage {
  /** The language the page is drawn in at this moment. */
  language: PublicTripLanguage;
  /** The one a press of the button changes it to. */
  other: PublicTripLanguage;
  /** Changes the language because the reader asked, on this page, for it to change. */
  choose: (language: PublicTripLanguage) => void;
}

/**
 * The language of a published trip's page, and the only place such a page touches it.
 *
 * <b>Four things can say, and they are asked in this order.</b> The address, when it names a
 * language; then what a reader chose with this button on a published page in this browser before;
 * then whatever was chosen in the signed-in application here; then what the installation opens in.
 * The last two are one answer, settled before this page is drawn, and this hook only leaves it be.
 * The address comes first because of who wrote it: somebody put this link into an article written
 * in one language, for readers of that language, and a reader arriving from it has not been asked
 * anything yet. It is not what the reader's own browser is set to — most browsers say English
 * whatever their owner reads, which is why the application does not ask them either.
 *
 * <b>A language an address names lasts for the visit and is not written down.</b> It was the
 * author's choice, not the reader's. This page shares its origin with the signed-in application,
 * where "somebody chose a language in this browser" decides whether an account's own stored
 * language may be adopted at sign-in — so recording a link's language would let any address a
 * member happens to open settle the language of their account's sessions on that machine.
 *
 * <b>Pressing the button is the reader choosing, and that is written down — for the published
 * pages only</b>, under a key of their own, beside which the reasons are. It is also written
 * into the address, for two reasons: the address is asked first, so a press that left `lang=ro`
 * standing there would be undone by the next redraw; and an address that says which language the
 * page is in is one a reader can send to somebody else and have it open the way they saw it.
 * Replaced rather than pushed — a language is not a place the back button should return to.
 *
 * Applied before the browser paints, so a link naming English never shows a frame of Romanian
 * first; and long before the survey viewer is built, which takes its language once, when it is
 * constructed. A press after that leaves the viewer's own tooltips as they were until the page is
 * loaded again; everything this page draws itself changes at once.
 */
export function usePublicLanguage(): PublicLanguage {
  const { i18n } = useTranslation();
  const [search, setSearch] = useSearchParams();
  const asked = readLanguageLink(search);
  const language: PublicTripLanguage = i18n.resolvedLanguage?.startsWith('ro') ? 'ro' : 'en';

  useLayoutEffect(() => {
    // Compared with what is showing at the moment the address changes, and not listed as a
    // dependency: this answers the address, and must not fire a second time because the language
    // it has just set is now different from the one it read.
    const showing = i18n.resolvedLanguage?.startsWith('ro') ? 'ro' : 'en';
    const wanted = asked ?? chosenOnAPublishedPage();
    if (wanted !== null && wanted !== showing) {
      void i18n.changeLanguage(wanted);
    }
  }, [asked, i18n]);

  return {
    language,
    other: language === 'ro' ? 'en' : 'ro',
    choose: (chosen) => {
      void i18n.changeLanguage(chosen);
      rememberPublishedChoice(chosen);
      const next = new URLSearchParams(search);
      next.set(PUBLIC_TRIP_LANGUAGE_PARAM, chosen);
      setSearch(next, { replace: true });
    },
  };
}
