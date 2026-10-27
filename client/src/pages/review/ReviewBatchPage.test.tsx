// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { App as AntApp } from 'antd';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { HeldBatch, HeldLine } from '../../api/hooks.ts';

const spies = vi.hoisted(() => ({
  detail: vi.fn(),
  me: vi.fn(),
  can: vi.fn(),
  feature: vi.fn(),
  acceptItem: vi.fn(),
  rejectItem: vi.fn(),
  acceptBatch: vi.fn(),
  rejectBatch: vi.fn(),
  preview: vi.fn(),
}));

vi.mock('../../api/hooks.ts', async () => {
  const real = await vi.importActual<typeof import('../../api/hooks/review.ts')>('../../api/hooks/review.ts');
  return {
    changedSinceIds: real.changedSinceIds,
    REVIEW_NOT_PENDING: real.REVIEW_NOT_PENDING,
    useReviewBatch: () => spies.detail(),
    useMe: () => spies.me(),
    useCan: () => spies.can(),
    useFeature: (id: string | undefined) => spies.feature(id),
    useHeldPreview: (reviewId: string, size: number, enabled: boolean) => spies.preview(reviewId, size, enabled),
    useAcceptReviewItem: () => ({ mutateAsync: spies.acceptItem }),
    useRejectReviewItem: () => ({ mutateAsync: spies.rejectItem }),
    useAcceptReviewBatch: () => ({ mutateAsync: spies.acceptBatch }),
    useRejectReviewBatch: () => ({ mutateAsync: spies.rejectBatch }),
  };
});

const { default: ReviewBatchPage } = await import('./ReviewBatchPage.tsx');

function line(id: string, overrides: Partial<HeldLine>): HeldLine {
  return {
    reviewId: id,
    section: 'rows',
    rowId: null,
    action: 'create',
    decision: 'pending',
    dependsOnReviewIds: [],
    acceptedFields: null,
    acceptableFields: [],
    note: null,
    decidedAt: null,
    decidedByUserId: null,
    failureCode: null,
    failureDetail: null,
    changedSince: false,
    proposal: null,
    ...overrides,
  } as HeldLine;
}

const cave = line('L1', {
  rowId: 'c1',
  proposal: { act: 'create', row: { id: 'c1', kind: 'cave', name: 'Invented Cave' } },
});
const entrance = line('L2', {
  rowId: 'e1',
  dependsOnReviewIds: ['L1'],
  proposal: { act: 'create', row: { id: 'e1', kind: 'caveEntrance', parentId: 'c1', name: 'Invented Entrance' } },
});
const edit = line('L3', {
  action: 'attach',
  rowId: 'f1',
  changedSince: true,
  acceptableFields: ['name', 'description', 'properties', 'position'],
  proposal: {
    act: 'update',
    row: { id: 'f1', parentId: 'c1', name: 'New name', description: 'Same text', properties: {} },
  },
});

function batch(overrides: Partial<HeldBatch> = {}): HeldBatch {
  return {
    id: 'B1',
    source: 'deviceSync',
    uploaderId: 'uploader',
    uploaderName: 'Invented Uploader',
    reviewState: 'pending',
    heldAt: '2026-10-20T10:00:00Z',
    decidedAt: null,
    decidedByUserId: null,
    reviewNote: null,
    pendingCount: 3,
    lineCount: 3,
    heldBytes: 0,
    sections: { rows: 3 },
    ...overrides,
  } as HeldBatch;
}

function show(lines: HeldLine[], b: HeldBatch = batch()) {
  spies.detail.mockReturnValue({ data: { batch: b, lines }, isLoading: false, error: null });
  return render(
    <AntApp>
      <MemoryRouter initialEntries={['/review/batches/B1']}>
        <Routes>
          <Route path="/review/batches/:id" element={<ReviewBatchPage />} />
        </Routes>
      </MemoryRouter>
    </AntApp>,
  );
}

const lineEl = (id: string) => document.querySelector(`[data-review-id="${id}"]`) as HTMLElement;

afterEach(cleanup);
beforeEach(() => {
  for (const spy of Object.values(spies)) {
    spy.mockReset();
  }
  spies.me.mockReturnValue({ data: { id: 'reviewer' } });
  spies.can.mockReturnValue(true);
  spies.feature.mockImplementation((id: string | undefined) => ({
    isLoading: false,
    data: id
      ? {
          feature: {
            name: id === 'f1' ? 'Old name' : 'Existing Cave',
            description: 'Same text',
            properties: {},
            geometry: null,
            approximateLocation: false,
            omittedLocation: false,
          },
        }
      : undefined,
  }));
  spies.preview.mockImplementation((reviewId: string, size: number, enabled: boolean) => ({
    url: enabled ? `blob:held/${reviewId}/${size}` : undefined,
    loading: false,
    failed: false,
  }));
  spies.acceptItem.mockResolvedValue({ decided: [], batches: [] });
  spies.rejectItem.mockResolvedValue({ decided: [], batches: [] });
  spies.acceptBatch.mockResolvedValue({ decided: [], batches: [] });
  spies.rejectBatch.mockResolvedValue({ decided: [], batches: [] });
});

describe('the batch view', () => {
  it('names who sent it and lists the lines under the cave they belong to', () => {
    show([cave, entrance, edit]);
    expect(screen.getByTestId('review-batch-facts').textContent).toContain('Invented Uploader');
    const groups = screen.getAllByTestId('review-group');
    expect(groups).toHaveLength(1);
    expect(groups[0].textContent).toContain('Invented Cave');
    expect(within(groups[0]).getAllByTestId('review-line')).toHaveLength(3);
    expect(within(lineEl('L2')).getByTestId('review-needs').textContent).toContain('Invented Cave');
  });

  it('marks an edit whose row changed since the phone read it', () => {
    show([cave, edit]);
    expect(within(lineEl('L3')).getByTestId('review-changed-since')).toBeTruthy();
    expect(within(lineEl('L1')).queryByTestId('review-changed-since')).toBeNull();
  });

  it('shows an edit field by field, current beside proposed, ticking only what differs', () => {
    show([cave, edit]);
    fireEvent.click(within(lineEl('L3')).getByTestId('review-line-toggle'));
    const table = within(lineEl('L3')).getByTestId('review-field-diff');
    expect(table.textContent).toContain('Old name');
    expect(table.textContent).toContain('New name');
    expect((within(table).getByTestId('review-field-name') as HTMLInputElement).checked).toBe(true);
    expect((within(table).getByTestId('review-field-description') as HTMLInputElement).checked).toBe(false);
  });

  it('accepts one field of an edit and sends just that field', async () => {
    show([cave, edit]);
    fireEvent.click(within(lineEl('L3')).getByTestId('review-line-toggle'));
    // Untouched boxes mean the default; touching one states the choice.
    fireEvent.click(within(lineEl('L3')).getByTestId('review-field-description'));
    fireEvent.click(within(lineEl('L3')).getByTestId('review-field-name'));
    fireEvent.click(within(lineEl('L3')).getByTestId('review-line-accept'));
    await waitFor(() => expect(spies.acceptItem).toHaveBeenCalledTimes(1));
    expect(spies.acceptItem.mock.calls[0][0]).toMatchObject({ reviewId: 'L3', fields: ['description'], overCurrent: false });
  });

  it('sends exactly the ticked fields of an opened edit even when no box was touched', async () => {
    show([cave, edit]);
    fireEvent.click(within(lineEl('L3')).getByTestId('review-line-toggle'));
    fireEvent.click(within(lineEl('L3')).getByTestId('review-line-accept'));
    await waitFor(() => expect(spies.acceptItem).toHaveBeenCalledTimes(1));
    // The position was never shown (the proposal carries none), so it is not written.
    expect(spies.acceptItem.mock.calls[0][0]).toMatchObject({ reviewId: 'L3', fields: ['name'] });
  });

  it('asks again for a second changed line instead of writing it over unseen', async () => {
    const edit2 = line('L4', {
      action: 'attach',
      rowId: 'f2',
      changedSince: true,
      acceptableFields: ['name'],
      proposal: { act: 'update', row: { id: 'f2', parentId: 'c1', name: 'Other name' } },
    });
    spies.acceptItem
      .mockRejectedValueOnce(new ApiError(409, 'review.changed_since', 'moved', { reviewIds: ['L3'] }))
      .mockResolvedValueOnce({ decided: [], batches: [] })
      .mockRejectedValueOnce(new ApiError(409, 'review.changed_since', 'moved', { reviewIds: ['L4'] }))
      .mockResolvedValue({ decided: [], batches: [] });
    show([edit, edit2]);
    fireEvent.click(screen.getByTestId('review-group-select'));
    fireEvent.click(screen.getByTestId('review-accept-selected'));
    await screen.findByTestId('review-conflict');
    fireEvent.click(screen.getByTestId('review-conflict-over'));
    // L3 is written over; L4 is sent plainly, refused, and put to the reviewer in its own right.
    await waitFor(() => expect(spies.acceptItem).toHaveBeenCalledTimes(2));
    expect(spies.acceptItem.mock.calls[0][0]).toMatchObject({ reviewId: 'L3', overCurrent: false });
    expect(spies.acceptItem.mock.calls[1][0]).toMatchObject({ reviewId: 'L3', overCurrent: true });
    await waitFor(() => expect(spies.acceptItem).toHaveBeenCalledTimes(3));
    expect(spies.acceptItem.mock.calls[2][0]).toMatchObject({ reviewId: 'L4', overCurrent: false });
    expect((await screen.findByTestId('review-conflict')).textContent).toContain('Other name');
  });

  it('says a line also depends on a line of another upload and tells so when accepting', async () => {
    const orphan = line('L9', {
      rowId: 'e9',
      dependsOnReviewIds: ['elsewhere'],
      proposal: { act: 'create', row: { id: 'e9', kind: 'caveEntrance', parentId: 'c9', name: 'Lone Entrance' } },
    });
    show([orphan]);
    expect(within(lineEl('L9')).getByTestId('review-needs-other').textContent).toContain('another upload');
    fireEvent.click(within(lineEl('L9')).getByTestId('review-line-accept'));
    expect((await screen.findByTestId('review-reach-other')).textContent).toContain('other uploads');
    expect(spies.acceptItem).not.toHaveBeenCalled();
  });

  it('will not accept an edit with every field unticked', () => {
    show([cave, edit]);
    fireEvent.click(within(lineEl('L3')).getByTestId('review-line-toggle'));
    fireEvent.click(within(lineEl('L3')).getByTestId('review-field-name'));
    expect((within(lineEl('L3')).getByTestId('review-line-accept') as HTMLButtonElement).disabled).toBe(true);
  });

  it('says which waiting lines accepting a line pulls in, and accepts only after the reviewer agrees', async () => {
    show([cave, entrance]);
    fireEvent.click(within(lineEl('L2')).getByTestId('review-line-accept'));
    const list = await screen.findByTestId('review-pullIn');
    expect(list.textContent).toContain('Invented Cave');
    expect(spies.acceptItem).not.toHaveBeenCalled();
    fireEvent.click(screen.getAllByRole('button', { name: 'Accept' }).at(-1)!);
    await waitFor(() => expect(spies.acceptItem).toHaveBeenCalledTimes(1));
    expect(spies.acceptItem.mock.calls[0][0]).toMatchObject({ reviewId: 'L2' });
  });

  it('turns a changed-since refusal into a choice and writes over the current rows on request', async () => {
    spies.acceptItem
      .mockRejectedValueOnce(new ApiError(409, 'review.changed_since', 'moved', { reviewIds: ['L3'] }))
      .mockResolvedValue({ decided: [{ reviewId: 'L3' }], batches: [] });
    show([edit]);
    fireEvent.click(within(lineEl('L3')).getByTestId('review-line-accept'));
    const dialog = await screen.findByTestId('review-conflict');
    expect(dialog.textContent).toContain('New name');
    fireEvent.click(screen.getByTestId('review-conflict-over'));
    await waitFor(() => expect(spies.acceptItem).toHaveBeenCalledTimes(2));
    expect(spies.acceptItem.mock.calls[0][0].overCurrent).toBe(false);
    expect(spies.acceptItem.mock.calls[1][0]).toMatchObject({ reviewId: 'L3', overCurrent: true });
  });

  it('offers rejecting the changed lines instead', async () => {
    spies.acceptBatch.mockRejectedValueOnce(new ApiError(409, 'review.changed_since', 'moved', { reviewIds: ['L3'] }));
    show([edit]);
    fireEvent.click(screen.getByTestId('review-accept-all'));
    await screen.findByTestId('review-conflict');
    fireEvent.click(screen.getByTestId('review-conflict-reject'));
    await waitFor(() => expect(spies.rejectItem).toHaveBeenCalledTimes(1));
    expect(spies.rejectItem.mock.calls[0][0]).toMatchObject({ reviewId: 'L3' });
  });

  it('sends the note to the uploader with accept all', async () => {
    show([cave]);
    fireEvent.change(screen.getByTestId('review-note'), { target: { value: 'Thanks, looks right' } });
    fireEvent.click(screen.getByTestId('review-accept-all'));
    await waitFor(() => expect(spies.acceptBatch).toHaveBeenCalledTimes(1));
    expect(spies.acceptBatch.mock.calls[0][0]).toMatchObject({ id: 'B1', note: 'Thanks, looks right' });
  });

  it('rejects everything after a confirmation, with the note', async () => {
    show([cave]);
    fireEvent.change(screen.getByTestId('review-note'), { target: { value: 'Not these' } });
    fireEvent.click(screen.getByTestId('review-reject-all'));
    fireEvent.click((await screen.findAllByRole('button', { name: 'Reject all' })).at(-1)!);
    await waitFor(() => expect(spies.rejectBatch).toHaveBeenCalledTimes(1));
    expect(spies.rejectBatch.mock.calls[0][0]).toMatchObject({ id: 'B1', note: 'Not these' });
  });

  it('decides the selected lines one after another, skipping one a dependency already decided', async () => {
    spies.acceptItem.mockResolvedValueOnce({ decided: [{ reviewId: 'L1' }, { reviewId: 'L2' }], batches: [] });
    show([cave, entrance]);
    fireEvent.click(screen.getByTestId('review-group-select'));
    fireEvent.click(screen.getByTestId('review-accept-selected'));
    await waitFor(() => expect(spies.acceptItem).toHaveBeenCalledTimes(1));
    // The cave was first (it needs nothing), and its decision reached the entrance too.
    expect(spies.acceptItem.mock.calls[0][0].reviewId).toBe('L1');
  });

  it('offers to take a rejection back, and shows the reason a line could not be written', () => {
    show([
      { ...cave, decision: 'rejected' } as HeldLine,
      { ...entrance, decision: 'failed', failureDetail: 'The uploader may no longer add rows.' } as HeldLine,
    ]);
    expect(within(lineEl('L1')).getByTestId('review-line-reopen')).toBeTruthy();
    expect(within(lineEl('L2')).getByTestId('review-failure').textContent).toContain('may no longer add rows');
  });

  it('shows no controls to the uploader of the batch, and says why', () => {
    spies.me.mockReturnValue({ data: { id: 'uploader' } });
    show([cave, entrance]);
    expect(screen.getByTestId('review-own')).toBeTruthy();
    expect(screen.queryByTestId('review-accept-all')).toBeNull();
    expect(screen.queryByTestId('review-line-accept')).toBeNull();
  });

  it('shows no controls to a reader without the decide right, and says why', () => {
    spies.can.mockReturnValue(false);
    show([cave]);
    expect(screen.getByTestId('review-readonly')).toBeTruthy();
    expect(screen.queryByTestId('review-accept-all')).toBeNull();
  });

  it('shows a line whose content is held back from this reader as hidden, not as an error', () => {
    show([line('H1', { rowId: null, proposal: null })]);
    fireEvent.click(within(lineEl('H1')).getByTestId('review-line-toggle'));
    expect(within(lineEl('H1')).getByTestId('review-line-detail').textContent).toContain('not shown');
  });

  it('shows a pin without its place when the server left it off', () => {
    show([
      line('P1', {
        section: 'rasterPins',
        proposal: { act: 'create', pin: { id: 'p', rasterMapId: 'm' } },
      }),
    ]);
    fireEvent.click(within(lineEl('P1')).getByTestId('review-line-toggle'));
    expect(within(lineEl('P1')).getByTestId('review-line-detail').textContent).toContain('not shown');
  });

  it('shows the picture a held photo keeps, and only a sentence for a file the server has no picture of', () => {
    show([
      line('D1', {
        section: 'documents',
        rowId: 'd1',
        hasPreview: true,
        proposal: { act: 'create', row: { id: 'd1', title: 'Invented photo', fileName: 'IMG_1.jpg' } },
      }),
      line('D2', {
        section: 'documents',
        rowId: 'd2',
        hasPreview: false,
        proposal: { act: 'create', row: { id: 'd2', title: 'Invented notes', fileName: 'notes.txt' } },
      }),
    ]);
    fireEvent.click(within(lineEl('D1')).getByTestId('review-line-toggle'));
    expect(within(lineEl('D1')).getByTestId('review-picture')).toBeTruthy();
    const picture = within(lineEl('D1')).getByAltText('Picture of Invented photo') as HTMLImageElement;
    expect(picture.getAttribute('src')).toBe('blob:held/D1/480');
    expect(spies.preview).toHaveBeenCalledWith('D1', 480, true);
    // The larger rendering is asked for only once the picture is opened.
    expect(spies.preview).toHaveBeenCalledWith('D1', 1200, false);
    expect(within(lineEl('D1')).getByTestId('review-line-detail').textContent).not.toContain('kept on the server');

    fireEvent.click(within(lineEl('D2')).getByTestId('review-line-toggle'));
    expect(within(lineEl('D2')).queryByTestId('review-picture')).toBeNull();
    expect(within(lineEl('D2')).getByTestId('review-line-detail').textContent).toContain('kept on the server');
    expect(spies.preview).not.toHaveBeenCalledWith('D2', expect.anything(), expect.anything());
  });

  const photoEdit = line('E1', {
    section: 'documents',
    action: 'attach',
    rowId: 'd5',
    hasPreview: true,
    acceptableFields: ['title', 'description', 'file'],
    fields: [
      { field: 'title', current: 'Old photo title', proposed: 'New photo title', differs: true, currentWithheld: false },
      { field: 'description', current: 'Same caption', proposed: 'Same caption', differs: false, currentWithheld: false },
      {
        field: 'file',
        current: { fileName: 'old.jpg', sizeBytes: 2048 },
        proposed: { fileName: 'new.jpg', sizeBytes: 4096 },
        differs: true,
        currentWithheld: false,
      },
    ],
    proposal: { act: 'supersede', row: { id: 'd5', title: 'New photo title', description: 'Same caption', fileName: 'new.jpg' } },
  });

  it('shows a photo edit field by field as the server compared it, with its new picture, ticking what differs', () => {
    show([photoEdit]);
    fireEvent.click(within(lineEl('E1')).getByTestId('review-line-toggle'));
    const table = within(lineEl('E1')).getByTestId('review-field-diff');
    expect(table.textContent).toContain('Old photo title');
    expect(table.textContent).toContain('New photo title');
    expect(table.textContent).toContain('new.jpg');
    expect((within(table).getByTestId('review-field-title') as HTMLInputElement).checked).toBe(true);
    expect((within(table).getByTestId('review-field-description') as HTMLInputElement).checked).toBe(false);
    expect((within(table).getByTestId('review-field-file') as HTMLInputElement).checked).toBe(true);
    expect(within(lineEl('E1')).getByTestId('review-picture')).toBeTruthy();
  });

  it('sends exactly the ticked fields of an opened photo edit, and the whole of one never opened', async () => {
    const other = { ...photoEdit, reviewId: 'E2', rowId: 'd6' } as HeldLine;
    show([photoEdit, other]);
    fireEvent.click(within(lineEl('E1')).getByTestId('review-line-toggle'));
    fireEvent.click(within(lineEl('E1')).getByTestId('review-field-file'));
    fireEvent.click(within(lineEl('E1')).getByTestId('review-line-accept'));
    await waitFor(() => expect(spies.acceptItem).toHaveBeenCalledTimes(1));
    expect(spies.acceptItem.mock.calls[0][0]).toMatchObject({ reviewId: 'E1', fields: ['title'] });

    fireEvent.click(within(lineEl('E2')).getByTestId('review-line-accept'));
    await waitFor(() => expect(spies.acceptItem).toHaveBeenCalledTimes(2));
    expect(spies.acceptItem.mock.calls[1][0].reviewId).toBe('E2');
    expect(spies.acceptItem.mock.calls[1][0].fields).toBeUndefined();
  });

  it('will not accept a compared edit with every field unticked', () => {
    show([photoEdit]);
    fireEvent.click(within(lineEl('E1')).getByTestId('review-line-toggle'));
    fireEvent.click(within(lineEl('E1')).getByTestId('review-field-title'));
    fireEvent.click(within(lineEl('E1')).getByTestId('review-field-file'));
    expect((within(lineEl('E1')).getByTestId('review-line-accept') as HTMLButtonElement).disabled).toBe(true);
  });

  it('shows a scan edit by notes, place and time, and an earlier place the server left out as not shown', () => {
    show([
      line('S1', {
        section: 'tripPoints',
        action: 'attach',
        rowId: 's1',
        acceptableFields: ['notes', 'place', 'time'],
        fields: [
          { field: 'notes', current: 'first note', proposed: 'second note', differs: true, currentWithheld: false },
          { field: 'place', current: null, proposed: 'f9', differs: true, currentWithheld: true },
          { field: 'time', current: '2026-09-12T08:10:00Z', proposed: '2026-09-12T08:10:00Z', differs: false, currentWithheld: false },
        ],
        proposal: { act: 'update', point: { id: 's1', recordingId: 'r1', placeId: 'f9', notes: 'second note' } },
      }),
    ]);
    fireEvent.click(within(lineEl('S1')).getByTestId('review-line-toggle'));
    const table = within(lineEl('S1')).getByTestId('review-field-diff');
    expect(table.textContent).toContain('second note');
    expect(table.textContent).toContain('Not shown to you');
    // The new place is named through the feature's own route.
    expect(spies.feature).toHaveBeenCalledWith('f9');
    expect((within(table).getByTestId('review-field-time') as HTMLInputElement).checked).toBe(false);
  });
});
