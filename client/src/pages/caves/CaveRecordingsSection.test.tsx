// SPDX-License-Identifier: AGPL-3.0-or-later
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';
import '../../i18n';
import type { TripRecordingListItem } from '../../api/hooks.ts';

let page: { items: TripRecordingListItem[]; page: number; pageSize: number; totalItems: number };
let asked: { caveId: string | undefined; page: number } | null = null;
const navigate = vi.fn();

vi.mock('../../api/hooks.ts', () => ({
  useCaveRecordings: (caveId: string | undefined, pageNo: number) => {
    asked = { caveId, page: pageNo };
    return { data: page, isFetching: false };
  },
}));

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return { ...actual, useNavigate: () => navigate };
});

const { default: CaveRecordingsSection } = await import('./CaveRecordingsSection.tsx');

/** Everything below is invented. */
function recording(overrides: Partial<TripRecordingListItem>): TripRecordingListItem {
  return {
    id: '11111111-1111-4111-8111-111111111111',
    title: 'Sump trip',
    startedAt: '2026-09-12T08:00:00Z',
    endedAt: '2026-09-12T16:00:00Z',
    pointCount: 3,
    standing: 'waiting',
    tripLogId: null,
    canConvert: false,
    ...overrides,
  };
}

function show() {
  return render(
    <MemoryRouter>
      <CaveRecordingsSection caveId="cave-1" />
    </MemoryRouter>,
  );
}

afterEach(() => {
  cleanup();
  asked = null;
  navigate.mockReset();
});

describe('CaveRecordingsSection', () => {
  it('asks for this cave only and shows where each recording stands', () => {
    page = {
      items: [
        recording({ id: 'a', title: 'Waiting trip', standing: 'waiting', canConvert: true }),
        recording({ id: 'b', title: 'Done trip', standing: 'converted', tripLogId: 'trip-b' }),
        recording({ id: 'c', title: 'Flagged trip', standing: 'needsAttention', tripLogId: 'trip-c' }),
      ],
      page: 1,
      pageSize: 10,
      totalItems: 3,
    };
    show();

    expect(asked).toEqual({ caveId: 'cave-1', page: 1 });
    expect(screen.getByTestId('recording-standing-waiting').textContent).toBe('Waiting');
    expect(screen.getByTestId('recording-standing-converted').textContent).toBe('Made into a trip');
    expect(screen.getByTestId('recording-standing-needsAttention').textContent).toBe(
      'Needs attention',
    );
    // The trip is linked from the two that have one, and from no other.
    expect(screen.getAllByText('Open the trip')).toHaveLength(2);
  });

  it('offers the conversion by hand only on a recording the caller may convert, and opens its review', () => {
    page = {
      items: [
        recording({ id: 'a', title: 'Waiting trip', canConvert: true }),
        recording({ id: 'b', title: 'Somebody else\'s', canConvert: false }),
      ],
      page: 1,
      pageSize: 10,
      totalItems: 2,
    };
    show();

    expect(screen.getAllByRole('button', { name: 'Convert to a trip' })).toHaveLength(1);
    const row = screen.getByText('Waiting trip').closest('tr')!;
    fireEvent.click(within(row).getByRole('button', { name: 'Convert to a trip' }));
    expect(navigate).toHaveBeenCalledWith('/trip-logs/speleoloc-import/recording/a');
  });

  it('says a recording is still open on the phone', () => {
    page = {
      items: [recording({ title: 'Open trip', endedAt: null })],
      page: 1,
      pageSize: 10,
      totalItems: 1,
    };
    show();
    expect(screen.getByText('Still open on the phone')).toBeTruthy();
  });

  /**
   * An empty answer is what a reader who may not be told of the cave, or may not place it, gets.
   * Drawing an empty table would announce a list to be denied, so nothing is drawn.
   */
  it('draws nothing at all for an empty answer', () => {
    page = { items: [], page: 1, pageSize: 10, totalItems: 0 };
    const { container } = show();
    expect(container.textContent).toBe('');
  });
});
