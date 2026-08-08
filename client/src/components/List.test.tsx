// SPDX-License-Identifier: AGPL-3.0-or-later
import { Button } from 'antd';
import { cleanup, render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import '../i18n';
import List from './List.tsx';

afterEach(cleanup);

interface Row {
  id: string;
  name: string;
}

const rows: Row[] = [
  { id: 'a', name: 'Peștera Demo Mare' },
  { id: 'b', name: 'Avenul Negru' },
];

describe('List', () => {
  it('draws a row per record, in order, as a real list', () => {
    render(
      <List
        dataSource={rows}
        renderItem={(row) => <List.Item data-testid={`row-${row.id}`}>{row.name}</List.Item>}
      />,
    );

    // A list of list items, not a stack of divs: it is what a screen reader announces as one,
    // and what a test can ask for by role instead of by class.
    const items = screen.getAllByRole('listitem');
    expect(items).toHaveLength(2);
    expect(items.map((item) => item.textContent)).toEqual(['Peștera Demo Mare', 'Avenul Negru']);

    // Anything a caller puts on a row reaches the row itself, which is how a row is marked
    // for a test that needs to find one among many.
    expect(screen.getByTestId('row-a')).toBe(items[0]);
  });

  it('keeps a row\'s controls with the row, and reachable by name', () => {
    render(
      <List
        dataSource={rows}
        renderItem={(row) => (
          <List.Item
            actions={[
              <Button key="open">Open {row.name}</Button>,
              <Button key="delete">Delete {row.name}</Button>,
            ]}
          >
            {row.name}
          </List.Item>
        )}
      />,
    );

    const first = screen.getAllByRole('listitem')[0];
    expect(within(first).getByRole('button', { name: 'Open Peștera Demo Mare' })).toBeInTheDocument();
    // The controls belong to their own row and to no other, which is the property every
    // "find the row, then press its button" flow rests on.
    expect(within(first).queryByRole('button', { name: /Avenul Negru/ })).not.toBeInTheDocument();
  });

  it('carries an icon, a title and a quieter line under it', () => {
    render(
      <List
        dataSource={[rows[0]]}
        renderItem={(row) => (
          <List.Item>
            <List.Item.Meta
              avatar={<span>icon</span>}
              title={row.name}
              description="12 MB · 2026-03-12"
            />
          </List.Item>
        )}
      />,
    );

    expect(screen.getByText('Peștera Demo Mare')).toBeInTheDocument();
    expect(screen.getByText('12 MB · 2026-03-12')).toBeInTheDocument();
    expect(screen.getByText('icon')).toBeInTheDocument();
  });

  /**
   * Most callers hand the rows straight from a query, where "there are none" and "they have
   * not arrived" are the same absent value — so a list must draw before its rows exist.
   *
   * This is here because it did not, and the cost was not theoretical: eleven pages threw
   * during render on their first paint, the router's boundary caught it, and the workspace
   * showed an error screen instead of a map. The type said the rows were an array and the
   * compiler was satisfied, because the query hooks these come from are not typed tightly
   * enough to disagree — so nothing but a test that renders it empty can hold this.
   */
  it('draws before its rows have arrived', () => {
    render(<List dataSource={undefined} renderItem={() => null} locale={{ emptyText: ' ' }} />);
    expect(screen.queryAllByRole('listitem')).toHaveLength(0);
  });

  it('says an empty list is empty, in whatever way its caller wants said', () => {
    const { rerender } = render(<List dataSource={[]} renderItem={() => null} />);
    // Nothing was passed, so it says so itself rather than leaving a blank where rows go.
    expect(screen.getAllByText('No data').length).toBeGreaterThan(0);
    expect(screen.queryAllByRole('listitem')).toHaveLength(0);

    rerender(
      <List dataSource={[]} renderItem={() => null} locale={{ emptyText: 'Nothing saved yet' }} />,
    );
    expect(screen.getByText('Nothing saved yet')).toBeInTheDocument();
    expect(screen.queryAllByText('No data')).toHaveLength(0);

    // A blank string is how a caller says nothing at all — a panel where an empty state would
    // be more clutter than information.
    rerender(<List dataSource={[]} renderItem={() => null} locale={{ emptyText: ' ' }} />);
    expect(screen.queryAllByText('No data')).toHaveLength(0);
  });
});
