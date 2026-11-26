// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef } from 'react';

/** The library's own scroller around a table: the whole table, or its body under a fixed header. */
const SCROLLERS = '.ant-table-content, .ant-table-body';
/** Marks the attributes as ours, so they are taken off again only where this module put them. */
const MARK = 'scrollFocus';

/**
 * Gives a table's scroller a tab stop and a name while — and only while — it actually scrolls.
 *
 * A wide table on a phone scrolls sideways inside its own box. A keyboard user reaches the columns
 * past the edge only by focusing something inside that box; a table with no link or button in it
 * (an empty one, or one of plain text) offers nothing to focus, so those columns cannot be reached
 * at all. Focusing the scroller lets the arrow keys scroll it, and a name says what was focused.
 *
 * A scroller that fits gets nothing, so a desktop's tab order through a narrow table is unchanged.
 * A tabindex the page set itself is left alone.
 */
export function markScrollableTable(scroller: HTMLElement, name: string): void {
  const ours = scroller.dataset[MARK] === 'true';
  if (scrolls(scroller)) {
    if (ours) {
      if (scroller.getAttribute('aria-label') !== name) scroller.setAttribute('aria-label', name);
      return;
    }
    if (scroller.hasAttribute('tabindex')) return;
    scroller.tabIndex = 0;
    scroller.setAttribute('role', 'region');
    scroller.setAttribute('aria-label', name);
    scroller.dataset[MARK] = 'true';
  } else if (ours) {
    scroller.removeAttribute('tabindex');
    scroller.removeAttribute('role');
    scroller.removeAttribute('aria-label');
    delete scroller.dataset[MARK];
  }
}

function scrolls(scroller: HTMLElement): boolean {
  return scroller.scrollWidth > scroller.clientWidth || scroller.scrollHeight > scroller.clientHeight;
}

/** Whether the scroller is, or is about to be, one this module marks. */
function takesTabStop(scroller: HTMLElement): boolean {
  return scrolls(scroller) && (scroller.dataset[MARK] === 'true' || !scroller.hasAttribute('tabindex'));
}

/**
 * Marks every scroller under `root`. A named region is a landmark, and two landmarks of one kind
 * with one name cannot be told apart in a screen reader's list of them — so when more than one
 * table on the page scrolls, each is named by its place among them (`numbered(2, 3)`).
 */
export function markScrollableTables(
  root: ParentNode,
  name: string,
  numbered: (position: number, count: number) => string,
): void {
  const scrollers = Array.from(root.querySelectorAll<HTMLElement>(SCROLLERS));
  const marked = scrollers.filter(takesTabStop);
  for (const scroller of scrollers) {
    const position = marked.indexOf(scroller);
    markScrollableTable(scroller, marked.length > 1 && position >= 0 ? numbered(position + 1, marked.length) : name);
  }
}

/**
 * Keeps every table scroller in the document marked, for every table the application draws.
 *
 * Mounted once at the root rather than at each table: the scroller is drawn by the table library
 * with no way to pass it attributes, and a table can become wide after it is drawn (its rows
 * arrive, the window narrows), so the check is re-run after the document changes or is resized,
 * at most once per frame.
 */
export function useScrollableTablesReachable(
  name: string,
  numbered: (position: number, count: number) => string,
): void {
  // The numbering is read when a pass runs, so a new function each render does not re-subscribe.
  const numberedRef = useRef(numbered);
  useEffect(() => {
    numberedRef.current = numbered;
  });
  useEffect(() => {
    if (typeof MutationObserver !== 'function') return;
    let frame = 0;
    const schedule = () => {
      if (frame) return;
      frame = requestAnimationFrame(() => {
        frame = 0;
        markScrollableTables(document, name, (position, count) => numberedRef.current(position, count));
      });
    };
    const observer = new MutationObserver(schedule);
    observer.observe(document.body, { childList: true, subtree: true });
    window.addEventListener('resize', schedule);
    schedule();
    return () => {
      observer.disconnect();
      window.removeEventListener('resize', schedule);
      if (frame) cancelAnimationFrame(frame);
    };
  }, [name]);
}
