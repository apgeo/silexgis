// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import i18n from '../../i18n';
import en from '../../i18n/locales/en.json';
import ro from '../../i18n/locales/ro.json';
import { ApiError } from '../../api/client.ts';
import {
  CHECKLIST_FAILED_KEYS,
  CHECKLIST_PROBLEM_MESSAGE_KEYS,
  checklistProblem,
} from './checklistProblems.ts';

function lookup(document: unknown, key: string): unknown {
  return key.split('.').reduce<unknown>(
    (node, part) => (node && typeof node === 'object' ? (node as Record<string, unknown>)[part] : undefined),
    document,
  );
}

describe('checklistProblem', () => {
  it('has a non-empty entry for every key in both locales', () => {
    const keys = [...Object.values(CHECKLIST_PROBLEM_MESSAGE_KEYS), ...Object.values(CHECKLIST_FAILED_KEYS)];
    for (const key of keys) {
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
      const words = checklistProblem(new ApiError(403, code), 'save', t);
      expect(words, code).not.toBe(t('checklists.saveFailed'));
      seen.add(words as string);
    }
    expect(seen.size).toBe(Object.keys(CHECKLIST_PROBLEM_MESSAGE_KEYS).length);
    expect(checklistProblem(new ApiError(400, 'something.else'), 'save', t)).toBe(t('checklists.saveFailed'));
    expect(checklistProblem(new TypeError('offline'), 'save', t)).toBe(t('checklists.saveFailed'));
  });

  it('names the act that failed when it has no words for the refusal', () => {
    const t = i18n.getFixedT('en');
    for (const act of ['open', 'save', 'delete'] as const) {
      expect(checklistProblem(new TypeError('offline'), act, t)).toBe(t(CHECKLIST_FAILED_KEYS[act]));
    }
  });

  it('leaves a stale version to the shared conflict prompt', () => {
    const t = i18n.getFixedT('en');
    expect(checklistProblem(new ApiError(412, 'concurrency.version_mismatch'), 'save', t)).toBeNull();
    expect(checklistProblem(new ApiError(412, 'concurrency.version_mismatch'), 'delete', t)).toBeNull();
  });
});
