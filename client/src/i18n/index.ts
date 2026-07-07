// SPDX-License-Identifier: AGPL-3.0-or-later
import i18n from 'i18next';
import LanguageDetector from 'i18next-browser-languagedetector';
import { initReactI18next } from 'react-i18next';
import en from './locales/en.json';
import ro from './locales/ro.json';

// EN and RO are maintained together (ADR-016); the key-parity test enforces it.
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

export default i18n;
