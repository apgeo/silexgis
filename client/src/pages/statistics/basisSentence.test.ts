// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import i18n from '../../i18n';
import en from '../../i18n/locales/en.json';
import ro from '../../i18n/locales/ro.json';
import { basisSentence } from './basisSentence';

describe('basisSentence', () => {
  const kinds = ['readable', 'placeable', 'area'] as const;

  it('has a sentence in both languages for every kind the server names', () => {
    for (const kind of kinds) {
      expect(en.registryStats.basis[kind]).toBeTruthy();
      expect(ro.registryStats.basis[kind]).toBeTruthy();
      expect(ro.registryStats.basis[kind]).not.toBe(en.registryStats.basis[kind]);
    }
  });

  it('shows the sentence in the reader language, not the server text', async () => {
    await i18n.changeLanguage('ro');
    try {
      expect(basisSentence(i18n.t, 'area', 'English from the server')).toBe(ro.registryStats.basis.area);
    } finally {
      await i18n.changeLanguage('en');
    }
  });

  it('falls back to the server text for a kind this build does not know, or none', () => {
    expect(basisSentence(i18n.t, 'something-new', 'English from the server')).toBe('English from the server');
    expect(basisSentence(i18n.t, undefined, 'English from the server')).toBe('English from the server');
  });
});
