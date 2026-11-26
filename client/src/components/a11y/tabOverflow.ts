// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';

/** The tab bar's "more" button, drawn when its tabs do not fit the width. */
const MORE_BUTTONS = '.ant-tabs-nav-more';

/**
 * Takes a tab bar's "more" button out of the accessibility tree and the tab order.
 *
 * When a bar's tabs overflow its width the tab library draws a button that drops the hidden tabs
 * down as a list, and draws it inside the element that holds the tabs. That element says it holds
 * tabs and nothing else, so a screen reader meets a button where only tabs may be, and the bar is
 * announced as broken. The button is a pointer's way to the hidden tabs; the keyboard's way is
 * the arrow keys, which move through every tab, hidden ones included, and scroll each into view.
 * So the button stays clickable and is withdrawn only from what a keyboard or screen reader uses.
 */
export function markTabOverflowButtons(root: ParentNode): void {
  for (const button of Array.from(root.querySelectorAll<HTMLElement>(MORE_BUTTONS))) {
    if (button.getAttribute('aria-hidden') !== 'true') button.setAttribute('aria-hidden', 'true');
    if (button.getAttribute('tabindex') !== '-1') button.setAttribute('tabindex', '-1');
  }
}

/**
 * Keeps every tab bar's "more" button withdrawn, for every tab bar the application draws.
 *
 * Mounted once at the root: the button is drawn by the library with no way to pass it attributes,
 * and appears whenever a bar starts to overflow, so the check is re-run after the document
 * changes, at most once per frame.
 */
export function useTabOverflowButtonsWithdrawn(): void {
  useEffect(() => {
    if (typeof MutationObserver !== 'function') return;
    let frame = 0;
    const schedule = () => {
      if (frame) return;
      frame = requestAnimationFrame(() => {
        frame = 0;
        markTabOverflowButtons(document);
      });
    };
    const observer = new MutationObserver(schedule);
    observer.observe(document.body, { childList: true, subtree: true });
    schedule();
    return () => {
      observer.disconnect();
      if (frame) cancelAnimationFrame(frame);
    };
  }, []);
}
