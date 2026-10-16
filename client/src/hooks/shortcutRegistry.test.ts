// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import en from '../i18n/locales/en.json';
import ro from '../i18n/locales/ro.json';
import i18n from '../i18n';
import {
  FOCUSED_SHORTCUTS,
  LIGHTBOX_SHORTCUTS,
  MAP_WORKSPACE_SHORTCUTS,
  PANEL_HANDLE_SHORTCUTS,
  keyCaps,
  matchesAny,
  matchesCombination,
  shortcutRows,
  type KeyCombination,
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
  const en = i18n.getFixedT('en');
  const ro = i18n.getFixedT('ro');
  const pc = (combination: KeyCombination) => keyCaps(combination, en, false);

  it('names keys the way a keyboard prints them, modifiers first', () => {
    expect(pc({ key: 'ArrowLeft', alt: true })).toEqual(['Alt', '←']);
    expect(pc({ key: 'Escape' })).toEqual(['Esc']);
    expect(pc({ key: '?' })).toEqual(['?']);
    expect(pc({ key: 's', ctrl: true })).toEqual(['Ctrl', 'S']);
  });

  it("names the keys that are words in the reader's language", () => {
    expect(keyCaps({ key: ' ' }, en, false)).toEqual(['Space']);
    expect(keyCaps({ key: ' ' }, ro, false)).toEqual(['Spațiu']);
  });

  it('shows Cmd and Option on an Apple keyboard, where Cmd is what a Ctrl shortcut answers to', () => {
    expect(keyCaps({ key: 's', ctrl: true }, en, true)).toEqual(['⌘ Cmd', 'S']);
    expect(keyCaps({ key: 'ArrowUp', alt: true }, en, true)).toEqual(['⌥ Option', '↑']);
  });

  it('folds the combinations of one action into one line, in first-seen order', () => {
    const rows = shortcutRows(
      [
        { key: 'ArrowUp', labelKey: 'pan' },
        { key: '+', labelKey: 'zoom' },
        { key: 'ArrowDown', labelKey: 'pan' },
        { key: 'ArrowDown', labelKey: 'pan' },
      ],
      pc,
    );
    expect(rows).toEqual([
      { labelKey: 'pan', combinations: [['↑'], ['↓']] },
      { labelKey: 'zoom', combinations: [['+']] },
    ]);
  });
});

describe('focused-element keys are matched against their declaration', () => {
  it("a panel handle moves on Alt+arrow and on nothing else the help doesn't list", () => {
    expect(matchesAny(PANEL_HANDLE_SHORTCUTS.moveUp, press('ArrowUp', { altKey: true }))).toBe(true);
    expect(matchesAny(PANEL_HANDLE_SHORTCUTS.moveUp, press('ArrowUp'))).toBe(false);
    expect(matchesAny(PANEL_HANDLE_SHORTCUTS.moveDown, press('ArrowDown', { altKey: true, ctrlKey: true }))).toBe(false);
  });

  it('the photo viewer zooms in on either of the keys it lists', () => {
    expect(matchesAny(LIGHTBOX_SHORTCUTS.zoomIn, press('+', { shiftKey: true }))).toBe(true);
    expect(matchesAny(LIGHTBOX_SHORTCUTS.zoomIn, press('='))).toBe(true);
    expect(matchesAny(LIGHTBOX_SHORTCUTS.next, press('ArrowRight', { altKey: true }))).toBe(false);
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
      ...['ctrl', 'command', 'alt', 'option', 'shift', 'escape', 'enter', 'space'].map((cap) => `shortcuts.keyCap.${cap}`),
    ];
    for (const key of keys) {
      expect(typeof lookup(en, key), `en ${key}`).toBe('string');
      expect(typeof lookup(ro, key), `ro ${key}`).toBe('string');
    }
  });
});
