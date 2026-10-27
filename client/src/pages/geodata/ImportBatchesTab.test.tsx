// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { ImportBatch } from '../../api/hooks.ts';

const { batchesSpy, detailSpy, configSpy, revertSpy, historySpy } = vi.hoisted(() => ({
  batchesSpy: vi.fn(),
  detailSpy: vi.fn(),
  configSpy: vi.fn(),
  revertSpy: vi.fn(),
  historySpy: vi.fn(),
}));

vi.mock('../../api/hooks.ts', () => ({
  useImportBatches: () => batchesSpy(),
  useImportBatch: (id: string | undefined) => detailSpy(id),
  useRevertImportBatch: () => ({ mutateAsync: revertSpy }),
  useBatchHistory: (id: string | undefined, batch: string) => historySpy(id, batch),
  useTripLogConfig: () => configSpy(),
  // The panel of the account's own held uploads has its own test; here it has nothing to show.
  useMyReviewBatches: () => ({ data: undefined }),
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
    reviewState: 'notHeld',
    heldAt: null,
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
  revertSpy.mockReset().mockResolvedValue({ batch: {}, items: [] });
  historySpy.mockReset().mockReturnValue({ data: { items: [] }, isLoading: false });
});

describe('the imports this account made', () => {
  /**
   * A phone upload held for a reviewer wrote nothing yet, so its line says it waits rather than
   * reading like a batch that created nothing, and offers no undo.
   */
  it('shows a held batch as waiting for review, with nothing to undo', () => {
    show([batch({ source: 'deviceSync', createdCount: 0, canRevert: false, reviewState: 'pending', heldAt: '2026-10-01T10:00:00Z' })]);

    const list = screen.getByTestId('import-batches');
    expect(within(list).getByText('Waiting for review')).toBeTruthy();
    expect(within(list).queryByRole('button', { name: /Undo/ })).toBeNull();
  });

  it('marks a batch only partly accepted, and leaves one nobody held unmarked', () => {
    show([
      batch({ id: 'aaaaaaaa-0000-0000-0000-000000000002', source: 'deviceSync', reviewState: 'partlyAccepted' }),
      batch({ id: 'aaaaaaaa-0000-0000-0000-000000000003', source: 'vectorFile', geofileName: 'plain.gpx' }),
    ]);

    const list = screen.getByTestId('import-batches');
    expect(within(list).queryByText('Waiting for review')).toBeNull();
    expect(within(list).queryByText('Partly accepted in review')).toBeNull();
    expect(within(list).getAllByRole('button', { name: /Undo/ })).toHaveLength(2);
  });

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
   * The caves and features an undo deletes can be put back as well, whatever the source: a
   * spreadsheet of trips creates the caves its trips name, and those are deleted with them.
   */
  it.each(['vectorFile', 'tripCsv'] as const)(
    'says the caves and features an undo deletes can be restored (%s)',
    async (source) => {
      show([batch({ source, geofileName: source === 'vectorFile' ? 'entrances.gpx' : null })]);

      expect(await undoConfirmation()).toContain(
        'Each cave and feature it created can be restored from Deleted caves and features.',
      );
    },
  );

  /**
   * A deleted cave answers as not found at its own address, exactly as a deleted trip does, so
   * its line names it and points at where it can be put back rather than at itself.
   */
  it('sends the reader of an undone batch to where its caves and features can be put back', () => {
    detailSpy.mockReturnValue({
      isLoading: false,
      data: {
        items: [
          { id: 1, featureId: 'dddddddd-0000-0000-0000-000000000001', featureName: 'Pestera Mare', featureDeleted: true, action: 'create' },
          { id: 2, featureId: 'dddddddd-0000-0000-0000-000000000002', featureName: 'Pestera Mica', featureDeleted: false, action: 'create' },
        ],
      },
    });
    show([batch({ source: 'vectorFile', geofileName: 'caves.gpx', revertedAt: '2026-10-02T10:00:00Z', canRevert: false })]);

    fireEvent.click(screen.getByText('caves.gpx'));
    const drawer = screen.getByTestId('import-batch-detail');

    const restore = within(drawer).getByTestId('import-batch-feature-restore');
    expect(restore.getAttribute('href')).toBe('/features/deleted');
    expect(within(drawer).getByText('Pestera Mare')).toBeTruthy();
    expect(within(drawer).queryByRole('link', { name: 'Pestera Mare' })).toBeNull();

    expect(within(drawer).getByRole('link', { name: 'Pestera Mica' }).getAttribute('href'))
      .toBe('/features/dddddddd-0000-0000-0000-000000000002');
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

  describe('a phone upload', () => {
    const BATCH_ID = 'aaaaaaaa-0000-0000-0000-000000000009';
    const ROW = 'eeeeeeee-0000-0000-0000-000000000001';

    function openUpload(over: Partial<ImportBatch> = {}) {
      const upload = batch({ id: BATCH_ID, source: 'deviceSync', ...over });
      detailSpy.mockReturnValue({
        isLoading: false,
        data: {
          batch: upload,
          items: [
            {
              id: 41,
              featureId: null,
              attachedToFeatureId: ROW,
              attachedToFeatureName: 'Pestera Test',
              attachedToFeatureDeleted: false,
              action: 'attach',
              undoneAt: null,
            },
          ],
        },
      });
      show([upload]);
      fireEvent.click(screen.getByText('Phone upload'));
      return screen.getByTestId('import-batch-detail');
    }

    it('names the row an updating line changed instead of saying nothing was created', () => {
      const drawer = openUpload();
      expect(within(drawer).getByRole('link', { name: 'Pestera Test' }).getAttribute('href')).toBe(`/features/${ROW}`);
      expect(within(drawer).queryByText('Nothing created')).toBeNull();
    });

    it('undoes the whole upload, or the whole upload past what changed since, or one row', () => {
      const drawer = openUpload();
      const confirm = async (button: HTMLElement) => {
        fireEvent.click(button);
        const pop = await screen.findAllByRole('tooltip');
        fireEvent.click(within(pop[pop.length - 1]).getByRole('button', { name: /OK/ }));
      };

      return (async () => {
        await confirm(within(drawer).getByTestId('upload-undo'));
        expect(revertSpy).toHaveBeenLastCalledWith({ id: BATCH_ID });

        await confirm(within(drawer).getByTestId('upload-undo-anyway'));
        expect(revertSpy).toHaveBeenLastCalledWith({ id: BATCH_ID, force: true });

        await confirm(within(drawer).getByTestId('upload-undo-row'));
        expect(revertSpy).toHaveBeenLastCalledWith({ id: BATCH_ID, itemIds: [41] });
      })();
    });

    it('offers no undo to somebody who may not take the upload back', () => {
      const drawer = openUpload({ canRevert: false });
      expect(within(drawer).queryByTestId('upload-undo')).toBeNull();
      expect(within(drawer).queryByTestId('upload-undo-row')).toBeNull();
      // Reading what it changed is not an undo and stays.
      expect(within(drawer).getByRole('link', { name: 'Pestera Test' })).toBeTruthy();
    });

    it('says, line by line, what an undo left and why', async () => {
      revertSpy.mockResolvedValue({
        batch: {},
        items: [
          {
            itemId: 41,
            featureId: ROW,
            change: 'updated',
            outcome: 'partly',
            reason: null,
            applied: ['Description'],
            skipped: [{ field: 'Name', reason: 'superseded' }],
          },
        ],
      });
      const drawer = openUpload();
      fireEvent.click(within(drawer).getByTestId('upload-undo'));
      const pop = await screen.findAllByRole('tooltip');
      fireEvent.click(within(pop[pop.length - 1]).getByRole('button', { name: /OK/ }));

      const result = await within(drawer).findByTestId('upload-undo-result');
      expect(result.textContent).toContain('Partly undone');
      expect(result.textContent).toContain('Name was left: it has been changed since');
    });
  });
});
