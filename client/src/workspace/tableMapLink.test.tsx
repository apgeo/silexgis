// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  announceRowHover,
  MAP_PICKED_ROW_CLASS,
  onMapPick,
  onRowHover,
  rowIdsOfSelection,
  useTableMapLink,
} from './tableMapLink.ts';
import { publish } from './workspaceBus.ts';

afterEach(() => {
  announceRowHover(null);
});

describe('the row ids a pick on the map names', () => {
  it('names an entrance and its cave, so whichever list is open marks the row it has', () => {
    expect(rowIdsOfSelection({ kind: 'entrance', entranceId: 'e1', caveId: 'c1' })).toEqual(['e1', 'c1']);
    expect(rowIdsOfSelection({ kind: 'feature', featureId: 'f1' })).toEqual(['f1']);
    expect(rowIdsOfSelection({ kind: 'cave', caveId: 'c1' })).toEqual(['c1']);
  });

  it('names nothing for a cluster, a trip, or an empty click', () => {
    expect(rowIdsOfSelection({ kind: 'trip', tripId: 't1' })).toEqual([]);
    expect(rowIdsOfSelection(null)).toEqual([]);
  });
});

describe('the two directions over the workspace bus', () => {
  it('delivers a hovered row to a map in this window as well as to other windows', async () => {
    const heard: string[][] = [];
    const off = onRowHover((ids) => heard.push([...ids]));
    const posted: unknown[] = [];
    const channel = new BroadcastChannel('silexgis-workspace');
    channel.onmessage = (event) => posted.push(event.data);

    announceRowHover(['c1']);
    announceRowHover(null);

    expect(heard).toEqual([['c1'], []]);
    await vi.waitFor(() => expect(posted.length).toBe(2));
    // Ids, and only ids, cross to the other window.
    expect(posted[0]).toEqual({ kind: 'highlight', ids: ['c1'] });
    expect(posted[1]).toEqual({ kind: 'highlight', ids: null });
    channel.close();
    off();
  });

  it("follows the map's pick announcements as row ids", () => {
    const heard: string[][] = [];
    const off = onMapPick((ids) => heard.push([...ids]));
    publish({ kind: 'selection', selection: { kind: 'entrance', entranceId: 'e1', caveId: 'c1' } });
    publish({ kind: 'selection', selection: null });
    expect(heard).toEqual([['e1', 'c1'], []]);
    off();
  });
});

function Rows({ ids }: { ids: string[] }) {
  const link = useTableMapLink<{ id: string }>();
  return (
    <table>
      <tbody>
        {ids.map((id) => (
          <tr key={id} data-row-key={id} className={link.rowClassName({ id })} {...link.rowHover({ id })}>
            <td>{id}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

describe("a table's end of the link", () => {
  it('marks and scrolls to the row the map picked, and leaves the others alone', () => {
    const scroll = vi.fn();
    Element.prototype.scrollIntoView = scroll;
    render(<Rows ids={['c1', 'c2']} />);

    act(() => publish({ kind: 'selection', selection: { kind: 'entrance', entranceId: 'e9', caveId: 'c2' } }));

    expect(screen.getByText('c2').closest('tr')).toHaveClass(MAP_PICKED_ROW_CLASS);
    expect(screen.getByText('c1').closest('tr')).not.toHaveClass(MAP_PICKED_ROW_CLASS);
    expect(scroll).toHaveBeenCalledTimes(1);
  });

  it('announces the row pointed at, and clears it on leaving the row and on leaving the page', () => {
    const heard: string[][] = [];
    const off = onRowHover((ids) => heard.push([...ids]));
    const { unmount } = render(<Rows ids={['c1']} />);
    const row = screen.getByText('c1').closest('tr')!;

    act(() => {
      row.dispatchEvent(new MouseEvent('mouseover', { bubbles: true }));
    });
    act(() => {
      row.dispatchEvent(new MouseEvent('mouseout', { bubbles: true }));
    });
    act(() => {
      row.dispatchEvent(new MouseEvent('mouseover', { bubbles: true }));
    });
    unmount();

    expect(heard).toEqual([['c1'], [], ['c1'], []]);
    off();
  });
});
