// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, render } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { markTabOverflowButtons, useTabOverflowButtonsWithdrawn } from './tabOverflow.ts';

afterEach(cleanup);

/** The markup the tab library draws for a bar whose tabs overflow, reduced to what matters here. */
function overflowingBar(): HTMLElement {
  const bar = document.createElement('div');
  bar.setAttribute('role', 'tablist');
  bar.innerHTML =
    '<div class="ant-tabs-nav-wrap"><div class="ant-tabs-nav-list">' +
    '<div class="ant-tabs-tab"><div role="tab" tabindex="0">One</div></div>' +
    '<div class="ant-tabs-tab"><div role="tab" tabindex="-1">Two</div></div>' +
    '</div></div>' +
    '<div class="ant-tabs-nav-operations"><button type="button" class="ant-tabs-nav-more" aria-haspopup="listbox">…</button></div>';
  return bar;
}

describe("a tab bar's overflow button", () => {
  it('is withdrawn from the accessibility tree and the tab order, and the tabs are left alone', () => {
    const bar = overflowingBar();
    const more = bar.querySelector<HTMLElement>('.ant-tabs-nav-more')!;
    // Unmarked, the bar holds something other than tabs: the defect.
    expect(more.hasAttribute('aria-hidden')).toBe(false);

    markTabOverflowButtons(bar);

    expect(more.getAttribute('aria-hidden')).toBe('true');
    expect(more.getAttribute('tabindex')).toBe('-1');
    const tabs = Array.from(bar.querySelectorAll('[role="tab"]'));
    expect(tabs.map((tab) => tab.getAttribute('tabindex'))).toEqual(['0', '-1']);
    expect(tabs.some((tab) => tab.hasAttribute('aria-hidden'))).toBe(false);
  });

  it('is withdrawn when a bar starts to overflow after the page was drawn', async () => {
    vi.useFakeTimers({ toFake: ['requestAnimationFrame', 'cancelAnimationFrame'] });
    try {
      function Probe() {
        useTabOverflowButtonsWithdrawn();
        return null;
      }
      render(<Probe />);
      const bar = overflowingBar();
      document.body.appendChild(bar);
      await act(async () => {
        await Promise.resolve();
        vi.advanceTimersToNextFrame();
      });
      expect(bar.querySelector('.ant-tabs-nav-more')!.getAttribute('aria-hidden')).toBe('true');
      bar.remove();
    } finally {
      vi.useRealTimers();
    }
  });
});
