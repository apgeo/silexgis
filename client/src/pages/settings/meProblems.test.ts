// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import i18n from '../../i18n';
import en from '../../i18n/locales/en.json';
import ro from '../../i18n/locales/ro.json';
import { ApiError } from '../../api/client.ts';
import { ME_PROBLEM_MESSAGE_KEYS, meProblemMessage } from './meProblems.ts';

/**
 * The codes the account's own profile, address and preferences routes refuse with, as the server declares them.
 * Listed rather than derived, because nothing publishes them; both directions are checked — a code
 * with no wording, and wording for a code nothing raises.
 */
const codes = ['me.caving_club_unknown', 'me.email_resend_too_soon', 'me.email_not_usable', 'preferences.busy'];

function lookup(document: unknown, key: string): unknown {
  return key.split('.').reduce<unknown>(
    (node, part) => (node && typeof node === 'object' ? (node as Record<string, unknown>)[part] : undefined),
    document,
  );
}

describe('meProblemMessage', () => {
  it('names exactly the codes the server raises', () => {
    expect(Object.keys(ME_PROBLEM_MESSAGE_KEYS).sort()).toEqual([...codes].sort());
  });

  it('has a non-empty entry for every key in both locales', () => {
    for (const key of Object.values(ME_PROBLEM_MESSAGE_KEYS)) {
      for (const [language, document] of [['en', en], ['ro', ro]] as const) {
        const words = lookup(document, key);
        expect(typeof words, `${language} ${key}`).toBe('string');
        expect((words as string).trim(), `${language} ${key}`).not.toBe('');
      }
    }
  });

  it('has words of its own for every refusal it names, in both languages', () => {
    for (const language of ['en', 'ro']) {
      const t = i18n.getFixedT(language);
      const seen = new Set<string>();
      for (const code of codes) {
        const words = meProblemMessage(new ApiError(400, code), t);
        expect(words, `${language} ${code}`).not.toBe(t('common.saveFailed'));
        expect(words, `${language} ${code}`).not.toBe(t('settings.emails.requestFailed'));
        expect(words, `${language} ${code}`).not.toMatch(/^settings\./);
        seen.add(words);
      }
      expect(seen.size, language).toBe(codes.length);
    }
  });

  it('answers anything else with the sentence the caller chose', () => {
    const t = i18n.getFixedT('en');
    expect(meProblemMessage(new ApiError(400, 'something.else'), t)).toBe(t('common.saveFailed'));
    expect(meProblemMessage(new Error('offline'), t, 'settings.emails.requestFailed')).toBe(
      t('settings.emails.requestFailed'),
    );
  });
});
