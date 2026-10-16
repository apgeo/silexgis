// SPDX-License-Identifier: AGPL-3.0-or-later
import { create } from 'zustand';

/** A key combination. `key` is a `KeyboardEvent.key`, compared case-insensitively. */
export interface KeyCombination {
  key: string;
  alt?: boolean;
  ctrl?: boolean;
  shift?: boolean;
}

/**
 * A key combination and what it does, as the shortcut help lists it. The label is an i18n key
 * rather than text so that the registry can be declared outside any component and still follow
 * the reader's language.
 */
export interface DescribedShortcut extends KeyCombination {
  labelKey: string;
}

/**
 * Keys this application handles where they are pressed — on the focused map, on a focused panel
 * handle — rather than through the document-level hook. They are listed here, beside the hook's
 * own registrations, because the help dialog is only worth having if it is complete: a key that
 * works but is written down nowhere is a key nobody uses, and the two kinds would otherwise be
 * documented in two places of which one is always forgotten.
 *
 * The map's keys are OpenLayers' own keyboard interactions, which listen on the map's element
 * and so act only while it has focus.
 */
export const FOCUSED_SHORTCUTS: { contextKey: string; shortcuts: DescribedShortcut[] }[] = [
  {
    contextKey: 'shortcuts.context.map',
    shortcuts: [
      { key: 'ArrowUp', labelKey: 'shortcuts.action.pan' },
      { key: 'ArrowDown', labelKey: 'shortcuts.action.pan' },
      { key: 'ArrowLeft', labelKey: 'shortcuts.action.pan' },
      { key: 'ArrowRight', labelKey: 'shortcuts.action.pan' },
      { key: '+', labelKey: 'shortcuts.action.zoomIn' },
      { key: '-', labelKey: 'shortcuts.action.zoomOut' },
    ],
  },
  {
    contextKey: 'shortcuts.context.panelHandle',
    shortcuts: [
      { key: 'ArrowUp', alt: true, labelKey: 'shortcuts.action.moveSectionUp' },
      { key: 'ArrowDown', alt: true, labelKey: 'shortcuts.action.moveSectionDown' },
    ],
  },
  {
    contextKey: 'shortcuts.context.scene3dCallout',
    shortcuts: [{ key: 'Escape', labelKey: 'shortcuts.action.dismissCallout' }],
  },
];

/**
 * The map workspace's document-level shortcuts, without what they do — the page supplies that.
 * Declared here rather than inline in the page so that their wording can be checked in both
 * languages without mounting a map.
 */
export const MAP_WORKSPACE_SHORTCUTS = {
  help: { key: '?', labelKey: 'shortcuts.action.help' },
  clearSelection: { key: 'Escape', labelKey: 'shortcuts.action.clearSelection' },
  toggleLeftDock: { key: '[', labelKey: 'shortcuts.action.toggleLeftDock' },
  toggleRightDock: { key: ']', labelKey: 'shortcuts.action.toggleRightDock' },
  selectionBack: { key: 'ArrowLeft', alt: true, labelKey: 'shortcuts.action.selectionBack' },
  selectionForward: { key: 'ArrowRight', alt: true, labelKey: 'shortcuts.action.selectionForward' },
} satisfies Record<string, DescribedShortcut>;

interface ShortcutRegistryState {
  /** The document-level shortcuts of every mounted screen, by the registering hook's id. */
  bound: Record<string, DescribedShortcut[]>;
  register: (id: string, shortcuts: DescribedShortcut[]) => void;
  unregister: (id: string) => void;
}

/**
 * What the document-level shortcut hook currently has bound. Written only by `useShortcuts`, so
 * the list the help shows is the list that is listening — there is no second declaration of a
 * binding to fall out of step with the first.
 */
export const useShortcutRegistry = create<ShortcutRegistryState>((set) => ({
  bound: {},
  register: (id, shortcuts) => set((state) => ({ bound: { ...state.bound, [id]: shortcuts } })),
  unregister: (id) =>
    set((state) => {
      const { [id]: _removed, ...rest } = state.bound;
      return { bound: rest };
    }),
}));

/**
 * Whether a key event is this combination.
 *
 * Shift is compared only for letters and named keys. A printed symbol already says whether Shift
 * was needed to type it — `?` is Shift+/ on one layout and a key of its own on another — so
 * demanding a particular Shift state for one would make the shortcut work on some keyboards only.
 */
export function matchesCombination(combination: KeyCombination, event: KeyboardEvent): boolean {
  const symbol = combination.key.length === 1 && combination.key.toLowerCase() === combination.key.toUpperCase();
  return (
    combination.key.toLowerCase() === event.key.toLowerCase() &&
    Boolean(combination.alt) === event.altKey &&
    Boolean(combination.ctrl) === (event.ctrlKey || event.metaKey) &&
    (symbol || Boolean(combination.shift) === event.shiftKey)
  );
}

const KEY_NAMES: Record<string, string> = {
  ArrowUp: '↑',
  ArrowDown: '↓',
  ArrowLeft: '←',
  ArrowRight: '→',
  Escape: 'Esc',
  ' ': 'Space',
};

/** The key caps a combination is pressed with, in the order they are held down. */
export function keyCaps(combination: KeyCombination): string[] {
  return [
    ...(combination.ctrl ? ['Ctrl'] : []),
    ...(combination.alt ? ['Alt'] : []),
    ...(combination.shift ? ['Shift'] : []),
    KEY_NAMES[combination.key] ?? (combination.key.length === 1 ? combination.key.toUpperCase() : combination.key),
  ];
}

/** One line of the help: every combination that does the same thing, and what that is. */
export interface ShortcutRow {
  labelKey: string;
  combinations: string[][];
}

/**
 * Folds a list of shortcuts into help lines, one per action, in the order the actions first
 * appear — so the four arrows that pan the map are one line, not four.
 */
export function shortcutRows(shortcuts: DescribedShortcut[]): ShortcutRow[] {
  const rows: ShortcutRow[] = [];
  for (const shortcut of shortcuts) {
    const caps = keyCaps(shortcut);
    const row = rows.find((candidate) => candidate.labelKey === shortcut.labelKey);
    if (row) {
      if (!row.combinations.some((existing) => existing.join('+') === caps.join('+'))) {
        row.combinations.push(caps);
      }
    } else {
      rows.push({ labelKey: shortcut.labelKey, combinations: [caps] });
    }
  }
  return rows;
}
