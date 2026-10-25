// SPDX-License-Identifier: AGPL-3.0-or-later
import { App } from 'antd';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
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
    source: null,
    changeSetId: null,
    importBatchId: null,
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
    source: null,
    changeSetId: null,
    importBatchId: null,
  },
];

const restoreOnServer = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useHistory: () => ({ data: { items: events }, isLoading: false }),
  useRestoreFromHistory: () => ({ mutateAsync: restoreOnServer }),
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
        source: null,
        changeSetId: null,
        importBatchId: null,
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

  it('says how large a geometry was when the trail recorded only a summary of it', () => {
    const before = events;
    const summary = (sha256: string, points: number) => ({
      geometryType: 'MultiLineString',
      points,
      sha256,
      omitted: true,
    });
    events = [
      {
        id: 4,
        at: '2026-07-14T12:00:00Z',
        userId: null,
        userName: 'Ana',
        action: 'updated',
        entityType: 'Centerline',
        entityId: 'l1',
        changes: { Geom: { old: summary('aa', 48211), new: summary('bb', 48230) } } as unknown as HistoryEvent['changes'],
        redactedProperties: [],
        source: null,
        changeSetId: null,
        importBatchId: null,
      },
    ];
    try {
      render(
        <App>
          <HistoryPanel entityType="centerline" entityId="l1" restore={{ entityType: 'Centerline', onRestore: vi.fn() }} />
        </App>,
      );

      expect(screen.getByText('Geometry of 48211 points (too large to record in full)')).toBeInTheDocument();
      expect(screen.getByText('Geometry of 48230 points (too large to record in full)')).toBeInTheDocument();
      expect(screen.queryByText(/sha256/)).not.toBeInTheDocument();
      expect(screen.queryByLabelText('Restore this value')).not.toBeInTheDocument();
    } finally {
      events = before;
    }
  });
  describe('with the changes of a phone upload', () => {
    const upload = {
      source: 'deviceSync' as const,
      changeSetId: '0192a0a0-0000-7000-8000-000000000001',
      importBatchId: '0192a0a0-0000-7000-8000-0000000000b1',
    };
    const base = {
      at: '2026-07-14T10:00:00Z',
      userId: null,
      userName: 'Ana',
      action: 'updated',
      redactedProperties: [] as string[],
    };

    const withEvents = async (items: HistoryEvent[], run: () => Promise<void> | void) => {
      const before = events;
      events = items;
      try {
        await run();
      } finally {
        events = before;
      }
    };

    it('names the source once for a change set, links its import and lists each part under it', async () => {
      await withEvents(
        [
          {
            ...base,
            ...upload,
            id: 12,
            entityType: 'Feature:CaveEntrance',
            entityId: 'e1',
            changes: { Altitude: { old: 100, new: 110 } } as unknown as HistoryEvent['changes'],
          },
          {
            ...base,
            ...upload,
            id: 11,
            entityType: 'Feature:Cave',
            entityId: 'c1',
            changes: { Description: { old: 'Old note', new: 'New note' } } as unknown as HistoryEvent['changes'],
          },
          { ...base, id: 10, source: 'web', changeSetId: null, importBatchId: null, entityType: 'Feature:Cave', entityId: 'c1',
            changes: { Name: { old: 'A', new: 'B' } } as unknown as HistoryEvent['changes'] },
        ],
        () => {
          render(
            <MemoryRouter>
              <App>
                <HistoryPanel entityType="feature" entityId="c1" />
              </App>
            </MemoryRouter>,
          );

          const set = screen.getByTestId('history-change-set');
          // Who, when and from where once, then the two changes.
          expect(within(set).getByTestId('history-source')).toHaveTextContent('Phone upload');
          expect(within(set).getAllByTestId('history-source')).toHaveLength(1);
          expect(within(set).getByText('2 changes')).toBeInTheDocument();
          expect(within(set).getByText('Entrance')).toBeInTheDocument();
          expect(within(set).getByText('Cave')).toBeInTheDocument();
          expect(within(set).getByTestId('history-batch-link')).toHaveAttribute(
            'href',
            `/geodata?tab=batches&batch=${upload.importBatchId}`,
          );

          // The browser edit stands alone, named as such, with no link.
          expect(screen.getAllByTestId('history-source')).toHaveLength(2);
          expect(screen.getByText('Edited in the browser')).toBeInTheDocument();
          expect(screen.getAllByTestId('history-batch-link')).toHaveLength(1);
        },
      );
    });

    it('restores through the server and says each field it left, with the reason', async () => {
      restoreOnServer.mockResolvedValueOnce({
        applied: ['Description'],
        skipped: [{ field: 'Geom', reason: 'no_pre_image' }],
      });
      const onRestored = vi.fn();
      const summary = { omitted: true, geometryType: 'LineString', points: 900, sha256: 'ab' };
      await withEvents(
        [
          {
            ...base,
            ...upload,
            id: 21,
            entityType: 'Feature:Generic',
            entityId: 'f1',
            changes: {
              Description: { old: 'Old note', new: 'New note' },
              Geom: { old: summary, new: { ...summary, sha256: 'cd' } },
            } as unknown as HistoryEvent['changes'],
          },
        ],
        async () => {
          render(
            <MemoryRouter>
              <App>
                <HistoryPanel
                  entityType="feature"
                  entityId="f1"
                  restore={{ entityType: 'Feature:Generic', onRestored }}
                />
              </App>
            </MemoryRouter>,
          );

          // The server holds the full shape beside a summary, so even the summarised geometry offers it.
          expect(screen.getAllByLabelText('Restore this value')).toHaveLength(2);
          fireEvent.click(screen.getByText('Restore all values from before this change'));

          await waitFor(() =>
            expect(restoreOnServer).toHaveBeenCalledWith({
              entityType: 'feature',
              entityId: 'f1',
              auditEntryId: 21,
              fields: ['Description', 'Geom'],
            }),
          );
          const outcome = await screen.findByTestId('history-restore-outcome');
          expect(outcome).toHaveTextContent('1 value was put back');
          expect(outcome).toHaveTextContent('Location');
          expect(outcome).toHaveTextContent('the earlier shape was not kept');
          expect(onRestored).toHaveBeenCalledTimes(1);
        },
      );
    });
  });
});
