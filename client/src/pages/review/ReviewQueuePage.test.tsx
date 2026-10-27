// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import { ApiError } from '../../api/client.ts';
import type { HeldBatch } from '../../api/hooks.ts';

const { queueSpy, canSpy, capsSpy } = vi.hoisted(() => ({ queueSpy: vi.fn(), canSpy: vi.fn(), capsSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useReviewQueue: (page: number, pageSize: number, enabled: boolean) => queueSpy(page, pageSize, enabled),
  useCan: () => canSpy(),
  useCapabilities: () => ({ data: capsSpy() }),
}));

const { default: ReviewQueuePage } = await import('./ReviewQueuePage.tsx');

function batch(id: string, overrides: Partial<HeldBatch> = {}): HeldBatch {
  return {
    id,
    source: 'deviceSync',
    uploaderId: 'u1',
    uploaderName: 'Invented Uploader',
    reviewState: 'pending',
    heldAt: '2026-10-01T10:00:00Z',
    decidedAt: null,
    decidedByUserId: null,
    reviewNote: null,
    pendingCount: 4,
    lineCount: 4,
    heldBytes: 3 * 1024 * 1024,
    sections: { rows: 2, documents: 2 },
    ...overrides,
  } as HeldBatch;
}

function show() {
  return render(
    <MemoryRouter initialEntries={['/review']}>
      <Routes>
        <Route path="/review" element={<ReviewQueuePage />} />
        <Route path="/review/batches/:id" element={<div data-testid="opened-batch" />} />
      </Routes>
    </MemoryRouter>,
  );
}

afterEach(cleanup);
beforeEach(() => {
  queueSpy.mockReset();
  canSpy.mockReset().mockReturnValue(true);
  capsSpy.mockReset().mockReturnValue({});
});

describe('the queue of uploads awaiting review', () => {
  it('lists who sent each batch, from where, what it holds and how much, in the order the server gave', () => {
    queueSpy.mockReturnValue({
      data: {
        items: [
          batch('b1'),
          batch('b2', { source: 'speleolocArchive', uploaderName: 'Second Uploader', sections: { tripSheet: 5, keptRecording: 1 }, heldBytes: 0 }),
        ],
        page: 1,
        pageSize: 25,
        totalItems: 2,
      },
      isFetching: false,
    });
    show();
    const rows = screen.getAllByTestId('review-queue-row');
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('Invented Uploader');
    expect(rows[0].textContent).toContain('Phone');
    expect(rows[0].textContent).toContain('Places: 2');
    expect(rows[0].textContent).toContain('Photos and documents: 2');
    expect(rows[0].textContent).toContain('3.0 MB');
    expect(rows[1].textContent).toContain('Second Uploader');
    expect(rows[1].textContent).toContain('File import (SpeleoLoc archive)');
    expect(rows[1].textContent).toContain('Trip sheet: 5');
    expect(rows[1].textContent).toContain('Kept recording: 1');
    expect(rows[1].textContent).not.toContain('tripSheet');
  });

  it('opens a batch when its row is pressed', () => {
    queueSpy.mockReturnValue({ data: { items: [batch('b1')], page: 1, pageSize: 25, totalItems: 1 }, isFetching: false });
    show();
    fireEvent.click(screen.getByTestId('review-queue-row'));
    expect(screen.getByTestId('opened-batch')).toBeTruthy();
  });

  it('says when nothing is waiting', () => {
    queueSpy.mockReturnValue({ data: { items: [], page: 1, pageSize: 25, totalItems: 0 }, isFetching: false });
    show();
    expect(within(screen.getByTestId('review-queue')).getByText('Nothing is waiting for review.')).toBeTruthy();
  });

  it('shows a refusal as a refusal when the reader may not review', () => {
    queueSpy.mockReturnValue({ data: undefined, isFetching: false, error: new ApiError(403, 'forbidden') });
    show();
    expect(screen.getByText('You may not review uploads.')).toBeTruthy();
  });

  it('refuses a reader without the right instead of saying nothing waits', () => {
    canSpy.mockReturnValue(false);
    queueSpy.mockReturnValue({ data: undefined, isFetching: false });
    show();
    expect(screen.getByText('You may not review uploads.')).toBeTruthy();
    expect(screen.queryByTestId('review-queue')).toBeNull();
  });
});
