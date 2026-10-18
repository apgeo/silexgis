// SPDX-License-Identifier: AGPL-3.0-or-later
import { useLayoutEffect, useRef } from 'react';
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
  /**
   * Whether this installation offers the choice at all: false only where it has said so.
   *
   * <b>True while nothing has said.</b> A page whose link answers nothing at all — taken back,
   * lapsed, a cave since protected — never learns this installation's answer, and that page is
   * one sentence a reader may not be able to read: it keeps the way into the other language, as
   * it had before any of this was a setting. A page draws no control for it where this is false;
   * nothing else about a page depends on it.
   */
  offered: boolean;
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
 * <b>Whether the choice is offered at all is the installation's, and it arrives late.</b> The
 * answer that says so is the published trip's own, and a visitor has no account for anything to
 * have been handed to before it: so this is asked for the first frames without knowing, answers
 * the address as it always has, and puts the language back to what the browser was showing when
 * the answer turns out to be no. That order is deliberate. The other way round — nothing applied
 * until the answer is in hand — would cost every reader of every installation a frame in the
 * wrong language to spare the rare installation that has switched the choice off its own, and the
 * frame it costs them is the first one of a page somebody opened to find out whether their family
 * is out of the cave.
 *
 * Applied before the browser paints, so a link naming English never shows a frame of Romanian
 * first; and long before the survey viewer is built, which takes its language once, when it is
 * constructed. A press after that leaves the viewer's own tooltips as they were until the page is
 * loaded again; everything this page draws itself changes at once.
 */
export function usePublicLanguage(offered: boolean | undefined): PublicLanguage {
  const { i18n } = useTranslation();
  const [search, setSearch] = useSearchParams();
  const asked = readLanguageLink(search);
  const language: PublicTripLanguage = i18n.resolvedLanguage?.startsWith('ro') ? 'ro' : 'en';
  // What this browser was showing before this page touched anything: the signed-in choice, or
  // what the installation opens in. Taken on the first render, which is before the effect below
  // has run, and kept for the life of the page — it is what an installation that does not offer
  // the choice is put back to.
  const settled = useRef(language);

  useLayoutEffect(() => {
    // Compared with what is showing at the moment the address changes, and not listed as a
    // dependency: this answers the address, and must not fire a second time because the language
    // it has just set is now different from the one it read.
    const showing = i18n.resolvedLanguage?.startsWith('ro') ? 'ro' : 'en';
    if (offered === false) {
      // The installation does not offer the choice: an address naming a language says nothing, a
      // choice made on a published page before the installation decided says nothing, and the
      // page is in the language it would have been in had neither existed.
      if (showing !== settled.current) {
        void i18n.changeLanguage(settled.current);
      }
      return;
    }
    const wanted = asked ?? chosenOnAPublishedPage();
    if (wanted !== null && wanted !== showing) {
      void i18n.changeLanguage(wanted);
    }
  }, [asked, i18n, offered]);

  return {
    language,
    other: language === 'ro' ? 'en' : 'ro',
    offered: offered !== false,
    choose: (chosen) => {
      void i18n.changeLanguage(chosen);
      rememberPublishedChoice(chosen);
      const next = new URLSearchParams(search);
      next.set(PUBLIC_TRIP_LANGUAGE_PARAM, chosen);
      setSearch(next, { replace: true });
    },
  };
}
