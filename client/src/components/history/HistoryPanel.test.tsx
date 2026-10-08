// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { HistoryEvent } from '../../api/hooks.ts';

let events: HistoryEvent[] = [
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

  it('words the entry for roster times taken from the tracking log, instead of printing its keys', () => {
    // That entry is the record of an act, not of fields that changed: it has no earlier
    // values, and its members are the act's facts. Drawn as a table of changes it read
    // "Roster times — → fromTracking", "People — → 2", which says nothing to anybody.
    const before = events;
    events = [
      {
        id: 3,
        at: '2026-07-14T11:00:00Z',
        userId: null,
        userName: 'Ana',
        action: 'updated',
        entityType: 'TripLog',
        entityId: 't1',
        changes: {
          RosterTimes: { new: 'fromTracking' },
          TimeZone: { new: 'Europe/Bucharest' },
          People: { new: 2 },
          Rows: { new: 3 },
        } as unknown as HistoryEvent['changes'],
        redactedProperties: [],
      },
    ];
    try {
      render(
        <App>
          <HistoryPanel entityType="tripLog" entityId="t1" restore={{ entityType: 'TripLog', onRestore: vi.fn() }} />
        </App>,
      );

      const line = screen.getByTestId('history-roster-times');
      expect(line).toHaveTextContent(
        'Entry and exit times on the roster were taken from the tracking log.',
      );
      expect(line).toHaveTextContent('People: 2');
      expect(line).toHaveTextContent('Roster rows: 3');
      expect(line).toHaveTextContent('Read on the clocks of Europe/Bucharest.');
      // Nothing of the raw record is left on screen, and nothing offers to put a value
      // back: there is no earlier value to put back.
      expect(screen.queryByText(/fromTracking/)).not.toBeInTheDocument();
      expect(screen.queryByText('Roster times')).not.toBeInTheDocument();
      expect(screen.queryByText('Time zone')).not.toBeInTheDocument();
      expect(screen.queryByLabelText('Restore this value')).not.toBeInTheDocument();
    } finally {
      events = before;
    }
  });
});
