// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { ImportBatch } from '../../api/hooks.ts';

const { batchesSpy, detailSpy, configSpy } = vi.hoisted(() => ({
  batchesSpy: vi.fn(),
  detailSpy: vi.fn(),
  configSpy: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  useImportBatches: () => batchesSpy(),
  useImportBatch: (id: string | undefined) => detailSpy(id),
  useRevertImportBatch: () => ({ mutateAsync: vi.fn() }),
  useTripLogConfig: () => configSpy(),
}));

const { default: ImportBatchesTab } = await import('./ImportBatchesTab.tsx');

function batch(overrides: Partial<ImportBatch> = {}): ImportBatch {
  return {
    id: 'aaaaaaaa-0000-0000-0000-000000000001',
    source: 'tripCsv',
    geofileId: null,
    geofileName: null,
    tripLogId: null,
    termRuleSetId: null,
    termRuleSetName: null,
    confirmedByUserId: 'bbbbbbbb-0000-0000-0000-000000000001',
    mode: 'reviewed',
    createdCount: 2,
    attachedCount: 0,
    skippedCount: 0,
    confirmedAt: '2026-10-01T10:00:00Z',
    revertedAt: null,
    revertedByUserId: null,
    canRevert: true,
    failures: [],
    ...overrides,
  } as unknown as ImportBatch;
}

function show(batches: ImportBatch[]) {
  batchesSpy.mockReturnValue({
    data: { items: batches, page: 1, pageSize: 20, totalItems: batches.length },
    isLoading: false,
  });
  return render(
    <AntApp>
      <MemoryRouter>
        <ImportBatchesTab />
      </MemoryRouter>
    </AntApp>,
  );
}

/** Opens the undo confirmation of the one batch listed and reads what it says. */
async function undoConfirmation(): Promise<string> {
  fireEvent.click(within(screen.getByTestId('import-batches')).getByRole('button', { name: /Undo/ }));
  return (await screen.findByRole('tooltip')).textContent ?? '';
}

afterEach(cleanup);
beforeEach(() => {
  batchesSpy.mockReset();
  detailSpy.mockReset().mockReturnValue({ data: undefined, isLoading: false });
  configSpy.mockReset().mockReturnValue({ data: { deletedRetentionDays: 30 } });
});

describe('the imports this account made', () => {
  /**
   * Undoing a batch of trips deletes them the way a person would, so each can be put back — and
   * the confirmation is where somebody about to undo five hundred of them needs to read that.
   * For how long is the installation's number, taken from the server.
   */
  it('says the trips an undo deletes can be restored, and for how long', async () => {
    configSpy.mockReturnValue({ data: { deletedRetentionDays: 14 } });
    show([batch({ source: 'tripCsv' })]);

    const text = await undoConfirmation();
    expect(text).toContain('Delete all 2 objects this import created?');
    expect(text).toContain('Each trip it created can be restored from Deleted trips for 14 days.');
  });

  it('names no deadline where the installation keeps deleted trips', async () => {
    configSpy.mockReturnValue({ data: { deletedRetentionDays: null } });
    show([batch({ source: 'speleolocArchive' })]);

    expect(await undoConfirmation()).toContain('Each trip it created can be restored from Deleted trips.');
  });

  /** A batch read off a map file creates no trips, and is promised nothing about any. */
  it('says nothing about trips for a batch that created none', async () => {
    show([batch({ source: 'vectorFile', geofileName: 'entrances.gpx' })]);

    const text = await undoConfirmation();
    expect(text).toContain('Delete all 2 objects this import created?');
    expect(text).not.toContain('trip');
  });

  /**
   * A deleted trip answers as not found at its own address. A line that went on linking there
   * would send the reader to a page saying no such trip, one click after a page that named it.
   */
  it('sends the reader of an undone batch to where its trips can be put back', () => {
    detailSpy.mockReturnValue({
      isLoading: false,
      data: {
        items: [
          { id: 1, tripLogId: 'cccccccc-0000-0000-0000-000000000001', tripTitle: 'Prima tura', tripDeleted: true, action: 'create' },
          { id: 2, tripLogId: 'cccccccc-0000-0000-0000-000000000002', tripTitle: 'A doua tura', tripDeleted: false, action: 'create' },
          { id: 3, tripLogId: null, tripTitle: 'A treia tura', tripDeleted: false, action: 'create' },
        ],
      },
    });
    show([batch({ revertedAt: '2026-10-02T10:00:00Z', canRevert: false })]);

    fireEvent.click(screen.getByText('From a trip spreadsheet'));
    const drawer = screen.getByTestId('import-batch-detail');

    // Deleted and restorable: named, tagged, and pointed at the deleted trips — not at itself.
    const restore = within(drawer).getByTestId('import-batch-trip-restore');
    expect(restore.getAttribute('href')).toBe('/trip-logs/deleted');
    expect(within(drawer).queryByRole('link', { name: 'Prima tura' })).toBeNull();

    // Still there: a link to the trip, as before.
    expect(within(drawer).getByRole('link', { name: 'A doua tura' }).getAttribute('href'))
      .toBe('/trip-logs/cccccccc-0000-0000-0000-000000000002');

    // Removed for good: its name, and nowhere to go.
    expect(within(drawer).getByText('A treia tura')).toBeTruthy();
    expect(within(drawer).queryByRole('link', { name: 'A treia tura' })).toBeNull();
  });
});
