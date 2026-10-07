// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it } from 'vitest';
import i18n from './index.ts';

/**
 * The page's markup is shipped saying one language and the application opens in another, so the
 * attribute is only ever right if something keeps it so. Nothing on screen shows when it is
 * wrong: the symptom is a screen reader pronouncing Romanian with an English voice, and a
 * browser offering to translate a page out of the language it is not in.
 */
describe('the language the document says it is in', () => {
  afterEach(async () => {
    await i18n.changeLanguage('en');
  });

  it('follows the language the interface settled on, on every change', async () => {
    await i18n.changeLanguage('ro');
    expect(document.documentElement.lang).toBe('ro');

    await i18n.changeLanguage('en');
    expect(document.documentElement.lang).toBe('en');
  });
});
