// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import i18n from '../../i18n';
import type { HeldBatch } from '../../api/hooks.ts';

const { mineSpy } = vi.hoisted(() => ({ mineSpy: vi.fn() }));

vi.mock('../../api/hooks.ts', () => ({
  useMyReviewBatches: (page: number, pageSize: number) => mineSpy(page, pageSize),
}));

const { default: MyReviewUploads } = await import('./MyReviewUploads.tsx');

function batch(id: string, overrides: Partial<HeldBatch> = {}): HeldBatch {
  return {
    id,
    source: 'deviceSync',
    uploaderId: 'me',
    uploaderName: 'Invented Uploader',
    reviewState: 'pending',
    heldAt: '2026-10-01T10:00:00Z',
    decidedAt: null,
    decidedByUserId: null,
    decidedByName: null,
    reviewNote: null,
    pendingCount: 3,
    lineCount: 4,
    heldBytes: 0,
    sections: { rows: 3 },
    ...overrides,
  } as HeldBatch;
}

function show() {
  return render(
    <MemoryRouter initialEntries={['/geodata']}>
      <Routes>
        <Route path="/geodata" element={<MyReviewUploads />} />
        <Route path="/review/batches/:id" element={<div data-testid="opened-batch" />} />
      </Routes>
    </MemoryRouter>,
  );
}

afterEach(cleanup);
beforeEach(async () => {
  await i18n.changeLanguage('en');
  mineSpy.mockReset();
});

describe('the uploader\'s own held uploads', () => {
  it('takes no room for an account that has nothing held', () => {
    mineSpy.mockReturnValue({ data: { items: [], page: 1, pageSize: 10, totalItems: 0 } });
    const { container } = show();
    expect(container.textContent).toBe('');
  });

  it('shows what waits, what was decided, who decided it and the note that came with it', () => {
    mineSpy.mockReturnValue({
      data: {
        items: [
          batch('b1'),
          batch('b2', {
            reviewState: 'rejected',
            pendingCount: 0,
            decidedAt: '2026-10-02T09:00:00Z',
            decidedByName: 'Invented Reviewer',
            reviewNote: 'Not this one',
          }),
        ],
        page: 1,
        pageSize: 10,
        totalItems: 2,
      },
    });
    show();

    const rows = screen.getAllByTestId('review-mine-row');
    expect(rows).toHaveLength(2);
    expect(within(rows[0]).getByTestId('review-mine-state')).toHaveTextContent('Waiting');
    expect(within(rows[0]).getByText('3 waiting of 4')).toBeInTheDocument();
    expect(within(rows[1]).getByTestId('review-mine-state')).toHaveTextContent('Rejected');
    expect(within(rows[1]).getByTestId('review-mine-note')).toHaveTextContent('Not this one');
    expect(within(rows[1]).getByText(/by Invented Reviewer/)).toBeInTheDocument();
    // Nothing waits in a decided upload, so no count of what waits is said.
    expect(within(rows[1]).queryByText(/waiting of/)).toBeNull();
  });

  it('opens the upload on pressing its row', () => {
    mineSpy.mockReturnValue({ data: { items: [batch('b1')], page: 1, pageSize: 10, totalItems: 1 } });
    show();
    fireEvent.click(screen.getByTestId('review-mine-row'));
    expect(screen.getByTestId('opened-batch')).toBeInTheDocument();
  });
});
