// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';

/**
 * How often a page that words moments as gaps is drawn again when nothing else redraws it.
 *
 * Half a minute, because the smallest unit such a page goes on showing is the minute: a figure is
 * then never more than thirty seconds behind the one a fresh render would print, and a phone left
 * open on a table all night is woken twice a minute rather than every second.
 */
export const NOW_TICK_MS = 30_000;

/**
 * The present moment, for a page that says "twenty minutes ago" and has to go on being right.
 *
 * <b>A gap worded once is true for a minute and wrong from then on.</b> A page that takes the time
 * when it renders is redrawn only when something else changes — a read landing, a press — so on a
 * trip that is read once and never again "12 minutes ago" stands all night, under the eyes of
 * somebody who is waiting for exactly that figure to move. This is the one thing that redraws such
 * a page by itself.
 *
 * <b>One per page, handed down.</b> Every gap on a page must be measured from the same instant, or
 * two figures drawn in one render disagree by however long it took to draw the first; and one
 * interval is what a phone's battery is asked for, however many gaps the page prints.
 *
 * <b>The clock is read at every render, not only at a tick.</b> What is returned is the time now,
 * whatever caused the render. A value stored at the last tick would be up to half a minute old
 * when a read lands in between, and a report made five seconds ago would then be measured from
 * before it was made — worded as something that is going to happen.
 *
 * <b>Stopped while nobody can see the page.</b> A tab in the background has no reader, and a
 * timer there is battery spent drawing for nobody. It is caught up the moment the page is looked
 * at again, before the next interval, because that first glance is the one the figure is for.
 */
export function useNow(intervalMs: number = NOW_TICK_MS): number {
  const [, setTicks] = useState(0);

  useEffect(() => {
    let timer: ReturnType<typeof setInterval> | undefined;
    const tick = () => setTicks((count) => count + 1);
    const start = () => {
      if (timer === undefined) {
        timer = setInterval(tick, intervalMs);
      }
    };
    const stop = () => {
      if (timer !== undefined) {
        clearInterval(timer);
        timer = undefined;
      }
    };
    const onVisibility = () => {
      if (document.hidden) {
        stop();
      } else {
        tick();
        start();
      }
    };

    if (!document.hidden) {
      start();
    }
    document.addEventListener('visibilitychange', onVisibility);
    return () => {
      stop();
      document.removeEventListener('visibilitychange', onVisibility);
    };
  }, [intervalMs]);

  return Date.now();
}
