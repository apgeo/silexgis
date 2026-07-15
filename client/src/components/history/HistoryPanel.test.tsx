// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { HistoryEvent } from '../../api/hooks.ts';

const events: HistoryEvent[] = [
  {
    id: 2,
    at: '2026-07-14T10:00:00Z',
    userId: null,
    userName: 'Ana',
    action: 'updated',
    entityType: 'Cave',
    entityId: 'c1',
    changes: { Description: { old: 'Old note', new: 'New note' } } as unknown as HistoryEvent['changes'],
    redactedProperties: [],
  },
  {
    id: 1,
    at: '2026-07-14T09:00:00Z',
    userId: null,
    userName: 'Ana',
    action: 'updated',
    entityType: 'CaveEntrance',
    entityId: 'e1',
    changes: null,
    redactedProperties: ['Geom', 'Altitude'],
  },
];

vi.mock('../../api/hooks.ts', () => ({
  useHistory: () => ({ data: { items: events }, isLoading: false }),
}));

// Imported after the mock so HistoryPanel binds to the mocked useHistory.
const { default: HistoryPanel } = await import('./HistoryPanel.tsx');

describe('HistoryPanel', () => {
  it('renders diffs, redacted rows and a working restore for matching updated rows', () => {
    const onRestore = vi.fn(() => Promise.resolve());
    render(
      <App>
        <HistoryPanel entityType="cave" entityId="c1" restore={{ entityType: 'Cave', onRestore }} />
      </App>,
    );

    // Diff row for the cave update.
    expect(screen.getByText('Old note')).toBeInTheDocument();
    expect(screen.getByText('New note')).toBeInTheDocument();

    // The entrance row's protected coordinates render as "hidden", not values.
    expect(screen.getAllByText(/Value hidden \(protected location\)/).length).toBe(2);

    // Restore is offered for the Cave update (matching restore.entityType) and calls back.
    const restoreButtons = screen.getAllByLabelText('Restore this value');
    expect(restoreButtons).toHaveLength(1); // only the Cave row; the entrance rows are redacted + wrong type
    fireEvent.click(restoreButtons[0]);
    expect(onRestore).toHaveBeenCalledWith(events[0], ['Description']);
  });
});
