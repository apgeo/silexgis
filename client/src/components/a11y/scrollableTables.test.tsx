// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, cleanup, render } from '@testing-library/react';
import { Table } from 'antd';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { markScrollableTable, markScrollableTables, useScrollableTablesReachable } from './scrollableTables.ts';

const NAME = 'Table. Scroll sideways.';
const numbered = (position: number, count: number) => `Table ${position} of ${count}.`;

/** jsdom lays nothing out, so a scroller's sizes are set by hand: wider content than box = scrolls. */
function sized(element: HTMLElement, content: number, box: number): HTMLElement {
  Object.defineProperty(element, 'scrollWidth', { configurable: true, value: content });
  Object.defineProperty(element, 'clientWidth', { configurable: true, value: box });
  Object.defineProperty(element, 'scrollHeight', { configurable: true, value: 10 });
  Object.defineProperty(element, 'clientHeight', { configurable: true, value: 10 });
  return element;
}

afterEach(cleanup);

describe('a table scroller', () => {
  it('gets a tab stop and a name while it scrolls, and loses both once it fits', () => {
    const scroller = sized(document.createElement('div'), 900, 360);

    markScrollableTable(scroller, NAME);
    expect(scroller.tabIndex).toBe(0);
    expect(scroller.getAttribute('role')).toBe('region');
    expect(scroller.getAttribute('aria-label')).toBe(NAME);

    sized(scroller, 360, 360);
    markScrollableTable(scroller, NAME);
    expect(scroller.hasAttribute('tabindex')).toBe(false);
    expect(scroller.hasAttribute('role')).toBe(false);
    expect(scroller.hasAttribute('aria-label')).toBe(false);
  });

  it('adds nothing to a scroller that fits, so a desktop tab order is unchanged', () => {
    const scroller = sized(document.createElement('div'), 600, 1200);

    markScrollableTable(scroller, NAME);
    expect(scroller.hasAttribute('tabindex')).toBe(false);
    expect(scroller.hasAttribute('role')).toBe(false);
  });

  it('leaves a tabindex the page set itself alone, and does not take it away later', () => {
    const scroller = sized(document.createElement('div'), 900, 360);
    scroller.tabIndex = -1;

    markScrollableTable(scroller, NAME);
    expect(scroller.tabIndex).toBe(-1);
    expect(scroller.hasAttribute('role')).toBe(false);

    sized(scroller, 360, 360);
    markScrollableTable(scroller, NAME);
    expect(scroller.tabIndex).toBe(-1);
  });
});

describe('several tables on one page', () => {
  it('names each scrolling table by its place, so no two regions share a name', () => {
    const page = document.createElement('div');
    const first = sized(document.createElement('div'), 900, 360);
    const fits = sized(document.createElement('div'), 300, 360);
    const second = sized(document.createElement('div'), 900, 360);
    for (const scroller of [first, fits, second]) {
      scroller.className = 'ant-table-content';
      page.appendChild(scroller);
    }

    markScrollableTables(page, NAME, numbered);
    expect(first.getAttribute('aria-label')).toBe('Table 1 of 2.');
    expect(second.getAttribute('aria-label')).toBe('Table 2 of 2.');
    expect(fits.hasAttribute('aria-label')).toBe(false);

    // Once only one still scrolls, it goes back to the plain name.
    sized(second, 300, 360);
    markScrollableTables(page, NAME, numbered);
    expect(first.getAttribute('aria-label')).toBe(NAME);
    expect(second.hasAttribute('aria-label')).toBe(false);
  });
});

describe('every table the application draws', () => {
  function Mounted() {
    useScrollableTablesReachable(NAME, numbered);
    return (
      <Table
        rowKey="id"
        scroll={{ x: 'max-content' }}
        columns={[{ title: 'Name', dataIndex: 'name' }]}
        dataSource={[]}
      />
    );
  }

  it('is marked once drawn, through the one hook mounted at the root', async () => {
    vi.useFakeTimers({ toFake: ['requestAnimationFrame'] });
    try {
      // Any scroller drawn from now on is wider than its box, as an empty wide table is on a phone.
      const width = vi.spyOn(HTMLElement.prototype, 'scrollWidth', 'get').mockReturnValue(900);
      const box = vi.spyOn(HTMLElement.prototype, 'clientWidth', 'get').mockReturnValue(360);
      const { container } = render(<Mounted />);
      await act(async () => {
        vi.advanceTimersToNextFrame();
      });

      const scroller = container.querySelector<HTMLElement>('.ant-table-content');
      expect(scroller).not.toBeNull();
      expect(scroller!.tabIndex).toBe(0);
      expect(scroller).toHaveAccessibleName(NAME);
      width.mockRestore();
      box.mockRestore();
    } finally {
      vi.useRealTimers();
    }
  });
});
