// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import i18n from '../../i18n';
import en from '../../i18n/locales/en.json';
import ro from '../../i18n/locales/ro.json';
import { ApiError } from '../../api/client.ts';
import { CHECKLIST_PROBLEM_MESSAGE_KEYS, checklistProblem } from './checklistProblems.ts';

function lookup(document: unknown, key: string): unknown {
  return key.split('.').reduce<unknown>(
    (node, part) => (node && typeof node === 'object' ? (node as Record<string, unknown>)[part] : undefined),
    document,
  );
}

describe('checklistProblem', () => {
  it('has a non-empty entry for every key in both locales', () => {
    for (const key of Object.values(CHECKLIST_PROBLEM_MESSAGE_KEYS)) {
      for (const [language, document] of [['en', en], ['ro', ro]] as const) {
        const words = lookup(document, key);
        expect(typeof words, `${language} ${key}`).toBe('string');
        expect((words as string).trim(), `${language} ${key}`).not.toBe('');
      }
    }
  });

  it('gives each refusal it names words of its own, and anything else the general failure', () => {
    const t = i18n.getFixedT('en');
    const seen = new Set<string>();
    for (const code of Object.keys(CHECKLIST_PROBLEM_MESSAGE_KEYS)) {
      const words = checklistProblem(new ApiError(403, code), t);
      expect(words, code).not.toBe(t('checklists.saveFailed'));
      seen.add(words);
    }
    expect(seen.size).toBe(Object.keys(CHECKLIST_PROBLEM_MESSAGE_KEYS).length);
    expect(checklistProblem(new ApiError(400, 'something.else'), t)).toBe(t('checklists.saveFailed'));
    expect(checklistProblem(new TypeError('offline'), t)).toBe(t('checklists.saveFailed'));
  });
});
