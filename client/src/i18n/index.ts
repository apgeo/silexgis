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

// EN and RO are maintained together; the key-parity test enforces it.
void i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources: {
      en: { translation: en },
      ro: { translation: ro },
    },
    fallbackLng: 'en',
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
