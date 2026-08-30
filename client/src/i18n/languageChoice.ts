// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef } from 'react';
import { App } from 'antd';
import { useTranslation } from 'react-i18next';
import { useMe, useUpdateLocale } from '../api/hooks.ts';
import { chosenHere, rememberChoice } from './languageStorage.ts';

/** The browser's IANA zone name, or nothing when it will not say. */
function browserTimeZone(): string | null {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || null;
  } catch {
    return null;
  }
}

/**
 * The language switch, wherever it is drawn.
 *
 * Repainting the interface and telling the server are two different things, and only the second
 * one reaches the messages this account is sent: the language is stored against the account and
 * read when a notification is written, so a choice that never left the browser meant every
 * message was English no matter what the reader saw on screen. The switch repaints immediately
 * and saves in the background — waiting for a round trip before changing the visible language
 * would feel broken — but a save that fails is said out loud, because the only symptom otherwise
 * is mail arriving in the wrong language weeks later.
 */
export function useLanguageChoice() {
  const { t, i18n } = useTranslation();
  const { message } = App.useApp();
  const { data: me } = useMe();
  const save = useUpdateLocale();

  /**
   * Which account has already been offered its stored language in this tab. Held per account
   * rather than as a single flag, so a second person signing in to the same tab is offered
   * theirs instead of being skipped.
   */
  const adoptedFor = useRef<string | null>(null);

  useEffect(() => {
    // Adopt the account's language once, and only in a browser where nobody has picked one —
    // otherwise signing in on a shared machine would silently change its language.
    if (!me?.locale || adoptedFor.current === me.id || chosenHere()) {
      return;
    }

    adoptedFor.current = me.id;
    if (me.locale !== i18n.resolvedLanguage) {
      void i18n.changeLanguage(me.locale);
    }
  }, [me?.id, me?.locale, i18n]);

  return {
    language: i18n.resolvedLanguage,
    choose: (language: string) => {
      void i18n.changeLanguage(language);
      rememberChoice(language);
      // The zone rides along because this is the one moment the browser volunteers it, and a
      // failure here must not undo a language the reader can already see.
      save.mutate(
        { language, timeZone: browserTimeZone() },
        { onError: () => message.error(t('common.languageSaveFailed')) },
      );
    },
  };
}
