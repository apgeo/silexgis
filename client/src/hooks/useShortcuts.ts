// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';

export interface Shortcut {
  /** `KeyboardEvent.key`, compared case-insensitively. */
  key: string;
  alt?: boolean;
  ctrl?: boolean;
  shift?: boolean;
  run: () => void;
}

/**
 * Document-level keyboard shortcuts for a screen.
 *
 * One listener rather than handlers scattered over the components that care, because the two
 * things that make shortcuts go wrong are global and cannot be decided locally: whether the person
 * is typing, and whether something modal is open. A shortcut that fires while somebody is naming a
 * cave eats the letter; one that fires while a dialog is up acts on the screen behind it.
 */
export function useShortcuts(shortcuts: Shortcut[], enabled = true) {
  useEffect(() => {
    if (!enabled) {
      return;
    }

    const onKeyDown = (event: KeyboardEvent) => {
      if (isTyping(event.target) || hasOpenDialog()) {
        return;
      }

      const match = shortcuts.find(
        (shortcut) =>
          shortcut.key.toLowerCase() === event.key.toLowerCase() &&
          Boolean(shortcut.alt) === event.altKey &&
          Boolean(shortcut.ctrl) === (event.ctrlKey || event.metaKey) &&
          Boolean(shortcut.shift) === event.shiftKey,
      );

      if (match) {
        event.preventDefault();
        match.run();
      }
    };

    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [shortcuts, enabled]);
}

/** Whether the event came from somewhere a keystroke means a character. */
function isTyping(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) {
    return false;
  }

  const tag = target.tagName;
  return (
    tag === 'INPUT' ||
    tag === 'TEXTAREA' ||
    tag === 'SELECT' ||
    target.isContentEditable ||
    // A combo box is a div that behaves like a field; its own keys must reach it.
    target.closest('.ant-select, .ant-picker, .ant-input-number') !== null
  );
}

/**
 * Whether a modal or drawer is open.
 *
 * Escape in particular belongs to whatever is on top: closing a dialog and deselecting the object
 * behind it with the same press is the kind of thing that loses somebody's work.
 */
function hasOpenDialog(): boolean {
  return document.querySelector('.ant-modal-wrap:not([style*="display: none"]), .ant-drawer-open') !== null;
}
