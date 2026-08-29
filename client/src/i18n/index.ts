// SPDX-License-Identifier: AGPL-3.0-or-later
import dayjs from 'dayjs';
// The date pickers draw their weekday initials, their month names and — the one nobody looks for
// — the day a week starts on out of dayjs, not out of antd's locale bundle. dayjs ships only
// English and registers nothing else unless it is imported, and asking it for a locale it does
// not hold fails by returning the one it has: no warning, no error, an English Sunday-first week
// under a Romanian interface. English is the built-in and needs no import; every other language
// the application speaks needs its line here.
import 'dayjs/locale/ro';
import i18n from 'i18next';
import LanguageDetector from 'i18next-browser-languagedetector';
import { initReactI18next } from 'react-i18next';
import en from './locales/en.json';
import ro from './locales/ro.json';
import { CHOICE_KEY } from './languageStorage.ts';

// EN and RO are maintained together; the key-parity test enforces it.
void i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources: {
      en: { translation: en },
      ro: { translation: ro },
    },
    supportedLngs: ['ro', 'en'],
    // Romanian is what this application opens in. English is reached by asking for it — from
    // the switch in the header, or from the language stored against the account — not by
    // happening to run in a browser configured in English, which is most of them.
    fallbackLng: 'ro',
    detection: {
      // The one key a deliberate choice is recorded under, and the same one the switch writes:
      // the detector's default order would consult the browser's own languages and settle on
      // English before anybody had chosen anything, which is the whole of what this replaces.
      // Named from that module rather than repeated, because the two disagreeing is silent —
      // every visit would re-detect and the choice made last time would never be found.
      order: ['localStorage'],
      lookupLocalStorage: CHOICE_KEY,
      // The detector's own `i18nextLng` cache is deliberately not written. It would record
      // what was *detected* rather than what was chosen, and the account-adoption rule reads
      // the absence of a choice to decide whether it may act.
      caches: [],
    },
    interpolation: { escapeValue: false },
  });

// dayjs keeps one default locale for the whole module, and it is what anything formatting a date
// outside a picker reads. Kept in step with the language i18next settled on, so the two cannot
// disagree about which language the page is in.
function followLanguage(): void {
  dayjs.locale(i18n.resolvedLanguage?.startsWith('ro') ? 'ro' : 'en');
}

followLanguage();
i18n.on('languageChanged', followLanguage);

export default i18n;
