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
 * handle, inside the photo viewer — rather than through the document-level hook. Each handler
 * matches its keys against the declaration here (`matchesAny`), so the key the help lists and the
 * key that works are one value: changing either changes both.
 *
 * The map's arrows and +/− are OpenLayers' own keyboard interactions, which listen on the map's
 * element and so act only while it has focus; they are declared here without a handler of ours,
 * because the library binds them. The menu keys are ours (the map's context menu module).
 */
export const MAP_FOCUSED_SHORTCUTS = {
  pan: [
    { key: 'ArrowUp', labelKey: 'shortcuts.action.pan' },
    { key: 'ArrowDown', labelKey: 'shortcuts.action.pan' },
    { key: 'ArrowLeft', labelKey: 'shortcuts.action.pan' },
    { key: 'ArrowRight', labelKey: 'shortcuts.action.pan' },
  ],
  zoomIn: [{ key: '+', labelKey: 'shortcuts.action.zoomIn' }],
  zoomOut: [{ key: '-', labelKey: 'shortcuts.action.zoomOut' }],
  contextMenu: [
    { key: 'F10', shift: true, labelKey: 'shortcuts.action.contextMenu' },
    { key: 'ContextMenu', labelKey: 'shortcuts.action.contextMenu' },
  ],
} satisfies Record<string, DescribedShortcut[]>;

/** A panel section's drag handle: the keyboard's way of doing what dragging it does. */
export const PANEL_HANDLE_SHORTCUTS = {
  moveUp: [{ key: 'ArrowUp', alt: true, labelKey: 'shortcuts.action.moveSectionUp' }],
  moveDown: [{ key: 'ArrowDown', alt: true, labelKey: 'shortcuts.action.moveSectionDown' }],
} satisfies Record<string, DescribedShortcut[]>;

/** The 3D scene's selection card. */
export const SCENE3D_CALLOUT_SHORTCUTS = {
  dismiss: [{ key: 'Escape', labelKey: 'shortcuts.action.dismissCallout' }],
} satisfies Record<string, DescribedShortcut[]>;

/** The full-screen photo viewer, which listens while it is open. */
export const LIGHTBOX_SHORTCUTS = {
  close: [{ key: 'Escape', labelKey: 'shortcuts.action.closeViewer' }],
  previous: [{ key: 'ArrowLeft', labelKey: 'shortcuts.action.previousPhoto' }],
  next: [{ key: 'ArrowRight', labelKey: 'shortcuts.action.nextPhoto' }],
  // `=` as well as `+`: on many keyboards `+` is Shift+=, and the unshifted key is the one a
  // finger finds while the other hand is on the mouse.
  zoomIn: [
    { key: '+', labelKey: 'shortcuts.action.zoomInPhoto' },
    { key: '=', labelKey: 'shortcuts.action.zoomInPhoto' },
  ],
  zoomOut: [{ key: '-', labelKey: 'shortcuts.action.zoomOutPhoto' }],
  reset: [{ key: '0', labelKey: 'shortcuts.action.resetPhoto' }],
} satisfies Record<string, DescribedShortcut[]>;

/** A marked passage of an annotated text, which is a link and is followed the way one is. */
export const PASSAGE_SHORTCUTS = {
  follow: [
    { key: 'Enter', labelKey: 'shortcuts.action.followPassage' },
    { key: ' ', labelKey: 'shortcuts.action.followPassage' },
  ],
} satisfies Record<string, DescribedShortcut[]>;

/** Every focused-element group, as the help lists them: what has focus, then its keys. */
export const FOCUSED_SHORTCUTS: { contextKey: string; shortcuts: DescribedShortcut[] }[] = [
  { contextKey: 'shortcuts.context.map', shortcuts: Object.values(MAP_FOCUSED_SHORTCUTS).flat() },
  { contextKey: 'shortcuts.context.panelHandle', shortcuts: Object.values(PANEL_HANDLE_SHORTCUTS).flat() },
  { contextKey: 'shortcuts.context.scene3dCallout', shortcuts: Object.values(SCENE3D_CALLOUT_SHORTCUTS).flat() },
  { contextKey: 'shortcuts.context.lightbox', shortcuts: Object.values(LIGHTBOX_SHORTCUTS).flat() },
  { contextKey: 'shortcuts.context.passage', shortcuts: Object.values(PASSAGE_SHORTCUTS).flat() },
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

/** What a key event says about the key pressed — the DOM's and React's events both carry it. */
export type PressedKey = Pick<KeyboardEvent, 'key' | 'altKey' | 'ctrlKey' | 'metaKey' | 'shiftKey'>;

/**
 * Whether a key event is this combination.
 *
 * Shift is compared only for letters and named keys. A printed symbol already says whether Shift
 * was needed to type it — `?` is Shift+/ on one layout and a key of its own on another — so
 * demanding a particular Shift state for one would make the shortcut work on some keyboards only.
 */
export function matchesCombination(combination: KeyCombination, event: PressedKey): boolean {
  const symbol = combination.key.length === 1 && combination.key.toLowerCase() === combination.key.toUpperCase();
  return (
    combination.key.toLowerCase() === event.key.toLowerCase() &&
    Boolean(combination.alt) === event.altKey &&
    Boolean(combination.ctrl) === (event.ctrlKey || event.metaKey) &&
    (symbol || Boolean(combination.shift) === event.shiftKey)
  );
}

/** Whether a key event is any of an action's combinations. */
export function matchesAny(combinations: readonly KeyCombination[], event: PressedKey): boolean {
  return combinations.some((combination) => matchesCombination(combination, event));
}

/**
 * Whether this is an Apple keyboard, on which a Ctrl shortcut is pressed with ⌘ and Alt is the key
 * printed Option — `matchesCombination` already takes Cmd for Ctrl, so the help has to say so.
 */
export function isApplePlatform(): boolean {
  if (typeof navigator === 'undefined') {
    return false;
  }
  const hinted = (navigator as Navigator & { userAgentData?: { platform?: string } }).userAgentData?.platform;
  return /mac|iphone|ipad|ipod/i.test(hinted ?? navigator.platform ?? '');
}

/** Keys printed as a symbol, the same in every language. */
const KEY_SYMBOLS: Record<string, string> = {
  ArrowUp: '↑',
  ArrowDown: '↓',
  ArrowLeft: '←',
  ArrowRight: '→',
};

/** Keys printed as a word, which is the reader's language's word for it. */
const KEY_WORDS: Record<string, string> = {
  ContextMenu: 'shortcuts.keyCap.menu',
  Escape: 'shortcuts.keyCap.escape',
  Enter: 'shortcuts.keyCap.enter',
  ' ': 'shortcuts.keyCap.space',
};

/** The key caps a combination is pressed with, in the order they are held down. */
export function keyCaps(
  combination: KeyCombination,
  t: (key: string) => string,
  apple: boolean = isApplePlatform(),
): string[] {
  const word = KEY_WORDS[combination.key];
  return [
    ...(combination.ctrl ? [t(apple ? 'shortcuts.keyCap.command' : 'shortcuts.keyCap.ctrl')] : []),
    ...(combination.alt ? [t(apple ? 'shortcuts.keyCap.option' : 'shortcuts.keyCap.alt')] : []),
    ...(combination.shift ? [t('shortcuts.keyCap.shift')] : []),
    word
      ? t(word)
      : KEY_SYMBOLS[combination.key] ??
        (combination.key.length === 1 ? combination.key.toUpperCase() : combination.key),
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
export function shortcutRows(
  shortcuts: DescribedShortcut[],
  capsOf: (combination: KeyCombination) => string[],
): ShortcutRow[] {
  const rows: ShortcutRow[] = [];
  for (const shortcut of shortcuts) {
    const caps = capsOf(shortcut);
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
