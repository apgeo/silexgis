// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import en from '../i18n/locales/en.json';
import ro from '../i18n/locales/ro.json';
import {
  FOCUSED_SHORTCUTS,
  MAP_WORKSPACE_SHORTCUTS,
  keyCaps,
  matchesCombination,
  shortcutRows,
} from './shortcutRegistry.ts';

function press(key: string, modifiers: Partial<Pick<KeyboardEvent, 'altKey' | 'ctrlKey' | 'metaKey' | 'shiftKey'>> = {}) {
  return new KeyboardEvent('keydown', { key, ...modifiers });
}

function lookup(table: unknown, key: string): unknown {
  return key.split('.').reduce<unknown>(
    (node, part) => (node && typeof node === 'object' ? (node as Record<string, unknown>)[part] : undefined),
    table,
  );
}

describe('matchesCombination', () => {
  it('matches a printed symbol whatever Shift was needed to type it', () => {
    // `?` is Shift+/ on a US keyboard and a key of its own elsewhere; both must open the help.
    expect(matchesCombination({ key: '?' }, press('?', { shiftKey: true }))).toBe(true);
    expect(matchesCombination({ key: '?' }, press('?'))).toBe(true);
  });

  it('still tells a letter with Shift from one without', () => {
    expect(matchesCombination({ key: 'k' }, press('K', { shiftKey: true }))).toBe(false);
    expect(matchesCombination({ key: 'k', shift: true }, press('K', { shiftKey: true }))).toBe(true);
  });

  it('keeps Alt and Ctrl exact, so Alt+arrow is not also a plain arrow', () => {
    expect(matchesCombination({ key: 'ArrowLeft', alt: true }, press('ArrowLeft'))).toBe(false);
    expect(matchesCombination({ key: 'ArrowLeft', alt: true }, press('ArrowLeft', { altKey: true }))).toBe(true);
    expect(matchesCombination({ key: '[' }, press('[', { ctrlKey: true }))).toBe(false);
  });

  it('counts Cmd as Ctrl', () => {
    expect(matchesCombination({ key: 's', ctrl: true }, press('s', { metaKey: true }))).toBe(true);
  });
});

describe('keyCaps and shortcutRows', () => {
  it('names keys the way a keyboard prints them, modifiers first', () => {
    expect(keyCaps({ key: 'ArrowLeft', alt: true })).toEqual(['Alt', '←']);
    expect(keyCaps({ key: 'Escape' })).toEqual(['Esc']);
    expect(keyCaps({ key: '?' })).toEqual(['?']);
  });

  it('folds the combinations of one action into one line, in first-seen order', () => {
    const rows = shortcutRows([
      { key: 'ArrowUp', labelKey: 'pan' },
      { key: '+', labelKey: 'zoom' },
      { key: 'ArrowDown', labelKey: 'pan' },
      { key: 'ArrowDown', labelKey: 'pan' },
    ]);
    expect(rows).toEqual([
      { labelKey: 'pan', combinations: [['↑'], ['↓']] },
      { labelKey: 'zoom', combinations: [['+']] },
    ]);
  });
});

describe('the declared shortcuts', () => {
  it('have words in both languages for every context and action they list', () => {
    const keys = [
      ...FOCUSED_SHORTCUTS.flatMap((group) => [
        group.contextKey,
        ...group.shortcuts.map((shortcut) => shortcut.labelKey),
      ]),
      ...Object.values(MAP_WORKSPACE_SHORTCUTS).map((shortcut) => shortcut.labelKey),
      'shortcuts.context.screen',
    ];
    for (const key of keys) {
      expect(typeof lookup(en, key), `en ${key}`).toBe('string');
      expect(typeof lookup(ro, key), `ro ${key}`).toBe('string');
    }
  });
});
