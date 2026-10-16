// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useId } from 'react';
import { matchesCombination, useShortcutRegistry, type DescribedShortcut } from './shortcutRegistry.ts';

/**
 * A document-level shortcut. What it does is described as well as done — `labelKey` is required —
 * because the shortcut help is read from what this hook has bound: a binding cannot be added
 * without also being written down.
 */
export interface Shortcut extends DescribedShortcut {
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
  const id = useId();

  useEffect(() => {
    if (!enabled) {
      return;
    }
    const { register, unregister } = useShortcutRegistry.getState();
    register(id, shortcuts.map(({ run: _run, ...described }) => described));
    return () => unregister(id);
  }, [id, shortcuts, enabled]);

  useEffect(() => {
    if (!enabled) {
      return;
    }

    const onKeyDown = (event: KeyboardEvent) => {
      if (isTyping(event.target) || hasOpenDialog()) {
        return;
      }

      const match = shortcuts.find((shortcut) => matchesCombination(shortcut, event));

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
 * Whether a modal, a drawer or a dropdown menu is open.
 *
 * Escape in particular belongs to whatever is on top: closing a dialog — or a menu — and
 * deselecting the object behind it with the same press is the kind of thing that loses somebody's
 * work.
 */
function hasOpenDialog(): boolean {
  return (
    document.querySelector(
      '.ant-modal-wrap:not([style*="display: none"]), .ant-drawer-open, .ant-dropdown:not(.ant-dropdown-hidden)',
    ) !== null
  );
}
